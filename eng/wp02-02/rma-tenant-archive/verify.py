#!/usr/bin/env python3
"""Real PostgreSQL archive acceptance, with one private full-Core fixture."""

import argparse
from concurrent.futures import ThreadPoolExecutor
import csv
from dataclasses import replace
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import secrets
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "rma-tenant-migration"))
from verify import A, B, OWNER, BASE_ENV, FLAGS, Fixture, ident, q, require, schema, token
from verify_commission import COMMISSION, database_for, expected_histories

import archive


def add_outbound(tenant, outbound, number, status):
    return """
        INSERT INTO public."T_OutboundOrder"
            ("Id","TenantId","OutboundNo","OutboundType","WarehouseCd","PlannedDate","Status",
             "Priority","CreateDate","IsDeleted","RowVersion")
        VALUES ({},{},{},1,'W1','2026-11-09',{},0,'2026-11-09',false,decode('0000000000000000','hex'));
    """.format(q(outbound), q(tenant), q(number), status)


def add_application(tenant, application, actor, outbound, marker):
    return """
        INSERT INTO {s}.heads (tenant_id,id,actor_id,outbound_id,version)
        VALUES ({tenant},{app},{actor},{outbound},2);
        INSERT INTO {s}.versions (tenant_id,id,version,state,payload)
        VALUES ({tenant},{app},1,'DRAFT',jsonb_build_object('marker',{marker}::text,'quantity',1.00000000000001)),
               ({tenant},{app},2,'SUBMITTED',jsonb_build_object('marker',{marker}::text,'quantity',1234567890.12345678901234));
    """.format(s=schema(tenant), tenant=q(tenant), app=q(application), actor=q(actor),
               outbound=q(outbound), marker=q(marker))


def read_business(f, db, tenant):
    return json.loads(f.sql(db, """
        SELECT json_build_object(
            'heads', (SELECT json_agg(h ORDER BY h.id) FROM {s}.heads h),
            'versions', (SELECT json_agg(row(v.tenant_id,v.id,v.version,v.state,v.payload::text)
                           ORDER BY v.id,v.version) FROM {s}.versions v),
            'history', (SELECT row_to_json(h) FROM {s}.rma_migration_history_v1 h));
    """.format(s=schema(tenant))))


def read_sources(f, db, tenant):
    return f.sql(db, """
        SELECT "Id","Status" FROM public."T_OutboundOrder" WHERE "TenantId"={} ORDER BY "Id";
    """.format(q(tenant)))


def assert_rejected(f, label, message, operation):
    try:
        operation()
    except archive.ArchiveError as error:
        require(message in str(error), "{} reported {}".format(label, error))
        f.save(label + ".json", {"Error": str(error)})
    else:
        raise AssertionError(label + " was accepted")


def staging(f, bundle, manifest, tenant, other, expected, source_target, environ):
    db = "w363_staging"
    f.create_target(db)
    f.sql("postgres", """
        REVOKE ALL ON DATABASE {db} FROM PUBLIC;
        GRANT CONNECT ON DATABASE {db} TO {a},{b};
    """.format(db=ident(db), a=ident(A), b=ident(B)))
    target = replace(source_target, database=db)
    process = subprocess.run(
        [f.pg("pg_restore"), *archive.connection_args(target, OWNER),
         "--single-transaction", "--exit-on-error", "--no-password", str(bundle / "rma.pgcustom")],
        env=archive.client_environment(environ, "W363-staging-restore"), capture_output=True,
        cwd=f.root, timeout=45, creationflags=FLAGS)
    f.save("staging-restore.stdout", process.stdout)
    f.save("staging-restore.stderr", process.stderr)
    require(process.returncode == 0, "native staging restore failed")

    shared = (bundle / "outbounds.csv").read_bytes()
    with f.psql(db, app="W363-staging-copy") as proc:
        statement = """
            BEGIN;
            CREATE SCHEMA rma_archive_stage;
            REVOKE ALL ON SCHEMA rma_archive_stage FROM PUBLIC;
            CREATE TABLE rma_archive_stage.outbound_snapshot (
                tenant_id uuid NOT NULL CHECK (tenant_id={}),
                outbound_id uuid NOT NULL, outbound_no text NOT NULL, warehouse_cd text NOT NULL,
                status integer NOT NULL, is_deleted boolean NOT NULL, PRIMARY KEY (tenant_id,outbound_id));
            COPY rma_archive_stage.outbound_snapshot FROM STDIN WITH (FORMAT csv, HEADER true);
        """.format(q(tenant)).encode("utf-8") + shared + b"\\.\nCOMMIT;\n"
        out, err = f.collect(proc, statement)
        f.save("staging-copy.stdout", out)
        f.save("staging-copy.stderr", err)
        require(proc.returncode == 0, "native staging shared slice import failed")

    require(read_business(f, db, tenant) == expected, "staging changed original RMA state or receipt")
    require(f.sql(db, "SELECT payload->>'quantity' FROM {}.versions WHERE version=2;".format(schema(tenant)))
            == "1234567890.12345678901234", "staging changed stored numeric precision")
    require(f.sql(db, """
        SELECT (to_regnamespace({other}) IS NULL AND
                to_regnamespace('cp6_rma_tenant_v1') IS NULL AND
                to_regnamespace('cp6_main_owner_v1') IS NULL
                AND NOT EXISTS (SELECT FROM {s}.heads h
                  LEFT JOIN rma_archive_stage.outbound_snapshot o
                    ON o.tenant_id=h.tenant_id AND o.outbound_id=h.outbound_id
                  WHERE o.outbound_id IS NULL))::text;
    """.format(other=q(schema(other)), s=schema(tenant))) == "true", "staging leaked B or activated authority")
    require(f.sql(db, "SELECT count(*) FROM rma_archive_stage.outbound_snapshot;") == str(manifest["counts"]["outbounds"]),
            "staging shared slice count mismatch")

    table = schema(tenant)
    require(f.sql(db, "SELECT count(*) FROM {}.heads;".format(table), role=A) == "1",
            "the original ordinary A LOGIN cannot read its restored table")
    f.sql(db, "SELECT count(*) FROM {}.heads;".format(table), role=B, error="42501", label="staging-b-denied")
    f.sql(db, "SELECT count(*) FROM rma_archive_stage.outbound_snapshot;", role=A, error="42501",
          label="staging-shared-ordinary-denied")
    f.sql(db, "UPDATE {}.versions SET payload='{{}}';".format(table), role=A, error="42501",
          label="staging-a-write-denied")
    f.sql(db, "UPDATE {}.versions SET payload='{{}}';".format(table), error="23514", label="staging-immutable")
    f.sql(db, """
        BEGIN; INSERT INTO {s}.heads VALUES ({tenant},{app},{actor},{outbound},1); COMMIT;
    """.format(s=table, tenant=q(tenant), app=q(token()), actor=q(token()), outbound=q(token())),
          error="23503", label="staging-deferred-fk")
    f.sql(db, """
        INSERT INTO {s}.heads VALUES ({tenant},{app},{actor},{outbound},1);
    """.format(s=table, tenant=q(other), app=q(token()), actor=q(token()), outbound=q(token())),
          error="23514", label="staging-tenant-check")
    f.save("staging-state.json", read_business(f, db, tenant))


def smoke_cli(f, target, installation, tenant):
    # Exercise the operator entry with a private reader distinct from the database owner.
    username = "w363_backup"
    f.passwords[username] = secrets.token_hex(24)
    f.sql("postgres", "SET log_statement='none'; CREATE ROLE {} LOGIN IN ROLE pg_read_all_data PASSWORD {};".format(
        ident(username), q(f.passwords[username])))
    f.sql(target.database, """
        GRANT CONNECT ON DATABASE {} TO {};
        GRANT EXECUTE ON FUNCTION pg_catalog.pg_control_system() TO {};
    """.format(ident(target.database), ident(username), ident(username)))
    output = f.root / "cli-export"
    f.save("cli-target.json", target.mapping())
    command = [sys.executable, "-B", archive.__file__]
    exported = subprocess.run(
        [*command, "export", "--pg-bin", str(f.args.pg_bin), "--target-file", str(f.root / "cli-target.json"),
         "--output", str(output), "--username", username, "--installation", installation, "--tenant", tenant],
        env={**BASE_ENV, "PGPASSWORD": f.passwords[username]}, capture_output=True,
        cwd=f.root, timeout=25, creationflags=FLAGS)
    f.save("cli-export.stdout", exported.stdout)
    f.save("cli-export.stderr", exported.stderr)
    require(exported.returncode == 0 and not exported.stderr, "archive export CLI failed")
    bundle = output / "bundle"
    checked = subprocess.run(
        [*command, "verify", "--bundle", str(bundle), "--tenant", tenant],
        env=BASE_ENV, capture_output=True, cwd=f.root, timeout=15, creationflags=FLAGS)
    f.save("bundle-verify-cli.stdout", checked.stdout)
    f.save("bundle-verify-cli.stderr", checked.stderr)
    require(checked.returncode == 0 and json.loads(checked.stdout) ==
            {"tenantId": tenant, "format": archive.FORMAT, "recoveryReady": False}, "bundle verify CLI failed")
    manifest = json.loads((bundle / "manifest.json").read_text(encoding="utf-8"))
    require(manifest["counts"] == {"heads": 2, "versions": 4, "history": 1, "outbounds": 2},
            "normal CLI did not archive the new committed state")


def exercise(f):
    db = "w363_live"
    a, b = token(), token()
    request, _ = database_for(f, db, (a, b))
    f.invoke("w363-commission", COMMISSION, request)
    source_target = archive.PhysicalTarget.from_mapping(request["Target"])
    environ = {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}
    installation = request["InstallationId"]

    app, later_app, actor = token(), token(), token()
    a_before, b_before, a_later, b_later, unused = (token() for _ in range(5))
    f.sql(db, "BEGIN;\n" + add_outbound(a, a_before, "A-BEFORE", 1) + add_outbound(a, unused, "A-UNREFERENCED", 4)
          + add_outbound(b, b_before, "B-BEFORE", 1) + add_application(a, app, actor, a_before, "A-BEFORE")
          + add_application(b, app, actor, b_before, "B-BEFORE") + "COMMIT;")
    before_a = read_business(f, db, a)
    before_b = read_business(f, db, b)
    before_sources = {"a": read_sources(f, db, a), "b": read_sources(f, db, b)}

    def write_delta(tenant, old_id, new_id, marker):
        with f.psql(db, app="W363-writer-" + marker) as proc:
            sql = """
                BEGIN;
                UPDATE public."T_OutboundOrder" SET "Status"=3 WHERE "TenantId"={} AND "Id"={};
            """.format(q(tenant), q(old_id)) + add_outbound(tenant, new_id, marker, 2) + add_application(
                tenant, later_app, actor, new_id, marker) + "SELECT pg_backend_pid();\nCOMMIT;\n"
            out, err = f.collect(proc, sql.encode("utf-8"))
            f.save(marker + "-delta.stdout", out)
            f.save(marker + "-delta.stderr", err)
            require(proc.returncode == 0, "simulated writer failed")
            return int(out)

    def on_snapshot(snapshot):
        active = f.activity(db)
        holders = [row for row in active if row["application_name"] == "cp6-rma-archive-holder"]
        require(len(holders) == 1 and holders[0]["state"] == "idle in transaction", "snapshot holder not active")
        with ThreadPoolExecutor(max_workers=2) as pool:
            first = pool.submit(write_delta, a, a_before, a_later, "A-LATER")
            second = pool.submit(write_delta, b, b_before, b_later, "B-LATER")
            pids = [first.result(), second.result()]
        require(len(set(pids)) == 2, "A/B did not use independent PostgreSQL sessions")
        f.save("snapshot-and-writers.json", {"Snapshot": snapshot, "Holder": holders[0]["pid"], "Writers": pids})

    def export(label, target=source_target, tenant=a, hook=None):
        return archive.export_bundle(f.args.pg_bin, target, OWNER, installation, tenant, f.root / label,
                                     environ=environ, on_snapshot=hook)

    bundle = export("export-a", hook=on_snapshot)
    manifest = archive.verify_bundle(bundle, a)
    require(manifest["counts"] == {"heads": 1, "versions": 2, "history": 1, "outbounds": 1},
            "archive did not capture the earlier complete bounded slice")
    require(manifest["migration"]["receiptSha256"] == hashlib.sha256(
        before_a["history"]["result_json"].encode("utf-8")).hexdigest(), "migration receipt changed")
    with (bundle / "outbounds.csv").open(encoding="utf-8", newline="") as source:
        rows = list(csv.DictReader(source))
    require(rows == [{"tenant_id": a, "outbound_id": a_before, "outbound_no": "A-BEFORE",
                      "warehouse_cd": "W1", "status": "1", "is_deleted": "f"}],
            "shared slice included a new, other-tenant, or unreferenced row")
    after = {"a": read_business(f, db, a), "b": read_business(f, db, b),
             "aSources": read_sources(f, db, a), "bSources": read_sources(f, db, b)}
    for key, later_id, old_id, before in (("a", a_later, a_before, before_a), ("b", b_later, b_before, before_b)):
        require(after[key]["history"] == before["history"] and {row["id"] for row in after[key]["heads"]} == {app, later_app},
                key + " live RMA changes were lost")
        require(later_id + "|2" in after[key + "Sources"] and old_id + "|3" in after[key + "Sources"]
                and old_id + "|1" in before_sources[key], key + " live shared changes were lost")
    f.save("live-before.json", {"a": before_a, "b": before_b, **before_sources})
    f.save("live-after.json", after)

    staging(f, bundle, manifest, a, b, before_a, source_target, environ)
    require(read_business(f, db, a) == after["a"] and read_business(f, db, b) == after["b"]
            and read_sources(f, db, a) == after["aSources"] and read_sources(f, db, b) == after["bSources"],
            "isolated staging changed the source")
    smoke_cli(f, source_target, installation, a)

    broken = f.root / "missing-shared"
    shutil.copytree(bundle, broken)
    (broken / "outbounds.csv").unlink()
    assert_rejected(f, "missing-shared-rejected", "bundle-member-mismatch", lambda: archive.verify_bundle(broken, a))
    modified = f.root / "changed-shared"
    shutil.copytree(bundle, modified)
    with (modified / "outbounds.csv").open("ab") as out:
        out.write(b"unverified")
    assert_rejected(f, "changed-shared-rejected", "bundle-digest-mismatch", lambda: archive.verify_bundle(modified, a))
    assert_rejected(f, "other-tenant-rejected", "bundle-tenant-mismatch", lambda: archive.verify_bundle(bundle, b))
    assert_rejected(f, "wrong-target-rejected", "target-identity-rejected", lambda: export(
        "wrong-target", target=replace(source_target, database_oid=source_target.database_oid + 1)))
    assert_rejected(f, "missing-ready-rejected", "rma-ready-rejected", lambda: export("missing-ready", tenant=token()))
    f.sql(db, "GRANT USAGE ON SCHEMA {} TO {};".format(schema(a), ident(B)))
    try:
        assert_rejected(f, "cross-tenant-acl-rejected", "rma-schema-acl-rejected", lambda: export("cross-tenant-acl"))
    finally:
        f.sql(db, "REVOKE USAGE ON SCHEMA {} FROM {};".format(schema(a), ident(B)))
    change_owner = "ALTER FUNCTION {}.rma_application_immutable_v1() OWNER TO {{}};".format(schema(a))
    f.sql(db, change_owner.format(ident(B)))
    try:
        assert_rejected(f, "wrong-owner-rejected", "rma-schema-owner-rejected", lambda: export("wrong-owner"))
    finally:
        f.sql(db, change_owner.format(ident(OWNER)))
    for failure in ("wrong-target", "missing-ready", "cross-tenant-acl", "wrong-owner"):
        require(not (f.root / failure / "bundle/manifest.json").exists(), "failed export published a manifest")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "dotnet", "cli", "webapi"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--webapi-source", required=True)
    args = parser.parse_args()
    for name in ("root", "pg_bin", "dotnet", "cli", "webapi"):
        setattr(args, name, getattr(args, name).resolve(strict=True))
    args.expected = expected_histories(args.webapi_source)
    fixture = Fixture(args, "rma-archive-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    fixture.save("inputs.json", {"DatabaseInitSource": args.webapi_source,
                                "DatabaseInitSha256": hashlib.sha256(args.webapi.read_bytes()).hexdigest(),
                                "CommissionCliSha256": hashlib.sha256(args.cli.read_bytes()).hexdigest()})
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        exercise(fixture)
    print("real PostgreSQL snapshot, bounded RMA archive and isolated staging passed", flush=True)


if __name__ == "__main__":
    main()
