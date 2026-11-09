#!/usr/bin/env python3
"""Observe current Identity Priority keys from a pinned v5, v6 or v7 archive on its exact local source."""

import argparse
from contextlib import closing
import csv
import hashlib
from itertools import islice
import json
import os
from pathlib import Path
import re
import shutil
import sys
from tempfile import TemporaryDirectory

sys.dont_write_bytecode = True
import archive
import file_content_assets
import iam_v6
import identity_priority


FORMAT = "cp6-identity-priority-reconciliation-v1"
ARCHIVE_FORMATS = (identity_priority.FORMAT, iam_v6.FORMAT, file_content_assets.FORMAT)
REPORT = "reconciliation.jsonl"
BATCH_SIZE = 128
# Platform 0.10.3 accepts a maximum 1 MiB envelope, emitted as native hex bytea in v5.
MAX_PAYLOAD_HEX = 2 + 2 * 1_048_576
OUTBOX = next(table for table in identity_priority.TABLES if table.name == "Cp6_OutboxMessage")
INBOX = next(table for table in identity_priority.TABLES if table.name == "Cp6_InboxMessage")
TABLES = (OUTBOX, INBOX)
STATES = {OUTBOX: {0: "Pending", 1: "Dispatching", 2: "Published", 3: "DeadLettered"},
          INBOX: {0: "Processing", 1: "Processed"}}


def checked_bundle(bundle, tenant, target, digest, inputs, *, file_content_root=None):
    manifest_path = bundle / "manifest.json"
    if bundle.is_symlink() or manifest_path.is_symlink() or not manifest_path.is_file():
        raise archive.ArchiveError("manifest-member-rejected")
    manifest_bytes = manifest_path.read_bytes()
    if hashlib.sha256(manifest_bytes).hexdigest() != digest:
        raise archive.ArchiveError("manifest-digest-mismatch")
    manifest = archive.verify_bundle(bundle, str(tenant), file_content_root=file_content_root)
    if manifest != json.loads(manifest_bytes) or manifest["format"] not in ARCHIVE_FORMATS \
            or manifest.get("profile") != archive.PROFILE:
        raise archive.ArchiveError("priority-v5-bundle-rejected")
    if archive.PhysicalTarget.from_mapping(manifest.get("source")) != target:
        raise archive.ArchiveError("source-target-mismatch")
    installation = archive.guid(manifest.get("installationId"))
    # Check the private bytes the reconciler will consume, even if the bundle path later changes.
    for table in TABLES:
        member = inputs / table.member
        shutil.copyfile(bundle / table.member, member, follow_symlinks=False)
        if member.is_symlink() or archive.file_digest(member) != manifest["members"][table.member]:
            raise archive.ArchiveError("bundle-digest-mismatch")
    return manifest, installation


def binding_columns(table):
    shared = (*table.unique_keys[1], "TopicName", "PartitionKey", "PayloadSha256", "AggregateId")
    return (*shared, "CorrelationId", "CausationId") if table is OUTBOX else shared


def state_name(table, status):
    try:
        return STATES[table][int(status)]
    except (KeyError, ValueError) as error:
        raise archive.ArchiveError("priority-message-status-rejected") from error


def archived_rows(path, table, tenant, count):
    with path.open(encoding="utf-8", newline="") as file:
        rows = csv.DictReader(file, strict=True)
        if rows.fieldnames != list(table.columns):
            raise archive.ArchiveError("priority-member-header-rejected")
        seen = 0
        for row in rows:
            seen += 1
            if seen > count or None in row or None in row.values() or row["TenantId"] != str(tenant):
                raise archive.ArchiveError("priority-member-row-rejected")
            binding = {column: row[column] for column in binding_columns(table)}
            if not re.fullmatch(r"[0-9a-f]{64}", binding["PayloadSha256"]):
                raise archive.ArchiveError("priority-payload-digest-rejected")
            binding["Id"] = str(archive.guid(row["Id"]))
            binding["AggregateVersion"] = int(row["AggregateVersion"])
            if table is OUTBOX:
                payload = row["Payload"]
                if not payload.startswith("\\x") or hashlib.sha256(bytes.fromhex(payload[2:])).hexdigest() != binding["PayloadSha256"]:
                    raise archive.ArchiveError("priority-payload-digest-rejected")
                lease = row["LeaseToken"]
                binding["LeaseTokenSha256"] = hashlib.sha256(lease.encode("utf-8")).hexdigest() if lease else None
            yield binding, state_name(table, row["Status"])
        if seen != count:
            raise archive.ArchiveError("priority-member-count-mismatch")


def lookup(query, table, tenant, bindings):
    # Native bpchar equality uses the original unique index's collation and padding semantics.
    key = " AND ".join('m."{0}"=(wanted.binding->>\'{0}\')::bpchar'.format(column)
                       for column in table.unique_keys[1])
    changed = ["m.\"Id\"::text IS DISTINCT FROM wanted.binding->>'Id'",
               'm."AggregateVersion" IS DISTINCT FROM (wanted.binding->>\'AggregateVersion\')::int']
    changed.extend('bpcharsend(m."{0}") IS DISTINCT FROM convert_to(wanted.binding->>\'{0}\',\'UTF8\')'.format(column)
                   for column in binding_columns(table))
    if table is OUTBOX:
        changed.append('encode(sha256(m."Payload"),\'hex\') IS DISTINCT FROM wanted.binding->>\'PayloadSha256\'')
        facts = """
            'status',m."Status",'publishedAtUtc',m."PublishedAtUtc",'deadLetteredAtUtc',m."DeadLetteredAtUtc",
            'leaseExpiresAtUtc',m."LeaseExpiresAtUtc",'leaseActive',coalesce(m."Status"=1 AND m."LeaseOwner" IS NOT NULL
              AND m."LeaseToken" IS NOT NULL AND m."LeaseExpiresAtUtc">transaction_timestamp(),false),
            'leaseChanged',encode(sha256(bpcharsend(m."LeaseToken")),'hex') IS DISTINCT FROM wanted.binding->>'LeaseTokenSha256'
        """
    else:
        facts = "'status',m.\"Status\",'processedAtUtc',m.\"ProcessedAtUtc\",'outcomeCode',m.\"OutcomeCode\""
    return query("""
        SELECT coalesce(json_agg(CASE
            WHEN m."Id" IS NULL THEN json_build_object('decision','UNKNOWN','reason','ORIGINAL_KEY_NOT_FOUND')
            WHEN m."TenantId" IS DISTINCT FROM {tenant}::uuid
                THEN json_build_object('decision','BLOCKED','reason','GLOBAL_KEY_CONFLICT')
            WHEN {changed} THEN json_build_object('decision','BLOCKED','reason','BINDING_CONFLICT')
            ELSE json_build_object('decision','PRESERVE_CURRENT','current',json_build_object({facts}))
            END ORDER BY wanted.ordinal), '[]'::json)
        FROM jsonb_array_elements({input}::jsonb) WITH ORDINALITY AS wanted(binding,ordinal)
        LEFT JOIN {relation} m ON {key};
    """.format(tenant=archive.literal(tenant), changed=" OR ".join(changed), facts=facts,
               input=archive.literal(json.dumps(bindings)), relation=identity_priority.relation(table), key=key),
        failure="priority-current-read-failed (see native logs)")


def reconcile(pg_bin, target, username, tenant, bundle, digest, output, *, file_content_root=None):
    if not re.fullmatch(r"[0-9a-fA-F]{64}", digest):
        raise archive.ArchiveError("invalid-manifest-digest")
    digest = digest.lower()
    archive.private_directory(output)
    pending = output / (REPORT + ".pending")
    report = output / REPORT
    counts = dict(PRESERVE_CURRENT=0, BLOCKED=0, UNKNOWN=0)
    csv.field_size_limit(MAX_PAYLOAD_HEX)
    try:
        with TemporaryDirectory(dir=output) as private_inputs:
            inputs = Path(private_inputs)
            manifest, installation = checked_bundle(bundle, tenant, target, digest, inputs, file_content_root=file_content_root)
            with pending.open("x", encoding="utf-8", newline="\n") as stream, \
                    archive.snapshot_session(pg_bin, archive.connection_args(target, username), os.environ, output) as query:
                def record(value):
                    stream.write(json.dumps(value) + "\n")

                ready = archive.check_source(query, target, installation, tenant)
                identity_priority.check_layout(query, ready["ownerOid"], TABLES)
                current = query("""
                    SET LOCAL row_security=off;
                    SET LOCAL timezone='UTC';
                    SELECT json_build_object('backendPid',pg_backend_pid(),'watermark',pg_current_snapshot()::text,
                        'capturedAt',transaction_timestamp(),'readOnly',current_setting('transaction_read_only')::bool,
                        'isolation',current_setting('transaction_isolation'));
                """)
                record(dict(type="header", format=FORMAT, tenantId=str(tenant), installationId=str(installation),
                            source=target.mapping(), manifestSha256=digest, archived=manifest["snapshot"], current=current))
                for table in TABLES:
                    count = manifest["identityPriority"]["tables"][table.name]["count"]
                    with closing(archived_rows(inputs / table.member, table, tenant, count)) as rows:
                        if count == 0:
                            # Check native authority even when no archived key needs a lookup.
                            lookup(query, table, tenant, [])
                        while batch := list(islice(rows, BATCH_SIZE)):
                            results = lookup(query, table, tenant, [binding for binding, _ in batch])
                            if len(results) != len(batch):
                                raise archive.ArchiveError("priority-key-cardinality-rejected")
                            for (binding, old_state), result in zip(batch, results):
                                if "current" in result:
                                    facts = result["current"]
                                    facts["state"] = state_name(table, facts.pop("status"))
                                counts[result["decision"]] += 1
                                record(dict(type="observation", table=table.name,
                                            key={column: binding[column] for column in table.unique_keys[1]},
                                            archivedState=old_state, **result))
                result = "BLOCKED" if counts["BLOCKED"] or counts["UNKNOWN"] else "OBSERVED"
                record(dict(type="summary", result=result, counts=counts, recoveryReady=False))
        pending.replace(report)
    finally:
        pending.unlink(missing_ok=True)
    return report, result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("pg-bin", "target-file", "bundle", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    for name in ("username", "tenant", "manifest-sha256"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--file-content-root", type=Path, help="original private local asset root, required for v7")
    args = parser.parse_args()
    try:
        target = archive.PhysicalTarget.from_mapping(json.loads(args.target_file.read_text(encoding="utf-8-sig")))
        report, result = reconcile(args.pg_bin.resolve(strict=True), target, args.username, archive.guid(args.tenant),
                                   args.bundle.absolute(), args.manifest_sha256, args.output.absolute(),
                                   file_content_root=args.file_content_root)
        print(json.dumps(dict(report=str(report), result=result, recoveryReady=False)))
        return 2 if result == "BLOCKED" else 0
    except (archive.ArchiveError, OSError, ValueError, csv.Error) as error:
        print("reconciliation rejected: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
