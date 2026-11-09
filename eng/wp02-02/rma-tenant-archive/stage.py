#!/usr/bin/env python3
"""Stage a trusted RMA bundle into a caller-created isolated local PostgreSQL database."""

import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
import archive
import erp_integration
import file_content_assets
import iam_v6
import identity_priority
import privacy_evidence
import source_v3
import visibility_v4

TARGET_PLAN = "cp6_rma_stage.plan"
STAGE_FORMATS = (archive.FORMAT, archive.FORMAT_WITH_JOURNAL, source_v3.FORMAT, visibility_v4.FORMAT,
                 identity_priority.FORMAT, iam_v6.FORMAT, file_content_assets.FORMAT, erp_integration.FORMAT)


class StageError(RuntimeError):
    pass


def digest_pin(value):
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-fA-F]{64}", value):
        raise StageError("rma-stage-invalid-manifest-digest")
    return value.lower()


def read_counts(value, names):
    if not isinstance(value, dict) or set(value) != set(names) \
            or any(type(count) is not int or not 0 <= count <= 9223372036854775807 for count in value.values()):
        raise StageError("rma-stage-manifest-metadata-rejected")
    return value


def checked_bundle(bundle, output, tenant, digest, target, file_content_root=None):
    if bundle.is_symlink():
        raise StageError("rma-stage-bundle-member-rejected")
    files = list(bundle.iterdir())
    names = {entry.name for entry in files}
    base_names = {*archive.MEMBERS, "manifest.json"}
    journal_names = base_names | {archive.JOURNAL_MEMBER}
    source_names = journal_names | set(source_v3.MEMBERS)
    visibility_names = source_names | {visibility_v4.MEMBER}
    identity_names = visibility_names | set(identity_priority.MEMBERS)
    iam_names = identity_names | {iam_v6.MEMBER}
    asset_names = iam_names | {file_content_assets.MEMBER}
    erp_names = asset_names | set(erp_integration.MEMBERS)
    if names not in (base_names, journal_names, source_names, visibility_names, identity_names, iam_names,
                     asset_names, erp_names, erp_names | set(privacy_evidence.MEMBERS)) \
            or any(entry.is_symlink() or not entry.is_file() for entry in files):
        raise StageError("rma-stage-bundle-member-rejected")

    # Restore the private bytes we verified, even if the caller's source directory changes.
    checked = output / "bundle"
    checked.mkdir(mode=0o700)
    manifest_file = checked / "manifest.json"
    shutil.copyfile(bundle / "manifest.json", manifest_file, follow_symlinks=False)
    if manifest_file.is_symlink() or archive.file_digest(manifest_file)["sha256"] != digest:
        raise StageError("rma-stage-manifest-digest-mismatch")
    header = json.loads(manifest_file.read_text(encoding="utf-8"))
    if not isinstance(header, dict) or header.get("format") not in STAGE_FORMATS:
        raise StageError("rma-stage-format-rejected")
    for entry in files:
        if entry.name != "manifest.json":
            shutil.copyfile(entry, checked / entry.name, follow_symlinks=False)
    manifest = archive.verify_bundle(checked, str(tenant), file_content_root=file_content_root)

    source = archive.PhysicalTarget.from_mapping(manifest.get("source"))
    if (source.system_identifier, source.database_oid) == (target.system_identifier, target.database_oid):
        raise StageError("rma-stage-target-is-source")
    projection = manifest.get("sharedProjection")
    has_erp = manifest["format"] == erp_integration.FORMAT
    has_assets = has_erp or manifest["format"] == file_content_assets.FORMAT
    has_iam = has_assets or manifest["format"] == iam_v6.FORMAT
    has_identity = has_iam or manifest["format"] == identity_priority.FORMAT
    has_visibility = has_identity or manifest["format"] == visibility_v4.FORMAT
    has_source = has_visibility or manifest["format"] == source_v3.FORMAT
    columns = source_v3.HEADER_COLUMNS if has_source else archive.COLUMNS
    migration = manifest.get("migration")
    counts = read_counts(manifest.get("counts"), ("heads", "versions", "history", "outbounds"))
    if manifest.get("profile") != archive.PROFILE or manifest.get("tenantTables") != list(archive.TABLES) \
            or not isinstance(projection, dict) or projection.get("table") != "public.T_OutboundOrder" \
            or projection.get("columns") != list(columns) or counts["history"] != 1 \
            or not isinstance(migration, dict) or migration.get("migrationId") != archive.MIGRATION:
        raise StageError("rma-stage-manifest-metadata-rejected")
    archive.guid(migration.get("operationId"))
    for field in ("scriptSha256", "receiptSha256"):
        digest_pin(migration.get(field))
    journal = None
    if has_source or manifest["format"] == archive.FORMAT_WITH_JOURNAL:
        journal = read_counts(manifest["journal"].get("counts"), ("rows", "committed", "closed"))
        if journal["rows"] != journal["committed"] + journal["closed"]:
            raise StageError("rma-stage-manifest-metadata-rejected")
    plan = dict(format=manifest["format"], counts=counts, journalCounts=journal, migration=migration)
    if has_source:
        plan["sourceCounts"] = read_counts(manifest["rmaSource"]["counts"], ("headers", "details", "lineage"))
    if has_visibility:
        plan["visibilityCounts"] = read_counts(manifest["rmaVisibility"]["counts"], visibility_v4.FACTS)
    if has_identity:
        tables = manifest["identityPriority"]["tables"]
        plan["identityPriorityCounts"] = read_counts({name: table["count"] for name, table in tables.items()},
                                                     (table.name for table in identity_priority.TABLES))
    if has_iam:
        plan["iamReferenceCounts"] = read_counts(manifest["rmaIamReferences"]["counts"], iam_v6.FACTS)
    if has_assets:
        plan["fileContentAssetCount"] = read_counts({"rows": manifest["fileContentAssets"]["count"]}, ("rows",))["rows"]
    if has_erp:
        tables = manifest["erpIntegration"]["tables"]
        plan["erpIntegrationCounts"] = read_counts({name: table["count"] for name, table in tables.items()},
                                                   (table.name for table in erp_integration.TABLES))
    return checked, plan


def emit_restore(pg_bin, bundle, output, schema):
    archive_file = str(bundle / "rma.pgcustom")
    archive.run_native(pg_bin, "pg_restore", ["--list", archive_file], os.environ, output, "pg-restore-list")
    toc = (output / "pg-restore-list.stdout").read_text(encoding="utf-8")
    schemas = re.findall(r"(?m)^\d+; \d+ \d+ SCHEMA - (\S+)", toc)
    data = re.findall(r"(?m)^\d+; \d+ \d+ TABLE DATA (\S+) (\S+)", toc)
    if schemas != [schema] or sorted(data) != [(schema, table) for table in archive.TABLES]:
        raise StageError("rma-stage-archive-scope-rejected")
    # The complete pinned archive includes the schema and its ACL. pg_restore --schema would omit them.
    archive.run_native(pg_bin, "pg_restore", ["--file=" + str(output / "tenant.sql"), archive_file],
                       os.environ, output, "pg-restore-script")


def initial_sql(plan, read_only=False):
    # Keep arbitrary target text outside dollar-quoted bodies and use a transaction-local value.
    settings = """
        SET LOCAL search_path=pg_catalog;
        SET LOCAL standard_conforming_strings=on;
        SET LOCAL lock_timeout='5s';
        SET LOCAL statement_timeout='60s';
        SELECT set_config('{setting}',{plan},true) AS stage_input \\gset
    """.format(setting=TARGET_PLAN, plan=archive.literal(json.dumps(plan)))
    return ("SET TRANSACTION READ ONLY;\n" if read_only else "") + settings + """
        DO $cp6_stage_target$
        DECLARE
            target jsonb := current_setting('cp6_rma_stage.plan')::jsonb->'target';
            db pg_database%ROWTYPE;
        BEGIN
            SELECT * INTO STRICT db FROM pg_database WHERE datname=current_database();
            IF pg_is_in_recovery() OR db.datistemplate OR NOT db.datallowconn
                OR db.datname::text IS DISTINCT FROM target->>'Database'
                OR db.oid::text IS DISTINCT FROM target->>'DatabaseOid'
                OR host(inet_server_addr()) IS DISTINCT FROM target->>'Server'
                OR inet_server_port()::text IS DISTINCT FROM target->>'Port'
                OR (SELECT system_identifier::text FROM pg_control_system()) IS DISTINCT FROM target->>'SystemIdentifier'
                OR session_user<>current_user OR db.datdba<>to_regrole(quote_ident(current_user))::oid THEN
                RAISE EXCEPTION 'rma-stage-target-rejected';
            END IF;
            IF EXISTS (SELECT FROM aclexplode(coalesce(db.datacl,acldefault('d',db.datdba))) a
                       WHERE a.grantee<>db.datdba)
                OR EXISTS (SELECT FROM pg_stat_activity WHERE datid=db.oid AND pid<>pg_backend_pid()
                           AND backend_type='client backend') THEN
                RAISE EXCEPTION 'rma-stage-target-not-isolated';
            END IF;
        END
        $cp6_stage_target$;
    """


EMPTY_STAGE = """
    DO $cp6_stage_empty$
    BEGIN
        IF NOT pg_try_advisory_xact_lock(hashtextextended('cp6:rma:isolated-stage:v1',0)) THEN
            RAISE EXCEPTION 'rma-stage-target-busy';
        END IF;
        IF EXISTS (SELECT FROM pg_namespace WHERE nspname NOT IN ('pg_catalog','pg_toast','information_schema','public'))
            OR EXISTS (SELECT FROM pg_depend WHERE refclassid='pg_namespace'::regclass
                       AND refobjid=to_regnamespace('public'))
            OR EXISTS (SELECT FROM pg_extension WHERE extname<>'plpgsql')
            OR EXISTS (SELECT FROM pg_event_trigger)
            OR EXISTS (SELECT FROM pg_default_acl) THEN
            RAISE EXCEPTION 'rma-stage-target-not-empty';
        END IF;
    END
    $cp6_stage_empty$;
"""


def source_tables_sql(tenant):
    # Match PostgreSqlMigrationPrerequisitesV1 for document equality, inside staging only.
    return """
        CREATE COLLATION rma_archive_stage.outbound_number
            (provider=icu, locale='und-u-ks-level2', deterministic=false);
        CREATE TABLE rma_archive_stage.outbound_snapshot (
            tenant_id uuid NOT NULL CHECK (tenant_id={tenant}), outbound_id uuid NOT NULL,
            outbound_no bpchar COLLATE rma_archive_stage.outbound_number NOT NULL, warehouse_cd bpchar NOT NULL,
            status integer NOT NULL, is_deleted boolean NOT NULL, customer_cd bpchar, web_order_no bpchar,
            outbound_type integer NOT NULL,
            PRIMARY KEY (tenant_id,outbound_id), UNIQUE (tenant_id,outbound_id,outbound_no)
        );
        \\copy rma_archive_stage.outbound_snapshot FROM 'bundle/outbounds.csv' WITH (FORMAT csv, HEADER MATCH)
        CREATE TABLE rma_archive_stage.outbound_detail_snapshot (
            tenant_id uuid NOT NULL CHECK (tenant_id={tenant}), outbound_id uuid NOT NULL,
            outbound_no bpchar COLLATE rma_archive_stage.outbound_number NOT NULL, outbound_line_id uuid NOT NULL,
            line_no integer NOT NULL, product_cd bpchar NOT NULL, shipped_qty numeric(21,8) NOT NULL,
            unit_cd bpchar, warehouse_cd bpchar, location_cd bpchar, lot_no bpchar, ship_txn_no bpchar,
            PRIMARY KEY (tenant_id,outbound_line_id), UNIQUE (tenant_id,outbound_id,line_no),
            FOREIGN KEY (tenant_id,outbound_id,outbound_no)
                REFERENCES rma_archive_stage.outbound_snapshot (tenant_id,outbound_id,outbound_no)
        );
        \\copy rma_archive_stage.outbound_detail_snapshot FROM 'bundle/rma-source-details.csv' WITH (FORMAT csv, HEADER MATCH)
        CREATE TABLE rma_archive_stage.outbound_lineage_snapshot (
            tenant_id uuid NOT NULL CHECK (tenant_id={tenant}), outbound_line_id uuid NOT NULL,
            outbound_no text COLLATE "C" NOT NULL, payload jsonb NOT NULL,
            PRIMARY KEY (tenant_id,outbound_line_id),
            FOREIGN KEY (tenant_id,outbound_line_id)
                REFERENCES rma_archive_stage.outbound_detail_snapshot (tenant_id,outbound_line_id)
        );
        \\copy rma_archive_stage.outbound_lineage_snapshot FROM 'bundle/rma-source-lineage.csv' WITH (FORMAT csv, HEADER MATCH)
    """.format(tenant=tenant)


def neutral_sql(tenant, has_journal, has_source=False):
    value = archive.literal(tenant)
    sql = """
        CREATE SCHEMA rma_archive_stage;
        REVOKE ALL ON SCHEMA rma_archive_stage FROM PUBLIC;
    """
    if has_source:
        sql += source_tables_sql(value)
    else:
        sql += """
            CREATE TABLE rma_archive_stage.outbound_snapshot (
                tenant_id uuid NOT NULL CHECK (tenant_id={tenant}), outbound_id uuid NOT NULL,
                outbound_no text NOT NULL, warehouse_cd text NOT NULL, status integer NOT NULL,
                is_deleted boolean NOT NULL, PRIMARY KEY (tenant_id,outbound_id)
            );
            \\copy rma_archive_stage.outbound_snapshot FROM 'bundle/outbounds.csv' WITH (FORMAT csv, HEADER MATCH)
        """.format(tenant=value)
    if has_journal:
        sql += """
            CREATE TABLE rma_archive_stage.original_journal_snapshot (
                tenant_id uuid NOT NULL CHECK (tenant_id={tenant}), owner text NOT NULL CHECK (owner={owner}),
                operation_id uuid NOT NULL, actor_id uuid NOT NULL,
                contract text NOT NULL CHECK (contract={contract}), state text NOT NULL CHECK (state IN ('COMMITTED','CLOSED')),
                input_bytes bytea, input_sha256 text, result_bytes bytea NOT NULL, object_ids uuid[] NOT NULL,
                audit_json jsonb NOT NULL, committed_at timestamptz NOT NULL,
                PRIMARY KEY (tenant_id,owner,operation_id),
                CHECK ((state='CLOSED' AND input_bytes IS NULL AND input_sha256 IS NULL)
                    OR (state='COMMITTED' AND input_bytes IS NOT NULL AND input_sha256 IS NOT NULL
                        AND input_sha256 ~ '^[0-9a-f]{{64}}$'))
            );
            \\copy rma_archive_stage.original_journal_snapshot FROM 'bundle/rma-original.csv' WITH (FORMAT csv, HEADER MATCH)
        """.format(tenant=value, owner=archive.literal(archive.JOURNAL_OWNER),
                   contract=archive.literal(archive.JOURNAL_CONTRACT))
    return sql


def visibility_table_sql(tenant):
    required_text = {
        "outbound": ("outbound_no", "warehouse_cd"),
        "outbound_detail": ("outbound_id", "outbound_no"),
        "order": ("web_order_no",),
        "work_order": ("work_order_no",),
        "location": ("warehouse_cd", "location_cd"),
    }
    kinds = ",".join(map(archive.literal, visibility_v4.FACTS))
    shapes = []
    for kind, (_, fields) in visibility_v4.FACTS.items():
        keys = "ARRAY[" + ",".join(map(archive.literal, fields)) + "]"
        types = {**dict.fromkeys(required_text[kind], "'string'"),
                 "is_deleted": "'boolean'", "is_initialization_quarantined": "'boolean'"}
        field_types = "\n                AND ".join(
            "jsonb_typeof(facts->{}) IN ({})".format(archive.literal(field), types.get(field, "'string','null'"))
            for field in fields)
        shapes.append(f"WHEN {archive.literal(kind)} THEN facts ?& {keys} AND facts - {keys} = '{{}}'::jsonb\n"
                      f"                AND {field_types}")
    shape = "\n            ".join(shapes)
    return f"""
        CREATE TABLE rma_archive_stage.visibility_snapshot (
            kind text COLLATE "C" NOT NULL CHECK (kind IN ({kinds})),
            tenant_id uuid NOT NULL CHECK (tenant_id={archive.literal(tenant)}), id uuid NOT NULL,
            facts jsonb NOT NULL CHECK (jsonb_typeof(facts)='object'),
            PRIMARY KEY (tenant_id,kind,id),
            CHECK (CASE kind
            {shape}
            END)
        );
        \\copy rma_archive_stage.visibility_snapshot FROM 'bundle/{visibility_v4.MEMBER}' WITH (FORMAT csv, HEADER MATCH)
    """


def check_restored(schema, has_journal, has_source=False):
    sql = """
        DO $cp6_stage_check$
        DECLARE
            p jsonb := current_setting('cp6_rma_stage.plan')::jsonb;
            t uuid := (p->>'tenantId')::uuid;
            c jsonb := p->'counts';
            m jsonb := p->'migration';
        BEGIN
            IF EXISTS (SELECT FROM pg_namespace WHERE nspname NOT IN
                       ('pg_catalog','pg_toast','information_schema','public','rma_archive_stage','{schema}'))
                OR (SELECT count(*) FROM {schema}.heads)<>(c->>'heads')::bigint
                OR (SELECT count(*) FROM {schema}.versions)<>(c->>'versions')::bigint
                OR (SELECT count(*) FROM {schema}.{history})<>(c->>'history')::bigint
                OR (SELECT count(*) FROM rma_archive_stage.outbound_snapshot)<>(c->>'outbounds')::bigint
                OR EXISTS (SELECT FROM {schema}.heads WHERE tenant_id IS DISTINCT FROM t)
                OR EXISTS (SELECT FROM {schema}.versions WHERE tenant_id IS DISTINCT FROM t)
                OR NOT EXISTS (SELECT FROM {schema}.{history} WHERE tenant_id=t AND lineage='{lineage}'
                     AND migration_id=m->>'migrationId' AND operation_id=(m->>'operationId')::uuid
                     AND script_sha256=m->>'scriptSha256'
                     AND encode(sha256(convert_to(result_json,'UTF8')),'hex')=m->>'receiptSha256')
                OR EXISTS (SELECT outbound_id FROM {schema}.heads
                           EXCEPT SELECT outbound_id FROM rma_archive_stage.outbound_snapshot)
                OR EXISTS (SELECT outbound_id FROM rma_archive_stage.outbound_snapshot
                           EXCEPT SELECT outbound_id FROM {schema}.heads) THEN
                RAISE EXCEPTION 'rma-stage-data-mismatch';
            END IF;
    """.format(schema=schema, history=archive.HISTORY, lineage=archive.LINEAGE)
    if has_journal:
        sql += """
            IF (SELECT count(*) FROM rma_archive_stage.original_journal_snapshot)<>(p->'journalCounts'->>'rows')::bigint
                OR (SELECT count(*) FROM rma_archive_stage.original_journal_snapshot WHERE state='COMMITTED')
                    <>(p->'journalCounts'->>'committed')::bigint THEN
                RAISE EXCEPTION 'rma-stage-journal-mismatch';
            END IF;
        """
    if has_source:
        sql += """
            IF (SELECT count(*) FROM rma_archive_stage.outbound_detail_snapshot)
                    <>(p->'sourceCounts'->>'details')::bigint
                OR (SELECT count(*) FROM rma_archive_stage.outbound_lineage_snapshot)
                    <>(p->'sourceCounts'->>'lineage')::bigint THEN
                RAISE EXCEPTION 'rma-stage-source-count-mismatch';
            END IF;
            IF EXISTS (
                SELECT FROM rma_archive_stage.outbound_lineage_snapshot l
                JOIN rma_archive_stage.outbound_detail_snapshot d USING (tenant_id,outbound_line_id)
                JOIN rma_archive_stage.outbound_snapshot h USING (tenant_id,outbound_id)
                WHERE l.outbound_no IS DISTINCT FROM convert_from(bpcharsend(h.outbound_no),'UTF8')
            ) THEN
                RAISE EXCEPTION 'rma-stage-source-lineage-mismatch';
            END IF;
        """
    return sql + "END\n$cp6_stage_check$;\n"


CHECK_VISIBILITY = """
    DO $cp6_stage_visibility$
    DECLARE
        expected jsonb := current_setting('cp6_rma_stage.plan')::jsonb->'visibilityCounts';
    BEGIN
        IF EXISTS (
            SELECT FROM jsonb_each_text(expected) e(kind, count)
            LEFT JOIN (SELECT kind,count(*) AS count FROM rma_archive_stage.visibility_snapshot GROUP BY kind) v
                USING (kind)
            WHERE coalesce(v.count,0)<>e.count::bigint
        ) THEN
            RAISE EXCEPTION 'rma-stage-visibility-count-mismatch';
        END IF;

        -- Duplicate source facts must agree byte-for-byte, including NULL versus an empty string.
        -- All detail references, even deleted ones, use the same native BPCHAR equality as the source.
        IF EXISTS (
            SELECT FROM rma_archive_stage.outbound_snapshot h
            FULL JOIN (SELECT * FROM rma_archive_stage.visibility_snapshot WHERE kind='outbound') v
                ON v.tenant_id=h.tenant_id AND v.id=h.outbound_id
            WHERE h.outbound_id IS NULL OR v.id IS NULL
                OR convert_from(bpcharsend(h.outbound_no),'UTF8') COLLATE "C" IS DISTINCT FROM v.facts->>'outbound_no'
                OR convert_from(bpcharsend(h.warehouse_cd),'UTF8') COLLATE "C" IS DISTINCT FROM v.facts->>'warehouse_cd'
                OR convert_from(bpcharsend(h.web_order_no),'UTF8') COLLATE "C" IS DISTINCT FROM v.facts->>'web_order_no'
                OR to_jsonb(h.is_deleted) IS DISTINCT FROM v.facts->'is_deleted'
        ) OR EXISTS (
            SELECT FROM rma_archive_stage.visibility_snapshot v
            LEFT JOIN rma_archive_stage.outbound_snapshot h
                ON h.tenant_id=v.tenant_id AND h.outbound_id=(v.facts->>'outbound_id')::uuid
            WHERE v.kind='outbound_detail' AND (h.outbound_id IS NULL OR h.outbound_no IS DISTINCT FROM
                (v.facts->>'outbound_no')::bpchar COLLATE rma_archive_stage.outbound_number)
        ) OR EXISTS (
            SELECT FROM rma_archive_stage.outbound_detail_snapshot d
            FULL JOIN (SELECT * FROM rma_archive_stage.visibility_snapshot
                       WHERE kind='outbound_detail' AND facts->'is_deleted'='false'::jsonb) v
                ON v.tenant_id=d.tenant_id AND v.id=d.outbound_line_id
            WHERE d.outbound_line_id IS NULL OR v.id IS NULL OR d.outbound_id IS DISTINCT FROM (v.facts->>'outbound_id')::uuid
                OR convert_from(bpcharsend(d.outbound_no),'UTF8') COLLATE "C" IS DISTINCT FROM v.facts->>'outbound_no'
                OR convert_from(bpcharsend(d.warehouse_cd),'UTF8') COLLATE "C" IS DISTINCT FROM v.facts->>'warehouse_cd'
                OR convert_from(bpcharsend(d.location_cd),'UTF8') COLLATE "C" IS DISTINCT FROM v.facts->>'location_cd'
        ) THEN
            RAISE EXCEPTION 'rma-stage-visibility-graph-mismatch';
        END IF;
    END
    $cp6_stage_visibility$;
"""


def identity_priority_sql(tenant):
    # Nullability follows PostgreSqlIdentityPriorityBaselineV1. BPCHAR uses the same
    # native ICU collation already present in the neutral v3+ schema, without padding.
    nullable = {
        "Cp6_DeadLetterRecord": {"ConsumerName", "SupportReference", "ReplayedAtUtc", "ReplayReasonCode"},
        "Cp6_InboxAggregateCheckpoint": set(),
        "Cp6_InboxMessage": {"OutcomeCode", "LastErrorCode", "SupportReference", "ProcessedAtUtc"},
        "Cp6_OutboxMessage": {"LeaseOwner", "LeaseToken", "LeaseExpiresAtUtc", "PublishedAtUtc",
                              "DeadLetteredAtUtc", "LastErrorCode", "SupportReference"},
    }
    statements = []
    counts = []
    for table in identity_priority.TABLES:
        relation = f'rma_archive_stage."{table.name}"'
        columns = []
        for name in table.columns:
            native_type = identity_priority.COLUMN_TYPES.get(name, "bpchar COLLATE rma_archive_stage.outbound_number")
            nullability = "" if name in nullable[table.name] else " NOT NULL"
            columns.append(f'"{name}" {native_type}{nullability}')
        columns.extend((f'CHECK ("TenantId"={archive.literal(tenant)})', 'CHECK (octet_length("RowVersion")=8)'))
        for key in table.unique_keys:
            constraint = "PRIMARY KEY" if key == ("Id",) else "UNIQUE"
            key_columns = ",".join(f'"{name}"' for name in key)
            columns.append(f"{constraint} ({key_columns})")
        definition = ",\n            ".join(columns)
        statements.append(f"""
        CREATE TABLE {relation} (
            {definition}
        );
        \\copy {relation} FROM 'bundle/{table.member}' WITH (FORMAT csv, HEADER MATCH)
        """)
        counts.append(f"(SELECT count(*) FROM {relation})<>(expected->>{archive.literal(table.name)})::bigint")
    mismatch = "\n            OR ".join(counts)
    return "".join(statements) + f"""
    DO $cp6_stage_identity_priority$
    DECLARE
        expected jsonb := current_setting('{TARGET_PLAN}')::jsonb->'identityPriorityCounts';
    BEGIN
        IF {mismatch} THEN
            RAISE EXCEPTION 'rma-stage-identity-priority-count-mismatch';
        END IF;
    END
    $cp6_stage_identity_priority$;
    """


def iam_reference_sql(tenant, schema):
    kinds = ",".join(map(archive.literal, iam_v6.FACTS))
    shapes = []
    for kind, (_, fields) in iam_v6.FACTS.items():
        keys = "ARRAY[" + ",".join(map(archive.literal, fields)) + "]"
        shapes.append(f"WHEN {archive.literal(kind)} THEN facts ?& {keys} AND facts - {keys} = '{{}}'::jsonb")
    shape = "\n            ".join(shapes)
    return f"""
        CREATE TABLE rma_archive_stage.iam_reference_snapshot (
            kind text COLLATE "C" NOT NULL CHECK (kind IN ({kinds})),
            tenant_id uuid NOT NULL CHECK (tenant_id={archive.literal(tenant)}),
            id text COLLATE "C" NOT NULL CHECK (id<>''), facts jsonb NOT NULL CHECK (jsonb_typeof(facts)='object'),
            PRIMARY KEY (tenant_id,kind,id),
            CHECK (kind<>'tenant' OR id=tenant_id::text),
            CHECK (CASE kind
            {shape}
            END),
            CHECK (kind<>'participant' OR (jsonb_typeof(facts->'user_present')='boolean' AND
                (facts->'user_present'='true'::jsonb OR facts @>
                    '{{"user_name":null,"enabled":null,"locked_until":null,"department_id":null,"primary_role_id":null}}'::jsonb))),
            CHECK (kind<>'role' OR (jsonb_typeof(facts->'role_present')='boolean' AND
                (facts->'role_present'='true'::jsonb OR facts->'enabled'='null'::jsonb)))
        );
        \\copy rma_archive_stage.iam_reference_snapshot FROM 'bundle/{iam_v6.MEMBER}' WITH (FORMAT csv, HEADER MATCH)

        DO $cp6_stage_iam_references$
        DECLARE
            expected jsonb := current_setting('{TARGET_PLAN}')::jsonb->'iamReferenceCounts';
        BEGIN
            IF EXISTS (
                SELECT FROM jsonb_each_text(expected) e(kind, count)
                LEFT JOIN (SELECT kind,count(*) AS count FROM rma_archive_stage.iam_reference_snapshot GROUP BY kind) v
                    USING (kind)
                WHERE coalesce(v.count,0)<>e.count::bigint
            ) THEN
                RAISE EXCEPTION 'rma-stage-iam-reference-count-mismatch';
            END IF;
            IF EXISTS (
                WITH original_references AS (
                    SELECT tenant_id,actor_id::text AS id,true AS referenced_head,false AS referenced_journal FROM {schema}.heads
                    UNION ALL
                    SELECT tenant_id,actor_id::text,false,true FROM rma_archive_stage.original_journal_snapshot
                ), participants AS (
                    SELECT tenant_id,id,bool_or(referenced_head) AS referenced_head,bool_or(referenced_journal) AS referenced_journal
                    FROM original_references GROUP BY tenant_id,id
                )
                SELECT FROM participants p
                FULL JOIN (SELECT tenant_id,id,facts FROM rma_archive_stage.iam_reference_snapshot WHERE kind='participant') v
                    ON v.tenant_id=p.tenant_id AND v.id=p.id
                WHERE p.id IS NULL OR v.id IS NULL OR v.facts->'referenced_head' IS DISTINCT FROM to_jsonb(p.referenced_head)
                    OR v.facts->'referenced_journal' IS DISTINCT FROM to_jsonb(p.referenced_journal)
            ) THEN
                RAISE EXCEPTION 'rma-stage-iam-participant-mismatch';
            END IF;
        END
        $cp6_stage_iam_references$;
    """


def file_content_assets_sql(tenant):
    # parent_version is stored as text and state_etag is the original event JSON text.
    return f"""
        CREATE TABLE rma_archive_stage.file_content_asset_snapshot (
            tenant_id uuid NOT NULL CHECK (tenant_id={archive.literal(tenant)}),
            file_id uuid NOT NULL CHECK (file_id<>'00000000-0000-0000-0000-000000000000'),
            parent_id uuid NOT NULL CHECK (parent_id<>'00000000-0000-0000-0000-000000000000'),
            parent_version text NOT NULL CHECK (parent_version~'^[1-9][0-9]*$'),
            parent_digest text NOT NULL CHECK (parent_digest~'^[0-9a-f]{{64}}$'),
            file_name text NOT NULL CHECK (length(file_name) BETWEEN 1 AND 255),
            digest text NOT NULL CHECK (digest~'^[0-9a-f]{{64}}$'),
            byte_length bigint NOT NULL CHECK (byte_length BETWEEN 1 AND {file_content_assets.MAX_BYTES}),
            media_type text NOT NULL, store_path text NOT NULL UNIQUE,
            state_version bigint NOT NULL CHECK (state_version>0), state_etag text NOT NULL,
            state_digest text NOT NULL CHECK (state_digest~'^[0-9a-f]{{64}}$'),
            PRIMARY KEY (tenant_id,file_id)
        );
        \\copy rma_archive_stage.file_content_asset_snapshot FROM 'bundle/{file_content_assets.MEMBER}' WITH (FORMAT csv, HEADER MATCH)

        DO $cp6_stage_file_content_assets$
        BEGIN
            IF (SELECT count(*) FROM rma_archive_stage.file_content_asset_snapshot) <>
                (current_setting('{TARGET_PLAN}')::jsonb->>'fileContentAssetCount')::bigint THEN
                RAISE EXCEPTION 'rma-stage-file-content-count-mismatch';
            END IF;
        END
        $cp6_stage_file_content_assets$;
    """


def erp_integration_sql(tenant):
    # Preserve the original ERP migration's nullability and collations in the private staging schema.
    nullable = {
        "CommandInbox": {"LastConflictSha256", "ErrorCode", "ProcessedAtUtc", "RetryAtUtc", "ReplayedAtUtc", "ReplayReasonCode"},
        "Cp6_DeadLetterRecord": {"ConsumerName", "SupportReference", "ReplayedAtUtc", "ReplayReasonCode"},
        "Cp6_OutboxMessage": {"LeaseOwner", "LeaseToken", "LeaseExpiresAtUtc", "PublishedAtUtc", "DeadLetteredAtUtc",
                              "LastErrorCode", "SupportReference"},
        "DeliveryReplayAudit": {"MessageId"},
        "OrderBridgeDispatch": {"CompletedAtUtc", "LastErrorCode", "LeaseOwner", "LeaseExpiresAtUtc"},
    }
    c_collation = {
        "CommandInbox": {"MessageId"}, "DeliveryReplayAudit": {"TargetId", "MessageId"},
        "InboxReplayAudit": {"MessageId"}, "OrderBridgeDispatch": {"OrderKey"},
    }
    statements, counts = [], []
    for table in erp_integration.TABLES:
        relation = f'rma_archive_stage."erp_{table.name}"'
        types = {**erp_integration.COLUMN_TYPES, **erp_integration.TABLE_TYPES.get(table.name, {})}
        columns = []
        for name in table.columns:
            native_type = types.get(name, "bpchar")
            if native_type == "bpchar":
                collation = 'pg_catalog."C"' if name in c_collation.get(table.name, ()) else "rma_archive_stage.outbound_number"
                native_type += " COLLATE " + collation
            nullability = "" if name in nullable.get(table.name, ()) else " NOT NULL"
            columns.append(f'"{name}" {native_type}{nullability}')
        columns.append(f'CHECK ("TenantId"={archive.literal(tenant)})')
        if "RowVersion" in table.columns:
            columns.append('CHECK (octet_length("RowVersion")=8)')
        if "InputRowVersion" in table.columns:
            columns.append('CHECK (octet_length("InputRowVersion")<=8)')
        for index, key in enumerate(table.unique_keys):
            constraint = "PRIMARY KEY" if index == 0 else "UNIQUE"
            key_columns = ",".join(f'"{name}"' for name in key)
            columns.append(f"{constraint} ({key_columns})")
        definition = ",\n            ".join(columns)
        statements.append(f"""
        CREATE TABLE {relation} (
            {definition}
        );
        \\copy {relation} FROM 'bundle/{table.member}' WITH (FORMAT csv, HEADER MATCH)
        """)
        counts.append(f"(SELECT count(*) FROM {relation})<>(expected->>{archive.literal(table.name)})::bigint")
    mismatch = "\n            OR ".join(counts)
    return "".join(statements) + f"""
    DO $cp6_stage_erp_integration$
    DECLARE
        expected jsonb := current_setting('{TARGET_PLAN}')::jsonb->'erpIntegrationCounts';
    BEGIN
        IF {mismatch} THEN
            RAISE EXCEPTION 'rma-stage-erp-integration-count-mismatch';
        END IF;
    END
    $cp6_stage_erp_integration$;
    """


WRITE_RECEIPT = """
    CREATE TABLE rma_archive_stage.receipt (
        singleton boolean PRIMARY KEY CHECK (singleton), tenant_id uuid NOT NULL,
        manifest_sha256 text NOT NULL CHECK (manifest_sha256 ~ '^[0-9a-f]{{64}}$'),
        result_json text NOT NULL
    );
    REVOKE ALL ON ALL TABLES IN SCHEMA rma_archive_stage FROM PUBLIC;
    INSERT INTO rma_archive_stage.receipt
    SELECT true,(p->>'tenantId')::uuid,p->>'manifestSha256',
           json_build_object('outcome','STAGED','recoveryReady',false,'stageProfile','cp6-rma-archive-stage-v1',
               'target',p->'target','tenantId',p->>'tenantId','manifestSha256',p->>'manifestSha256',
               'format',p->>'format','counts',p->'counts','journalCounts',p->'journalCounts'{source_field})::text
    FROM (SELECT current_setting('cp6_rma_stage.plan')::jsonb AS p) stage;
    SELECT result_json FROM rma_archive_stage.receipt;
"""


READ_RECEIPT = """
    DO $cp6_stage_recover$
    BEGIN
        IF NOT EXISTS (SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            JOIN pg_database d ON d.datname=current_database()
            WHERE c.oid=to_regclass('rma_archive_stage.receipt') AND c.relkind='r'
                AND c.relowner=d.datdba AND n.nspowner=d.datdba AND NOT c.relrowsecurity
                AND NOT EXISTS (SELECT FROM aclexplode(n.nspacl) a WHERE a.grantee<>d.datdba)
                AND NOT EXISTS (SELECT FROM aclexplode(c.relacl) a WHERE a.grantee<>d.datdba)
                AND NOT EXISTS (SELECT FROM pg_attribute a, LATERAL aclexplode(a.attacl) x
                                WHERE a.attrelid=c.oid AND x.grantee<>d.datdba)) THEN
            RAISE EXCEPTION 'rma-stage-receipt-not-found';
        END IF;
    END
    $cp6_stage_recover$;
    SELECT r.result_json
    FROM rma_archive_stage.receipt r CROSS JOIN (SELECT current_setting('cp6_rma_stage.plan')::jsonb p) stage
    WHERE r.singleton AND r.tenant_id=(p->>'tenantId')::uuid AND r.manifest_sha256=p->>'manifestSha256'
          AND r.result_json::jsonb->'target'=p->'target'
          AND r.result_json::jsonb->>'stageProfile'='cp6-rma-archive-stage-v1'
          AND r.result_json::jsonb->>'outcome'='STAGED'
          AND r.result_json::jsonb->'recoveryReady'='false'::jsonb
          AND r.result_json::jsonb->>'tenantId'=p->>'tenantId'
          AND r.result_json::jsonb->>'manifestSha256'=p->>'manifestSha256';
"""


def run_driver(pg_bin, target, username, output, action):
    stdout, stderr = output / (action + ".stdout"), output / (action + ".stderr")
    command = [archive.binary(pg_bin, "psql"), "-XqAtw", "-v", "ON_ERROR_STOP=1", "-v", "VERBOSITY=verbose",
               *archive.connection_args(target, username), "--single-transaction", "--file=driver.sql"]
    try:
        with stdout.open("xb") as out, stderr.open("xb") as err:
            result = subprocess.run(command, cwd=output, env=archive.client_environment(os.environ, "cp6-rma-" + action),
                                    stdin=subprocess.DEVNULL, stdout=out, stderr=err, timeout=75,
                                    creationflags=archive.FLAGS)
    except subprocess.TimeoutExpired as error:
        raise StageError("rma-stage-outcome-unknown (use recover)" if action == "stage" else "rma-stage-recover-timeout") from error
    if result.returncode:
        reason = re.search(rb"\bERROR:\s+P0001:\s+(rma-stage-[a-z-]+)", stderr.read_bytes())
        if reason:
            raise StageError(reason[1].decode("ascii"))
        if result.returncode == 2 and action == "stage":
            raise StageError("rma-stage-outcome-unknown (use recover)")
        raise StageError("rma-stage-native-failed (see native logs)")
    lines = [line for line in stdout.read_bytes().splitlines() if line.strip()]
    if not lines and action == "recover":
        raise StageError("rma-stage-receipt-not-found")
    if len(lines) != 1:
        raise StageError("rma-stage-receipt-output-rejected (use recover)")
    return lines[0]


def execute(args):
    pg_bin = args.pg_bin.resolve(strict=True)
    target = archive.PhysicalTarget.from_mapping(json.loads(args.target_file.read_text(encoding="utf-8-sig")))
    tenant = archive.guid(args.tenant)
    digest = digest_pin(args.manifest_sha256)
    if not args.username or "\0" in args.username:
        raise StageError("rma-stage-invalid-username")
    output = args.output.absolute()
    archive.private_directory(output)
    plan = {"target": target.mapping(), "tenantId": str(tenant), "manifestSha256": digest}
    if args.action == "stage":
        checked, metadata = checked_bundle(args.bundle, output, tenant, digest, target, getattr(args, "file_content_root", None))
        plan.update(metadata)
        schema = "t_" + tenant.hex
        has_journal = plan["journalCounts"] is not None
        has_source = "sourceCounts" in plan
        has_visibility = "visibilityCounts" in plan
        emit_restore(pg_bin, checked, output, schema)
        driver = initial_sql(plan) + EMPTY_STAGE + "\n\\ir tenant.sql\n"
        # pg_restore changes the session search_path and statement settings.
        driver += "\nSET LOCAL search_path=pg_catalog;\nSET LOCAL statement_timeout='60s';\n"
        source_field = ",'sourceCounts',p->'sourceCounts'" if has_source else ""
        driver += neutral_sql(tenant, has_journal, has_source)
        if has_visibility:
            driver += visibility_table_sql(tenant)
        driver += check_restored(schema, has_journal, has_source)
        if has_visibility:
            driver += CHECK_VISIBILITY
            source_field += ",'visibilityCounts',p->'visibilityCounts'"
        if "identityPriorityCounts" in plan:
            driver += identity_priority_sql(tenant)
            source_field += ",'identityPriorityCounts',p->'identityPriorityCounts'"
        if "iamReferenceCounts" in plan:
            driver += iam_reference_sql(tenant, schema)
            source_field += ",'iamReferenceCounts',p->'iamReferenceCounts'"
        if "fileContentAssetCount" in plan:
            driver += file_content_assets_sql(tenant)
            source_field += ",'fileContentAssetCount',p->'fileContentAssetCount'"
        if "erpIntegrationCounts" in plan:
            driver += erp_integration_sql(tenant)
            source_field += ",'erpIntegrationCounts',p->'erpIntegrationCounts'"
        driver += WRITE_RECEIPT.format(source_field=source_field)
    else:
        driver = initial_sql(plan, read_only=True) + READ_RECEIPT
    with (output / "driver.sql").open("x", encoding="utf-8", newline="\n") as script:
        script.write(driver)
    return run_driver(pg_bin, target, args.username, output, args.action)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    for name in ("stage", "recover"):
        command = actions.add_parser(name)
        for key in ("pg-bin", "target-file", "output"):
            command.add_argument("--" + key, type=Path, required=True)
        for key in ("username", "tenant", "manifest-sha256"):
            command.add_argument("--" + key, required=True)
        if name == "stage":
            command.add_argument("--bundle", type=Path, required=True)
            command.add_argument("--file-content-root", type=Path, help="original private local asset root, required for v7 and v8")
    args = parser.parse_args()
    try:
        sys.stdout.buffer.write(execute(args) + b"\n")
    except (StageError, archive.ArchiveError, OSError, ValueError, subprocess.SubprocessError) as error:
        print("stage rejected:", error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
