#!/usr/bin/env python3
"""Export or verify a private, one-tenant snapshot of the Main Stock Outbox."""

import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from typing import NamedTuple

sys.dont_write_bytecode = True
if "archive" not in sys.modules:
    sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "rma-tenant-archive"))
import archive
from identity_priority import COPY_SETTINGS, native_catalog
from privacy_evidence import CSV_LIMIT_LOCK


FORMAT = "cp6-main-stock-outbox-archive-v1"
SCHEMA = "cp6_stock_outbox_v1"
JOURNAL_SCHEMA = "cp6_main_owner_v1"
HISTORY = "__EFMigrationsHistory"
MIGRATIONS = ("20261022061500_MainOwnerOperationsV1", "20261023003000_MainStockOutboxV1",
              "20261026001000_MainStockCreationOutboxV2")
OWNER = "Main.Wms.InventoryNotification"
DOMAIN = "Main.Stock.Notification.v1"
ROUTE = "InventoryInvalidatedV1"
PUBLISHER = "Main.SignalR.InventoryInvalidated.v1"
POLICY = "Main.StockOutbox.Technical.v1"
TOPIC = "main.inventory.invalidated.v1"
PROFILE = "cp6-outbox-lease-v2"
JOURNAL_OWNER = "Main.Wms.StockMovement"
JOURNAL_CONTRACTS = ("Main.Wms.StockOperation.v1", "Main.Wms.StockOperation.v2")


class Table(NamedTuple):
    name: str
    fields: tuple
    keys: tuple
    scope: str
    xmin: bool

    @property
    def member(self):
        return self.name + ".csv"

    @property
    def columns(self):
        return tuple(name for name, _ in self.fields) + (("source_xmin",) if self.xmin else ())


def table(name, fields, key, scope, *unique, xmin=False):
    native = []
    for field in fields.split():
        column, _, data_type = field.partition(":")
        native.append((column, data_type or "text"))
    return Table(name, tuple(native), tuple(tuple(columns.split()) for columns in (key, *unique)), scope, xmin)


AUTHORITY = table("Authority", "RootId TenantId:uuid Enabled:bool PolicyVersion PublisherReference", "RootId", "tenant", "TenantId", xmin=True)
NATIVE = table("Cp6_OutboxMessage", """Id:uuid MessageId:varchar TenantId:uuid TopicName:varchar PartitionKey:varchar
               Payload:bytea PayloadSha256:varchar CorrelationId:varchar CausationId:varchar AggregateId:varchar
               AggregateVersion:int4 Status:int4 AttemptCount:int4 CreatedAtUtc:timestamptz AvailableAtUtc:timestamptz
               LeaseOwner:varchar LeaseToken:varchar LeaseExpiresAtUtc:timestamptz PublishedAtUtc:timestamptz
               DeadLetteredAtUtc:timestamptz LastErrorCode:varchar SupportReference:varchar""",
               "Id", "tenant", "MessageId", xmin=True)
SOURCE = table("Source", "NativeId:uuid TenantId:uuid OperationId:uuid SourceOwner ResultSha256", "NativeId", "tenant", "TenantId OperationId")
BINDING = table("Cp6_OutboxBindingV2", """NativeId:uuid RootId MessageId RawEnvelopeSha256 AssignedEpoch:int8
                CurrentClaimId:uuid ClaimEpoch:int8 ClaimStartedAtUtc:timestamptz""", "NativeId", "root", "MessageId", xmin=True)
ROUTE_TABLE = table("Cp6_OutboxRouteV2", """Id OwnerId TenantId:uuid QueueDomainId RouteId ProfileId ProfileVersion:int4
                    CurrentEpoch:int8 Phase:int4 LockKey:int8 PublisherSetReference LastSwitchId:uuid""",
                    "Id", "tenant", "LockKey", "OwnerId TenantId QueueDomainId RouteId", xmin=True)
RUN = table("DispatchRun", "RootId RequestId:uuid CreatedAtUtc:timestamptz CompletedAtUtc:timestamptz", "RootId RequestId", "root")
CLAIM = table("Cp6_OutboxClaimReceiptV2", "RootId RequestId:uuid Input Tickets WriterTransaction", "RootId RequestId", "root")
TICKET = table("Cp6_OutboxClaimTicketV2", "RootId RequestId:uuid NativeId:uuid ReceiptReference:uuid Ticket WriterTransaction",
               "RootId RequestId NativeId", "root", "ReceiptReference")
MUTATION = table("Cp6_OutboxMutationReceiptV2", "RootId MutationId:uuid NativeId:uuid Input Output WriterTransaction",
                 "RootId MutationId", "root")
SEND = table("Cp6_OutboxSendAttemptV2", "RootId ClaimId:uuid MutationId:uuid OriginalTicket PolicyVersion WriterTransaction",
             "RootId ClaimId", "root", "RootId MutationId")
AUDIT = table("Cp6_OutboxAuditV2", "Id:uuid RootId NativeId:uuid Kind Evidence AtUtc:timestamptz", "Id", "root")
SEAL = table("Cp6_OutboxTokenSealV2", "NativeId:uuid WriterTransaction", "NativeId WriterTransaction", "native")
DEAD_LETTER = table("Cp6_DeadLetterRecord", """Id:uuid Direction:int4 TenantId:uuid MessageId:varchar ConsumerName:varchar
                     PayloadSha256:varchar ErrorCode:varchar SupportReference:varchar CreatedAtUtc:timestamptz
                     ReplayedAtUtc:timestamptz ReplayReasonCode:varchar""", "Id", "tenant", xmin=True)
JOURNAL = table("operations", """tenant_id:uuid owner operation_id:uuid actor_id:uuid contract state input_bytes:bytea
                input_sha256 result_bytes:bytea object_ids:_uuid audit_json:jsonb committed_at:timestamptz""",
                "tenant_id owner operation_id", "journal")
STOCK_TABLES = (AUTHORITY, ROUTE_TABLE, NATIVE, SOURCE, BINDING, RUN, CLAIM, TICKET, MUTATION, SEND, AUDIT, SEAL, DEAD_LETTER)
TABLES = (*STOCK_TABLES, JOURNAL)
MEMBERS = tuple(item.member for item in TABLES)


def ident(value):
    return '"' + value + '"'


def relation(item):
    schema = JOURNAL_SCHEMA if item is JOURNAL else SCHEMA
    return schema + "." + ident(item.name)


def root(tenant):
    value = json.dumps([OWNER, str(tenant), DOMAIN, ROUTE], separators=(",", ":")).encode("utf-8")
    root_id = hashlib.sha256(value).hexdigest()
    lock = (0x4350364F << 32) | int.from_bytes(hashlib.sha256(root_id.encode("utf-8")).digest()[:4], "big")
    return dict(id=root_id, owner=OWNER, domain=DOMAIN, route=ROUTE, profile=PROFILE, profileVersion=1,
                epoch=1, phase="Active", lockKey=lock, publisher=PUBLISHER, policy=POLICY)


def rows_sql(item, tenant, root_id, *, ordered=True):
    tenant = archive.literal(tenant) + "::uuid"
    if item.scope == "tenant":
        selected = f'r."TenantId"={tenant}'
    elif item.scope == "root":
        selected = f'r."RootId"={archive.literal(root_id)}'
    elif item is SEAL:
        selected = f'EXISTS(SELECT FROM {relation(NATIVE)} n WHERE n."Id"=r."NativeId" AND n."TenantId"={tenant})'
    else:
        selected = f'''r.tenant_id={tenant} AND r.owner={archive.literal(JOURNAL_OWNER)} AND EXISTS(
            SELECT FROM {relation(SOURCE)} s WHERE (s."TenantId",s."SourceOwner",s."OperationId")=
                (r.tenant_id,r.owner,r.operation_id))'''
    fields = ",".join("r." + ident(name) for name, _ in item.fields)
    if item.xmin:
        fields += ',r.xmin::text AS "source_xmin"'
    order = " ORDER BY " + ",".join("r." + ident(name) for name in item.keys[0]) if ordered else ""
    return f"SELECT {fields} FROM {relation(item)} r WHERE {selected}{order}"


def check_target(query, target):
    actual = query("""
        BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
        SET LOCAL search_path=pg_catalog;
        SET LOCAL standard_conforming_strings=on;
        SET LOCAL lock_timeout='5s';
        SET LOCAL statement_timeout='45s';
        SET LOCAL row_security=off;
        SELECT json_build_object('source',json_build_object(
            'Server',host(inet_server_addr()),'Port',inet_server_port(),'Database',d.datname,
            'DatabaseOid',d.oid::bigint,'SystemIdentifier',p.system_identifier::text),
            'ownerOid',d.datdba::bigint,'primary',NOT pg_is_in_recovery())
        FROM pg_database d CROSS JOIN pg_control_system() p WHERE d.datname=current_database();
    """)
    if actual["source"] != target.mapping() or not actual["primary"]:
        raise archive.ArchiveError("stock-source-target-rejected")
    return actual["ownerOid"], source_metadata(query)


def source_metadata(query):
    """Read the Stock source-specific boundary inside an already pinned snapshot."""
    actual = query("""
        SELECT json_build_object(
            'restoreCandidate',coalesce(shobj_description(d.oid,'pg_database'),'') LIKE 'CP6-PRIVACY-RESTORE-CANDIDATE/v1/%',
            'encoding',current_setting('server_encoding'),
            'collation',json_build_object('provider',d.datlocprovider,'collate',d.datcollate,'ctype',d.datctype,
                'locale',coalesce(to_jsonb(d)->>'datlocale',to_jsonb(d)->>'daticulocale')))
        FROM pg_database d WHERE d.datname=current_database();
    """)
    if actual["restoreCandidate"] or actual["encoding"] != "UTF8":
        raise archive.ArchiveError("stock-source-target-rejected")
    return actual["collation"]


def check_layout(query, owner_oid):
    for schema, tables, migration in ((SCHEMA, STOCK_TABLES, MIGRATIONS[1]), (JOURNAL_SCHEMA, (JOURNAL,), MIGRATIONS[0])):
        found = native_catalog(query, owner_oid, tables, schema=schema, history=HISTORY, migration=migration)
        if not found["migration"] or set(found["tables"]) != {item.name for item in tables}:
            raise archive.ArchiveError("stock-native-layout-rejected")
        for item in tables:
            entry = found["tables"][item.name]
            keys = [(tuple(key["columns"]), key["primary"]) for key in entry["keys"] or []]
            expected = [(columns, position == 0) for position, columns in enumerate(item.keys)]
            if entry["columns"] != [list(field) for field in item.fields] or sorted(keys) != sorted(expected):
                raise archive.ArchiveError("stock-native-layout-rejected: " + item.name)
    checked = query(f"""
        LOCK TABLE public."{HISTORY}", public."Sys_Tenants" IN ACCESS SHARE MODE;
        SELECT json_build_object(
          'history',EXISTS(SELECT FROM pg_class WHERE oid='public."{HISTORY}"'::regclass AND relowner={owner_oid}::oid),
          'tenant',EXISTS(SELECT FROM pg_class WHERE oid='public."Sys_Tenants"'::regclass AND relowner={owner_oid}::oid),
          'creation',EXISTS(SELECT FROM public."{HISTORY}" WHERE "MigrationId"={archive.literal(MIGRATIONS[2])}),
          'sourceFk',EXISTS(SELECT FROM pg_constraint WHERE conrelid='{relation(SOURCE)}'::regclass
              AND confrelid='{relation(JOURNAL)}'::regclass AND conname='stock_source_original'
              AND contype='f' AND convalidated AND confdeltype='r'
              AND conkey=ARRAY[2,4,3]::smallint[] AND confkey=ARRAY[1,2,3]::smallint[]),
          'globalKeys',(SELECT count(*) FROM pg_index i
              JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=i.indkey[0]
              JOIN pg_collation c ON c.oid=a.attcollation
              WHERE i.indrelid IN ('{relation(NATIVE)}'::regclass,'{relation(BINDING)}'::regclass)
                AND a.attname='MessageId' AND i.indnkeyatts=1 AND i.indisunique AND i.indisvalid AND i.indisready
                AND i.indpred IS NULL AND i.indexprs IS NULL AND i.indcollation[0]=a.attcollation
                AND c.collnamespace='pg_catalog'::regnamespace AND c.collname='default'),
          'oneActiveRun',EXISTS(SELECT FROM pg_index i JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=i.indkey[0]
              WHERE i.indrelid='{relation(RUN)}'::regclass AND i.indisunique AND i.indisvalid AND i.indisready
                AND i.indnkeyatts=1 AND i.indexprs IS NULL AND a.attname='RootId'
                AND pg_get_expr(i.indpred,i.indrelid)='("CompletedAtUtc" IS NULL)'));
    """)
    if checked != dict(history=True, tenant=True, creation=True, sourceFk=True, globalKeys=2, oneActiveRun=True):
        raise archive.ArchiveError("stock-native-keys-or-history-rejected")


def graph_sql(tenant, route, *, table_relation=relation, source_tenant=True):
    tenant_sql = archive.literal(tenant) + "::uuid"
    root_sql = archive.literal(route["id"])
    contract_sql = ",".join(map(archive.literal, JOURNAL_CONTRACTS))
    tenant_join = 'JOIN public."Sys_Tenants" ten ON ten."Id"=a."TenantId"' if source_tenant else ""
    return f"""
        WITH n AS (SELECT * FROM {table_relation(NATIVE)} WHERE "TenantId"={tenant_sql}),
             s AS (SELECT * FROM {table_relation(SOURCE)} WHERE "TenantId"={tenant_sql}),
             b AS (SELECT * FROM {table_relation(BINDING)} WHERE "RootId"={root_sql}),
             c AS (SELECT * FROM {table_relation(CLAIM)} WHERE "RootId"={root_sql}),
             t AS (SELECT * FROM {table_relation(TICKET)} WHERE "RootId"={root_sql}),
             m AS (SELECT * FROM {table_relation(MUTATION)} WHERE "RootId"={root_sql}),
             sent AS (SELECT * FROM {table_relation(SEND)} WHERE "RootId"={root_sql})
        SELECT json_build_object(
          'route',EXISTS(SELECT FROM {table_relation(ROUTE_TABLE)} r JOIN {table_relation(AUTHORITY)} a ON a."RootId"=r."Id"
            {tenant_join}
            WHERE r."Id"={root_sql} AND r."TenantId"={tenant_sql} AND a."TenantId"=r."TenantId"
              AND r."OwnerId"={archive.literal(OWNER)} AND r."QueueDomainId"={archive.literal(DOMAIN)}
              AND r."RouteId"={archive.literal(ROUTE)} AND r."ProfileId"={archive.literal(PROFILE)} AND r."ProfileVersion"=1
              AND r."CurrentEpoch"=1 AND r."Phase"=0 AND r."LastSwitchId" IS NULL AND r."LockKey"={route['lockKey']}
              AND r."PublisherSetReference"={archive.literal(PUBLISHER)} AND a."PublisherReference"=r."PublisherSetReference"
              AND a."PolicyVersion"={archive.literal(POLICY)})
            AND (SELECT count(*) FROM {table_relation(ROUTE_TABLE)} WHERE "TenantId"={tenant_sql})=1,
          'originals',NOT EXISTS(SELECT FROM n FULL JOIN s ON s."NativeId"=n."Id"
            FULL JOIN b ON b."NativeId"=coalesce(n."Id",s."NativeId")
            LEFT JOIN {table_relation(JOURNAL)} j ON (j.tenant_id,j.owner,j.operation_id)=(s."TenantId",s."SourceOwner",s."OperationId")
            WHERE n."Id" IS NULL OR s."NativeId" IS NULL OR b."NativeId" IS NULL OR j.operation_id IS NULL
              OR s."SourceOwner" IS DISTINCT FROM {archive.literal(JOURNAL_OWNER)} OR j.state IS DISTINCT FROM 'COMMITTED'
              OR j.contract NOT IN ({contract_sql}) OR j.input_sha256 IS DISTINCT FROM encode(sha256(j.input_bytes),'hex')
              OR s."ResultSha256" IS DISTINCT FROM encode(sha256(j.result_bytes),'hex')
              OR n."MessageId" COLLATE "C" IS DISTINCT FROM
                   ('stock-hint:'||replace(s."TenantId"::text,'-','')||':'||replace(s."OperationId"::text,'-',''))
              OR n."TopicName" COLLATE "C" IS DISTINCT FROM {archive.literal(TOPIC)}
              OR n."PartitionKey" COLLATE "C" IS DISTINCT FROM s."TenantId"::text OR n."Payload"<>convert_to('{{}}','UTF8')
              OR n."PayloadSha256" COLLATE "C" IS DISTINCT FROM encode(sha256(n."Payload"),'hex')
              OR n."AggregateId" COLLATE "C" IS DISTINCT FROM s."OperationId"::text OR n."AggregateVersion"<>1
              OR n."CorrelationId" COLLATE "C" IS DISTINCT FROM n."AggregateId" COLLATE "C"
              OR n."CausationId" COLLATE "C" IS DISTINCT FROM n."AggregateId" COLLATE "C"
              OR b."MessageId" COLLATE "C" IS DISTINCT FROM n."MessageId" COLLATE "C"
              OR b."RawEnvelopeSha256" COLLATE "C" IS DISTINCT FROM n."PayloadSha256" COLLATE "C"
              OR b."AssignedEpoch"<>1 OR b."ClaimEpoch"<>1 OR n."Status" NOT BETWEEN 0 AND 3),
          'companions',NOT EXISTS(SELECT FROM t WHERE NOT EXISTS(SELECT FROM n WHERE n."Id"=t."NativeId")
                OR NOT EXISTS(SELECT FROM c WHERE c."RequestId"=t."RequestId"
                    AND c."Tickets"::jsonb @> jsonb_build_array(t."Ticket"::jsonb)))
            AND NOT EXISTS(SELECT FROM c WHERE jsonb_array_length(c."Tickets"::jsonb)<>
                (SELECT count(*) FROM t WHERE t."RequestId"=c."RequestId"))
            AND NOT EXISTS(SELECT FROM {table_relation(RUN)} r WHERE r."RootId"={root_sql} AND r."CompletedAtUtc" IS NOT NULL
                AND (NOT EXISTS(SELECT FROM c WHERE c."RequestId"=r."RequestId") OR EXISTS(
                    SELECT FROM t WHERE t."RequestId"=r."RequestId" AND NOT EXISTS(SELECT FROM m
                        WHERE m."MutationId"=t."ReceiptReference" AND m."NativeId"=t."NativeId"
                            AND m."Output"::jsonb->>'Transition' IN ('Published','RetryScheduled','DeadLettered')))))
            AND NOT EXISTS(SELECT FROM m WHERE NOT EXISTS(SELECT FROM n WHERE n."Id"=m."NativeId"))
            AND NOT EXISTS(SELECT FROM {table_relation(AUDIT)} a WHERE a."RootId"={root_sql} AND a."NativeId" IS NOT NULL
                AND NOT EXISTS(SELECT FROM n WHERE n."Id"=a."NativeId"))
            AND NOT EXISTS(SELECT FROM sent a WHERE NOT EXISTS(SELECT FROM t WHERE t."ReceiptReference"=a."MutationId"
                    AND t."Ticket" COLLATE "C"=a."OriginalTicket" COLLATE "C" AND t."Ticket"::jsonb->>'ClaimId'=a."ClaimId"::text))
            AND NOT EXISTS(SELECT FROM {table_relation(DEAD_LETTER)} d WHERE d."TenantId"={tenant_sql}
                AND NOT EXISTS(SELECT FROM n WHERE n."MessageId" COLLATE "C"=d."MessageId" COLLATE "C"
                    AND n."PayloadSha256" COLLATE "C"=d."PayloadSha256" COLLATE "C"))
            AND NOT EXISTS(SELECT FROM n WHERE n."Status"=2 AND NOT EXISTS(SELECT FROM m JOIN t
                ON t."ReceiptReference"=m."MutationId" AND t."NativeId"=m."NativeId"
                WHERE m."NativeId"=n."Id" AND m."Output"::jsonb->>'Transition'='Published')));
    """


def check_graph(query, tenant, route):
    facts = query(graph_sql(tenant, route), failure="stock-source-read-failed (see native logs)")
    for name in ("route", "originals", "companions"):
        if not facts[name]:
            raise archive.ArchiveError("stock-" + name + "-rejected")


def capture(query, tenant, owner_oid):
    """Inspect and plan the Stock slice inside the caller's open source snapshot."""
    query("SET LOCAL row_security=off; SELECT to_json(true);")
    route = root(tenant)
    check_layout(query, owner_oid)
    check_graph(query, tenant, route)
    counts = ",".join(f"{archive.literal(item.name)},(SELECT count(*) FROM ({rows_sql(item, tenant, route['id'], ordered=False)}) r)"
                      for item in TABLES)
    return route, query("SELECT json_build_object(" + counts + ");", failure="stock-source-read-failed (see native logs)")


def table_manifest(item, count):
    return dict(relation=relation(item), columns=list(item.columns), uniqueKeys=[list(key) for key in item.keys],
                count=count, scope=item.scope, sourceXmin="historical-observation" if item.xmin else None)


def journal_manifest():
    return dict(owner=JOURNAL_OWNER, contracts=list(JOURNAL_CONTRACTS), scope="referenced-by-stock-source")


def export(pg_bin, target, username, tenant, output):
    archive.private_directory(output)
    bundle, logs = output / "bundle", output / "native"
    bundle.mkdir(mode=0o700)
    logs.mkdir(mode=0o700)
    connection = archive.connection_args(target, username)
    with archive.snapshot_session(pg_bin, connection, os.environ, logs) as query:
        owner_oid, collation = check_target(query, target)
        route, counts = capture(query, tenant, owner_oid)
        snapshot = query("""
            SET LOCAL TIME ZONE 'UTC';
            SELECT json_build_object('snapshotId',pg_export_snapshot(),'watermark',pg_current_snapshot()::text,
                'capturedAt',transaction_timestamp(),'serverVersionNum',current_setting('server_version_num')::int,
                'readOnly',current_setting('transaction_read_only')::bool,'isolation',current_setting('transaction_isolation'));
        """)
        for item in TABLES:
            archive.copy_snapshot(pg_bin, connection, os.environ, logs, bundle / item.member, item.name, snapshot["snapshotId"],
                                  rows_sql(item, tenant, route["id"]), COPY_SETTINGS)
    manifest = dict(format=FORMAT, recoveryReady=False, tenantId=str(tenant), source=target.mapping(), snapshot=snapshot,
                    migrations=list(MIGRATIONS), databaseCollation=collation, root=route,
                    journal=journal_manifest(),
                    tables={item.member: table_manifest(item, counts[item.name]) for item in TABLES},
                    members={member: archive.file_digest(bundle / member) for member in MEMBERS})
    pending = logs / "manifest.pending"
    with pending.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(manifest, stream, indent=2)
        stream.write("\n")
    pending.replace(bundle / "manifest.json")
    return bundle, manifest


def verify(bundle, tenant, digest):
    paths = list(bundle.iterdir())
    if bundle.is_symlink() or {path.name for path in paths} != {*MEMBERS, "manifest.json"} \
            or any(path.is_symlink() or not path.is_file() for path in paths):
        raise archive.ArchiveError("stock-bundle-members-rejected")
    pinned = (bundle / "manifest.json").read_bytes()
    if not re.fullmatch(r"[0-9a-fA-F]{64}", digest) or hashlib.sha256(pinned).hexdigest() != digest.lower():
        raise archive.ArchiveError("stock-manifest-digest-mismatch")
    manifest = json.loads(pinned)
    route = root(tenant)
    if not isinstance(manifest, dict) or manifest.get("format") != FORMAT or manifest.get("recoveryReady") is not False \
            or manifest.get("tenantId") != str(tenant) or manifest.get("root") != route \
            or manifest.get("migrations") != list(MIGRATIONS) or manifest.get("journal") != journal_manifest():
        raise archive.ArchiveError("stock-manifest-scope-rejected")
    archive.PhysicalTarget.from_mapping(manifest.get("source"))
    snapshot = manifest.get("snapshot")
    if not isinstance(snapshot, dict) or snapshot.get("readOnly") is not True or snapshot.get("isolation") != "repeatable read" \
            or not isinstance(snapshot.get("snapshotId"), str) or not snapshot["snapshotId"]:
        raise archive.ArchiveError("stock-manifest-snapshot-rejected")
    digests = manifest.get("members")
    if not isinstance(digests, dict) or set(digests) != set(MEMBERS):
        raise archive.ArchiveError("stock-manifest-members-rejected")
    for item in TABLES:
        if archive.file_digest(bundle / item.member) != digests[item.member]:
            raise archive.ArchiveError("stock-member-digest-mismatch: " + item.name)
    verify_tables(bundle, tenant, route["id"], manifest.get("tables"))
    return manifest


def verify_tables(bundle, tenant, root_id, tables, *, prefix=""):
    """Check scoped Stock members after their enclosing bundle hashes have been verified."""
    if not isinstance(tables, dict) or set(tables) != {prefix + name for name in MEMBERS}:
        raise archive.ArchiveError("stock-manifest-members-rejected")
    with CSV_LIMIT_LOCK:
        previous_limit = csv.field_size_limit(2_147_483_647)
        try:
            for item in TABLES:
                verify_table(bundle / (prefix + item.member), tenant, root_id, item, tables[prefix + item.member])
        finally:
            csv.field_size_limit(previous_limit)


def verify_table(path, tenant, root_id, item, entry):
    count = entry.get("count") if isinstance(entry, dict) else None
    if type(count) is not int or count < 0 or entry != table_manifest(item, count):
        raise archive.ArchiveError("stock-manifest-table-rejected: " + item.name)
    columns = list(item.columns)
    scope = {"tenant": ("TenantId", str(tenant)), "root": ("RootId", root_id),
             "journal": ("tenant_id", str(tenant))}.get(item.scope)
    scope_index = columns.index(scope[0]) if scope else None
    with path.open(encoding="utf-8", newline="") as stream:
        rows = csv.reader(stream, strict=True)
        if next(rows, None) != columns:
            raise archive.ArchiveError("stock-member-header-rejected: " + item.name)
        actual = 0
        for row in rows:
            if len(row) != len(columns):
                raise archive.ArchiveError("stock-member-row-rejected: " + item.name)
            if scope_index is not None and row[scope_index] != scope[1]:
                raise archive.ArchiveError("stock-member-scope-rejected: " + item.name)
            actual += 1
        if actual != count:
            raise archive.ArchiveError("stock-member-count-mismatch: " + item.name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    create, inspect = actions.add_parser("export"), actions.add_parser("verify")
    for name in ("pg-bin", "target-file", "output"):
        create.add_argument("--" + name, type=Path, required=True)
    create.add_argument("--username", required=True)
    inspect.add_argument("--bundle", type=Path, required=True)
    inspect.add_argument("--manifest-sha256", required=True)
    for command in (create, inspect):
        command.add_argument("--tenant", required=True)
    args = parser.parse_args()
    try:
        tenant = archive.guid(args.tenant)
        if args.action == "export":
            target = archive.PhysicalTarget.from_mapping(json.loads(args.target_file.read_text(encoding="utf-8-sig")))
            bundle, manifest = export(args.pg_bin.resolve(strict=True), target, args.username, tenant, args.output.absolute())
        else:
            bundle = args.bundle.absolute()
            manifest = verify(bundle, tenant, args.manifest_sha256)
        print(json.dumps(dict(bundle=str(bundle), format=manifest["format"], tenantId=str(tenant),
                              manifestSha256=archive.file_digest(bundle / "manifest.json")["sha256"], recoveryReady=False)))
        return 0
    except (archive.ArchiveError, OSError, ValueError, csv.Error, subprocess.SubprocessError) as error:
        print("stock archive rejected: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
