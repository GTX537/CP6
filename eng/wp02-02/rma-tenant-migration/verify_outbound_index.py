#!/usr/bin/env python3
"""Exercise the deployed Core RMA profile's nontransactional tenant index step."""
import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
from verify import A, B, OWNER, Fixture, ident, migration_request, q, require, schema, token, wait_for
from verify_commission import COMMISSION, database_for, expected_histories

COMMAND = "migrate-rma-core-outbound-index-v1"
APPLICATION = "CP6.TenantStorage.RmaOutboundIndexV1"
FINAL = "rma_heads_outbound_id_v1"
STEPS = "cp6_rma_tenant_v1.online_index_steps_v1"
MIGRATION = "rma-tenant-storage-0002-outbound-index-v1"


def staging(operation):
    return "rma_heads_outbound_build_" + operation.replace("-", "")


def current_index(fixture, database, tenant, name):
    value = fixture.sql(database, """
        SELECT pg_catalog.json_build_object(
            'oid',c.oid::bigint,'table_oid',i.indrelid::bigint,'valid',i.indisvalid,
            'ready',i.indisready,'live',i.indislive,'definition',pg_catalog.pg_get_indexdef(c.oid))
        FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
        LEFT JOIN pg_catalog.pg_index i ON i.indexrelid=c.oid
        WHERE n.nspname={} AND c.relname={};
    """.format(q(schema(tenant)), q(name)))
    return json.loads(value) if value else None


def current_step(fixture, database, tenant):
    value = fixture.sql(database, """
        SELECT pg_catalog.to_jsonb(s) || pg_catalog.jsonb_build_object(
            'table_oid',s.table_oid::bigint,'index_oid',s.index_oid::bigint)
        FROM {} s WHERE tenant_id={} AND migration_id={};
    """.format(STEPS, q(tenant), q(MIGRATION)))
    return json.loads(value) if value else None


def seed_application(fixture, database, tenant, outbound):
    fixture.sql(database, """
        BEGIN;
        INSERT INTO {s}.heads (tenant_id,id,actor_id,outbound_id,version)
        VALUES ({tenant},{id},{actor},{outbound},1);
        INSERT INTO {s}.versions (tenant_id,id,version,state,payload)
        VALUES ({tenant},{id},1,'DRAFT','{{"simulated":true}}'::jsonb);
        COMMIT;
    """.format(s=schema(tenant), tenant=q(tenant), id=q(token()), actor=q(token()), outbound=q(outbound)))


def ordinary_read(fixture, database, role, tenant, outbound):
    return fixture.sql(database, """
        SELECT session_user || '|' || cp6_tenancy_v1.current_tenant_id() || '|' || h.id || '|' || v.state
        FROM {s}.heads h JOIN {s}.versions v USING (tenant_id,id,version)
        WHERE h.outbound_id={outbound};
    """.format(s=schema(tenant), outbound=q(outbound)), role=role)


@contextmanager
def hold_heads(fixture, database, tenant, role, statement, app):
    with fixture.psql(database, role=role, app=app) as holder:
        holder.stdin.write(("BEGIN; " + statement + "\n").encode("utf-8"))
        holder.stdin.flush()
        try:
            ready = wait_for(lambda: next((row for row in fixture.activity(database)
                                          if row["application_name"] == app and row["state"] == "idle in transaction"), None),
                             app + " did not hold " + schema(tenant))
            yield ready["pid"]
        finally:
            out, err = fixture.collect(holder, b"ROLLBACK;\n" if holder.poll() is None else None)
            fixture.save(app + ".stdout", out)
            fixture.save(app + ".stderr", err)
            require(holder.returncode == 0 and not err, app + " did not release cleanly")


@contextmanager
def active_cli(fixture, request):
    proc = fixture.start_cli(COMMAND, request)
    try:
        yield proc
    finally:
        if proc.poll() is None:
            proc.kill()
            fixture.collect(proc)


def while_building(fixture, database, cli, heads_oid, name, holder):
    if cli.poll() is not None:
        fixture.receipt("build-ended-before-invalid", cli)
        raise RuntimeError("CLI ended before the invalid index was visible")
    value = fixture.sql(database, """
        SELECT pg_catalog.json_build_object('pid',p.pid,'table_oid',p.relid::bigint,
            'index_oid',p.index_relid::bigint,'phase',p.phase,'holder',p.current_locker_pid,
            'valid',i.indisvalid,'ready',i.indisready,'live',i.indislive,
            'blockers',pg_catalog.pg_blocking_pids(p.pid))
        FROM pg_catalog.pg_stat_progress_create_index p
        JOIN pg_catalog.pg_stat_activity a ON a.pid=p.pid
        JOIN pg_catalog.pg_class c ON c.oid=p.index_relid
        JOIN pg_catalog.pg_index i ON i.indexrelid=c.oid AND i.indrelid=p.relid
        WHERE a.application_name={} AND p.command='CREATE INDEX CONCURRENTLY'
          AND p.relid={} AND c.relname={} AND p.phase='waiting for writers before build'
          AND p.current_locker_pid={} AND NOT i.indisvalid;
    """.format(q(APPLICATION), heads_oid, q(name), holder))
    return json.loads(value) if value else None


def terminate(fixture, database, proc, backend, label):
    require(fixture.sql(database, "SELECT pg_catalog.pg_terminate_backend({},5000);".format(backend)) == "t",
            "could not terminate the exact PostgreSQL backend")
    fixture.receipt(label, proc, 4, "outcome-unknown-retry-same-operation")


def while_dropping(fixture, database, cli, holder):
    if cli.poll() is not None:
        fixture.receipt("drop-ended-before-wait", cli)
        raise RuntimeError("CLI ended before the concurrent drop waited")
    return next((row for row in fixture.activity(database)
                 if row["application_name"] == APPLICATION and row["query"].startswith("DROP INDEX CONCURRENTLY")
                 and holder in row["blockers"]), None)


def expect_initial_conflicts(fixture, database, commission, tenant_b, request):
    wrong = {**request, "Target": {**request["Target"], "DatabaseOid": request["Target"]["DatabaseOid"] + 1}}
    fixture.invoke("wrong-oid", COMMAND, wrong, 1, "target-identity-rejected")
    fixture.invoke("ordinary-cannot-migrate", COMMAND, request, 1, "target-identity-rejected", role=A)
    clone = fixture.create_target("rma_index_clone", template=database)
    fixture.invoke("different-physical-clone", COMMAND, {**request, "Target": clone}, 1, "binding-rejected")
    fixture.invoke("other-installation", COMMAND, {**request, "InstallationId": token()}, 1, "binding-rejected")

    table_b = schema(tenant_b)
    fixture.sql(database, "CREATE INDEX {} ON {}.heads(outbound_id);".format(ident(FINAL), table_b))
    prior = current_index(fixture, database, tenant_b, FINAL)
    b = migration_request(commission, tenant_b, token())
    fixture.invoke("preexisting-canonical", COMMAND, b, 1, "index-object-conflict")
    require(current_index(fixture, database, tenant_b, FINAL) == prior, "an existing object was changed or adopted")
    fixture.sql(database, "DROP INDEX {}.{};".format(table_b, ident(FINAL)))


def exercise(fixture):
    database = "rma_index"
    tenant_a, tenant_b = token(), token()
    commission, _ = database_for(fixture, database, (tenant_a, tenant_b))
    original = fixture.invoke("commission", COMMISSION, commission)
    ready_a, ready_b = fixture.state(database, tenant_a), fixture.state(database, tenant_b)
    require(ready_a["control"]["state"] == ready_b["control"]["state"] == "Ready", "initial tenant Ready missing")
    a_outbound, b_outbound = token(), token()
    seed_application(fixture, database, tenant_a, a_outbound)
    seed_application(fixture, database, tenant_b, b_outbound)
    prior_a = ordinary_read(fixture, database, A, tenant_a, a_outbound)
    prior_b = ordinary_read(fixture, database, B, tenant_b, b_outbound)
    require(prior_a.startswith(A + "|") and prior_b.startswith(B + "|") and prior_a != prior_b,
            "the existing ordinary paths did not see their own data")
    operation = token()
    request = migration_request(commission, tenant_a, operation)
    table_a, stage = schema(tenant_a), staging(operation)
    expect_initial_conflicts(fixture, database, commission, tenant_b, request)

    with hold_heads(fixture, database, tenant_a, OWNER, "LOCK TABLE {}.heads IN ROW EXCLUSIVE MODE;".format(table_a),
                    "W358-old-writer") as blocker:
        with active_cli(fixture, request) as proc:
            waiting = wait_for(lambda: while_building(fixture, database, proc, ready_a["heads_oid"], stage, blocker),
                               "no catalog-visible index waiting on the exact A writer", seconds=12)
            require(blocker in waiting["blockers"] and waiting["live"] and not waiting["ready"],
                    "the catalog and progress phase did not agree")
            fixture.save("create-waiting.json", waiting)
            fixture.invoke("A-competing-key", COMMAND, {**request, "OperationId": token()}, 1, "migration-in-progress")
            require(ordinary_read(fixture, database, A, tenant_a, a_outbound) == prior_a
                    and ordinary_read(fixture, database, B, tenant_b, b_outbound) == prior_b,
                    "the old ordinary paths changed while A was building")
            terminate(fixture, database, proc, waiting["pid"], "A-build-terminated")

    invalid = current_index(fixture, database, tenant_a, stage)
    step = current_step(fixture, database, tenant_a)
    require(invalid and invalid["oid"] == waiting["index_oid"] and not invalid["valid"]
            and step["state"] == "Building" and step["operation_id"] == operation and step["result_json"] is None
            and current_index(fixture, database, tenant_a, FINAL) is None,
            "an interrupted build did not retain exactly one invalid unfinished original")
    fixture.save("A-invalid.json", {"Index": invalid, "Step": step})
    fixture.invoke("A-different-key", COMMAND, {**request, "OperationId": token()}, 1, "operation-conflict")

    fixture.sql(database, """
        ALTER INDEX {s}.{stage} RENAME TO w358_saved_invalid;
        CREATE INDEX {stage} ON {s}.heads(id);
    """.format(s=table_a, stage=ident(stage)))
    wrong = current_index(fixture, database, tenant_a, stage)
    fixture.invoke("A-wrong-staging", COMMAND, request, 1, "index-object-conflict")
    require(current_index(fixture, database, tenant_a, stage) == wrong and current_step(fixture, database, tenant_a) == step,
            "wrong staging object or original step was changed")
    fixture.sql(database, "DROP INDEX {s}.{stage}; ALTER INDEX {s}.w358_saved_invalid RENAME TO {stage};"
                .format(s=table_a, stage=ident(stage)))
    require(current_index(fixture, database, tenant_a, stage) == invalid, "the exact fixture invalid index was not restored")

    with hold_heads(fixture, database, tenant_a, A, "SELECT count(*) FROM {}.heads;".format(table_a),
                    "W358-old-reader") as reader:
        with active_cli(fixture, request) as proc:
            drop_waiting = wait_for(lambda: while_dropping(fixture, database, proc, reader),
                                    "concurrent drop did not wait on the original ordinary reader", seconds=12)
            dropping = current_step(fixture, database, tenant_a)
            require(dropping["state"] == "Dropping" and dropping["index_oid"] == invalid["oid"]
                    and dropping["operation_id"] == operation and dropping["result_json"] is None,
                    "the invalid native OID was not durably bound before the concurrent drop")
            fixture.save("drop-waiting.json", {"Backend": drop_waiting, "Step": dropping})
            require(ordinary_read(fixture, database, B, tenant_b, b_outbound) == prior_b,
                    "the other ordinary reader could not continue while A's drop waited")
            terminate(fixture, database, proc, drop_waiting["pid"], "A-drop-terminated")
    interrupted_drop = current_index(fixture, database, tenant_a, stage)
    require(interrupted_drop and interrupted_drop["oid"] == invalid["oid"] and not interrupted_drop["valid"]
            and current_step(fixture, database, tenant_a) == dropping,
            "interrupted repair lost the bound invalid object or its original step")
    fixture.save("A-drop-interrupted.json", {"Index": interrupted_drop, "Step": dropping})

    finished = fixture.invoke("A-recover", COMMAND, request)
    applied = current_step(fixture, database, tenant_a)
    valid = current_index(fixture, database, tenant_a, FINAL)
    receipt = json.loads(finished)
    require(receipt["Outcome"] == "APPLIED" and receipt["OperationId"] == operation
            and receipt["IndexOid"] == valid["oid"] == applied["index_oid"] and valid["oid"] != invalid["oid"]
            and valid["table_oid"] == ready_a["heads_oid"] and valid["valid"] and valid["ready"] and valid["live"]
            and applied["state"] == "Applied" and applied["result_json"] == finished.decode("utf-8").strip()
            and current_index(fixture, database, tenant_a, stage) is None,
            "recovery did not install the exact new usable native index and original result")

    fixture.sql(database, "ALTER INDEX {}.{} RENAME TO w358_saved_valid;".format(table_a, ident(FINAL)))
    fixture.invoke("A-applied-missing-object", COMMAND, request, 1, "index-object-conflict")
    fixture.sql(database, "CREATE INDEX {} ON {}.heads(outbound_id);".format(ident(FINAL), table_a))
    replacement = current_index(fixture, database, tenant_a, FINAL)
    require(replacement["oid"] != valid["oid"] and replacement["definition"] == valid["definition"],
            "the test did not create a same-shape different physical object")
    fixture.invoke("A-applied-wrong-oid", COMMAND, request, 1, "index-object-conflict")
    require(current_index(fixture, database, tenant_a, FINAL) == replacement and current_step(fixture, database, tenant_a) == applied,
            "an applied replay adopted or changed the replacement")
    fixture.sql(database, "DROP INDEX {s}.{idx}; ALTER INDEX {s}.w358_saved_valid RENAME TO {idx};"
                .format(s=table_a, idx=ident(FINAL)))

    require(fixture.invoke("A-original-replay", COMMAND, request) == finished,
            "same-key replay did not return original bytes")
    require(fixture.invoke("commission-original-replay", COMMISSION, commission) == original
            and fixture.state(database, tenant_a) == ready_a and fixture.state(database, tenant_b) == ready_b,
            "the initial W353 Ready or original bytes changed")
    require(current_step(fixture, database, tenant_b) is None and current_index(fixture, database, tenant_b, FINAL) is None
            and ordinary_read(fixture, database, A, tenant_a, a_outbound) == prior_a
            and ordinary_read(fixture, database, B, tenant_b, b_outbound) == prior_b,
            "tenant B or the old reads changed after recovery")
    fixture.save("final.json", {"A": valid, "Step": applied, "BStep": None, "ARead": prior_a, "BRead": prior_b,
                              "InitialReady": [ready_a, ready_b]})


def exercise_rename_race(fixture):
    database = "rma_index_rename"
    tenant_a, tenant_b = token(), token()
    commission, _ = database_for(fixture, database, (tenant_a, tenant_b))
    original = fixture.invoke("commission", COMMISSION, commission)
    ready = fixture.state(database, tenant_a)
    request = migration_request(commission, tenant_a, token())
    table_a, stage = schema(tenant_a), staging(request["OperationId"])
    fixture.sql(database, """
        CREATE SCHEMA w358_rename_fixture;
        CREATE FUNCTION w358_rename_fixture.wait_before_rename() RETURNS event_trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF pg_catalog.current_setting('application_name') = '{}' THEN
            PERFORM pg_catalog.pg_advisory_xact_lock(418358,9);
          END IF;
        END $$;
        CREATE EVENT TRIGGER w358_wait_before_rename ON ddl_command_start WHEN TAG IN ('ALTER INDEX')
          EXECUTE FUNCTION w358_rename_fixture.wait_before_rename();
    """.format(APPLICATION))

    proc = None
    try:
        with hold_heads(fixture, database, tenant_a, OWNER, "SELECT pg_catalog.pg_advisory_xact_lock(418358,9);",
                        "W358-rename-gate") as holder:
            proc = fixture.start_cli(COMMAND, request)
            waiting = wait_for(lambda: next((row for row in fixture.activity(database)
                                             if row["application_name"] == APPLICATION and holder in row["blockers"]
                                             and row["query"].startswith("ALTER INDEX") and stage in row["query"]), None),
                               "the product did not wait immediately before the rename", seconds=12)
            valid_stage = current_index(fixture, database, tenant_a, stage)
            require(valid_stage and valid_stage["valid"] and valid_stage["ready"] and valid_stage["live"],
                    "the original staged index was not usable before the swap")
            fixture.sql(database, """
                BEGIN; SET LOCAL lock_timeout='4s';
                ALTER INDEX {s}.{stage} RENAME TO w358_original_stage;
                CREATE INDEX {stage} ON {s}.heads(outbound_id);
                COMMIT;
            """.format(s=table_a, stage=ident(stage)))
            replacement = current_index(fixture, database, tenant_a, stage)
            require(replacement["oid"] != valid_stage["oid"] and replacement["definition"] == valid_stage["definition"],
                    "the fixture did not replace the exact staging name with a same-shape different OID")
            fixture.save("rename-waiting.json", {"Backend": waiting, "Stage": valid_stage, "Replacement": replacement})
        attempt = fixture.receipt("rename-race-product", proc)
    finally:
        if proc is not None and proc.poll() is None:
            proc.kill()
            fixture.collect(proc)

    step = current_step(fixture, database, tenant_a)
    final = current_index(fixture, database, tenant_a, FINAL)
    fixture.save("rename-after-swap.json", {"Step": step, "Final": final, "ExitCode": proc.returncode})
    require(proc.returncode == 1 and not attempt
            and b"index-object-conflict" in (fixture.root / "rename-race-product.stderr").read_bytes(),
            "rename accepted the replacement OID or returned the wrong outcome")
    require(final is None and step["state"] == "Building" and step["result_json"] is None
            and current_index(fixture, database, tenant_a, stage) == replacement
            and current_index(fixture, database, tenant_a, "w358_original_stage")["oid"] == valid_stage["oid"],
            "the conflicting rename or durable completion did not roll back")

    fixture.sql(database, """
        DROP EVENT TRIGGER w358_wait_before_rename;
        DROP FUNCTION w358_rename_fixture.wait_before_rename();
        DROP SCHEMA w358_rename_fixture;
        DROP INDEX {s}.{stage};
        ALTER INDEX {s}.w358_original_stage RENAME TO {stage};
    """.format(s=table_a, stage=ident(stage)))
    recovered = fixture.invoke("rename-race-recovered", COMMAND, request)
    result = json.loads(recovered)
    final = current_index(fixture, database, tenant_a, FINAL)
    require(result["IndexOid"] == valid_stage["oid"] == final["oid"] and final["valid"]
            and fixture.invoke("rename-race-replay", COMMAND, request) == recovered
            and fixture.state(database, tenant_a) == ready
            and fixture.invoke("commission-original-replay", COMMISSION, commission) == original,
            "the same-key valid-stage recovery did not preserve the original physical object and bytes")
    fixture.save("rename-race-final.json", {"Result": result, "Index": final, "Ready": ready})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--pg-bin", type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--cli", type=Path, required=True)
    parser.add_argument("--webapi", type=Path, required=True)
    parser.add_argument("--webapi-source", required=True)
    parser.add_argument("--rename-race", action="store_true")
    args = parser.parse_args()
    for item in ("root", "pg_bin", "dotnet", "cli", "webapi"):
        setattr(args, item, getattr(args, item).resolve(strict=True))
    args.expected = expected_histories(args.webapi_source)
    require(len(args.expected["Core"]) > 1 and all(args.expected.values()), "published migration inventory missing")
    args.root /= "rma-index-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8]
    args.root.mkdir(mode=0o700)
    print(args.root, flush=True)
    fixture = Fixture(args, "outbound-index")
    fixture.save("source.json", {"WebApiSource": args.webapi_source,
                                 "WebApiSha256": hashlib.sha256(args.webapi.read_bytes()).hexdigest(),
                                 "CliSha256": hashlib.sha256(args.cli.read_bytes()).hexdigest()})
    with fixture.running():
        check = exercise_rename_race if args.rename_race else exercise
        check(fixture)
    print("nontransactional tenant index recovery passed", flush=True)


if __name__ == "__main__":
    try:
        main()
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
