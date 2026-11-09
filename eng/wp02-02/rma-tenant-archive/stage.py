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
import source_v3

TARGET_PLAN = "cp6_rma_stage.plan"


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


def checked_bundle(bundle, output, tenant, digest, target):
    if bundle.is_symlink():
        raise StageError("rma-stage-bundle-member-rejected")
    files = list(bundle.iterdir())
    names = {entry.name for entry in files}
    base_names = {*archive.MEMBERS, "manifest.json"}
    journal_names = base_names | {archive.JOURNAL_MEMBER}
    if names not in (base_names, journal_names, journal_names | set(source_v3.MEMBERS)) \
            or any(entry.is_symlink() or not entry.is_file() for entry in files):
        raise StageError("rma-stage-bundle-member-rejected")

    # Restore the private bytes we verified, even if the caller's source directory changes.
    checked = output / "bundle"
    checked.mkdir(mode=0o700)
    for entry in files:
        shutil.copyfile(entry, checked / entry.name, follow_symlinks=False)
    manifest_file = checked / "manifest.json"
    if manifest_file.is_symlink() or archive.file_digest(manifest_file)["sha256"] != digest:
        raise StageError("rma-stage-manifest-digest-mismatch")
    manifest = archive.verify_bundle(checked, str(tenant))

    source = archive.PhysicalTarget.from_mapping(manifest.get("source"))
    if (source.system_identifier, source.database_oid) == (target.system_identifier, target.database_oid):
        raise StageError("rma-stage-target-is-source")
    projection = manifest.get("sharedProjection")
    has_source = manifest["format"] == source_v3.FORMAT
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
    if manifest["format"] in (archive.FORMAT_WITH_JOURNAL, source_v3.FORMAT):
        journal = read_counts(manifest["journal"].get("counts"), ("rows", "committed", "closed"))
        if journal["rows"] != journal["committed"] + journal["closed"]:
            raise StageError("rma-stage-manifest-metadata-rejected")
    plan = dict(format=manifest["format"], counts=counts, journalCounts=journal, migration=migration)
    if has_source:
        plan["sourceCounts"] = read_counts(manifest["rmaSource"]["counts"], ("headers", "details", "lineage"))
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
        checked, metadata = checked_bundle(args.bundle, output, tenant, digest, target)
        plan.update(metadata)
        schema = "t_" + tenant.hex
        has_journal = plan["journalCounts"] is not None
        has_source = "sourceCounts" in plan
        emit_restore(pg_bin, checked, output, schema)
        driver = initial_sql(plan) + EMPTY_STAGE + "\n\\ir tenant.sql\n"
        # pg_restore changes the session search_path and statement settings.
        driver += "\nSET LOCAL search_path=pg_catalog;\nSET LOCAL statement_timeout='60s';\n"
        source_field = ",'sourceCounts',p->'sourceCounts'" if has_source else ""
        driver += neutral_sql(tenant, has_journal, has_source) + check_restored(schema, has_journal, has_source)
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
    args = parser.parse_args()
    try:
        sys.stdout.buffer.write(execute(args) + b"\n")
    except (StageError, archive.ArchiveError, OSError, ValueError, subprocess.SubprocessError) as error:
        print("stage rejected:", error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
