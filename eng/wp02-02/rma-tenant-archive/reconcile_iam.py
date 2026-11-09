#!/usr/bin/env python3
"""Reconcile pinned RMA IAM references against a read-only cut of their original local source."""

import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
from tempfile import TemporaryDirectory

sys.dont_write_bytecode = True
import archive
import erp_integration
import file_content_assets
import iam_v6
import source_v3
import visibility_v4


FORMAT = "cp6-rma-iam-reconciliation-v1"
ARCHIVE_FORMATS = (iam_v6.FORMAT, file_content_assets.FORMAT, erp_integration.FORMAT)
REPORT = "iam-reconciliation.jsonl"


def checked_original(bundle, tenant, target, digest, file_content_root, destination):
    manifest_path = bundle / "manifest.json"
    if bundle.is_symlink() or manifest_path.is_symlink() or not manifest_path.is_file():
        raise archive.ArchiveError("manifest-member-rejected")
    pinned = manifest_path.read_bytes()
    if hashlib.sha256(pinned).hexdigest() != digest:
        raise archive.ArchiveError("manifest-digest-mismatch")
    manifest = archive.verify_bundle(bundle, str(tenant), file_content_root=file_content_root)
    if manifest != json.loads(pinned) or manifest["format"] not in ARCHIVE_FORMATS \
            or manifest.get("profile") != archive.PROFILE:
        raise archive.ArchiveError("iam-original-bundle-rejected")
    if archive.PhysicalTarget.from_mapping(manifest.get("source")) != target:
        raise archive.ArchiveError("source-target-mismatch")
    # Consume the same IAM member bytes even if the input path changes after verification.
    shutil.copyfile(bundle / iam_v6.MEMBER, destination, follow_symlinks=False)
    if destination.is_symlink() or archive.file_digest(destination) != manifest["members"][iam_v6.MEMBER]:
        raise archive.ArchiveError("bundle-digest-mismatch")
    return manifest, archive.guid(manifest.get("installationId"))


def capture_current(pg_bin, target, username, installation, tenant, logs, output):
    connection = archive.connection_args(target, username)
    with archive.snapshot_session(pg_bin, connection, os.environ, logs) as query:
        ready = archive.check_source(query, target, installation, tenant)
        heads = "t_" + tenant.hex + ".heads"
        if not query("""
            LOCK TABLE {heads} IN ACCESS SHARE MODE;
            SELECT to_json(EXISTS(SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE c.oid='{heads}'::regclass AND c.relkind='r' AND c.relowner={owner}::oid AND n.nspowner=c.relowner));
        """.format(heads=heads, owner=ready["ownerOid"])):
            raise archive.ArchiveError("iam-current-head-authority-rejected")
        originals = archive.original_journal_rows(tenant)
        journal = archive.check_original_journal(query, target, installation, ready, originals)
        current = query("""
            SELECT json_build_object('snapshotId',pg_export_snapshot(),'watermark',pg_current_snapshot()::text,
                'backendPid',pg_backend_pid(),'capturedAt',transaction_timestamp(),
                'readOnly',current_setting('transaction_read_only')::bool,'isolation',current_setting('transaction_isolation'),
                'serverVersionNum',current_setting('server_version_num')::int);
        """)
        rows = iam_v6.rows_sql(tenant, originals, visibility_v4.rows_sql(tenant))
        archive.copy_snapshot(pg_bin, connection, os.environ, logs, output, "iam-current-copy", current["snapshotId"],
                              rows + " ORDER BY " + iam_v6.ORDER, source_v3.READ_ALL_ROWS)
    current["originalJournalCatalogSha256"] = journal["catalogSha256"]
    return current


def read_rows(stream, tenant, counts):
    reader = csv.DictReader(stream, strict=True)
    if reader.fieldnames != list(iam_v6.COLUMNS):
        raise archive.ArchiveError("iam-member-header-rejected")
    previous = None
    for row in reader:
        if None in row or None in row.values() or row["tenant_id"] != str(tenant) \
                or row["kind"] not in iam_v6.FACTS or not row["id"]:
            raise archive.ArchiveError("iam-member-row-rejected")
        kind = row["kind"]
        key = (kind, row["id"])
        facts = json.loads(row["facts"])
        if (previous is not None and key <= previous) or not isinstance(facts, dict) \
                or facts.keys() != set(iam_v6.FACTS[kind][1]):
            raise archive.ArchiveError("iam-member-key-or-facts-rejected")
        counts[kind] += 1
        previous = key
        yield key, facts


def merge(original, current):
    old, live = next(original, None), next(current, None)
    while old is not None or live is not None:
        if live is None or old is not None and old[0] < live[0]:
            key, old_facts = old
            live_facts = None
            decision = "UNKNOWN"
            old = next(original, None)
        elif old is None or live[0] < old[0]:
            key, live_facts = live
            old_facts = None
            decision = "CURRENT_ONLY"
            live = next(current, None)
        else:
            key, old_facts = old
            _, live_facts = live
            decision = "UNCHANGED" if old_facts == live_facts else "CHANGED"
            old, live = next(original, None), next(current, None)
        result = dict(type="observation", kind=key[0], id=key[1], decision=decision,
                      originalFacts=old_facts, currentFacts=live_facts)
        if decision == "UNKNOWN":
            result["reason"] = "OUTSIDE_CURRENT_RMA_GRAPH_OR_ABSENT"
        yield result


def reconcile(pg_bin, target, username, tenant, bundle, digest, output, *, file_content_root=None):
    if not re.fullmatch(r"[0-9a-fA-F]{64}", digest):
        raise archive.ArchiveError("invalid-manifest-digest")
    digest = digest.lower()
    archive.private_directory(output)
    logs = output / "native"
    logs.mkdir(mode=0o700)
    pending, report = output / (REPORT + ".pending"), output / REPORT
    original_counts = dict.fromkeys(iam_v6.FACTS, 0)
    current_counts = dict.fromkeys(iam_v6.FACTS, 0)
    decisions = dict(UNCHANGED=0, CHANGED=0, CURRENT_ONLY=0, UNKNOWN=0)
    try:
        with TemporaryDirectory(dir=output) as temporary:
            original_path, current_path = Path(temporary) / "original.csv", Path(temporary) / "current.csv"
            manifest, installation = checked_original(bundle, tenant, target, digest, file_content_root, original_path)
            current = capture_current(pg_bin, target, username, installation, tenant, logs, current_path)
            with original_path.open(encoding="utf-8", newline="") as original, \
                    current_path.open(encoding="utf-8", newline="") as live, \
                    pending.open("x", encoding="utf-8", newline="\n") as stream:
                def record(value):
                    stream.write(json.dumps(value) + "\n")

                record(dict(type="header", format=FORMAT, tenantId=str(tenant), source=target.mapping(),
                            installationId=str(installation), current=current,
                            original=dict(format=manifest["format"], manifestSha256=digest, snapshot=manifest["snapshot"])))
                for observation in merge(read_rows(original, tenant, original_counts), read_rows(live, tenant, current_counts)):
                    decisions[observation["decision"]] += 1
                    record(observation)
                if original_counts != manifest["rmaIamReferences"]["counts"]:
                    raise archive.ArchiveError("iam-original-count-mismatch")
                record(dict(type="summary", result="OBSERVED", counts=decisions, originalCounts=original_counts,
                            currentCounts=current_counts, recoveryReady=False))
        pending.replace(report)
    finally:
        pending.unlink(missing_ok=True)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("pg-bin", "target-file", "bundle", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--file-content-root", type=Path, help="original private local asset root, required for v7 and v8")
    for name in ("username", "tenant", "manifest-sha256"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    try:
        target = archive.PhysicalTarget.from_mapping(json.loads(args.target_file.read_text(encoding="utf-8-sig")))
        report = reconcile(args.pg_bin.resolve(strict=True), target, args.username, archive.guid(args.tenant),
                           args.bundle.absolute(), args.manifest_sha256, args.output.absolute(),
                           file_content_root=args.file_content_root)
        print(json.dumps(dict(report=str(report), recoveryReady=False)))
    except (archive.ArchiveError, OSError, ValueError, csv.Error, subprocess.SubprocessError) as error:
        print("reconciliation rejected: " + str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
