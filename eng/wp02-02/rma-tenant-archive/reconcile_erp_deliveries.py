#!/usr/bin/env python3
"""Observe current ERP delivery keys from a pinned v8 archive on its exact PostgreSQL source."""

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
import erp_integration


FORMAT = "cp6-erp-delivery-reconciliation-v1"
REPORT = "reconciliation.jsonl"
BATCH_SIZE = 128
COMMAND, OUTBOX, BRIDGE = (next(table for table in erp_integration.TABLES if table.name == name)
                           for name in ("CommandInbox", "Cp6_OutboxMessage", "OrderBridgeDispatch"))
TEXT_BINDINGS = {
    COMMAND: ("MessageId", "EventType", "PayloadSha256"),
    OUTBOX: ("MessageId", "TopicName", "PartitionKey", "PayloadSha256", "CorrelationId", "CausationId", "AggregateId"),
    BRIDGE: ("OrderKey",),
}
ORIGIN_TIME = {COMMAND: "ReceivedAtUtc", OUTBOX: "CreatedAtUtc"}
TIMES = {
    COMMAND: ("ReceivedAtUtc", "ProcessedAtUtc", "RetryAtUtc", "ReplayedAtUtc"),
    OUTBOX: ("CreatedAtUtc", "AvailableAtUtc", "LeaseExpiresAtUtc", "PublishedAtUtc", "DeadLetteredAtUtc"),
    BRIDGE: ("AvailableAtUtc", "CompletedAtUtc", "LeaseExpiresAtUtc"),
}
STATES = {
    COMMAND: {0: "Processing", 1: "Processed", 2: "DeadLettered"},
    OUTBOX: {0: "Pending", 1: "Dispatching", 2: "Published", 3: "DeadLettered"},
}


def checked_bundle(bundle, tenant, target, digest, inputs, file_content_root):
    manifest_path = bundle / "manifest.json"
    if bundle.is_symlink() or manifest_path.is_symlink() or not manifest_path.is_file():
        raise archive.ArchiveError("manifest-member-rejected")
    manifest_bytes = manifest_path.read_bytes()
    if hashlib.sha256(manifest_bytes).hexdigest() != digest:
        raise archive.ArchiveError("manifest-digest-mismatch")
    manifest = archive.verify_bundle(bundle, str(tenant), file_content_root=file_content_root)
    if manifest != json.loads(manifest_bytes) or manifest["format"] != erp_integration.FORMAT \
            or manifest.get("profile") != archive.PROFILE:
        raise archive.ArchiveError("erp-v8-bundle-rejected")
    if archive.PhysicalTarget.from_mapping(manifest.get("source")) != target:
        raise archive.ArchiveError("source-target-mismatch")
    # Consume private, verified bytes even if the original member paths later change.
    for table in TEXT_BINDINGS:
        member = inputs / table.member
        shutil.copyfile(bundle / table.member, member, follow_symlinks=False)
        if member.is_symlink() or archive.file_digest(member) != manifest["members"][table.member]:
            raise archive.ArchiveError("bundle-digest-mismatch")
    return manifest, archive.guid(manifest.get("installationId"))


def report_field(column):
    return column[0].lower() + column[1:]


def state(table, facts):
    if not re.fullmatch(r"\\x[0-9a-f]{16}", facts["rowVersion"]):
        raise archive.ArchiveError("erp-row-version-rejected")
    if table is BRIDGE:
        if facts["completedAtUtc"] is not None:
            return "Completed"
        # ErpOrderBridgeWorker stops polling after its tenth attempt.
        return "AttemptsExhausted" if facts["attemptCount"] >= 10 else "Pending"
    try:
        return STATES[table][facts.pop("status")]
    except KeyError as error:
        raise archive.ArchiveError("erp-delivery-status-rejected") from error


def archived_rows(path, table, tenant, count):
    with path.open(encoding="utf-8", newline="") as file:
        rows = csv.DictReader(file, strict=True)
        if rows.fieldnames != list(table.columns):
            raise archive.ArchiveError("erp-member-header-rejected")
        seen = 0
        for row in rows:
            seen += 1
            if seen > count or None in row or None in row.values() or row["TenantId"] != str(tenant):
                raise archive.ArchiveError("erp-member-row-rejected")
            binding = {column: row[column] for column in TEXT_BINDINGS[table]}
            facts = {report_field(column): row[column] or None for column in TIMES[table]}
            facts.update(attemptCount=int(row["AttemptCount"]), rowVersion=row["RowVersion"])
            if table in ORIGIN_TIME:
                digest = binding["PayloadSha256"]
                payload = row["Payload"]
                if not re.fullmatch(r"[0-9a-f]{64}", digest) or not payload.startswith("\\x") \
                        or hashlib.sha256(bytes.fromhex(payload[2:])).hexdigest() != digest:
                    raise archive.ArchiveError("erp-payload-digest-rejected")
                binding[ORIGIN_TIME[table]] = row[ORIGIN_TIME[table]]
                binding["AggregateVersion"] = int(row["AggregateVersion"])
                facts["status"] = int(row["Status"])
            if table is COMMAND:
                binding["AggregateId"] = row["AggregateId"]
                facts["conflictCount"] = int(row["ConflictCount"])
            if table is OUTBOX:
                binding["Id"] = row["Id"]
                facts.update(hasLeaseOwner=bool(row["LeaseOwner"]), hasLeaseToken=bool(row["LeaseToken"]))
            facts["state"] = state(table, facts)
            lease = row["LeaseToken"] if table is OUTBOX else ""
            yield binding, facts, hashlib.sha256(lease.encode("utf-8")).hexdigest() if lease else None
        if seen != count:
            raise archive.ArchiveError("erp-member-count-mismatch")


def lookup(query, table, tenant, bindings):
    # Native bpchar equality finds a key first. Raw spelling and payload are checked after tenant binding.
    column = TEXT_BINDINGS[table][0]
    key = f'''m."{column}"=(wanted.binding->>'{column}')::bpchar'''
    if table is BRIDGE:
        key += f' AND m."TenantId"={archive.literal(tenant)}::uuid'
    changed = [f'''bpcharsend(m."{column}") IS DISTINCT FROM convert_to(wanted.binding->>'{column}','UTF8')'''
               for column in TEXT_BINDINGS[table]]
    facts = [f'''{archive.literal(report_field(column))},m."{column}"::text''' for column in TIMES[table]]
    facts.extend(["'attemptCount',m.\"AttemptCount\"", "'rowVersion',m.\"RowVersion\"::text"])
    if table in ORIGIN_TIME:
        origin = ORIGIN_TIME[table]
        changed.extend([
            f'''m."{origin}" IS DISTINCT FROM (wanted.binding->>'{origin}')::timestamptz''',
            '''m."AggregateVersion" IS DISTINCT FROM (wanted.binding->>'AggregateVersion')::int''',
            '''sha256(m."Payload") IS DISTINCT FROM decode(wanted.binding->>'PayloadSha256','hex')''',
        ])
        facts.append("'status',m.\"Status\"")
    if table is COMMAND:
        changed.append('''m."AggregateId" IS DISTINCT FROM (wanted.binding->>'AggregateId')::uuid''')
        facts.append("'conflictCount',m.\"ConflictCount\"")
    if table is OUTBOX:
        changed.append('''m."Id" IS DISTINCT FROM (wanted.binding->>'Id')::uuid''')
        facts.append("""
            'hasLeaseOwner',lease.owner_present,'hasLeaseToken',lease.token IS NOT NULL,
            'hasUnexpiredDispatchLease',coalesce(m."Status"=1 AND lease.owner_present AND lease.token IS NOT NULL
                AND m."LeaseExpiresAtUtc">transaction_timestamp(),false),
            'leaseTokenChanged',sha256(lease.token) IS DISTINCT FROM decode(wanted.binding->>'LeaseTokenSha256','hex')
        """)
    lease = """
        CROSS JOIN LATERAL (SELECT nullif(bpcharsend(m."LeaseOwner"),''::bytea) IS NOT NULL AS owner_present,
                                   nullif(bpcharsend(m."LeaseToken"),''::bytea) AS token) lease
    """ if table is OUTBOX else ""
    return query(f"""
        SELECT coalesce(json_agg(CASE
            WHEN m."TenantId" IS NULL THEN json_build_object('decision','UNKNOWN','reason','ORIGINAL_KEY_NOT_FOUND')
            WHEN m."TenantId" IS DISTINCT FROM {archive.literal(tenant)}::uuid
                THEN json_build_object('decision','BLOCKED','reason','GLOBAL_KEY_CONFLICT')
            WHEN {' OR '.join(changed)} THEN json_build_object('decision','BLOCKED','reason','BINDING_CONFLICT')
            ELSE json_build_object('decision','PRESERVE_CURRENT','current',json_build_object({','.join(facts)}))
            END ORDER BY wanted.ordinal), '[]'::json)
        FROM jsonb_array_elements({archive.literal(json.dumps(bindings))}::jsonb) WITH ORDINALITY AS wanted(binding,ordinal)
        LEFT JOIN {erp_integration.relation(table, erp_integration.SCHEMA)} m ON {key}
        {lease};
    """, failure="erp-current-read-failed (see native logs)")


def reconcile(pg_bin, target, username, tenant, bundle, digest, output, file_content_root):
    if not re.fullmatch(r"[0-9a-fA-F]{64}", digest):
        raise archive.ArchiveError("invalid-manifest-digest")
    digest = digest.lower()
    archive.private_directory(output)
    pending = output / (REPORT + ".pending")
    report = output / REPORT
    counts = dict(PRESERVE_CURRENT=0, BLOCKED=0, UNKNOWN=0)
    # Native bytea is emitted as hex. Stream one archive row at a time without narrowing its accepted length.
    csv.field_size_limit(2_147_483_647)
    try:
        with TemporaryDirectory(dir=output) as private_inputs:
            inputs = Path(private_inputs)
            manifest, installation = checked_bundle(bundle, tenant, target, digest, inputs, file_content_root)
            with pending.open("x", encoding="utf-8", newline="\n") as stream, \
                    archive.snapshot_session(pg_bin, archive.connection_args(target, username), os.environ, output) as query:
                def record(value):
                    stream.write(json.dumps(value) + "\n")

                ready = archive.check_source(query, target, installation, tenant)
                if ready["migration"] != manifest.get("migration"):
                    raise archive.ArchiveError("erp-source-lineage-mismatch")
                erp_integration.check_layout(query, ready["ownerOid"])
                current = query(erp_integration.COPY_SETTINGS + """
                    SELECT json_build_object('backendPid',pg_backend_pid(),'watermark',pg_current_snapshot()::text,
                        'capturedAt',transaction_timestamp(),'readOnly',current_setting('transaction_read_only')::bool,
                        'isolation',current_setting('transaction_isolation'));
                """)
                record(dict(type="header", format=FORMAT, tenantId=str(tenant), installationId=str(installation),
                            source=target.mapping(), manifestSha256=digest, archived=manifest["snapshot"], current=current))
                for table in TEXT_BINDINGS:
                    count = manifest["erpIntegration"]["tables"][table.name]["count"]
                    with closing(archived_rows(inputs / table.member, table, tenant, count)) as rows:
                        if count == 0:
                            # Check native read authority even when the pinned tenant has no keys here.
                            lookup(query, table, tenant, [])
                        while batch := list(islice(rows, BATCH_SIZE)):
                            bindings = [dict(binding, LeaseTokenSha256=lease) for binding, _, lease in batch]
                            results = lookup(query, table, tenant, bindings)
                            if len(results) != len(batch):
                                raise archive.ArchiveError("erp-key-cardinality-rejected")
                            for (binding, archived, _), result in zip(batch, results):
                                if "current" in result:
                                    facts = result["current"]
                                    facts["state"] = state(table, facts)
                                counts[result["decision"]] += 1
                                record(dict(type="observation", table=table.name, archivedBinding=binding,
                                            archived=archived, **result))
                result = "BLOCKED" if counts["BLOCKED"] or counts["UNKNOWN"] else "OBSERVED"
                record(dict(type="summary", result=result, counts=counts, recoveryReady=False))
        pending.replace(report)
    finally:
        pending.unlink(missing_ok=True)
    return report, result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("pg-bin", "target-file", "bundle", "output", "file-content-root"):
        parser.add_argument("--" + name, type=Path, required=True)
    for name in ("username", "tenant", "manifest-sha256"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    try:
        target = archive.PhysicalTarget.from_mapping(json.loads(args.target_file.read_text(encoding="utf-8-sig")))
        report, result = reconcile(args.pg_bin.resolve(strict=True), target, args.username, archive.guid(args.tenant),
                                   args.bundle.absolute(), args.manifest_sha256, args.output.absolute(), args.file_content_root)
        print(json.dumps(dict(report=str(report), result=result, recoveryReady=False)))
        return 2 if result == "BLOCKED" else 0
    except (archive.ArchiveError, OSError, ValueError, csv.Error) as error:
        print("reconciliation rejected: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
