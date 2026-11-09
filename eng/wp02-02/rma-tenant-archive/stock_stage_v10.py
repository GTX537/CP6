"""Build the private PostgreSQL portion of a pinned v10 Stock stage."""

import archive


NULLABLE = {
    "Cp6_OutboxMessage": {"LeaseOwner", "LeaseToken", "LeaseExpiresAtUtc", "PublishedAtUtc",
                          "DeadLetteredAtUtc", "LastErrorCode", "SupportReference"},
    "Cp6_OutboxBindingV2": {"CurrentClaimId", "ClaimEpoch", "ClaimStartedAtUtc"},
    "Cp6_OutboxRouteV2": {"LastSwitchId"},
    "DispatchRun": {"CompletedAtUtc"},
    "Cp6_OutboxAuditV2": {"NativeId"},
    "Cp6_DeadLetterRecord": {"ConsumerName", "SupportReference", "ReplayedAtUtc", "ReplayReasonCode"},
    "operations": {"input_bytes", "input_sha256"},
}


def relation(table):
    return 'rma_archive_stage."stock_' + table.name + '"'


def member(table):
    return archive.STOCK_MEMBER_PREFIX + table.member


def table_sql(stock, table, tenant, route):
    # The archive accepts native varchar values and preserves the source database collation
    # in its metadata. Neutral text keys use byte equality without a new length restriction.
    fields = [*table.fields, ("source_xmin", "xid")] if table.xmin else table.fields
    columns = []
    for name, native_type in fields:
        if native_type == "_uuid":
            native_type = "uuid[]"
        if native_type in ("text", "varchar"):
            native_type += ' COLLATE pg_catalog."C"'
        nullability = "" if name in NULLABLE.get(table.name, ()) else " NOT NULL"
        columns.append(f"{stock.ident(name)} {native_type}{nullability}")
    for index, key in enumerate(table.keys):
        constraint = "PRIMARY KEY" if index == 0 else "UNIQUE"
        columns.append(f"{constraint} ({','.join(map(stock.ident, key))})")

    tenant_sql = archive.literal(tenant) + "::uuid"
    root_sql = archive.literal(route["id"])
    if table.scope == "tenant":
        columns.append(f'CHECK ("TenantId"={tenant_sql})')
    if table.scope == "root" or table is stock.AUTHORITY:
        columns.append(f'CHECK ("RootId"={root_sql})')
    if table is stock.ROUTE_TABLE:
        columns.append(f'CHECK ("Id"={root_sql})')
    if table is stock.SOURCE:
        columns.append(f'CHECK ("SourceOwner"={archive.literal(stock.JOURNAL_OWNER)})')
    if table is stock.RUN:
        columns.append('CHECK ("RequestId"<>\'00000000-0000-0000-0000-000000000000\'::uuid)')
    if table is stock.JOURNAL:
        columns.extend((
            f"CHECK (tenant_id={tenant_sql} AND owner={archive.literal(stock.JOURNAL_OWNER)})",
            "CHECK (operation_id<>'00000000-0000-0000-0000-000000000000'::uuid"
            " AND actor_id<>'00000000-0000-0000-0000-000000000000'::uuid)",
            "CHECK (state='COMMITTED' AND input_bytes IS NOT NULL"
            " AND input_sha256 IS NOT NULL AND input_sha256 ~ '^[0-9a-f]{64}$')",
        ))
    definition = ",\n            ".join(columns)
    return f"""
        CREATE TABLE {relation(table)} (
            {definition}
        );
        \\copy {relation(table)} FROM 'bundle/{member(table)}' WITH (FORMAT csv, HEADER MATCH)
    """


def stage_sql(stock, tenant, section, plan_setting):
    route = section["root"]
    tables = "".join(table_sql(stock, table, tenant, route) for table in stock.TABLES)
    source, native = relation(stock.SOURCE), relation(stock.NATIVE)
    journal, run = relation(stock.JOURNAL), relation(stock.RUN)
    binding, route_table = relation(stock.BINDING), relation(stock.ROUTE_TABLE)
    counts = "\n            OR ".join(
        f"(SELECT count(*) FROM {relation(table)})<>(expected->{archive.literal(member(table))}->>'count')::bigint"
        for table in stock.TABLES)
    graph = stock.graph_sql(tenant, route, table_relation=relation, source_tenant=False).strip().rstrip(";")
    return tables + f"""
        ALTER TABLE {source}
            ADD FOREIGN KEY ("NativeId") REFERENCES {native} ("Id") ON DELETE RESTRICT,
            ADD FOREIGN KEY ("TenantId","SourceOwner","OperationId")
                REFERENCES {journal} (tenant_id,owner,operation_id) ON DELETE RESTRICT;
        ALTER TABLE {binding}
            ADD FOREIGN KEY ("NativeId") REFERENCES {native} ("Id") ON DELETE RESTRICT,
            ADD FOREIGN KEY ("RootId") REFERENCES {route_table} ("Id") ON DELETE RESTRICT;
        ALTER TABLE {run}
            ADD FOREIGN KEY ("RootId") REFERENCES {route_table} ("Id") ON DELETE RESTRICT;
        CREATE UNIQUE INDEX stock_dispatch_run_one_open ON {run} ("RootId") WHERE "CompletedAtUtc" IS NULL;
        ALTER TABLE {relation(stock.TICKET)}
            ADD FOREIGN KEY ("RootId","RequestId") REFERENCES {relation(stock.CLAIM)} ("RootId","RequestId") ON DELETE RESTRICT;
        ALTER TABLE {relation(stock.SEAL)}
            ADD FOREIGN KEY ("NativeId") REFERENCES {native} ("Id") ON DELETE RESTRICT;

    DO $cp6_stage_stock_outbox$
    DECLARE
        expected jsonb := current_setting('{plan_setting}')::jsonb->'stockOutbox'->'tables';
        facts jsonb;
    BEGIN
        IF {counts} THEN
            RAISE EXCEPTION 'rma-stage-stock-count-mismatch';
        END IF;
        SELECT observed.facts INTO facts FROM ({graph}) AS observed(facts);
        IF NOT (facts->>'route')::boolean THEN
            RAISE EXCEPTION 'rma-stage-stock-route-rejected';
        END IF;
        IF NOT (facts->>'originals')::boolean OR
                (SELECT count(*) FROM {journal})<>(SELECT count(*) FROM {source}) THEN
            RAISE EXCEPTION 'rma-stage-stock-originals-rejected';
        END IF;
        IF NOT (facts->>'companions')::boolean THEN
            RAISE EXCEPTION 'rma-stage-stock-companions-rejected';
        END IF;
    END
    $cp6_stage_stock_outbox$;
    """
