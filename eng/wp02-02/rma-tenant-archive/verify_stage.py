#!/usr/bin/env python3
"""Exercise atomic operator staging on a private PostgreSQL with pinned W363/W366/W374 bundles."""

import argparse
import csv
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "rma-tenant-migration"))
from verify import A, B, BASE_ENV, FLAGS, OWNER, Fixture, ident, q, require, token

import archive
import source_v3
import stage

STAGE = Path(__file__).resolve().with_name("stage.py")


def new_target(f, name, sealed=True):
    target = f.create_target(name)
    if sealed:
        f.sql("postgres", "REVOKE ALL ON DATABASE {} FROM PUBLIC;".format(ident(name)))
    return target


def put_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def assert_empty(f, db):
    require(f.sql(db, """
        SELECT NOT EXISTS (SELECT FROM pg_namespace WHERE left(nspname,2)='t_' OR nspname='rma_archive_stage');
    """) == "t", db + " retained a partial stage")


def invoke(f, label, action, target, tenant, digest, bundle=None, expected=0, error=None, discard=False):
    request = f.root / (label + "-target.json")
    put_json(request, target)
    output = f.root / (label + "-native")
    args = [sys.executable, "-B", str(STAGE), action, "--pg-bin", str(f.args.pg_bin),
            "--target-file", str(request), "--username", OWNER, "--tenant", tenant,
            "--manifest-sha256", digest, "--output", str(output)]
    if bundle is not None:
        args += ["--bundle", str(bundle)]
    result = subprocess.run(args, env={**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, cwd=f.root,
                            timeout=95, capture_output=True, creationflags=FLAGS)
    f.save(label + ".stderr", result.stderr)
    if not discard:
        f.save(label + ".stdout", result.stdout)
    f.save(label + ".process.json", {"ExitCode": result.returncode,
                                    "DiscardedStdoutBytes": len(result.stdout) if discard else 0})
    require(result.returncode == expected, "{} exit {}, expected {}".format(label, result.returncode, expected))
    if error:
        require(error.encode("utf-8") in result.stderr and not result.stdout, label + " wrong rejection")
    return None if discard else result.stdout.rstrip(b"\r\n")


def check_acl(f, db, tenant):
    schema = "t_" + tenant.replace("-", "")
    require(f.sql(db, """
        SELECT has_schema_privilege({a},{schema},'USAGE')
               AND has_table_privilege({a},{heads},'SELECT')
               AND NOT has_schema_privilege({b},{schema},'USAGE')
               AND NOT has_database_privilege({a},current_database(),'CONNECT')
               AND NOT has_database_privilege({b},current_database(),'CONNECT')
               AND NOT has_schema_privilege({a},'rma_archive_stage','USAGE')
               AND to_regnamespace('cp6_rma_tenant_v1') IS NULL
               AND to_regnamespace('cp6_main_owner_v1') IS NULL;
    """.format(a=q(A), b=q(B), schema=q(schema), heads=q(schema + ".heads"))) == "t",
        "original ACL or inactive boundary changed")


def check_journal(f, db, bundle):
    with (bundle / "rma-original.csv").open(encoding="utf-8", newline="") as source:
        original = list(csv.DictReader(source))
    staged = json.loads(f.sql(db, """
        SELECT jsonb_agg(to_jsonb(j) ORDER BY operation_id)
        FROM (
          SELECT tenant_id,owner,operation_id,actor_id,contract,state,
                 encode(input_bytes,'hex') AS input_bytes,input_sha256,encode(result_bytes,'hex') AS result_bytes,
                 object_ids,audit_json,committed_at AT TIME ZONE 'UTC' AS committed_at
          FROM rma_archive_stage.original_journal_snapshot
        ) j;
    """))
    require(len(original) == len(staged), "wrong staged journal cardinality")
    for original_row, staged_row in zip(original, staged):
        for name in ("tenant_id", "owner", "operation_id", "actor_id", "contract", "state"):
            require(original_row[name] == staged_row[name], "changed native " + name)
        for name in ("input_bytes", "result_bytes"):
            require((original_row[name][2:] if original_row[name] else None) == staged_row[name], "changed bytes " + name)
        captured_time = original_row["committed_at"]
        if captured_time.endswith("+00"):
            captured_time += ":00"
        require((original_row["input_sha256"] or None) == staged_row["input_sha256"]
                and json.loads(original_row["audit_json"]) == staged_row["audit_json"]
                and original_row["object_ids"].strip("{}").split(",") == (staged_row["object_ids"] or [""])
                and datetime.fromisoformat(captured_time).replace(tzinfo=None)
                == datetime.fromisoformat(staged_row["committed_at"]), "changed typed original fields")
    f.save("v2-staged-journal.json", staged)


def exercise(f, v1, v2):
    v1_bundle, v1_tenant, v1_digest = v1
    v2_bundle, v2_tenant, v2_digest = v2
    guard = new_target(f, "w370_guard")
    invoke(f, "wrong-digest", "stage", guard, v1_tenant, "0" * 64, v1_bundle, 1, "rma-stage-manifest-digest-mismatch")
    assert_empty(f, guard["Database"])
    invoke(f, "wrong-target", "stage", {**guard, "DatabaseOid": guard["DatabaseOid"] + 1},
           v1_tenant, v1_digest, v1_bundle, 1, "rma-stage-target-rejected")
    assert_empty(f, guard["Database"])
    f.sql(guard["Database"], "CREATE TABLE public.sentinel(v int); INSERT INTO public.sentinel VALUES(42);")
    invoke(f, "nonempty", "stage", guard, v1_tenant, v1_digest, v1_bundle, 1, "rma-stage-target-not-empty")
    require(f.sql(guard["Database"], "SELECT v FROM public.sentinel;") == "42", "existing candidate was changed")
    assert_empty(f, guard["Database"])

    v1_target = new_target(f, "w370_v1", sealed=False)
    invoke(f, "unsealed", "stage", v1_target, v1_tenant, v1_digest, v1_bundle, 1, "rma-stage-target-not-isolated")
    assert_empty(f, v1_target["Database"])
    f.sql("postgres", "REVOKE ALL ON DATABASE {} FROM PUBLIC;".format(ident(v1_target["Database"])))
    receipt_v1 = json.loads(invoke(f, "stage-v1", "stage", v1_target, v1_tenant, v1_digest, v1_bundle))
    require(receipt_v1["format"] == archive.FORMAT and receipt_v1["recoveryReady"] is False
            and receipt_v1["counts"] == {"heads": 1, "versions": 2, "history": 1, "outbounds": 1}
            and f.sql(v1_target["Database"], """
                SELECT to_regclass('rma_archive_stage.original_journal_snapshot') IS NULL;
            """) == "t", "v1 stage changed scope")
    check_acl(f, v1_target["Database"], v1_tenant)

    # The fixture vouches for the altered member only to force a late native COPY failure.
    malformed = f.root / "pinned-bad-journal"
    shutil.copytree(v2_bundle, malformed)
    journal = malformed / "rma-original.csv"
    data = journal.read_bytes()
    journal.write_bytes(data.replace(b"tenant_id,owner,", b"tenant_id,wrong_owner,", 1))
    manifest = json.loads((malformed / "manifest.json").read_text(encoding="utf-8"))
    manifest["members"]["rma-original.csv"] = archive.file_digest(journal)
    put_json(malformed / "manifest.json", manifest)
    malformed_digest = archive.file_digest(malformed / "manifest.json")["sha256"]

    v2_target = new_target(f, "w370_v2")
    invoke(f, "late-copy", "stage", v2_target, v2_tenant, malformed_digest, malformed, 1, "rma-stage-native-failed")
    error = (f.root / "late-copy-native/stage.stderr").read_bytes()
    require(b"22P04" in error and b"wrong_owner" in error, "native late journal COPY was not reached")
    assert_empty(f, v2_target["Database"])
    invoke(f, "rollback-no-receipt", "recover", v2_target, v2_tenant, malformed_digest, None, 1,
           "rma-stage-receipt-not-found")
    invoke(f, "stage-v2-discarded", "stage", v2_target, v2_tenant, v2_digest, v2_bundle, discard=True)
    original = f.sql(v2_target["Database"], "SELECT result_json FROM rma_archive_stage.receipt;").encode("utf-8")
    recovered = invoke(f, "recover-v2", "recover", v2_target, v2_tenant, v2_digest)
    require(recovered == original, "read-only recovery did not return the original persisted bytes")
    receipt = json.loads(recovered)
    require(receipt["format"] == archive.FORMAT_WITH_JOURNAL and receipt["outcome"] == "STAGED"
            and receipt["recoveryReady"] is False and receipt["target"] == v2_target
            and receipt["tenantId"] == v2_tenant and receipt["manifestSha256"] == v2_digest
            and receipt["journalCounts"] == {"rows": 2, "committed": 1, "closed": 1},
            "v2 receipt did not bind exact physical target and archive")
    invoke(f, "recover-wrong-digest", "recover", v2_target, v2_tenant, malformed_digest, None, 1,
           "rma-stage-receipt-not-found")
    invoke(f, "recover-wrong-tenant", "recover", v2_target, v1_tenant, v2_digest, None, 1,
           "rma-stage-receipt-not-found")
    invoke(f, "repeat-nonempty", "stage", v2_target, v2_tenant, v2_digest, v2_bundle, 1,
           "rma-stage-target-not-empty")
    require(f.sql(v2_target["Database"], "SELECT result_json FROM rma_archive_stage.receipt;").encode("utf-8") == original,
            "repeat changed persisted receipt")
    check_acl(f, v2_target["Database"], v2_tenant)
    check_journal(f, v2_target["Database"], v2_bundle)


def exercise_writer(f, v2):
    bundle, tenant, digest = v2
    executor = "cp6_rma_original_journal_executor_v1"
    target = new_target(f, "w370_writer")
    db = target["Database"]

    invoke(f, "writer-missing-role", "stage", target, tenant, digest, bundle, 1, "rma-stage-native-failed")
    error = (f.root / "writer-missing-role-native/stage.stderr").read_bytes()
    require(b"42704" in error and executor.encode("ascii") in error, "archive did not fail on its missing role")
    assert_empty(f, db)

    # These are restore prerequisites created by the fixture, not by the operator command.
    f.sql("postgres", "CREATE ROLE {} NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS "
          "NOCREATEDB NOCREATEROLE NOREPLICATION;".format(ident(executor)))
    original = invoke(f, "writer-stage", "stage", target, tenant, digest, bundle)
    receipt = json.loads(original)
    require(receipt["recoveryReady"] is False and receipt["counts"] ==
            {"heads": 1, "versions": 1, "history": 1, "outbounds": 1}
            and receipt["journalCounts"] == {"rows": 1, "committed": 1, "closed": 0}, "writer receipt mismatch")
    check_acl(f, db, tenant)
    schema = "t_" + tenant.replace("-", "")
    grants = json.loads(f.sql(db, """
        SELECT jsonb_build_object(
          'ordinaryRole',NOT (r.rolcanlogin OR r.rolsuper OR r.rolbypassrls OR r.rolinherit
                             OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
                         AND NOT has_database_privilege(r.oid,current_database(),'CONNECT'),
          'schema',(SELECT jsonb_agg(jsonb_build_array(x.privilege_type,x.is_grantable))
                      FROM aclexplode(n.nspacl) x WHERE x.grantee=r.oid),
          'column',(SELECT jsonb_agg(jsonb_build_array(c.relname,a.attname,x.privilege_type,x.is_grantable)
                                  ORDER BY c.relname,a.attname)
                    FROM pg_class c JOIN pg_attribute a ON a.attrelid=c.oid,
                         LATERAL aclexplode(a.attacl) x
                    WHERE c.relnamespace=n.oid AND x.grantee=r.oid),
          'table',EXISTS (SELECT FROM pg_class c, LATERAL aclexplode(c.relacl) x
                          WHERE c.relnamespace=n.oid AND x.grantee=r.oid))
        FROM pg_roles r,pg_namespace n WHERE r.rolname={role} AND n.nspname={schema};
    """.format(role=q(executor), schema=q(schema))))
    expected = {"heads": ("tenant_id", "id", "actor_id", "outbound_id", "version"),
                "versions": ("tenant_id", "id", "version", "state", "payload")}
    columns = sorted([table, column, "INSERT", False] for table, names in expected.items() for column in names)
    require(grants == {"ordinaryRole": True, "schema": [["USAGE", False]], "column": columns, "table": False},
            "native restore changed the exact writer role privileges")
    f.save("writer-staged-grants.json", grants)
    check_journal(f, db, bundle)
    for key, wrong in (("outcome", "READY"), ("recoveryReady", True),
                       ("tenantId", token()), ("manifestSha256", "0" * 64)):
        f.sql(db, """
            UPDATE rma_archive_stage.receipt
            SET result_json=jsonb_set(result_json::jsonb,ARRAY[{}],{}::jsonb)::text;
        """.format(q(key), q(json.dumps(wrong))))
        try:
            invoke(f, "writer-tampered-" + key, "recover", target, tenant, digest, None, 1,
                   "rma-stage-receipt-not-found")
        finally:
            f.sql(db, "UPDATE rma_archive_stage.receipt SET result_json={};".format(q(original.decode("utf-8"))))
    recovered = invoke(f, "writer-recover", "recover", target, tenant, digest)
    require(recovered == original, "original writer stage receipt changed")


def copy_staged(f, database, table, order, original):
    with f.psql(database, app="W377-roundtrip") as proc:
        sql = "COPY (SELECT * FROM rma_archive_stage.{} ORDER BY {}) TO STDOUT WITH (FORMAT csv, HEADER true);\n"
        out, err = f.collect(proc, sql.format(table, order).encode("utf-8"))
        f.save(table + ".csv", out)
        f.save(table + ".stderr", err)
        require(proc.returncode == 0 and out == original.read_bytes(), "staging changed original " + table)


def stage_with_lineage_fault(f, target, tenant, digest, bundle):
    request = f.root / "v3-late-target.json"
    put_json(request, target)
    args = argparse.Namespace(action="stage", pg_bin=f.args.pg_bin, target_file=request, username=OWNER,
                              tenant=tenant, manifest_sha256=digest, bundle=bundle, output=f.root / "v3-late-native")
    run_driver = stage.run_driver

    def inject(pg_bin, physical, username, output, action):
        # Corrupt the just-loaded lineage text in the same native transaction,
        # without changing the trusted package, digest, or production entry.
        driver = output / "driver.sql"
        script = driver.read_text(encoding="utf-8")
        marker = "DO $cp6_stage_check$"
        require(script.count(marker) == 1, "missing native stage verification boundary")
        fault = "UPDATE rma_archive_stage.outbound_lineage_snapshot SET outbound_no=rtrim(outbound_no);\n"
        driver.write_text(script.replace(marker, fault + marker, 1), encoding="utf-8")
        return run_driver(pg_bin, physical, username, output, action)

    with patch.dict(os.environ, {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, clear=True), \
            patch.object(stage, "run_driver", inject):
        try:
            stage.execute(args)
        except stage.StageError as error:
            require(str(error) == "rma-stage-source-lineage-mismatch", "late native lineage check did not reject")
            f.save("v3-late-error.json", {"Error": str(error), "ManifestSha256": digest})
        else:
            raise AssertionError("late lineage corruption was accepted")
    stderr = (args.output / "stage.stderr").read_bytes()
    require(b"P0001" in stderr and b"rma-stage-source-lineage-mismatch" in stderr, "missing native late lineage error")


def exercise_source(f, v3):
    bundle, tenant, digest = v3
    target = new_target(f, "w377_source")
    database = target["Database"]
    stage_with_lineage_fault(f, target, tenant, digest, bundle)
    assert_empty(f, database)
    invoke(f, "v3-rollback-no-receipt", "recover", target, tenant, digest, None, 1, "rma-stage-receipt-not-found")

    invoke(f, "v3-stage-discarded", "stage", target, tenant, digest, bundle, discard=True)
    original = f.sql(database, "SELECT result_json FROM rma_archive_stage.receipt;").encode("utf-8")
    recovered = invoke(f, "v3-recover", "recover", target, tenant, digest)
    require(recovered == original, "v3 recovery changed persisted receipt bytes")
    receipt = json.loads(recovered)
    require(receipt["format"] == source_v3.FORMAT and receipt["target"] == target
            and receipt["tenantId"] == tenant and receipt["manifestSha256"] == digest
            and receipt["outcome"] == "STAGED" and receipt["recoveryReady"] is False
            and receipt["counts"] == {"heads": 1, "versions": 1, "history": 1, "outbounds": 1}
            and receipt["journalCounts"] == {"rows": 1, "committed": 1, "closed": 0}
            and receipt["sourceCounts"] == {"headers": 1, "details": 3, "lineage": 1}, "v3 stage receipt mismatch")

    copy_staged(f, database, "outbound_snapshot", "outbound_id", bundle / "outbounds.csv")
    copy_staged(f, database, "outbound_detail_snapshot", "outbound_id,line_no,outbound_line_id",
                bundle / "rma-source-details.csv")
    copy_staged(f, database, "outbound_lineage_snapshot", "outbound_line_id", bundle / "rma-source-lineage.csv")
    check_journal(f, database, bundle)
    check_acl(f, database, tenant)
    require(f.sql(database, """
        SELECT format_type(atttypid,atttypmod) FROM pg_attribute
        WHERE attrelid='rma_archive_stage.outbound_detail_snapshot'::regclass AND attname='shipped_qty';
    """) == "numeric(21,8)", "v3 stage changed native shipped quantity type")
    require(f.sql(database, """
        SELECT (d.outbound_no=h.outbound_no)::text || '|' ||
            (bpcharsend(d.outbound_no)<>bpcharsend(h.outbound_no))::text || '|' ||
            (l.outbound_no=convert_from(bpcharsend(h.outbound_no),'UTF8'))::text
        FROM rma_archive_stage.outbound_lineage_snapshot l
        JOIN rma_archive_stage.outbound_detail_snapshot d USING (tenant_id,outbound_line_id)
        JOIN rma_archive_stage.outbound_snapshot h USING (tenant_id,outbound_id);
    """, label="v3-native-bpchar-lineage") == "true|true|true", "v3 native BPCHAR/text semantics changed")
    require(f.sql(database, """
        BEGIN;
        UPDATE rma_archive_stage.outbound_detail_snapshot
        SET outbound_no=lower(outbound_no::text) WHERE line_no=2;
        SELECT d.outbound_no=h.outbound_no AND bpcharsend(d.outbound_no)<>bpcharsend(h.outbound_no)
        FROM rma_archive_stage.outbound_detail_snapshot d JOIN rma_archive_stage.outbound_snapshot h
             USING (tenant_id,outbound_id) WHERE d.line_no=2;
        ROLLBACK;
    """, label="v3-native-case-fk") == "t", "source ICU case-insensitive FK semantics changed")
    f.sql(database, "UPDATE rma_archive_stage.outbound_detail_snapshot SET outbound_no='FOREIGN' WHERE line_no=2;",
          error="23503", label="v3-foreign-document-denied")
    f.sql(database, "UPDATE rma_archive_stage.outbound_detail_snapshot SET tenant_id={} WHERE line_no=2;".format(q(token())),
          error="23514", label="v3-foreign-tenant-denied")
    require(f.sql(database, "SELECT to_regnamespace('cp6_rma_original_journal_v1') IS NULL;") == "t",
            "source staging activated the original journal")


def pinned(bundle, digest, expected_format):
    bundle = bundle.resolve(strict=True)
    require(archive.file_digest(bundle / "manifest.json")["sha256"] == digest, "fixture manifest pin mismatch")
    manifest = json.loads((bundle / "manifest.json").read_text(encoding="utf-8"))
    require(manifest["format"] == expected_format, "fixture has the wrong version")
    return bundle, manifest["tenantId"], digest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ("root", "pg-bin"):
        parser.add_argument("--" + key, type=Path, required=True)
    for version in ("v1", "v2", "v3"):
        parser.add_argument("--" + version + "-bundle", type=Path)
        parser.add_argument("--" + version + "-sha256")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--writer-only", action="store_true")
    mode.add_argument("--v3-only", action="store_true")
    parser.add_argument("--port", type=int, required=True)
    args = parser.parse_args()
    required = ("v3",) if args.v3_only else ("v2",) if args.writer_only else ("v1", "v2")
    for version in required:
        if getattr(args, version + "_bundle") is None or getattr(args, version + "_sha256") is None:
            parser.error("--{0}-bundle and --{0}-sha256 are required for this fixture".format(version))
    args.root = args.root.resolve(strict=True)
    args.pg_bin = args.pg_bin.resolve(strict=True)
    versions = {"v1": archive.FORMAT, "v2": archive.FORMAT_WITH_JOURNAL, "v3": source_v3.FORMAT}
    bundles = {version: pinned(getattr(args, version + "_bundle"), getattr(args, version + "_sha256"), versions[version])
               for version in required}
    label = "rma-stage-v3-" if args.v3_only else "rma-stage-writer-" if args.writer_only else "rma-stage-"
    fixture = Fixture(args, label + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    fixture.save("sources.json", {version: [str(bundle[0]), *bundle[1:]] for version, bundle in bundles.items()})
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        if args.v3_only:
            exercise_source(fixture, bundles["v3"])
        elif args.writer_only:
            exercise_writer(fixture, bundles["v2"])
        else:
            exercise(fixture, bundles["v1"], bundles["v2"])
    print("real atomic staging and rollback passed (" + ("v3" if args.v3_only else "writer v2" if args.writer_only else "v1/v2")
          + ")", flush=True)


if __name__ == "__main__":
    main()
