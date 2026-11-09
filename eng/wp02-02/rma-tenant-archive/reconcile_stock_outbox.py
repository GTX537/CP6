#!/usr/bin/env python3
"""Observe current Main Stock delivery from a pinned v10 bundle on its exact PostgreSQL source."""

import argparse
from contextlib import ExitStack, closing
import csv
import hashlib
from itertools import islice, zip_longest
import json
import os
from pathlib import Path
import re
import shutil
import sys
from tempfile import TemporaryDirectory

sys.dont_write_bytecode = True
import archive
from privacy_evidence import CSV_LIMIT_LOCK


FORMAT = "cp6-main-stock-outbox-reconciliation-v1"
REPORT = "reconciliation.jsonl"
BATCH_SIZE = 128
STATES = {0: "Pending", 1: "Dispatching", 2: "Published", 3: "DeadLettered"}
ENVELOPE_TEXT = ("MessageId", "TopicName", "PartitionKey", "PayloadSha256", "CorrelationId", "CausationId", "AggregateId")
STATE_TIMES = ("AvailableAtUtc", "LeaseExpiresAtUtc", "PublishedAtUtc", "DeadLetteredAtUtc")
PAYLOAD_SHA256 = hashlib.sha256(b"{}").hexdigest()


def member(table):
    return archive.STOCK_MEMBER_PREFIX + table.member


def checked_bundle(stock, bundle, tenant, target, digest, inputs, file_content_root):
    path = bundle / "manifest.json"
    if bundle.is_symlink() or path.is_symlink() or not path.is_file():
        raise archive.ArchiveError("manifest-member-rejected")
    pinned = path.read_bytes()
    if hashlib.sha256(pinned).hexdigest() != digest:
        raise archive.ArchiveError("manifest-digest-mismatch")
    manifest = archive.verify_bundle(bundle, str(tenant), file_content_root=file_content_root)
    if manifest != json.loads(pinned) or manifest["format"] != archive.FORMAT_WITH_STOCK_OUTBOX \
            or manifest.get("profile") != archive.PROFILE:
        raise archive.ArchiveError("stock-v10-bundle-rejected")
    if archive.PhysicalTarget.from_mapping(manifest["source"]) != target:
        raise archive.ArchiveError("source-target-mismatch")
    # Only these four members are consumed again. Pin their private copies after full package verification.
    for table in (stock.NATIVE, stock.SOURCE, stock.BINDING, stock.JOURNAL):
        name = member(table)
        private = inputs / name
        shutil.copyfile(bundle / name, private, follow_symlinks=False)
        if private.is_symlink() or archive.file_digest(private) != manifest["members"][name]:
            raise archive.ArchiveError("bundle-digest-mismatch")
    return manifest, archive.guid(manifest.get("installationId"))


def bytea_sha256(value):
    if not value.startswith("\\x"):
        raise archive.ArchiveError("stock-archived-original-rejected")
    return hashlib.sha256(bytes.fromhex(value[2:])).hexdigest()


def journal_original(stock, tenant, row):
    input_sha = row["input_sha256"]
    if row["tenant_id"] != str(tenant) or row["owner"] != stock.JOURNAL_OWNER \
            or row["state"] != "COMMITTED" or row["contract"] not in stock.JOURNAL_CONTRACTS \
            or input_sha != bytea_sha256(row["input_bytes"]):
        raise archive.ArchiveError("stock-archived-original-rejected")
    return dict(OperationId=str(archive.guid(row["operation_id"])), ActorId=str(archive.guid(row["actor_id"])),
                Contract=row["contract"], InputSha256=input_sha, ResultSha256=bytea_sha256(row["result_bytes"]),
                CommittedAtUtc=row["committed_at"])


def original_key(stock, tenant, route, native, source, binding, journal):
    operation = archive.guid(source["OperationId"])
    native_id = str(archive.guid(native["Id"]))
    message = "stock-hint:" + tenant.hex + ":" + operation.hex
    if (native["TenantId"], source["TenantId"]) != (str(tenant), str(tenant)) \
            or (source["NativeId"], binding["NativeId"]) != (native_id, native_id) \
            or (native["MessageId"], binding["MessageId"]) != (message, message) \
            or source["SourceOwner"] != stock.JOURNAL_OWNER or source["ResultSha256"] != journal["ResultSha256"] \
            or str(operation) != journal["OperationId"] or binding["RootId"] != route["id"] \
            or int(binding["AssignedEpoch"]) != route["epoch"] or binding["RawEnvelopeSha256"] != PAYLOAD_SHA256 \
            or native["Payload"] != "\\x7b7d" or native["PayloadSha256"] != PAYLOAD_SHA256 \
            or native["TopicName"] != stock.TOPIC or native["PartitionKey"] != str(tenant) \
            or (native["CorrelationId"], native["CausationId"], native["AggregateId"]) != (str(operation),) * 3 \
            or native["AggregateVersion"] != "1":
        raise archive.ArchiveError("stock-archived-binding-rejected")
    key = dict(journal, NativeId=native_id, RootId=route["id"], AssignedEpoch=route["epoch"],
               SourceOwner=source["SourceOwner"], CreatedAtUtc=native["CreatedAtUtc"], AggregateVersion=1,
               **{name: native[name] for name in ENVELOPE_TEXT})
    lease_token = native["LeaseToken"]
    status = int(native["Status"])
    if status not in STATES:
        raise archive.ArchiveError("stock-archived-status-rejected")
    archived = {name[0].lower() + name[1:]: native[name] or None for name in STATE_TIMES}
    archived.update(state=STATES[status], attemptCount=int(native["AttemptCount"]),
                    nativeXmin=native.get("source_xmin"), bindingXmin=binding.get("source_xmin"),
                    currentClaimId=binding["CurrentClaimId"] or None,
                    claimEpoch=int(binding["ClaimEpoch"]) if binding["ClaimEpoch"] else None,
                    claimStartedAtUtc=binding["ClaimStartedAtUtc"] or None,
                    hasLeaseOwner=bool(native["LeaseOwner"]), hasLeaseToken=bool(lease_token))
    lease_sha = hashlib.sha256(lease_token.encode("utf-8")).hexdigest() if lease_token else None
    return key, archived, lease_sha


def archived_rows(stock, inputs, tenant, route):
    # The native, source and binding COPY members are all ordered by their shared NativeId.
    # The original journal is ordered by operation. Retain its digests instead of the bytea bodies.
    with ExitStack() as files:
        def rows(table):
            return csv.DictReader(files.enter_context((inputs / member(table)).open(encoding="utf-8", newline="")),
                                  strict=True)

        journal = {}
        for row in rows(stock.JOURNAL):
            proof = journal_original(stock, tenant, row)
            if proof["OperationId"] in journal:
                raise archive.ArchiveError("stock-archived-original-rejected")
            journal[proof["OperationId"]] = proof
        for native, source, binding in zip_longest(rows(stock.NATIVE), rows(stock.SOURCE), rows(stock.BINDING)):
            if native is None or source is None or binding is None:
                raise archive.ArchiveError("stock-archived-binding-rejected")
            proof = journal.pop(source["OperationId"], None)
            if proof is None:
                raise archive.ArchiveError("stock-archived-original-rejected")
            yield original_key(stock, tenant, route, native, source, binding, proof)
        if journal:
            raise archive.ArchiveError("stock-archived-original-rejected")


def lookup(query, stock, tenant, route, bindings):
    relation = stock.relation
    literal = archive.literal
    tenant_sql = literal(tenant) + "::uuid"
    root_sql = literal(route["id"])
    envelope_changed = " OR ".join(
        f"""convert_to(n."{name}",'UTF8') IS DISTINCT FROM convert_to(wanted.binding->>'{name}','UTF8')"""
        for name in ENVELOPE_TEXT)
    return query(f"""
        SELECT coalesce(json_agg(CASE
            WHEN (keyed."Id" IS NOT NULL AND keyed."TenantId"<>{tenant_sql})
                OR (n."Id" IS NOT NULL AND n."TenantId"<>{tenant_sql})
                THEN json_build_object('decision','BLOCKED','reason','GLOBAL_KEY_CONFLICT')
            WHEN keyed."Id" IS NULL AND n."Id" IS NULL AND s."NativeId" IS NULL
                AND b."NativeId" IS NULL AND bound_key."NativeId" IS NULL
                THEN json_build_object('decision','UNKNOWN','reason','ORIGINAL_KEY_NOT_FOUND')
            WHEN n."Id" IS NULL OR keyed."Id" IS DISTINCT FROM n."Id" OR s."NativeId" IS DISTINCT FROM n."Id"
                OR bound_key."NativeId" IS DISTINCT FROM n."Id" OR {envelope_changed}
                OR n."AggregateVersion" IS DISTINCT FROM (wanted.binding->>'AggregateVersion')::int
                OR n."CreatedAtUtc" IS DISTINCT FROM (wanted.binding->>'CreatedAtUtc')::timestamptz
                OR encode(sha256(n."Payload"),'hex') IS DISTINCT FROM wanted.binding->>'PayloadSha256'
                OR b."RootId" COLLATE "C" IS DISTINCT FROM {root_sql}
                OR b."MessageId" COLLATE "C" IS DISTINCT FROM wanted.binding->>'MessageId'
                OR b."RawEnvelopeSha256" COLLATE "C" IS DISTINCT FROM wanted.binding->>'PayloadSha256'
                OR b."AssignedEpoch" IS DISTINCT FROM (wanted.binding->>'AssignedEpoch')::bigint
                OR b."ClaimEpoch"<>r."CurrentEpoch"
                THEN json_build_object('decision','BLOCKED','reason','BINDING_CONFLICT')
            WHEN s."SourceOwner" COLLATE "C" IS DISTINCT FROM {literal(stock.JOURNAL_OWNER)}
                OR s."ResultSha256" COLLATE "C" IS DISTINCT FROM wanted.binding->>'ResultSha256'
                OR j.state IS DISTINCT FROM 'COMMITTED'
                OR j.actor_id IS DISTINCT FROM (wanted.binding->>'ActorId')::uuid
                OR j.contract IS DISTINCT FROM wanted.binding->>'Contract'
                OR j.committed_at IS DISTINCT FROM (wanted.binding->>'CommittedAtUtc')::timestamptz
                OR j.input_sha256 IS DISTINCT FROM wanted.binding->>'InputSha256'
                OR encode(sha256(j.input_bytes),'hex') IS DISTINCT FROM wanted.binding->>'InputSha256'
                OR encode(sha256(j.result_bytes),'hex') IS DISTINCT FROM wanted.binding->>'ResultSha256'
                THEN json_build_object('decision','BLOCKED','reason','ORIGINAL_OPERATION_CONFLICT')
            WHEN r."Id" IS NULL OR a."RootId" COLLATE "C" IS DISTINCT FROM {root_sql}
                OR a."Enabled" IS DISTINCT FROM true OR ten."Enable" IS DISTINCT FROM true
                OR r."TenantId" IS DISTINCT FROM {tenant_sql}
                OR r."OwnerId" COLLATE "C" IS DISTINCT FROM {literal(route["owner"])}
                OR r."QueueDomainId" COLLATE "C" IS DISTINCT FROM {literal(route["domain"])}
                OR r."RouteId" COLLATE "C" IS DISTINCT FROM {literal(route["route"])}
                OR r."ProfileId" COLLATE "C" IS DISTINCT FROM {literal(route["profile"])}
                OR r."ProfileVersion" IS DISTINCT FROM {route["profileVersion"]}
                OR r."CurrentEpoch" IS DISTINCT FROM {route["epoch"]}::bigint OR r."Phase" IS DISTINCT FROM 0
                OR r."LastSwitchId" IS NOT NULL OR r."LockKey" IS DISTINCT FROM {route["lockKey"]}::bigint
                OR r."PublisherSetReference" COLLATE "C" IS DISTINCT FROM {literal(route["publisher"])}
                OR a."PublisherReference" COLLATE "C" IS DISTINCT FROM {literal(route["publisher"])}
                OR a."PolicyVersion" COLLATE "C" IS DISTINCT FROM {literal(route["policy"])}
                THEN json_build_object('decision','BLOCKED','reason','ROUTE_AUTHORITY_CONFLICT')
            WHEN n."Status" NOT BETWEEN 0 AND 3 OR (n."Status"=3 AND n."DeadLetteredAtUtc" IS NULL)
                THEN json_build_object('decision','BLOCKED','reason','CURRENT_STATE_INVALID')
            WHEN n."Status"=2 AND (n."PublishedAtUtc" IS NULL OR done.originals IS NULL
                OR json_array_length(done.originals)<>1)
                THEN json_build_object('decision','BLOCKED','reason','PUBLISHED_ORIGINAL_REJECTED')
            ELSE json_build_object('decision','PRESERVE_CURRENT','current',json_build_object(
                'status',n."Status",'attemptCount',n."AttemptCount",
                'availableAtUtc',n."AvailableAtUtc"::text,'leaseExpiresAtUtc',n."LeaseExpiresAtUtc"::text,
                'publishedAtUtc',n."PublishedAtUtc"::text,'deadLetteredAtUtc',n."DeadLetteredAtUtc"::text,
                'nativeXmin',n.xmin::text,'bindingXmin',b.xmin::text,
                'currentClaimId',b."CurrentClaimId",'claimEpoch',b."ClaimEpoch",
                'claimStartedAtUtc',b."ClaimStartedAtUtc"::text,'routeEpoch',r."CurrentEpoch",
                'hasLeaseOwner',nullif(n."LeaseOwner",'') IS NOT NULL,
                'hasLeaseToken',nullif(n."LeaseToken",'') IS NOT NULL,
                'hasUnexpiredDispatchLease',coalesce(n."Status"=1 AND nullif(n."LeaseOwner",'') IS NOT NULL
                    AND nullif(n."LeaseToken",'') IS NOT NULL AND n."LeaseExpiresAtUtc">transaction_timestamp(),false),
                'leaseTokenChanged',encode(sha256(convert_to(nullif(n."LeaseToken",''),'UTF8')),'hex')
                    IS DISTINCT FROM wanted.binding->>'LeaseTokenSha256',
                'publishedOriginal',done.originals->0))
            END ORDER BY wanted.ordinal), '[]'::json)
        FROM jsonb_array_elements({literal(json.dumps(bindings))}::jsonb) WITH ORDINALITY AS wanted(binding,ordinal)
        -- The global native varchar key must use its installed default collation and index.
        LEFT JOIN {relation(stock.NATIVE)} keyed ON keyed."MessageId"=(wanted.binding->>'MessageId')::varchar
        LEFT JOIN {relation(stock.NATIVE)} n ON n."Id"=(wanted.binding->>'NativeId')::uuid
        LEFT JOIN {relation(stock.SOURCE)} s ON s."TenantId"={tenant_sql}
            AND s."OperationId"=(wanted.binding->>'OperationId')::uuid
        LEFT JOIN {relation(stock.BINDING)} b ON b."NativeId"=n."Id"
        LEFT JOIN {relation(stock.BINDING)} bound_key ON bound_key."MessageId"=(wanted.binding->>'MessageId')
        LEFT JOIN {relation(stock.JOURNAL)} j ON j.tenant_id={tenant_sql} AND j.owner={literal(stock.JOURNAL_OWNER)}
            AND j.operation_id=(wanted.binding->>'OperationId')::uuid
        LEFT JOIN {relation(stock.ROUTE_TABLE)} r ON r."Id"={root_sql}
        LEFT JOIN {relation(stock.AUTHORITY)} a ON a."TenantId"={tenant_sql}
        LEFT JOIN public."Sys_Tenants" ten ON ten."Id"={tenant_sql}
        LEFT JOIN LATERAL (
            SELECT json_agg(proof.original) AS originals FROM (
                SELECT json_build_object('claimRequestId',t."RequestId",'claimReceiptRef',t."ReceiptReference",
                    'mutationId',m."MutationId",'completionReceiptRef',m."Output"::jsonb->>'ReceiptReference',
                    'ticketSha256',encode(sha256(convert_to(t."Ticket",'UTF8')),'hex'),
                    'inputSha256',encode(sha256(convert_to(m."Input",'UTF8')),'hex'),
                    'outputSha256',encode(sha256(convert_to(m."Output",'UTF8')),'hex'),
                    'sendAttempt',CASE WHEN sent."ClaimId" IS NOT NULL THEN json_build_object(
                        'claimId',sent."ClaimId",
                        'ticketSha256',encode(sha256(convert_to(sent."OriginalTicket",'UTF8')),'hex')) END) AS original
                FROM {relation(stock.TICKET)} t
                JOIN {relation(stock.CLAIM)} c ON (c."RootId",c."RequestId")=(t."RootId",t."RequestId")
                JOIN {relation(stock.MUTATION)} m ON (m."RootId",m."NativeId",m."MutationId")=
                    (t."RootId",t."NativeId",t."ReceiptReference")
                LEFT JOIN {relation(stock.SEND)} sent ON (sent."RootId",sent."MutationId")=(m."RootId",m."MutationId")
                WHERE n."Status"=2 AND n."TenantId"={tenant_sql} AND t."NativeId"=n."Id" AND t."RootId"={root_sql}
                    AND c."Tickets"::jsonb @> jsonb_build_array(t."Ticket"::jsonb)
                    AND t."Ticket"::jsonb->>'NativeId'=n."Id"::text
                    AND (t."Ticket"::jsonb->>'MessageId') COLLATE "C"=n."MessageId" COLLATE "C"
                    AND t."Ticket"::jsonb->>'RawEnvelopeSha256'=b."RawEnvelopeSha256"
                    AND t."Ticket"::jsonb->>'ClaimReceiptRef'=t."ReceiptReference"::text
                    AND m."Output"::jsonb->>'MutationId'=m."MutationId"::text
                    AND m."Output"::jsonb->>'Transition'='Published'
                    AND (sent."ClaimId" IS NULL OR (sent."ClaimId"::text=t."Ticket"::jsonb->>'ClaimId'
                        AND sent."OriginalTicket" COLLATE "C"=t."Ticket" COLLATE "C"))
                LIMIT 2
            ) proof
        ) done ON true;
    """, failure="stock-current-read-failed (see native logs)")


def observe(query, stock, tenant, route, batch):
    results = lookup(query, stock, tenant, route, [dict(key, LeaseTokenSha256=lease) for key, _, lease in batch])
    if len(results) != len(batch):
        raise archive.ArchiveError("stock-key-cardinality-rejected")
    for (key, archived, _), result in zip(batch, results):
        if "current" in result:
            current = result["current"]
            current["state"] = STATES[current.pop("status")]
        yield dict(type="observation", archivedBinding=key, archived=archived, **result)


def reconcile(pg_bin, target, username, tenant, bundle, digest, output, file_content_root):
    if not re.fullmatch(r"[0-9a-fA-F]{64}", digest):
        raise archive.ArchiveError("invalid-manifest-digest")
    if not username or "\0" in username:
        raise archive.ArchiveError("invalid-backup-username")
    digest = digest.lower()
    stock = archive.stock_outbox_module()
    archive.private_directory(output)
    pending, report = output / (REPORT + ".pending"), output / REPORT
    counts = dict(PRESERVE_CURRENT=0, BLOCKED=0, UNKNOWN=0)
    try:
        with TemporaryDirectory(dir=output) as private_inputs:
            inputs = Path(private_inputs)
            manifest, installation = checked_bundle(stock, bundle, tenant, target, digest, inputs, file_content_root)
            section = manifest["stockOutbox"]
            route = section["root"]
            with pending.open("x", encoding="utf-8", newline="\n") as stream, \
                    archive.snapshot_session(pg_bin, archive.connection_args(target, username), os.environ, output) as query:
                def record(value):
                    stream.write(json.dumps(value) + "\n")

                ready = archive.check_source(query, target, installation, tenant)
                if ready["migration"] != manifest["migration"]:
                    raise archive.ArchiveError("stock-source-lineage-mismatch")
                if stock.source_metadata(query) != section["databaseCollation"]:
                    raise archive.ArchiveError("stock-source-collation-mismatch")
                current = query(stock.COPY_SETTINGS + """
                    SELECT json_build_object('backendPid',pg_backend_pid(),'watermark',pg_current_snapshot()::text,
                        'capturedAt',transaction_timestamp(),'readOnly',current_setting('transaction_read_only')::bool,
                        'isolation',current_setting('transaction_isolation'));
                """)
                stock.check_layout(query, ready["ownerOid"])
                record(dict(type="header", format=FORMAT, tenantId=str(tenant), installationId=str(installation),
                            source=target.mapping(), manifestSha256=digest, root=route,
                            archived=manifest["snapshot"], current=current))
                with CSV_LIMIT_LOCK:
                    previous = csv.field_size_limit(2_147_483_647)
                    try:
                        with closing(archived_rows(stock, inputs, tenant, route)) as rows:
                            if not section["tables"][member(stock.NATIVE)]["count"]:
                                lookup(query, stock, tenant, route, [])
                            while batch := list(islice(rows, BATCH_SIZE)):
                                for observation in observe(query, stock, tenant, route, batch):
                                    counts[observation["decision"]] += 1
                                    record(observation)
                    finally:
                        csv.field_size_limit(previous)
                result = "BLOCKED" if counts["BLOCKED"] else "UNKNOWN" if counts["UNKNOWN"] else "OBSERVED"
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
        return 0 if result == "OBSERVED" else 2
    except (archive.ArchiveError, OSError, ValueError, csv.Error) as error:
        print("reconciliation rejected: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
