#!/usr/bin/env python3
"""Adopt pinned FileContent originals in a closed, independent Core candidate."""

import argparse
import csv
import json
from pathlib import Path
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
import archive
import candidate_core as candidate
import file_content_history as history
import stage


PROFILE = "cp6-file-content-candidate-import-v1"
RECEIPTS = "cp6_file_content_candidate_import_v1"
PLAN = candidate.PLAN
CONTENTS, HEADS, EVENTS = (history.MEMBERS[key].relation for key in ("contents", "heads", "events"))
PARENTS = history.MEMBERS["parent-versions"].relation
JOURNAL = archive.JOURNAL_TABLE
OWNER = history.assets.OWNER


def temporary(key):
    return "file_import_" + key.replace("-", "_")


def temporary_rows(key):
    return f"SELECT {', '.join(history.MEMBERS[key].columns)} FROM pg_temp.{temporary(key)}"


def ready_sql():
    return f"""
    DO $file_candidate_ready$
    DECLARE
        tenant uuid := (current_setting('{PLAN}')::jsonb->'key'->>'tenantId')::uuid;
        db_owner oid := to_regrole(quote_ident(current_user));
        db pg_database%ROWTYPE;
    BEGIN
        SELECT * INTO STRICT db FROM pg_database WHERE datname=current_database();
        IF EXISTS (SELECT FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid()
                   AND backend_type='client backend') THEN
            RAISE EXCEPTION 'rma-stage-file-candidate-not-isolated';
        END IF;
        IF current_setting('session_replication_role')<>'origin'
           OR EXISTS (SELECT FROM pg_event_trigger WHERE evtenabled<>'D')
           OR EXISTS (SELECT FROM pg_default_acl WHERE defaclrole=db_owner AND defaclobjtype IN ('r','n'))
           OR NOT EXISTS (SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
               WHERE c.oid=to_regclass('cp6_privacy_disposition_v1.restore_admission') AND c.relkind='r'
                   AND c.relowner=db_owner AND n.nspowner=db_owner AND NOT c.relrowsecurity)
           OR to_regprocedure('cp6_authentication_v1.acquire()') IS NULL THEN
            RAISE EXCEPTION 'rma-stage-file-candidate-layout-rejected';
        END IF;
        IF EXISTS (SELECT FROM aclexplode(coalesce(db.datacl,acldefault('d',db_owner))) a WHERE a.grantee<>db_owner) THEN
            IF EXISTS (SELECT FROM aclexplode(coalesce(db.datacl,acldefault('d',db_owner))) a
                       WHERE a.grantee<>db_owner AND (a.grantee=0 OR a.grantor<>db_owner OR a.is_grantable
                                                      OR a.privilege_type<>'CONNECT'))
               OR NOT EXISTS (SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                   WHERE c.oid=to_regclass('cp6_tenancy_v1.login_tenant') AND c.relkind='r'
                     AND c.relowner=db_owner AND n.nspowner=db_owner AND NOT c.relrowsecurity) THEN
                RAISE EXCEPTION 'rma-stage-file-candidate-acl-rejected';
            END IF;
            IF EXISTS (SELECT FROM aclexplode(db.datacl) a WHERE a.grantee<>db_owner AND NOT EXISTS (
                  SELECT FROM cp6_tenancy_v1.login_tenant l JOIN pg_roles r ON r.oid=l.role_oid AND r.rolname=l.role_name
                  WHERE r.oid=a.grantee AND r.rolcanlogin AND NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb
                        OR r.rolcreaterole OR r.rolreplication)
                    AND NOT EXISTS (SELECT FROM pg_auth_members m WHERE m.roleid=r.oid OR m.member=r.oid))) THEN
                RAISE EXCEPTION 'rma-stage-file-candidate-acl-rejected';
            END IF;
        END IF;
        PERFORM cp6_authentication_v1.acquire();
        IF EXISTS (SELECT FROM cp6_privacy_disposition_v1.restore_admission) THEN
            RAISE EXCEPTION 'rma-stage-file-candidate-admission-exists';
        END IF;
        IF NOT EXISTS (SELECT FROM public."Sys_Tenants" WHERE "Id"=tenant) THEN
            RAISE EXCEPTION 'rma-stage-file-candidate-tenant-rejected';
        END IF;
    END
    $file_candidate_ready$;
    LOCK TABLE {history.PARENT_HEADS},{PARENTS} IN SHARE MODE;
    LOCK TABLE {CONTENTS},{HEADS},{EVENTS},{JOURNAL} IN SHARE ROW EXCLUSIVE MODE;
    DO $file_candidate_layout$
    DECLARE
        tenant uuid := (current_setting('{PLAN}')::jsonb->'key'->>'tenantId')::uuid;
        db_owner oid := to_regrole(quote_ident(current_user));
    BEGIN
        IF NOT ({history.layout_predicate('db_owner')})
           OR NOT EXISTS (SELECT FROM public."__EFMigrationsHistory" WHERE "MigrationId"='{history.assets.MIGRATION}')
           OR NOT EXISTS (SELECT FROM pg_proc WHERE oid=to_regprocedure('cp6_file_content_v1.ready()') AND proowner=db_owner)
           OR NOT cp6_file_content_v1.ready()
           OR (SELECT count(*) FROM pg_trigger WHERE tgrelid IN ('{CONTENTS}'::regclass,'{HEADS}'::regclass,
                     '{EVENTS}'::regclass) AND NOT tgisinternal)<>5
           OR (SELECT count(*) FROM (VALUES
                 ('{CONTENTS}','{history.PARENT_HEADS}',ARRAY[1,4]::smallint[],ARRAY[1,2]::smallint[],false),
                 ('{HEADS}','{CONTENTS}',ARRAY[1,2]::smallint[],ARRAY[1,2]::smallint[],false),
                 ('{EVENTS}','{HEADS}',ARRAY[1,2]::smallint[],ARRAY[1,2]::smallint[],false),
                 ('{HEADS}','{EVENTS}',ARRAY[1,2,3]::smallint[],ARRAY[1,2,3]::smallint[],true)
               ) e(relation,parent,child_key,parent_key,deferred) JOIN pg_constraint c
                 ON c.conrelid=to_regclass(e.relation) AND c.confrelid=to_regclass(e.parent)
                   AND c.contype='f' AND c.convalidated AND c.conkey=e.child_key AND c.confkey=e.parent_key
                   AND c.condeferrable=e.deferred AND c.condeferred=e.deferred)<>4 THEN
            RAISE EXCEPTION 'rma-stage-file-candidate-layout-rejected';
        END IF;
        IF EXISTS (SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
             WHERE c.oid IN ('{CONTENTS}'::regclass,'{HEADS}'::regclass,'{EVENTS}'::regclass) AND (
                 EXISTS (SELECT FROM aclexplode(n.nspacl) a WHERE a.grantee<>db_owner)
                 OR EXISTS (SELECT FROM aclexplode(c.relacl) a WHERE a.grantee<>db_owner)
                 OR EXISTS (SELECT FROM pg_attribute t,LATERAL aclexplode(t.attacl) a
                            WHERE t.attrelid=c.oid AND a.grantee<>db_owner))) THEN
            RAISE EXCEPTION 'rma-stage-file-candidate-acl-rejected';
        END IF;
        IF EXISTS (SELECT FROM {CONTENTS} WHERE tenant_id=tenant)
           OR EXISTS (SELECT FROM {HEADS} WHERE tenant_id=tenant)
           OR EXISTS (SELECT FROM {EVENTS} WHERE tenant_id=tenant)
           OR EXISTS (SELECT FROM {JOURNAL} WHERE tenant_id=tenant AND owner={archive.literal(OWNER)}) THEN
            RAISE EXCEPTION 'rma-stage-file-candidate-not-empty';
        END IF;
    END
    $file_candidate_layout$;
    """


def staged_sql():
    return archive.JOURNAL_COPY_SETTINGS + "\n" + "\n".join(f"""
    CREATE TEMP TABLE {temporary(key)} (LIKE {part.relation} INCLUDING CONSTRAINTS,
        PRIMARY KEY ({', '.join(part.key)})) ON COMMIT DROP;
    \\copy pg_temp.{temporary(key)} ({', '.join(part.columns)}) FROM 'bundle/{history.filename(key)}' WITH (FORMAT csv, HEADER MATCH)
    """ for key, part in history.MEMBERS.items())


def adopt_sql(source_format=history.FORMAT):
    temp = {key: "pg_temp." + temporary(key) for key in history.MEMBERS}
    rows = {key: temporary_rows(key) for key in history.MEMBERS}
    scope_errors = " UNION ALL ".join(f"SELECT 1 FROM {table} WHERE tenant_id IS DISTINCT FROM tenant" for table in temp.values())
    checks = []
    for key in ("contents", "heads", "events", "operations"):
        part = history.MEMBERS[key]
        where = "tenant_id=tenant" + (f" AND owner={archive.literal(OWNER)}" if key == "operations" else "")
        checks.append(f"(SELECT count(*) FROM {part.relation} WHERE {where})<>(p->'counts'->>'{key}')::bigint"
                      f" OR EXISTS ({rows[key]} EXCEPT SELECT {', '.join(part.columns)} FROM {part.relation} WHERE {where})")
    exact_rows = "\n           OR ".join(checks)
    return f"""
    DO $file_candidate_adopt$
    DECLARE
        p jsonb := current_setting('{PLAN}')::jsonb;
        tenant uuid := (p->'key'->>'tenantId')::uuid;
        graph jsonb;
        event record;
    BEGIN
        IF EXISTS ({scope_errors}) OR EXISTS (SELECT FROM {temp['operations']} WHERE owner<>{archive.literal(OWNER)})
           OR EXISTS (SELECT FROM {temp['events']} e LEFT JOIN {temp['heads']} h USING(tenant_id,file_id)
                       WHERE h.file_id IS NULL)
           OR EXISTS (SELECT FROM {temp['parent-versions']} v WHERE NOT EXISTS (SELECT FROM {temp['contents']} c
                        WHERE (c.tenant_id,c.parent_id,c.parent_version::bigint,c.parent_digest)=
                            (v.tenant_id,v.revision_id,v.revision,v.body_digest))) THEN
            RAISE EXCEPTION 'rma-stage-file-candidate-graph-rejected';
        END IF;
        SELECT result INTO graph FROM ({history.graph_sql(rows)}) g(result);
        IF graph->'valid' IS DISTINCT FROM 'true'::jsonb OR graph-'valid' IS DISTINCT FROM p->'counts'
           OR graph->'heads' IS DISTINCT FROM graph->'contents' THEN
            RAISE EXCEPTION 'rma-stage-file-candidate-graph-rejected';
        END IF;
        IF EXISTS (SELECT FROM {temp['parent-versions']} v LEFT JOIN {PARENTS} live
                     USING(tenant_id,revision_id,revision)
                   LEFT JOIN {history.PARENT_HEADS} h USING(tenant_id,revision_id)
                   WHERE live.revision_id IS NULL OR h.revision IS NULL OR h.revision<v.revision
                      OR (v.body_bytes,v.body_digest,v.actor_id,v.recorded_at) IS DISTINCT FROM
                         (live.body_bytes,live.body_digest,live.actor_id,live.recorded_at)) THEN
            RAISE EXCEPTION 'rma-stage-file-candidate-parent-rejected';
        END IF;
        INSERT INTO {CONTENTS} ({', '.join(history.MEMBERS['contents'].columns)}) {rows['contents']};
        FOR event IN SELECT * FROM {temp['events']} ORDER BY file_id,revision LOOP
            IF event.revision=1 THEN
                INSERT INTO {HEADS}(tenant_id,file_id,revision) VALUES (event.tenant_id,event.file_id,event.revision);
            ELSE
                UPDATE {HEADS} h SET revision=event.revision
                WHERE (h.tenant_id,h.file_id,h.revision)=(event.tenant_id,event.file_id,event.revision-1);
            END IF;
            INSERT INTO {EVENTS}(tenant_id,file_id,revision,body_bytes,body_digest,actor_id,recorded_at)
            VALUES (event.tenant_id,event.file_id,event.revision,event.body_bytes,event.body_digest,event.actor_id,event.recorded_at);
        END LOOP;
        INSERT INTO {JOURNAL} ({', '.join(archive.JOURNAL_COLUMNS)}) {rows['operations']};
        IF {exact_rows} THEN
            RAISE EXCEPTION 'rma-stage-file-candidate-data-rejected';
        END IF;
    END
    $file_candidate_adopt$;
    SET CONSTRAINTS ALL IMMEDIATE;
    CREATE SCHEMA {RECEIPTS};
    REVOKE ALL ON SCHEMA {RECEIPTS} FROM PUBLIC;
    CREATE TABLE {RECEIPTS}.receipt (
        singleton boolean PRIMARY KEY CHECK(singleton), input jsonb NOT NULL, result_json text NOT NULL);
    REVOKE ALL ON {RECEIPTS}.receipt FROM PUBLIC;
    INSERT INTO {RECEIPTS}.receipt
    SELECT true,p->'key',json_build_object('outcome','IMPORTED','recoveryReady',false,'profile','{PROFILE}',
        'binding',p->'key','format',{archive.literal(source_format)},'counts',p->'counts')::text
    FROM (SELECT current_setting('{PLAN}')::jsonb p) s;
    SELECT result_json FROM {RECEIPTS}.receipt;
    """


def execute(args):
    pg_bin = args.pg_bin.resolve(strict=True)
    target = archive.PhysicalTarget.from_mapping(json.loads(args.target_file.read_text(encoding="utf-8-sig")))
    tenant, candidate_id = archive.guid(args.tenant), archive.guid(args.candidate_id)
    pin = stage.digest_pin(args.manifest_sha256)
    if not args.username or "\0" in args.username:
        raise stage.StageError("rma-stage-invalid-username")
    recover = args.action == "recover"
    v11 = not recover and args.include_file_content_history
    root = args.file_content_root
    if not root.is_absolute() or ".." in root.parts:
        raise stage.StageError("rma-stage-file-candidate-root-rejected")
    if not recover:
        root = history.assets.checked_root(root)
    root = root.resolve(strict=not recover)
    output = history.checked_output(args.output.absolute(), root)
    if not recover:
        bundle = args.bundle.absolute()
        if v11:
            bundle = history.assets.checked_root(bundle).resolve(strict=True)
        else:
            bundle = history.checked_bundle_path(bundle)
        history.checked_output(output, bundle)
        if root == bundle or root in bundle.parents or bundle in root.parents:
            raise stage.StageError("rma-stage-file-candidate-root-rejected")
    archive.private_directory(output)
    plan = dict(key=dict(target=target.mapping(), candidateId=str(candidate_id), tenantId=str(tenant),
                         manifestSha256=pin, fileContentRoot=root.as_posix()))
    receipt = candidate.receipt_sql(schema=RECEIPTS, profile=PROFILE)
    if recover:
        driver = candidate.target_sql(plan, True) + receipt
    else:
        if v11:
            _, verified = stage.checked_bundle(bundle, output, tenant, pin, target, root,
                                               include_file_content_history=True)
            members = verified["fileContentHistory"]["members"]
            source_format = verified["format"]
        else:
            checked = output / "bundle"
            checked.mkdir(mode=0o700)
            for name in (history.MANIFEST, *(history.filename(key) for key in history.MEMBERS)):
                shutil.copyfile(bundle / name, checked / name, follow_symlinks=False)
            manifest = history.verify_bundle(checked, tenant, pin, root)
            source = archive.PhysicalTarget.from_mapping(manifest["source"])
            if (source.system_identifier, source.database_oid) == (target.system_identifier, target.database_oid):
                raise stage.StageError("rma-stage-target-is-source")
            members = manifest["members"]
            source_format = manifest["format"]
        plan["counts"] = {key: members[history.filename(key)]["rows"] for key in history.MEMBERS}
        driver = candidate.target_sql(plan, False) + f"""
        SELECT to_regnamespace('{RECEIPTS}') IS NOT NULL AS has_receipt \\gset
        \\if :has_receipt
        {receipt}
        \\else
        {ready_sql()}
        {staged_sql()}
        {adopt_sql(source_format)}
        \\endif
        """
    (output / "driver.sql").write_text(driver, encoding="utf-8")
    return stage.run_driver(pg_bin, target, args.username, output, "recover" if recover else "stage")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    for name in ("import", "recover"):
        command = actions.add_parser(name)
        for field in ("pg-bin", "target-file", "file-content-root", "output"):
            command.add_argument("--" + field, type=Path, required=True)
        for field in ("username", "tenant", "candidate-id", "manifest-sha256"):
            command.add_argument("--" + field, required=True)
        if name == "import":
            command.add_argument("--bundle", type=Path, required=True)
            command.add_argument("--include-file-content-history", action="store_true",
                                 help="import the FileContent originals from a complete v11 RMA archive")
    try:
        sys.stdout.buffer.write(execute(parser.parse_args()) + b"\n")
    except (stage.StageError, archive.ArchiveError, OSError, ValueError, csv.Error, subprocess.SubprocessError) as error:
        print("FileContent candidate rejected:", error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
