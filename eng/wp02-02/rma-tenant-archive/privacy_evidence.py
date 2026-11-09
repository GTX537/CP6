"""Historical audit and hold slices from the original Core snapshot."""

import csv
from dataclasses import dataclass
from threading import Lock
from uuid import UUID


FORMAT = "cp6-rma-source-archive-v9"
ENVIRONMENT = "cp6_scope_authorization_v1.environment"
MIGRATIONS = (
    "20261019093000_SecurityAuditContextV1",
    "20261028060000_ScopeAuthorizationFenceV1",
    "20261104120000_PrivacyDispositionV1",
    "20261108160000_SecurityFieldDispositionV1",
)
COPY_SETTINGS = "SET LOCAL row_security=off;\nSET LOCAL TIME ZONE 'UTC';\nSET LOCAL DateStyle='ISO, YMD';"
CSV_LIMIT_LOCK = Lock()


@dataclass(frozen=True)
class EvidenceTable:
    schema: str
    name: str
    member: str
    tenant_column: str
    columns: tuple
    keys: tuple

    @property
    def relation(self):
        return '{}."{}"'.format(self.schema, self.name)

    @property
    def qualified_name(self):
        return self.schema + "." + self.name


SECURITY_LOGS = EvidenceTable("public", "Sys_SecurityLogs", "security-audit.csv", "TenantId", (
    ("Id", "uuid", False), ("UserId", "uuid", True), ("UserName", "bpchar", True),
    ("RequestTenantCode", "bpchar", True), ("EventType", "int4", False), ("Reason", "bpchar", True),
    ("ClientIp", "bpchar", True), ("UserAgent", "bpchar", True), ("CreatedAt", "timestamp", False),
    ("Creator", "bpchar", True), ("CreateDate", "timestamp", False), ("Modifier", "bpchar", True),
    ("ModifyDate", "timestamp", True), ("TenantId", "uuid", False), ("ContextJson", "jsonb", True),
    ("privacy_revision", "uuid", False),
), (("Id",),))
HOLDS = EvidenceTable("cp6_privacy_disposition_v1", "holds", "legal-hold-history.csv", "target_tenant", (
    ("sequence", "int8", False), ("environment_id", "uuid", False), ("actor_tenant", "uuid", False),
    ("target_tenant", "uuid", False), ("subject_id", "uuid", False), ("version", "uuid", False),
    ("previous_version", "uuid", True), ("active", "bool", False), ("basis_ref", "uuid", False),
    ("review_at", "timestamptz", True), ("owner_id", "uuid", False), ("at", "timestamptz", False),
), (("sequence",), ("version",)))
TABLES = (SECURITY_LOGS, HOLDS)
MEMBERS = tuple(table.member for table in TABLES)


def row_filter(table, tenant, environment):
    predicate = "\"{}\"='{}'::uuid".format(table.tenant_column, tenant)
    if table is HOLDS:
        predicate += " AND environment_id='{}'::uuid".format(environment)
    return predicate


def rows_sql(table, tenant, environment):
    columns = ", ".join('"{}"'.format(name) for name, _, _ in table.columns)
    order = ", ".join('"{}"'.format(name) for name in table.keys[0])
    return "SELECT {} FROM {} WHERE {} ORDER BY {}".format(columns, table.relation, row_filter(table, tenant, environment), order)


def capture(query, tenant, owner_oid):
    # Use the same read-only holder for the source environment, native layout and counts.
    relations = ", ".join(table.relation for table in TABLES)
    wanted = ", ".join("('{}','{}')".format(table.schema, table.name) for table in TABLES)
    migrations = ", ".join("'{}'".format(migration) for migration in MIGRATIONS)
    native = query("""
        SET LOCAL row_security=off;
        LOCK TABLE {relations}, {environment} IN ACCESS SHARE MODE;
        SELECT jsonb_build_object(
          'migrations',(SELECT count(*) FROM public."__EFMigrationsHistory" WHERE "MigrationId" IN ({migrations})),
          'environmentOwned',EXISTS (SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
              WHERE c.oid='{environment}'::regclass AND c.relkind='r' AND c.relowner={owner}::oid AND n.nspowner=c.relowner),
          'environmentId',(SELECT environment_id FROM {environment}
              WHERE singleton AND environment_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          'tables',coalesce((SELECT jsonb_object_agg(n.nspname||'.'||c.relname,jsonb_build_object(
            'columns',(SELECT jsonb_agg(jsonb_build_array(a.attname,t.typname,NOT a.attnotnull) ORDER BY a.attnum)
                FROM pg_attribute a JOIN pg_type t ON t.oid=a.atttypid AND t.typnamespace='pg_catalog'::regnamespace
                WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped),
            'keys',(SELECT jsonb_agg(jsonb_build_object('primary',i.indisprimary,'columns',
                ARRAY(SELECT a.attname::text FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(num,ord)
                    JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum=k.num
                    WHERE k.ord<=i.indnkeyatts ORDER BY k.ord)))
                FROM pg_index i WHERE i.indrelid=c.oid AND i.indisunique AND i.indisvalid AND i.indisready
                  AND i.indpred IS NULL AND i.indexprs IS NULL)))
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE (n.nspname,c.relname) IN ({wanted}) AND c.relowner={owner}::oid AND c.relkind='r'
              AND (n.nspname='public' OR n.nspowner=c.relowner)), '{{}}'::jsonb));
    """.format(relations=relations, environment=ENVIRONMENT, migrations=migrations, wanted=wanted, owner=owner_oid),
                   failure="privacy-evidence-read-failed (see native logs)")
    if native["migrations"] != len(MIGRATIONS) or not native["environmentOwned"] \
            or set(native["tables"]) != {table.qualified_name for table in TABLES}:
        raise ValueError("privacy-evidence-layout-rejected")
    for table in TABLES:
        layout = native["tables"][table.qualified_name]
        actual_keys = sorted((tuple(key["columns"]), key["primary"]) for key in layout["keys"] or [])
        expected_keys = sorted((key, index == 0) for index, key in enumerate(table.keys))
        if layout["columns"] != [list(column) for column in table.columns] or actual_keys != expected_keys:
            raise ValueError("privacy-evidence-layout-rejected: " + table.qualified_name)
    if native["environmentId"] is None:
        raise ValueError("privacy-evidence-environment-required")
    environment = UUID(native["environmentId"])
    counts = ",\n".join("'{}',(SELECT count(*) FROM {} WHERE {})".format(
        table.qualified_name, table.relation, row_filter(table, tenant, environment)) for table in TABLES)
    return environment, query("SELECT jsonb_build_object(" + counts + ");",
                              failure="privacy-evidence-read-failed (see native logs)")


def manifest_section(environment, counts):
    return {"scope": "historical-only", "environmentId": str(environment), "migrations": list(MIGRATIONS), "tables": {
        table.qualified_name: {"member": table.member, "tenantColumn": table.tenant_column,
                               "environmentColumn": "environment_id" if table is HOLDS else None,
                               "columns": [{"name": name, "type": native, "nullable": nullable}
                                           for name, native, nullable in table.columns],
                               "uniqueKeys": [{"columns": list(key), "scope": "global", "primary": index == 0}
                                              for index, key in enumerate(table.keys)],
                               "count": counts[table.qualified_name]}
        for table in TABLES
    }}


def verify_members(bundle, tenant, manifest):
    section = manifest.get("privacyEvidence")
    tables = section.get("tables") if isinstance(section, dict) else None
    if not isinstance(tables, dict) or set(tables) != {table.qualified_name for table in TABLES}:
        raise ValueError("privacy-evidence-manifest-rejected")
    try:
        environment = UUID(section.get("environmentId"))
    except (AttributeError, TypeError, ValueError) as error:
        raise ValueError("privacy-evidence-manifest-rejected") from error
    counts = {name: value.get("count") if isinstance(value, dict) else None for name, value in tables.items()}
    if environment.int == 0 or any(type(count) is not int or not 0 <= count < 2 ** 63 for count in counts.values()) \
            or section != manifest_section(environment, counts):
        raise ValueError("privacy-evidence-manifest-rejected")

    tenant_id, environment_id = str(tenant), str(environment)
    # PostgreSQL permits a JSONB field larger than the CSV reader's process-wide default.
    with CSV_LIMIT_LOCK:
        field_limit = csv.field_size_limit(2 ** 30)
        try:
            for table in TABLES:
                names = [name for name, _, _ in table.columns]
                tenant_column = names.index(table.tenant_column)
                environment_column = names.index("environment_id") if table is HOLDS else None
                with (bundle / table.member).open(encoding="utf-8", newline="") as stream:
                    rows = csv.reader(stream, strict=True)
                    if next(rows, None) != names:
                        raise ValueError("privacy-evidence-header-rejected: " + table.member)
                    count = 0
                    previous = None
                    for row in rows:
                        if len(row) != len(names) or row[tenant_column] != tenant_id \
                                or environment_column is not None and row[environment_column] != environment_id:
                            raise ValueError("privacy-evidence-row-scope-rejected: " + table.member)
                        key = int(row[0]) if table is HOLDS else UUID(row[0]).int
                        if previous is not None and key <= previous:
                            raise ValueError("privacy-evidence-key-order-rejected: " + table.member)
                        previous = key
                        count += 1
                    if count != counts[table.qualified_name]:
                        raise ValueError("privacy-evidence-count-mismatch: " + table.member)
        finally:
            csv.field_size_limit(field_limit)
