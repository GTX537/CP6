#!/usr/bin/env python3
"""Stage and recover the pinned W402 v9 history in disposable PostgreSQL."""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import sys
from unittest.mock import patch

sys.dont_write_bytecode = True
from verify_stage import (A, B, BASE_ENV, OWNER, Fixture, assert_empty, check_acl, ident, invoke, new_target,
                          pinned, put_json, q, require, token)
from verify_file_content_stage import invoke_stage
import archive
import privacy_evidence
import stage


AUDIT = "rma_archive_stage.security_audit_snapshot"
HOLDS = "rma_archive_stage.legal_hold_history_snapshot"


def fail_after_privacy_copy(f, target, source):
    bundle, tenant, digest = source
    request = f.root / "v9-late-target.json"
    put_json(request, target)
    args = argparse.Namespace(action="stage", pg_bin=f.args.pg_bin, target_file=request, username=OWNER,
                              tenant=tenant, manifest_sha256=digest, bundle=bundle,
                              file_content_root=f.args.file_content_root, output=f.root / "v9-late-native")
    run_driver = stage.run_driver

    def inject(pg_bin, physical, username, output, action):
        script = output / "driver.sql"
        driver = script.read_text(encoding="utf-8")
        marker = "CREATE TABLE rma_archive_stage.receipt"
        require(driver.count(marker) == 1 and driver.index("legal-hold-history.csv") < driver.index(marker),
                "missing native late privacy stage boundary")
        columns = ["sequence+1000" if name == "sequence" else ident(name)
                   for name, _, _ in privacy_evidence.HOLDS.columns]
        fault = f"INSERT INTO {HOLDS} SELECT {', '.join(columns)} FROM {HOLDS} ORDER BY sequence LIMIT 1;\n"
        script.write_text(driver.replace(marker, fault + marker, 1), encoding="utf-8")
        return run_driver(pg_bin, physical, username, output, action)

    with patch.dict(os.environ, {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, clear=True), \
            patch.object(stage, "run_driver", inject):
        try:
            stage.execute(args)
        except stage.StageError as error:
            require(str(error) == "rma-stage-native-failed (see native logs)", "wrong late stage rejection")
        else:
            raise AssertionError("native late hold version duplicate was accepted")
    stderr = (args.output / "stage.stderr").read_bytes()
    require(b"23505" in stderr and b"legal_hold_history_snapshot_version_key" in stderr,
            "native hold version uniqueness did not reject the duplicate")


def check_native_scope(f, database, tenant):
    check_acl(f, database, tenant)
    audit_table = q(AUDIT)
    hold_table = q(HOLDS)
    require(f.sql(database, f"""
        SELECT to_regclass('public."Sys_SecurityLogs"') IS NULL
            AND to_regnamespace('cp6_privacy_disposition_v1') IS NULL
            AND to_regnamespace('cp6_scope_authorization_v1') IS NULL
            AND NOT EXISTS (SELECT FROM pg_class WHERE relkind='S' AND relnamespace='rma_archive_stage'::regnamespace)
            AND NOT EXISTS (SELECT FROM pg_roles r WHERE rolname IN ({q(A)},{q(B)}) AND (
                 has_database_privilege(r.oid,current_database(),'CONNECT')
                 OR has_schema_privilege(r.oid,'rma_archive_stage','USAGE')
                 OR has_table_privilege(r.oid,{audit_table},'SELECT,INSERT,UPDATE,DELETE')
                 OR has_table_privilege(r.oid,{hold_table},'SELECT,INSERT,UPDATE,DELETE')));
    """, label="v9-native-inactive-acl") == "t", "privacy history became active or exposed")
    f.sql(database, f"SET ROLE {ident(A)}; SELECT * FROM {AUDIT};", error="42501", label="ordinary-a-audit-denied")
    f.sql(database, f"SET ROLE {ident(B)}; SELECT * FROM {HOLDS};", error="42501", label="ordinary-b-hold-denied")
    wrong = q(token())
    f.sql(database, f'UPDATE {AUDIT} SET "TenantId"={wrong};', error="23514", label="audit-tenant-denied")
    f.sql(database, f"UPDATE {HOLDS} SET target_tenant={wrong};", error="23514", label="hold-tenant-denied")
    f.sql(database, f"UPDATE {HOLDS} SET environment_id={wrong};", error="23514", label="hold-environment-denied")


def check_original(f, database, bundle, relation, table):
    native = json.loads(f.sql(database, f"""
        SELECT json_build_object('columns',(SELECT json_agg(json_build_array(a.attname,t.typname,NOT a.attnotnull)
            ORDER BY a.attnum) FROM pg_attribute a JOIN pg_type t ON t.oid=a.atttypid
            WHERE a.attrelid={q(relation)}::regclass AND a.attnum>0 AND NOT a.attisdropped),
            'keys',(SELECT json_agg(json_build_array(c.contype,ARRAY(SELECT a.attname::text
                FROM unnest(c.conkey) WITH ORDINALITY k(num,ord) JOIN pg_attribute a
                ON a.attrelid=c.conrelid AND a.attnum=k.num ORDER BY k.ord)) ORDER BY c.contype)
                FROM pg_constraint c WHERE c.conrelid={q(relation)}::regclass AND c.contype IN ('p','u')));
    """, label=table.member + "-catalog"))
    require(native == {"columns": [list(column) for column in table.columns],
                       "keys": [["p" if i == 0 else "u", list(key)] for i, key in enumerate(table.keys)]},
            "staging changed native privacy types or keys")
    order = ", ".join(ident(key) for key in table.keys[0])
    with f.psql(database, app="W404-privacy-native-roundtrip") as process:
        sql = f"""SET TIME ZONE 'UTC'; SET DateStyle='ISO, YMD';
            COPY (SELECT * FROM {relation} ORDER BY {order}) TO STDOUT WITH (FORMAT csv, HEADER true);
        """
        out, err = f.collect(process, sql.encode("utf-8"))
    f.save(table.member, out)
    f.save(table.member + ".stderr", err)
    require(process.returncode == 0 and not err and out == (bundle / table.member).read_bytes(),
            "native stage changed original " + table.qualified_name)
    return archive.file_digest(f.root / table.member)


def exercise(f, source):
    bundle, tenant, digest = source
    target = new_target(f, "w404_privacy")
    database = target["Database"]
    fail_after_privacy_copy(f, target, source)
    assert_empty(f, database)
    invoke(f, "v9-rollback-no-receipt", "recover", target, tenant, digest, None, 1, "rma-stage-receipt-not-found")

    invoke_stage(f, "v9-stage-discarded", target, source, f.args.file_content_root, discard=True)
    original = f.sql(database, "SELECT result_json FROM rma_archive_stage.receipt;", label="v9-persisted-receipt").encode("utf-8")
    recovered = invoke(f, "v9-recover", "recover", target, tenant, digest)
    require(recovered == original, "same-target recovery changed the persisted receipt")
    receipt = json.loads(recovered)
    manifest = json.loads((bundle / "manifest.json").read_bytes())
    require(receipt["outcome"] == "STAGED" and receipt["recoveryReady"] is False
            and receipt["format"] == privacy_evidence.FORMAT and receipt["target"] == target
            and receipt["tenantId"] == tenant and receipt["manifestSha256"] == digest
            and receipt["privacyEvidence"] == manifest["privacyEvidence"],
            "v9 receipt did not bind the original history")
    check_native_scope(f, database, tenant)
    members = {table.member: check_original(f, database, bundle, relation, table)
               for relation, table in ((AUDIT, privacy_evidence.SECURITY_LOGS), (HOLDS, privacy_evidence.HOLDS))}
    f.save("actual.json", {"Target": target, "ManifestSha256": digest, "Members": members,
                           "Receipt": archive.file_digest(f.root / "v9-recover.stdout")})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "bundle", "file-content-root"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--manifest-sha256", required=True)
    parser.add_argument("--port", type=int, required=True)
    args = parser.parse_args()
    args.root = args.root.resolve(strict=True)
    args.pg_bin = args.pg_bin.resolve(strict=True)
    source = pinned(args.bundle, args.manifest_sha256, privacy_evidence.FORMAT)
    fixture = Fixture(args, "rma-stage-v9-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    fixture.save("disposable.json", {"PgData": "data", "Port": args.port, "Owner": "W404-01", "Disposable": True})
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        fixture.sql("postgres", "SELECT version();", label="pg-version")
        exercise(fixture, source)
    print("native v9 stage, late version-key rollback, exact receipt recovery, original bytes and ACL passed", flush=True)


if __name__ == "__main__":
    main()
