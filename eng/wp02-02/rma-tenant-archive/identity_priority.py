"""Exact read-only Identity Priority tenant slice for the optional v5 archive."""

from typing import NamedTuple

FORMAT = "cp6-rma-source-archive-v5"
SCHEMA = "crm_identity_priority"
MIGRATION = "20261002151342_PostgreSqlIdentityPriorityBaselineV1"
COPY_SETTINGS = """
    SET LOCAL row_security=off;
    SET LOCAL bytea_output='hex';
    SET LOCAL timezone='UTC';
    SET LOCAL datestyle='ISO, YMD';
"""


class Table(NamedTuple):
    name: str
    member: str
    columns: tuple
    unique_keys: tuple


# IdentityTenantRowSecurityV1.Tables and the actual PostgreSQL Identity Priority migration.
TABLES = (
    Table("Cp6_DeadLetterRecord", "identity-priority-dead-letter.csv",
          tuple("""Id Direction TenantId MessageId ConsumerName PayloadSha256 ErrorCode SupportReference
                   CreatedAtUtc ReplayedAtUtc ReplayReasonCode RowVersion""".split()),
          (("Id",),)),
    Table("Cp6_InboxAggregateCheckpoint", "identity-priority-inbox-checkpoints.csv",
          tuple("Id ConsumerName TenantId AggregateId AggregateVersion UpdatedAtUtc RowVersion".split()),
          (("Id",), ("ConsumerName", "TenantId", "AggregateId"))),
    Table("Cp6_InboxMessage", "identity-priority-inbox.csv",
          tuple("""Id ConsumerName MessageId TenantId TopicName PartitionKey PayloadSha256 AggregateId AggregateVersion
                   Status AttemptCount OutcomeCode LastErrorCode SupportReference ReceivedAtUtc ProcessedAtUtc
                   RowVersion""".split()),
          (("Id",), ("ConsumerName", "MessageId"))),
    Table("Cp6_OutboxMessage", "identity-priority-outbox.csv",
          tuple("""Id MessageId TenantId TopicName PartitionKey Payload PayloadSha256 CorrelationId CausationId AggregateId
                   AggregateVersion Status AttemptCount CreatedAtUtc AvailableAtUtc LeaseOwner LeaseToken
                   LeaseExpiresAtUtc PublishedAtUtc DeadLetteredAtUtc LastErrorCode SupportReference RowVersion""".split()),
          (("Id",), ("MessageId",))),
)
MEMBERS = tuple(table.member for table in TABLES)
COLUMN_TYPES = {
    "Id": "uuid", "TenantId": "uuid", "RowVersion": "bytea", "Payload": "bytea",
    "Direction": "int4", "AggregateVersion": "int4", "Status": "int4", "AttemptCount": "int4",
    **{name: "timestamptz" for name in (
        "CreatedAtUtc", "ReplayedAtUtc", "UpdatedAtUtc", "ReceivedAtUtc", "ProcessedAtUtc",
        "AvailableAtUtc", "LeaseExpiresAtUtc", "PublishedAtUtc", "DeadLetteredAtUtc",
    )},
}


def relation(table, schema=SCHEMA):
    return '{}."{}"'.format(schema, table.name)


def tenant_filter(tenant):
    # The archive boundary has already parsed the tenant as a UUID.
    return "\"TenantId\"='{}'::uuid".format(tenant)


def rows_sql(table, tenant):
    columns = ", ".join('"{}"'.format(name) for name in table.columns)
    return 'SELECT {} FROM {} WHERE {} ORDER BY "Id"'.format(columns, relation(table), tenant_filter(tenant))


def native_catalog(query, owner_oid, tables, *, schema, history, migration):
    # Keep the native relation layout stable until every snapshot consumer finishes.
    return query("""
        LOCK TABLE {relations} IN ACCESS SHARE MODE;
        SELECT json_build_object(
          'migration',EXISTS (SELECT FROM public."{history}" WHERE "MigrationId"='{migration}'),
          'tables',coalesce((SELECT jsonb_object_agg(c.relname,jsonb_build_object(
            'columns',(SELECT jsonb_agg(jsonb_build_array(a.attname,t.typname) ORDER BY a.attnum)
                FROM pg_attribute a JOIN pg_type t ON t.oid=a.atttypid
                WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped),
            'keys',(SELECT jsonb_agg(jsonb_build_object('primary',i.indisprimary,'columns',
                ARRAY(SELECT a.attname::text FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(num,ord)
                    JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum=k.num
                    WHERE k.ord<=i.indnkeyatts ORDER BY k.ord)))
                FROM pg_index i WHERE i.indrelid=c.oid AND i.indisunique AND i.indisvalid AND i.indisready
                  AND i.indpred IS NULL AND i.indexprs IS NULL)))
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='{schema}' AND n.nspowner={owner}::oid AND c.relowner=n.nspowner AND c.relkind='r'
              AND c.relname IN ({names})), '{{}}'::jsonb));
    """.format(relations=", ".join(relation(table, schema) for table in tables), migration=migration, history=history,
               schema=schema, owner=owner_oid, names=", ".join("'{}'".format(table.name) for table in tables)))


def check_layout(query, owner_oid, tables):
    catalog = native_catalog(query, owner_oid, tables, schema=SCHEMA, history="__EFMigrationsHistory_IdentityPriority",
                             migration=MIGRATION)
    if not catalog["migration"] or set(catalog["tables"]) != {table.name for table in tables}:
        raise ValueError("identity-priority-layout-rejected")
    for table in tables:
        native = catalog["tables"][table.name]
        columns = [[name, COLUMN_TYPES.get(name, "bpchar")] for name in table.columns]
        keys = [(tuple(key["columns"]), key["primary"]) for key in native["keys"] or []]
        expected_keys = [(key, key == ("Id",)) for key in table.unique_keys]
        if native["columns"] != columns or sorted(keys) != sorted(expected_keys):
            raise ValueError("identity-priority-layout-rejected: " + table.name)


def capture_counts(query, tenant, owner_oid):
    check_layout(query, owner_oid, TABLES)
    counts = ",\n".join("'{}',(SELECT count(*) FROM {} WHERE {})".format(
        table.name, relation(table), tenant_filter(tenant)) for table in TABLES)
    return query("SET LOCAL row_security=off;\nSELECT json_build_object(" + counts + ");",
                 failure="identity-priority-read-failed (see native logs)")


def manifest_section(counts):
    return {"schema": SCHEMA, "migrationId": MIGRATION, "tables": {
        table.name: {"member": table.member, "columns": list(table.columns),
                     "uniqueKeys": [list(key) for key in table.unique_keys], "count": counts[table.name]}
        for table in TABLES
    }}


def matches_manifest(manifest):
    section = manifest.get("identityPriority")
    tables = section.get("tables") if isinstance(section, dict) else None
    if not isinstance(tables, dict) or set(tables) != {table.name for table in TABLES}:
        return False
    counts = {name: row.get("count") if isinstance(row, dict) else None for name, row in tables.items()}
    return all(type(count) is int and count >= 0 for count in counts.values()) and section == manifest_section(counts)
