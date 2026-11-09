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


def staging(f, bundle, manifest, tenant, other, expected, source_target, environ, db="w363_staging"):
    f.create_target(db)
    evidence = "staging" if db == "w363_staging" else "v2-staging"
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
    f.save(evidence + "-restore.stdout", process.stdout)
    f.save(evidence + "-restore.stderr", process.stderr)
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
        f.save(evidence + "-copy.stdout", out)
        f.save(evidence + "-copy.stderr", err)
        require(proc.returncode == 0, "native staging shared slice import failed")

    require(read_business(f, db, tenant) == expected, "staging changed original RMA state or receipt")
    require(f.sql(db, "SELECT DISTINCT payload->>'quantity' FROM {}.versions WHERE version=2;".format(schema(tenant)))
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
    require(f.sql(db, "SELECT count(*) FROM {}.heads;".format(table), role=A) == str(len(expected["heads"])),
            "the original ordinary A LOGIN cannot read its restored table")
    f.sql(db, "SELECT count(*) FROM {}.heads;".format(table), role=B, error="42501", label=evidence + "-b-denied")
    f.sql(db, "SELECT count(*) FROM rma_archive_stage.outbound_snapshot;", role=A, error="42501",
          label=evidence + "-shared-ordinary-denied")
    f.sql(db, "UPDATE {}.versions SET payload='{{}}';".format(table), role=A, error="42501",
          label=evidence + "-a-write-denied")
    f.sql(db, "UPDATE {}.versions SET payload='{{}}';".format(table), error="23514", label=evidence + "-immutable")
    f.sql(db, """
        BEGIN; INSERT INTO {s}.heads VALUES ({tenant},{app},{actor},{outbound},1); COMMIT;
    """.format(s=table, tenant=q(tenant), app=q(token()), actor=q(token()), outbound=q(token())),
          error="23503", label=evidence + "-deferred-fk")
    f.sql(db, """
        INSERT INTO {s}.heads VALUES ({tenant},{app},{actor},{outbound},1);
    """.format(s=table, tenant=q(other), app=q(token()), actor=q(token()), outbound=q(token())),
          error="23514", label=evidence + "-tenant-check")
    f.save(evidence + "-state.json", read_business(f, db, tenant))


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


    if f.args.original_journal_cli:
        exercise_journal(f, source_target, installation, request, a, b, environ)


def read_journal(f, db, table, tenant):
    return json.loads(f.sql(db, """
        SELECT coalesce(jsonb_agg(r ORDER BY r.owner,r.operation_id),'[]'::jsonb)
        FROM (
            SELECT tenant_id,owner,operation_id,actor_id,contract,state,
                encode(input_bytes,'hex') AS input_bytes,input_sha256,encode(result_bytes,'hex') AS result_bytes,
                object_ids,audit_json,committed_at AT TIME ZONE 'UTC' AS committed_at
            FROM {table} WHERE tenant_id={tenant}
        ) r;
    """.format(table=table, tenant=q(tenant))))


def restore_journal(f, db, bundle, tenant, expected):
    table = "rma_archive_stage.original_journal_snapshot"
    with f.psql(db, app="W366-staging-journal") as proc:
        statement = ("""
            BEGIN;
            CREATE TABLE {table} (
                tenant_id uuid NOT NULL CHECK (tenant_id={tenant}), owner text NOT NULL,
                operation_id uuid NOT NULL, actor_id uuid NOT NULL, contract text NOT NULL,
                state text NOT NULL CHECK (state IN ('COMMITTED','CLOSED')), input_bytes bytea,
                input_sha256 text, result_bytes bytea NOT NULL, object_ids uuid[] NOT NULL,
                audit_json jsonb NOT NULL, committed_at timestamptz NOT NULL,
                PRIMARY KEY (tenant_id,owner,operation_id)
            );
            COPY {table} FROM STDIN WITH (FORMAT csv, HEADER MATCH);
        """.format(table=table, tenant=q(tenant)).rstrip() + "\n").encode("utf-8")
        out, err = f.collect(proc, statement + (bundle / "rma-original.csv").read_bytes() + b"\\.\nCOMMIT;\n")
        f.save("v2-staging-journal.stdout", out)
        f.save("v2-staging-journal.stderr", err)
        require(proc.returncode == 0, "native journal staging COPY failed")
    actual = read_journal(f, db, table, tenant)
    require(actual == expected, "journal staging changed original fields, bytes or operation membership")
    require(f.sql(db, """
        SELECT (to_regnamespace('cp6_main_owner_v1') IS NULL
                AND to_regnamespace('cp6_rma_original_journal_v1') IS NULL)::text;
    """) == "true", "staging activated original journal authority")
    f.sql(db, "SELECT count(*) FROM {};".format(table), role=A, error="42501", label="v2-staging-journal-a-denied")
    f.sql(db, "SELECT count(*) FROM {};".format(table), role=B, error="42501", label="v2-staging-journal-b-denied")
    f.save("v2-staging-journal.json", actual)


def journal_cli(f, label, username, *args):
    result = subprocess.run(
        [sys.executable, "-B", archive.__file__, *map(str, args)],
        env=BASE_ENV if username is None else {**BASE_ENV, "PGPASSWORD": f.passwords[username]},
        capture_output=True, cwd=f.root, timeout=30, creationflags=FLAGS)
    f.save(label + ".stdout", result.stdout)
    f.save(label + ".stderr", result.stderr)
    f.save(label + ".process.json", {"ExitCode": result.returncode})
    return result


def exercise_journal(f, source, installation, commission, a, b, environ):
    from verify_commission import OTHER_OWNER, journal
    from verify_original_journal import COMMAND, INSTALLATION, OPERATIONS, RMA_OWNER, cli_from, read_original, write_original

    db = source.database

    def export(label, hook=None):
        return archive.export_bundle(f.args.pg_bin, source, OWNER, installation, a, f.root / label,
                                     include_original_journal=True, environ=environ, on_snapshot=hook)

    assert_rejected(f, "v2-overlay-missing", "rma-original-journal-overlay-rejected",
                    lambda: export("v2-no-overlay"))
    operation, closed, later, actor_a, actor_b, other_object = (token() for _ in range(6))
    f.sql(db, "BEGIN; SELECT cp6_authentication_v1.acquire(); " + journal(a, OTHER_OWNER, operation) + " COMMIT;")
    with cli_from(f, f.args.original_journal_cli):
        f.invoke("v2-install-journal", COMMAND, {**commission, "OperationId": token()})
    # A current shared constraint trigger reads every original result as UTF-8 JSON.
    def result_bytes(key, state, note):
        return ('{\r\n  "Note": "' + note + '", "State": "' + state + '", "OperationId": "' + key + '" \r\n}').encode("utf-8")

    a_input = '{ "Reason": "原申请 A", "Qty": "1.12345678" }\r\n'.encode("utf-8")
    b_input = '{\r\n "Qty": "0.23456789", "Reason": "原申请 B" }'.encode("utf-8")
    a_result = result_bytes(operation, "DRAFT", "原字节 A")
    b_result = result_bytes(operation, "DRAFT", "原字节 B")
    closed_result = result_bytes(closed, "CLOSED", "原关闭")
    f.sql(db, write_original(a, operation, actor_a, a_input, a_result, [other_object]), role=A)
    f.sql(db, write_original(b, operation, actor_b, b_input, b_result, [other_object]), role=B)
    f.sql(db, write_original(a, closed, actor_a, None, closed_result, []), role=A)
    ordinary_a = read_original(f, db, A, operation)
    ordinary_b = read_original(f, db, B, operation)
    require(len(ordinary_a) == len(ordinary_b) == 1
            and ordinary_a[0]["tenant_id"] == a and ordinary_b[0]["tenant_id"] == b,
            "normal journal LOGIN did not isolate the same operation key")
    f.save("v2-ordinary-same-key.json", {"A": ordinary_a, "B": ordinary_b})
    live_a = read_journal(f, db, OPERATIONS, a)
    live_b = read_journal(f, db, OPERATIONS, b)
    expected = [row for row in live_a if row["owner"] == RMA_OWNER]
    require(len(expected) == 2 and any(row["owner"] == OTHER_OWNER for row in live_a), "native journal seed incomplete")
    rows = {row["state"]: row for row in expected}
    require(rows["COMMITTED"]["input_bytes"] == a_input.hex()
            and rows["COMMITTED"]["input_sha256"] == hashlib.sha256(a_input).hexdigest()
            and rows["COMMITTED"]["result_bytes"] == a_result.hex()
            and rows["CLOSED"]["input_bytes"] is None and rows["CLOSED"]["input_sha256"] is None
            and rows["CLOSED"]["result_bytes"] == closed_result.hex(), "native source bytes/NULL differ from fixture")

    old_business = read_business(f, db, a)
    changed_source = old_business["heads"][0]["outbound_id"]
    old_status = f.sql(db, 'SELECT "Status" FROM public."T_OutboundOrder" WHERE "Id"={};'.format(q(changed_source)))
    later_outbound = token()

    def after_snapshot(snapshot):
        active = f.activity(db)
        holder = next(row for row in active if row["application_name"] == "cp6-rma-archive-holder")
        require(holder["state"] == "idle in transaction", "original journal snapshot holder is not live")
        with ThreadPoolExecutor(max_workers=2) as workers:
            a_task = workers.submit(f.sql, db, write_original(a, later, actor_a, a_input, result_bytes(later, "DRAFT", "A-later"), []),
                                    A, label="v2-a-after-cut")
            b_task = workers.submit(f.sql, db, write_original(b, later, actor_b, b_input, result_bytes(later, "DRAFT", "B-later"), []),
                                    B, label="v2-b-after-cut")
            a_task.result()
            b_task.result()
        f.sql(db, """
            BEGIN; UPDATE public."T_OutboundOrder" SET "Status"=4 WHERE "Id"={outbound} AND "TenantId"={tenant};
        """.format(outbound=q(changed_source), tenant=q(a)) + add_outbound(a, later_outbound, "A-V2-LATER", 2)
              + add_application(a, token(), actor_a, later_outbound, "A-V2-LATER") + "COMMIT;")
        f.save("v2-snapshot.json", {"Snapshot": snapshot, "Holder": holder["pid"], "LaterOperation": later})

    bundle = export("v2-cut", after_snapshot)
    manifest = archive.verify_bundle(bundle, a)
    require(manifest["format"] == "cp6-rma-source-archive-v2" and manifest["recoveryReady"] is False
            and manifest["journal"]["counts"] == {"rows": 2, "committed": 1, "closed": 1}
            and manifest["counts"] == {"heads": 2, "versions": 4, "history": 1, "outbounds": 2},
            "the v2 archive did not preserve the earlier cut")
    current_a = read_journal(f, db, OPERATIONS, a)
    current_b = read_journal(f, db, OPERATIONS, b)
    for before, after in ((live_a, current_a), (live_b, current_b)):
        require(len(after) == len(before) + 1 and all(row in after for row in before)
                and any(row["operation_id"] == later for row in after), "live journal lost its original/delta")
    f.save("v2-live-journal.json", {"A": current_a, "B": current_b})

    stage = "w366_staging"
    staging(f, bundle, manifest, a, b, old_business, source, environ, db=stage)
    require(f.sql(stage, "SELECT status FROM rma_archive_stage.outbound_snapshot WHERE outbound_id={};".format(q(changed_source)))
            == old_status, "staging included the later shared outbound state")
    restore_journal(f, stage, bundle, a, expected)
    require(read_journal(f, db, OPERATIONS, a) == current_a and read_journal(f, db, OPERATIONS, b) == current_b,
            "journal staging modified the source")

    backup = "w363_backup"
    selection = "SELECT count(*) FROM {} WHERE tenant_id={} AND owner={};".format(OPERATIONS, q(a), q(RMA_OWNER))
    require(f.sql(db, selection, role=backup, label="v2-rls-masked-count") == "0", "fixture reader unexpectedly saw RLS rows")
    f.sql(db, "UPDATE {} SET result_json=result_json;".format(INSTALLATION), role=backup,
          error="42501", label="v2-marker-write-denied")
    grant = "GRANT UPDATE (result_json) ON {} TO {};".format(INSTALLATION, ident(backup))
    revoke = "REVOKE UPDATE (result_json) ON {} FROM {};".format(INSTALLATION, ident(backup))
    f.sql(db, grant)
    try:
        assert_rejected(f, "v2-marker-acl-rejected", "rma-original-journal-overlay-rejected",
                        lambda: export("v2-marker-acl"))
    finally:
        f.sql(db, revoke)
    export_args = ("export", "--pg-bin", f.args.pg_bin, "--target-file", f.root / "cli-target.json",
                   "--installation", installation, "--tenant", a, "--include-original-journal")
    blocked = journal_cli(f, "v2-cli-rls", backup, *export_args, "--username", backup, "--output", f.root / "v2-rls")
    native_error = (f.root / "v2-rls/native/holder.stderr").read_bytes()
    require(blocked.returncode == 1 and b"rma-original-journal-read-failed" in blocked.stderr
            and b"42501" in native_error and b"row-level security policy" in native_error,
            "v2 failed to reject the RLS-filtered reader at its native count")
    for bad in ("v2-no-overlay", "v2-marker-acl", "v2-rls"):
        require(not (f.root / bad / "bundle/manifest.json").exists(), "failed v2 export published a manifest")

    exported = journal_cli(f, "v2-cli-export", OWNER, *export_args, "--username", OWNER, "--output", f.root / "v2-cli")
    require(exported.returncode == 0, "normal v2 export CLI failed")
    cli_bundle = f.root / "v2-cli/bundle"
    verified = journal_cli(f, "v2-cli-verify", None, "verify", "--bundle", cli_bundle, "--tenant", a)
    require(verified.returncode == 0 and json.loads(verified.stdout)["format"] == "cp6-rma-source-archive-v2"
            and archive.verify_bundle(cli_bundle, a)["journal"]["counts"] == {"rows": 3, "committed": 2, "closed": 1},
            "normal v2 verify CLI did not observe the new committed state")
    incomplete = f.root / "v2-missing-journal"
    shutil.copytree(bundle, incomplete)
    (incomplete / "rma-original.csv").unlink()
    assert_rejected(f, "v2-missing-journal-rejected", "bundle-member-mismatch",
                    lambda: archive.verify_bundle(incomplete, a))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "dotnet", "cli", "webapi"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--webapi-source", required=True)
    parser.add_argument("--original-journal-cli", type=Path)
    parser.add_argument("--v1-bundle", type=Path)
    args = parser.parse_args()
    for name in ("root", "pg_bin", "dotnet", "cli", "webapi"):
        setattr(args, name, getattr(args, name).resolve(strict=True))
    if args.original_journal_cli:
        args.original_journal_cli = args.original_journal_cli.resolve(strict=True)
    args.expected = expected_histories(args.webapi_source)
    fixture = Fixture(args, "rma-archive-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    inputs = {"DatabaseInitSource": args.webapi_source,
              "DatabaseInitSha256": hashlib.sha256(args.webapi.read_bytes()).hexdigest(),
              "CommissionCliSha256": hashlib.sha256(args.cli.read_bytes()).hexdigest()}
    if args.original_journal_cli:
        inputs["JournalCliSha256"] = hashlib.sha256(args.original_journal_cli.read_bytes()).hexdigest()
        inputs["JournalCoreSha256"] = hashlib.sha256(args.original_journal_cli.with_name("CP6.Core.dll").read_bytes()).hexdigest()
    fixture.save("inputs.json", inputs)
    if args.v1_bundle:
        old = args.v1_bundle.resolve(strict=True)
        manifest = json.loads((old / "manifest.json").read_text(encoding="utf-8"))
        legacy = journal_cli(fixture, "w363-v1-verify", None, "verify", "--bundle", old, "--tenant", manifest["tenantId"])
        require(legacy.returncode == 0 and json.loads(legacy.stdout)["format"] == "cp6-rma-source-archive-v1",
                "original W363 v1 bundle is no longer accepted")
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        exercise(fixture)
    print("real PostgreSQL snapshot, bounded RMA archive and isolated staging passed", flush=True)


if __name__ == "__main__":
    main()
