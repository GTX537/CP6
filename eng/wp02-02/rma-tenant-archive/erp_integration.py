"""Exact read-only ERP integration tenant slice for the optional v8 archive."""

from identity_priority import COLUMN_TYPES as IDENTITY_TYPES, COPY_SETTINGS, Table, native_catalog, relation, tenant_filter


FORMAT = "cp6-rma-source-archive-v8"
SCHEMA = "erp_integration"
HISTORY = "__EFMigrationsHistory_ErpIntegration"
MIGRATION = "20261002151345_PostgreSqlErpIntegrationBaselineV1"

TABLES = (
    Table("Aggregate", "erp-aggregate.csv",
          tuple("TenantId Kind AggregateId LastRequestVersion LastAggregateVersion LastResultVersion RowVersion".split()),
          (("TenantId", "Kind", "AggregateId"),)),
    Table("CommandInbox", "erp-command-inbox.csv",
          tuple("""MessageId TenantId EventType AggregateId AggregateVersion Payload PayloadSha256 Status AttemptCount
                   ConflictCount LastConflictSha256 ErrorCode ReceivedAtUtc ProcessedAtUtc RetryAtUtc ReplayedAtUtc
                   ReplayReasonCode RowVersion""".split()),
          (("MessageId",),)),
    Table("Cp6_DeadLetterRecord", "erp-dead-letter.csv",
          tuple("""Id Direction TenantId MessageId ConsumerName PayloadSha256 ErrorCode SupportReference CreatedAtUtc
                   ReplayedAtUtc ReplayReasonCode RowVersion""".split()),
          (("Id",),)),
    Table("Cp6_OutboxMessage", "erp-outbox.csv",
          tuple("""Id MessageId TenantId TopicName PartitionKey Payload PayloadSha256 CorrelationId CausationId AggregateId
                   AggregateVersion Status AttemptCount CreatedAtUtc AvailableAtUtc LeaseOwner LeaseToken LeaseExpiresAtUtc
                   PublishedAtUtc DeadLetteredAtUtc LastErrorCode SupportReference RowVersion""".split()),
          (("Id",), ("MessageId",))),
    Table("DeliveryReplayAudit", "erp-delivery-replay-audit.csv",
          tuple("""TenantId OperationId Kind TargetId MessageId PayloadSha256 InputRowVersion ActorId ReasonCode
                   PreviousAttemptCount ReplayedAtUtc""".split()),
          (("TenantId", "OperationId"),)),
    Table("InboxReplayAudit", "erp-inbox-replay-audit.csv",
          tuple("TenantId OperationId MessageId PayloadSha256 InputRowVersion ActorId ReasonCode PreviousAttemptCount ReplayedAtUtc".split()),
          (("TenantId", "OperationId"),)),
    Table("OrderBridgeDispatch", "erp-order-bridge-dispatch.csv",
          tuple("""TenantId OrderKey AttemptCount AvailableAtUtc CompletedAtUtc LastErrorCode LeaseOwner LeaseExpiresAtUtc
                   RowVersion""".split()),
          (("TenantId", "OrderKey"),)),
    Table("Request", "erp-request.csv",
          tuple("""TenantId Kind AggregateId RequestVersion RequestId AccountId InputSha256 Terminal Succeeded ResultVersion
                   ResultType ResultDataJson UpdatedAtUtc RowVersion""".split()),
          (("TenantId", "Kind", "AggregateId", "RequestVersion"), ("TenantId", "RequestId"))),
)
MEMBERS = tuple(table.member for table in TABLES)
COLUMN_TYPES = {
    **IDENTITY_TYPES, "OperationId": "uuid", "RequestId": "uuid", "AccountId": "uuid", "InputRowVersion": "bytea",
    "Terminal": "bool", "Succeeded": "bool", "CompletedAtUtc": "timestamptz", "RetryAtUtc": "timestamptz",
    **{name: "int4" for name in ("LastRequestVersion", "LastAggregateVersion", "LastResultVersion", "ConflictCount",
                                  "PreviousAttemptCount", "RequestVersion", "ResultVersion")},
}
TABLE_TYPES = {"Aggregate": {"AggregateId": "uuid", "Kind": "int4"}, "CommandInbox": {"AggregateId": "uuid"},
               "Request": {"AggregateId": "uuid", "Kind": "int4"}}
# Table, key column, position within its native index, collation schema/name, deterministic.
KEY_COLLATIONS = (
    ("CommandInbox", "MessageId", 0, "pg_catalog", "C", True),
    ("Cp6_OutboxMessage", "MessageId", 0, "public", "cp6_ci_as_provider_v1", False),
    ("OrderBridgeDispatch", "OrderKey", 1, "pg_catalog", "C", True),
)


def rows_sql(table, tenant):
    columns = ", ".join('"{}"'.format(name) for name in table.columns)
    order = ", ".join('"{}"'.format(name) for name in table.unique_keys[0])
    return 'SELECT {} FROM {} WHERE {} ORDER BY {}'.format(columns, relation(table, SCHEMA), tenant_filter(tenant), order)


def check_layout(query, owner_oid):
    catalog = native_catalog(query, owner_oid, TABLES, schema=SCHEMA, history=HISTORY, migration=MIGRATION)
    if not catalog["migration"] or set(catalog["tables"]) != {table.name for table in TABLES}:
        raise ValueError("erp-integration-layout-rejected")
    for table in TABLES:
        native = catalog["tables"][table.name]
        types = {**COLUMN_TYPES, **TABLE_TYPES.get(table.name, {})}
        columns = [[name, types.get(name, "bpchar")] for name in table.columns]
        keys = [(tuple(key["columns"]), key["primary"]) for key in native["keys"] or []]
        expected = [(key, index == 0) for index, key in enumerate(table.unique_keys)]
        if native["columns"] != columns or sorted(keys) != sorted(expected):
            raise ValueError("erp-integration-layout-rejected: " + table.name)

    wanted = ", ".join("('{}','{}',{})".format(table, column, slot) for table, column, slot, *_ in KEY_COLLATIONS)
    collations = query("""
        WITH wanted(table_name,column_name,slot) AS (VALUES {wanted})
        SELECT coalesce(jsonb_object_agg(w.table_name,jsonb_build_array(n.nspname,c.collname,c.collisdeterministic,
            i.indcollation[w.slot]=a.attcollation)), '{{}}'::jsonb)
        FROM wanted w
        JOIN pg_class t ON t.relname=w.table_name AND t.relnamespace='{schema}'::regnamespace
        JOIN pg_attribute a ON a.attrelid=t.oid AND a.attname=w.column_name AND NOT a.attisdropped
        JOIN pg_collation c ON c.oid=a.attcollation
        JOIN pg_namespace n ON n.oid=c.collnamespace
        JOIN pg_index i ON i.indrelid=t.oid AND i.indisunique AND i.indisvalid AND i.indisready
            AND i.indpred IS NULL AND i.indexprs IS NULL AND i.indnkeyatts=w.slot+1 AND i.indkey[w.slot]=a.attnum;
    """.format(wanted=wanted, schema=SCHEMA))
    if collations != {table: [schema, name, deterministic, True] for table, _, _, schema, name, deterministic in KEY_COLLATIONS}:
        raise ValueError("erp-integration-key-collation-rejected")


def capture_counts(query, tenant, owner_oid):
    check_layout(query, owner_oid)
    counts = ",\n".join("'{}',(SELECT count(*) FROM {} WHERE {})".format(
        table.name, relation(table, SCHEMA), tenant_filter(tenant)) for table in TABLES)
    return query("SET LOCAL row_security=off;\nSELECT json_build_object(" + counts + ");",
                 failure="erp-integration-read-failed (see native logs)")


def manifest_section(counts):
    return {"schema": SCHEMA, "migrationId": MIGRATION, "keyCollations": {
        table + "." + column: {"schema": schema, "name": name, "deterministic": deterministic, "padding": "bpchar"}
        for table, column, _, schema, name, deterministic in KEY_COLLATIONS
    }, "tables": {
        table.name: {"member": table.member, "columns": list(table.columns),
                     "uniqueKeys": [{"columns": list(key), "scope": "tenant" if "TenantId" in key else "global"}
                                    for key in table.unique_keys], "count": counts[table.name]}
        for table in TABLES
    }}


def matches_manifest(manifest):
    section = manifest.get("erpIntegration")
    tables = section.get("tables") if isinstance(section, dict) else None
    if not isinstance(tables, dict) or set(tables) != {table.name for table in TABLES}:
        return False
    counts = {name: row.get("count") if isinstance(row, dict) else None for name, row in tables.items()}
    return all(type(count) is int and count >= 0 for count in counts.values()) and section == manifest_section(counts)
