#!/usr/bin/env python3
"""Import the original RMA tenant rows into a marked, isolated Core candidate."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
import archive
import candidate_journal
import stage


MARKER = "CP6-PRIVACY-RESTORE-CANDIDATE/v1/"
PROFILE = "cp6-rma-core-candidate-import-v1"
PLAN = "cp6_rma_candidate_import.plan"
RECEIPTS = "rma_core_candidate_import_v1"
COPY_COLUMNS = {"heads": "tenant_id, id, actor_id, outbound_id, version",
                "versions": "tenant_id, id, version, state, payload"}


def commissioning_pin(path, target):
    original = path.read_bytes().rstrip(b"\r\n")
    receipt = json.loads(original)
    if not isinstance(receipt, dict) or receipt.get("Outcome") != "COMMISSIONED" \
            or receipt.get("Profile") != archive.PROFILE or receipt.get("DatabaseOid") != target.database_oid \
            or receipt.get("SystemIdentifier") != target.system_identifier:
        raise stage.StageError("rma-stage-candidate-commission-rejected")
    return str(archive.guid(receipt.get("InstallationId"))), hashlib.sha256(original).hexdigest()


def emit_rows(pg_bin, checked, output, schema):
    native = output / "pg-restore-data.sql"
    archive.run_native(pg_bin, "pg_restore", ["--data-only", "--no-owner", "--no-acl", "--schema=" + schema,
                       "--table=heads", "--table=versions", "--file=-", str(checked / "rma.pgcustom")],
                       os.environ, output, "pg-restore-data", output=native)
    headers = [f"COPY {schema}.{table} ({columns}) FROM stdin;".encode("ascii")
               for table, columns in COPY_COLUMNS.items()]
    seen = 0
    in_copy = False
    # Keep only the native COPY blocks. The generated restore preamble never runs on the candidate.
    with native.open("rb") as source, (output / "tenant-rows.sql").open("xb") as rows:
        for line in source:
            token = line.rstrip(b"\r\n")
            if in_copy:
                rows.write(line)
                in_copy = token != b"\\."
            elif token.startswith(b"COPY "):
                if seen == len(headers) or token != headers[seen]:
                    raise stage.StageError("rma-stage-candidate-archive-scope-rejected")
                rows.write(line)
                seen += 1
                in_copy = True
    if in_copy or seen != len(headers):
        raise stage.StageError("rma-stage-candidate-archive-scope-rejected")


def target_sql(plan, read_only):
    return ("SET TRANSACTION READ ONLY;\n" if read_only else "") + f"""
    SET LOCAL search_path=pg_catalog;
    SET LOCAL standard_conforming_strings=on;
    SET LOCAL lock_timeout='5s';
    SET LOCAL statement_timeout='60s';
    SELECT set_config('{PLAN}',{archive.literal(json.dumps(plan))},true) AS import_input \\gset
    DO $candidate_target$
    DECLARE
        k jsonb := current_setting('{PLAN}')::jsonb->'key';
        expected jsonb := k->'target';
        db pg_database%ROWTYPE;
    BEGIN
        SELECT * INTO STRICT db FROM pg_database WHERE datname=current_database();
        IF db.datistemplate OR NOT db.datallowconn OR pg_is_in_recovery()
           OR NOT EXISTS(SELECT FROM pg_roles r WHERE r.oid=db.datdba AND r.rolname=current_user AND r.rolsuper)
           OR session_user<>current_user
           OR jsonb_build_object('Server',host(inet_server_addr()),'Port',inet_server_port(),'Database',db.datname,
               'DatabaseOid',db.oid::bigint,'SystemIdentifier',(SELECT system_identifier::text FROM pg_control_system()))<>expected THEN
            RAISE EXCEPTION 'rma-stage-candidate-target-rejected';
        END IF;
        IF shobj_description(db.oid,'pg_database') IS DISTINCT FROM '{MARKER}'||(k->>'candidateId') THEN
            RAISE EXCEPTION 'rma-stage-candidate-marker-rejected';
        END IF;
        IF NOT pg_try_advisory_xact_lock(hashtextextended('cp6:rma:core-candidate-import:v1',0)) THEN
            RAISE EXCEPTION 'rma-stage-candidate-busy';
        END IF;
    END
    $candidate_target$;
    """


def ready_sql(schema, *, expect_empty=True):
    empty = f"""IF EXISTS (SELECT FROM {schema}.heads) OR EXISTS (SELECT FROM {schema}.versions) THEN
            RAISE EXCEPTION 'rma-stage-candidate-not-empty';
        END IF;""" if expect_empty else ""
    return f"""
    DO $candidate_ready$
    DECLARE
        p jsonb := current_setting('{PLAN}')::jsonb;
        k jsonb := p->'key';
        tenant uuid := (k->>'tenantId')::uuid;
        owner oid := to_regrole(quote_ident(current_user));
        ordinary oid;
        db pg_database%ROWTYPE;
        heads regclass := to_regclass('{schema}.heads');
        versions regclass := to_regclass('{schema}.versions');
        history regclass := to_regclass('{schema}.{archive.HISTORY}');
    BEGIN
        SELECT * INTO STRICT db FROM pg_database WHERE datname=current_database();
        IF EXISTS (SELECT FROM pg_stat_activity WHERE datid=db.oid AND pid<>pg_backend_pid()
                   AND backend_type='client backend') THEN
            RAISE EXCEPTION 'rma-stage-candidate-not-isolated';
        END IF;
        IF (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
              WHERE c.oid IN (heads,versions,history,to_regclass('cp6_rma_tenant_v1.installation'),
                  to_regclass('cp6_rma_core_commission_v1.installation'),to_regclass('cp6_rma_tenant_v1.tenant_migrations'),
                  to_regclass('cp6_tenancy_v1.login_tenant'),to_regclass('cp6_privacy_disposition_v1.restore_admission'))
                AND c.relkind='r' AND c.relowner=owner AND n.nspowner=owner AND NOT c.relrowsecurity)<>8
            OR to_regprocedure('cp6_authentication_v1.acquire()') IS NULL THEN
            RAISE EXCEPTION 'rma-stage-candidate-layout-rejected';
        END IF;
        PERFORM cp6_authentication_v1.acquire();
        IF EXISTS (SELECT FROM cp6_privacy_disposition_v1.restore_admission WHERE singleton) THEN
            RAISE EXCEPTION 'rma-stage-candidate-admission-exists';
        END IF;
        SELECT r.oid INTO ordinary
        FROM cp6_rma_tenant_v1.installation i CROSS JOIN cp6_rma_core_commission_v1.installation c
        JOIN cp6_rma_tenant_v1.tenant_migrations m ON m.tenant_id=tenant
        JOIN cp6_tenancy_v1.login_tenant l ON (l.tenant_id,l.role_oid,l.role_name)=(m.tenant_id,m.role_oid,m.role_name)
        JOIN pg_roles r ON r.oid=l.role_oid AND r.rolname=l.role_name
        JOIN {schema}.{archive.HISTORY} h ON (h.tenant_id,h.lineage)=(m.tenant_id,m.lineage)
        WHERE i.singleton AND c.singleton AND i.target=k->'target' AND i.profile='{archive.PROFILE}'
            AND i.installation_id=(p->>'installationId')::uuid AND c.request_json::jsonb->'Target'=i.target
            AND i.bindings_json::jsonb=c.request_json::jsonb->'Tenants'
            AND i.bindings_json::jsonb @> jsonb_build_array(jsonb_build_object('TenantId',tenant,'Login',r.rolname))
            AND encode(sha256(convert_to(c.commission_receipt,'UTF8')),'hex')=k->>'commissionReceiptSha256'
            AND m.state='Ready' AND m.lineage='{archive.LINEAGE}' AND m.migration_id='{archive.MIGRATION}'
            AND (m.operation_id,m.migration_id,m.script_sha256,m.result_json)=(h.operation_id,h.migration_id,h.script_sha256,h.result_json)
            AND m.operation_id=(c.commission_receipt::jsonb->>'OperationId')::uuid
            AND EXISTS (SELECT FROM jsonb_array_elements(c.commission_receipt::jsonb->'Tenants') t
                        WHERE t.value=m.result_json::jsonb)
            AND r.oid<>owner AND r.rolcanlogin AND NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication)
            AND NOT EXISTS (SELECT FROM pg_auth_members WHERE member=r.oid OR roleid=r.oid)
        FOR SHARE OF i,c,m,l,h;
        IF ordinary IS NULL THEN
            RAISE EXCEPTION 'rma-stage-candidate-ready-rejected';
        END IF;
        LOCK TABLE {schema}.heads,{schema}.versions IN ACCESS EXCLUSIVE MODE;
        IF EXISTS (SELECT FROM aclexplode(coalesce(db.datacl,acldefault('d',owner))) a WHERE a.grantee<>owner AND (
                a.grantor<>owner OR a.is_grantable OR a.privilege_type<>'CONNECT'
                OR NOT EXISTS (SELECT FROM cp6_tenancy_v1.login_tenant l JOIN pg_roles r
                    ON r.oid=l.role_oid AND r.rolname=l.role_name WHERE l.role_oid=a.grantee AND r.rolcanlogin)))
           OR NOT has_database_privilege(ordinary,db.oid,'CONNECT')
           OR NOT has_schema_privilege(ordinary,'{schema}','USAGE')
           OR NOT has_table_privilege(ordinary,heads,'SELECT') OR NOT has_table_privilege(ordinary,versions,'SELECT')
           OR EXISTS (SELECT FROM pg_namespace n, LATERAL aclexplode(n.nspacl) a
               WHERE n.nspname='{schema}' AND a.grantee<>owner AND
                   (a.grantee<>ordinary OR a.grantor<>owner OR a.is_grantable OR a.privilege_type<>'USAGE'))
           OR EXISTS (SELECT FROM pg_class c, LATERAL aclexplode(c.relacl) a
               WHERE c.oid IN (heads,versions,history) AND a.grantee<>owner AND
                   (c.oid=history OR a.grantee<>ordinary OR a.grantor<>owner OR a.is_grantable OR a.privilege_type<>'SELECT'))
           OR EXISTS (SELECT FROM pg_attribute t,LATERAL aclexplode(t.attacl) a
               WHERE t.attrelid IN (heads,versions,history) AND a.grantee<>owner) THEN
            RAISE EXCEPTION 'rma-stage-candidate-acl-rejected';
        END IF;
        IF EXISTS (SELECT FROM pg_trigger WHERE tgrelid IN (heads,versions) AND NOT tgisinternal AND (tgtype&4)<>0)
            OR EXISTS (SELECT FROM pg_rewrite WHERE ev_class IN (heads,versions))
            OR EXISTS (SELECT FROM pg_inherits WHERE inhrelid IN (heads,versions) OR inhparent IN (heads,versions))
            OR EXISTS (SELECT FROM pg_index WHERE indrelid IN (heads,versions) AND (indexprs IS NOT NULL OR indpred IS NOT NULL))
            OR EXISTS (SELECT FROM pg_event_trigger WHERE evtenabled<>'D')
            OR EXISTS (SELECT FROM pg_default_acl WHERE defaclrole=owner AND defaclobjtype IN ('r','n'))
            OR EXISTS (SELECT FROM pg_attribute WHERE attrelid IN (heads,versions) AND attnum>0 AND NOT attisdropped
                       AND (NOT attnotnull OR atthasdef OR attidentity<>'' OR attgenerated<>''))
            OR (SELECT array_agg(attname::text||':'||atttypid::regtype::text ORDER BY attnum) FROM pg_attribute
                WHERE attrelid=heads AND attnum>0 AND NOT attisdropped) IS DISTINCT FROM
                    ARRAY['tenant_id:uuid','id:uuid','actor_id:uuid','outbound_id:uuid','version:integer']
            OR (SELECT array_agg(attname::text||':'||atttypid::regtype::text ORDER BY attnum) FROM pg_attribute
                WHERE attrelid=versions AND attnum>0 AND NOT attisdropped) IS DISTINCT FROM
                    ARRAY['tenant_id:uuid','id:uuid','version:integer','state:text','payload:jsonb']
            OR EXISTS (SELECT FROM pg_constraint WHERE conrelid IN (heads,versions) AND NOT convalidated)
            OR (SELECT count(*) FROM pg_constraint WHERE conrelid IN (heads,versions) AND contype='f')<>2
            OR (SELECT count(*) FROM pg_constraint WHERE conrelid IN (heads,versions) AND contype='f'
                AND ((conrelid=heads AND confrelid=versions AND conkey=ARRAY[1,2,5]::smallint[]
                      AND confkey=ARRAY[1,2,3]::smallint[] AND condeferrable AND condeferred)
                  OR (conrelid=versions AND confrelid=heads AND conkey=ARRAY[1,2]::smallint[]
                      AND confkey=ARRAY[1,2]::smallint[] AND NOT condeferrable)))<>2
            OR (SELECT count(*) FROM pg_constraint WHERE contype='p' AND ((conrelid=heads AND conkey=ARRAY[1,2]::smallint[])
                   OR (conrelid=versions AND conkey=ARRAY[1,2,3]::smallint[])))<>2 THEN
            RAISE EXCEPTION 'rma-stage-candidate-layout-rejected';
        END IF;
        {empty}
    END
    $candidate_ready$;
    """


def import_sql(schema):
    return f"""
    \\ir tenant-rows.sql
    SET CONSTRAINTS ALL IMMEDIATE;
    DO $candidate_rows$
    DECLARE
        p jsonb := current_setting('{PLAN}')::jsonb;
        t uuid := (p->'key'->>'tenantId')::uuid;
    BEGIN
        IF (SELECT count(*) FROM {schema}.heads)<>(p->'counts'->>'heads')::bigint
           OR (SELECT count(*) FROM {schema}.versions)<>(p->'counts'->>'versions')::bigint
           OR EXISTS (SELECT FROM {schema}.heads WHERE tenant_id IS DISTINCT FROM t)
           OR EXISTS (SELECT FROM {schema}.versions WHERE tenant_id IS DISTINCT FROM t) THEN
            RAISE EXCEPTION 'rma-stage-candidate-data-rejected';
        END IF;
    END
    $candidate_rows$;
    CREATE SCHEMA {RECEIPTS};
    REVOKE ALL ON SCHEMA {RECEIPTS} FROM PUBLIC;
    CREATE TABLE {RECEIPTS}.receipt (
        singleton boolean PRIMARY KEY CHECK (singleton), input jsonb NOT NULL, result_json text NOT NULL);
    REVOKE ALL ON {RECEIPTS}.receipt FROM PUBLIC;
    INSERT INTO {RECEIPTS}.receipt
    SELECT true,p->'key',json_build_object('outcome','IMPORTED','recoveryReady',false,'profile','{PROFILE}',
        'binding',p->'key','format',p->>'format','counts',p->'counts','sourceMigration',p->'migration')::text
    FROM (SELECT current_setting('{PLAN}')::jsonb p) s;
    SELECT result_json FROM {RECEIPTS}.receipt;
    """


def receipt_sql(*, schema=RECEIPTS, table="receipt", profile=PROFILE, show_result=True):
    sql = f"""
    DO $candidate_receipt$
    DECLARE
        k jsonb := current_setting('{PLAN}')::jsonb->'key';
        owner oid := to_regrole(quote_ident(current_user));
        original text;
    BEGIN
        IF NOT EXISTS (SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE c.oid=to_regclass('{schema}.{table}') AND c.relkind='r' AND c.relowner=owner AND n.nspowner=owner
              AND NOT c.relrowsecurity AND NOT EXISTS (SELECT FROM aclexplode(n.nspacl) a WHERE a.grantee<>owner)
              AND NOT EXISTS (SELECT FROM aclexplode(c.relacl) a WHERE a.grantee<>owner)
              AND NOT EXISTS (SELECT FROM pg_attribute t,LATERAL aclexplode(t.attacl) a
                  WHERE t.attrelid=c.oid AND a.grantee<>owner)) THEN
            RAISE EXCEPTION 'rma-stage-candidate-receipt-not-found';
        END IF;
        SELECT result_json INTO original FROM {schema}.{table} WHERE singleton AND input=k
            AND result_json::jsonb->'binding'=k AND result_json::jsonb->>'profile'='{profile}'
            AND result_json::jsonb->>'outcome'='IMPORTED' AND result_json::jsonb->'recoveryReady'='false'::jsonb;
        IF original IS NULL THEN
            RAISE EXCEPTION 'rma-stage-candidate-receipt-key-rejected';
        END IF;
        PERFORM set_config('{PLAN}_receipt',original,true);
    END
    $candidate_receipt$;
    """
    return sql + (f"SELECT current_setting('{PLAN}_receipt');\n    " if show_result else "")


def journal_driver(args, output, target, tenant, digest, plan, recover):
    journal_receipt = receipt_sql(table=candidate_journal.RECEIPT, profile=candidate_journal.PROFILE)
    if recover:
        return target_sql(plan, True) + receipt_sql(show_result=False) + journal_receipt

    _, metadata = stage.checked_bundle(args.bundle, output, tenant, digest, target, args.file_content_root,
                                       include_stock_outbox=True)
    if metadata["journalCounts"] is None:
        raise stage.StageError("rma-stage-candidate-journal-required")
    schema = "t_" + tenant.hex
    plan.update(format=metadata["format"], journalCounts=metadata["journalCounts"])
    return target_sql(plan, False) + receipt_sql(show_result=False) + f"""
    SELECT to_regclass('{RECEIPTS}.{candidate_journal.RECEIPT}') IS NOT NULL AS has_journal_receipt \\gset
    \\if :has_journal_receipt
    {journal_receipt}
    \\else
    {ready_sql(schema, expect_empty=False)}
    {candidate_journal.import_sql(schema, PLAN, RECEIPTS)}
    \\endif
    """


def execute(args):
    pg_bin = args.pg_bin.resolve(strict=True)
    target = archive.PhysicalTarget.from_mapping(json.loads(args.target_file.read_text(encoding="utf-8-sig")))
    tenant = archive.guid(args.tenant)
    digest = stage.digest_pin(args.manifest_sha256)
    candidate = archive.guid(args.candidate_id)
    installation, commission_pin = commissioning_pin(args.commission_receipt, target)
    if not args.username or "\0" in args.username:
        raise stage.StageError("rma-stage-invalid-username")
    output = args.output.absolute()
    archive.private_directory(output)
    key = dict(target=target.mapping(), tenantId=str(tenant), manifestSha256=digest, candidateId=str(candidate),
               commissionReceiptSha256=commission_pin)
    plan = dict(key=key, installationId=installation)
    recover = args.action.startswith("recover")
    if args.action.endswith("-journal"):
        driver = journal_driver(args, output, target, tenant, digest, plan, recover)
    elif recover:
        driver = target_sql(plan, True) + receipt_sql()
    else:
        checked, metadata = stage.checked_bundle(args.bundle, output, tenant, digest, target, args.file_content_root,
                                                 include_stock_outbox=True)
        schema = "t_" + tenant.hex
        emit_rows(pg_bin, checked, output, schema)
        counts = {name: metadata["counts"][name] for name in COPY_COLUMNS}
        plan.update(format=metadata["format"], counts=counts, migration=metadata["migration"])
        driver = target_sql(plan, False) + f"""
        SELECT to_regnamespace('{RECEIPTS}') IS NOT NULL AS has_receipt \\gset
        \\if :has_receipt
        {receipt_sql()}
        \\else
        {ready_sql(schema)}
        {import_sql(schema)}
        \\endif
        """
    (output / "driver.sql").write_text(driver, encoding="utf-8")
    return stage.run_driver(pg_bin, target, args.username, output, "recover" if recover else "stage")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    for name in ("import", "recover", "import-journal", "recover-journal"):
        command = actions.add_parser(name)
        for field in ("pg-bin", "target-file", "commission-receipt", "output"):
            command.add_argument("--" + field, type=Path, required=True)
        for field in ("username", "tenant", "candidate-id", "manifest-sha256"):
            command.add_argument("--" + field, required=True)
        if name.startswith("import"):
            command.add_argument("--bundle", type=Path, required=True)
            command.add_argument("--file-content-root", type=Path, help="original private asset root for applicable formats")
    try:
        sys.stdout.buffer.write(execute(parser.parse_args()) + b"\n")
    except (stage.StageError, archive.ArchiveError, OSError, ValueError, subprocess.SubprocessError) as error:
        print("candidate import rejected:", error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
