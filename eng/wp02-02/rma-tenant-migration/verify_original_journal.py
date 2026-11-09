#!/usr/bin/env python3
"""Exercise the native RMA original journal on a commissioned Core PostgreSQL database."""
import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
from verify import A, B, OWNER, Fixture, ident, q, require, token, wait_for
from verify_commission import COMMISSION, OTHER_OWNER, RMA_OWNER, database_for, expected_histories, journal, old_business

COMMAND = "install-rma-core-original-journal-v1"
STORE = "cp6_rma_original_journal_v1.store"
EXECUTOR = "cp6_rma_original_journal_executor_v1"
INSTALLATION = "cp6_rma_original_journal_v1.installation"
OPERATIONS = "cp6_main_owner_v1.operations"
FENCE = "rma_core_legacy_disabled"
CONTRACT = "RMA-NATIVE-APPLICATION/1"
SIGNATURE = "uuid,uuid,uuid,text,bytea,text,bytea,uuid[],jsonb"


@contextmanager
def cli_from(fixture, cli):
    current = fixture.args.cli
    fixture.args.cli = cli
    try:
        yield
    finally:
        fixture.args.cli = current


def bytea(value):
    return "NULL" if value is None else "pg_catalog.decode({},'hex')".format(q(value.hex()))


def write_original(tenant, operation, actor, original_input, result, objects):
    digest = "NULL" if original_input is None else q(hashlib.sha256(original_input).hexdigest())
    status = "CLOSED" if original_input is None else "COMMITTED"
    ids = "ARRAY[{}]::uuid[]".format(",".join(q(item) for item in objects))
    return "SELECT {}({},{},{},{},{},{},{},{},'{{\"fixture\":true}}'::jsonb);".format(
        STORE, q(tenant), q(operation), q(actor), q(status), bytea(original_input), digest, bytea(result), ids)


def read_original(fixture, database, role, operation, extra=""):
    return json.loads(fixture.sql(database, """
        SELECT COALESCE(pg_catalog.jsonb_agg(row), '[]'::jsonb) FROM (
            SELECT tenant_id,owner,operation_id,actor_id,contract,state,
                pg_catalog.encode(input_bytes,'hex') AS input,
                input_sha256 AS digest,pg_catalog.encode(result_bytes,'hex') AS result,object_ids
            FROM {operations} WHERE operation_id={operation} {extra}
            ORDER BY tenant_id,owner
        ) row;
    """.format(operations=OPERATIONS, operation=q(operation), extra=extra), role=role))


def journal_catalog(fixture, database):
    return json.loads(fixture.sql(database, """
        SELECT pg_catalog.jsonb_object_agg(c.relname||'.'||t.tgname, pg_catalog.jsonb_build_object(
            'oid',t.oid::bigint,'relation',t.tgrelid::bigint,'function',t.tgfoid::bigint,
            'enabled',t.tgenabled,'definition',pg_catalog.pg_get_triggerdef(t.oid)))
        FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_class c ON c.oid=t.tgrelid
        WHERE NOT t.tgisinternal AND (t.tgrelid='cp6_main_owner_v1.operations'::regclass
           OR t.tgrelid IN ('cp6_rma_application_v1.heads'::regclass,'cp6_rma_application_v1.versions'::regclass)
              AND t.tgname='rma_core_legacy_disabled');
    """))


def error_sql(fixture, database, statement, role, label, sqlstate="42501", detail=None):
    try:
        fixture.sql(database, statement, role=role, error=sqlstate, label=label)
    except RuntimeError as error:
        raise RuntimeError("{}: {}".format(label, error)) from error
    if detail:
        require(detail.encode() in (fixture.root / (label + ".stderr")).read_bytes(), label + " wrong boundary")


def assert_row(row, tenant, operation, actor, original_input, result, objects):
    require(row["tenant_id"] == tenant and row["owner"] == RMA_OWNER and row["operation_id"] == operation
            and row["actor_id"] == actor and row["contract"] == CONTRACT
            and row["state"] == ("CLOSED" if original_input is None else "COMMITTED")
            and row["input"] == (None if original_input is None else original_input.hex())
            and row["digest"] == (None if original_input is None else hashlib.sha256(original_input).hexdigest())
            and row["result"] == result.hex() and row["object_ids"] == objects,
            "the native journal did not preserve original fields and bytes")


def append_waiting_for_auth(fixture, database, statement, operation):
    holder = fixture.psql(database, app="W361-auth-holder")
    ordinary = None
    try:
        holder.stdin.write(b"BEGIN; SELECT cp6_authentication_v1.acquire();\n")
        holder.stdin.flush()
        held = wait_for(lambda: next((row for row in fixture.activity(database)
                                      if row["application_name"] == "W361-auth-holder"
                                      and row["state"] == "idle in transaction"), None), "original auth root not held")
        ordinary = fixture.psql(database, role=A, app="W361-rma-ordinary")
        ordinary.stdin.write(("BEGIN; " + statement + " COMMIT;\n").encode())
        ordinary.stdin.flush()
        waiting = wait_for(lambda: next((row for row in fixture.activity(database)
                                         if row["application_name"] == "W361-rma-ordinary"
                                         and held["pid"] in row["blockers"]), None),
                           "ordinary append did not wait on the original auth root")
        require(not any(lock["relation"] == OPERATIONS and lock["mode"] == "RowExclusiveLock" and lock["granted"]
                        for lock in waiting["locks"]), "ordinary append acquired the journal before the auth root")
        b_during_wait = read_original(fixture, database, B, operation)
        require(not b_during_wait, "B could not read its own empty result while A awaited the original auth root")
        fixture.save("auth-root-wait.json", {"Holder": held, "Ordinary": waiting, "BRead": b_during_wait})
        out, err = fixture.collect(holder, b"ROLLBACK;\n")
        fixture.save("auth-root-holder.stdout", out)
        fixture.save("auth-root-holder.stderr", err)
        require(holder.returncode == 0 and not err, "auth root holder did not release")
        out, err = fixture.collect(ordinary)
        fixture.save("auth-root-ordinary.stdout", out)
        fixture.save("auth-root-ordinary.stderr", err)
        require(ordinary.returncode == 0 and not err, "ordinary append did not resume and commit")
    finally:
        for proc in (ordinary, holder):
            if proc is not None and proc.poll() is None:
                proc.kill()
                fixture.collect(proc)


def prepare_overlay(fixture, database, commission):
    request = {**commission, "OperationId": token()}
    wrong_target = {**request, "Target": {**request["Target"], "DatabaseOid": request["Target"]["DatabaseOid"] + 1}}
    fixture.invoke("wrong-physical-oid", COMMAND, wrong_target, 1, "target-identity-rejected")
    fixture.invoke("wrong-installation", COMMAND, {**request, "InstallationId": token()}, 1, "binding-rejected")
    swapped = [{**binding, "Login": other["Login"]}
               for binding, other in zip(request["Tenants"], reversed(request["Tenants"]))]
    fixture.invoke("wrong-ordinary-bindings", COMMAND, {**request, "Tenants": swapped}, 1, "binding-rejected")
    fixture.invoke("ordinary-cannot-install", COMMAND, request, 1, "target-identity-rejected", role=A)
    with fixture.start_cli(COMMAND, request) as proc:
        stdout, stderr = fixture.collect(proc)
        fixture.save("install-stdout-discarded.process.json",
                     {"Pid": proc.pid, "ExitCode": proc.returncode, "DiscardedBytes": len(stdout)})
        fixture.save("install-stdout-discarded.stderr", stderr)
        require(proc.returncode == 0 and stdout and not stderr, "first overlay install did not commit")
        del stdout
    stored = fixture.sql(database, "SELECT result_json FROM {} WHERE singleton;".format(INSTALLATION))
    recovered = fixture.invoke("install-original-recovered", COMMAND, request)
    result = json.loads(recovered)
    require(recovered.decode().strip() == stored and result["Outcome"] == "INSTALLED"
            and result["OperationId"] == request["OperationId"] and result["InstallationId"] == request["InstallationId"],
            "the same key did not recover the persisted original installation")
    fixture.invoke("install-different-key", COMMAND, {**request, "OperationId": token()}, 1, "operation-conflict")
    return request, recovered

def exercise(fixture):
    database = "rma_original_journal"
    tenant_a, tenant_b = token(), token()
    with cli_from(fixture, fixture.args.old_cli):
        commission, _ = database_for(fixture, database, (tenant_a, tenant_b))
        commission_original = fixture.invoke("commission-original", COMMISSION, commission)
    initial_ready = [fixture.state(database, tenant) for tenant in (tenant_a, tenant_b)]
    before_catalog = journal_catalog(fixture, database)
    fixture.save("journal-catalog-before.json", before_catalog)
    operation, actor_a, actor_b, object_a, object_b = (token() for _ in range(5))
    old_sql = "BEGIN; SELECT cp6_authentication_v1.acquire(); " + journal(tenant_a, RMA_OWNER, operation) + " COMMIT;"
    error_sql(fixture, database, old_sql, OWNER, "old-rma-journal-before", detail="RMA_SHARED_AUTHORITY_RETIRED")
    fixture.sql(database, "BEGIN; SELECT cp6_authentication_v1.acquire(); " + journal(tenant_a, OTHER_OWNER, operation) + " COMMIT;")
    other_before = read_original(fixture, database, OWNER, operation)

    request, recovered = prepare_overlay(fixture, database, commission)
    after_catalog = journal_catalog(fixture, database)
    fixture.save("journal-catalog-after.json", after_catalog)
    changed = sorted(key for key in before_catalog if before_catalog[key] != after_catalog.get(key))
    require(before_catalog.keys() == after_catalog.keys() and changed == ["operations." + FENCE]
            and after_catalog["operations." + FENCE]["function"] == before_catalog["operations." + FENCE]["function"]
            and after_catalog["operations." + FENCE]["enabled"] == "A",
            "installation modified an existing journal or old business trigger outside the exact exception")

    input_a = '{ "Reason": "送错商品", "Qty": "1.12345678" }'.encode("utf-8")
    input_b = '{ "Reason": "B 原申请", "Qty": "0.23456789" }'.encode("utf-8")
    result_a = ('{"OperationId":"' + operation + '", "State":"DRAFT", "Note":"原字节 A"}').encode("utf-8")
    result_b = ('{"OperationId":"' + operation + '", "State":"DRAFT", "Note":"原字节 B"}').encode("utf-8")
    append_a = write_original(tenant_a, operation, actor_a, input_a, result_a, [object_a])
    append_b = write_original(tenant_b, operation, actor_b, input_b, result_b, [object_b])
    error_sql(fixture, database, write_original(tenant_b, token(), actor_a, input_a, result_a, [object_a]), A, "cross-tenant-append")
    append_waiting_for_auth(fixture, database, append_a, operation)
    fixture.sql(database, "BEGIN; " + append_b + " COMMIT;", role=B, label="tenant-b-append")

    a_rows = read_original(fixture, database, A, operation)
    b_rows = read_original(fixture, database, B, operation)
    require(len(a_rows) == len(b_rows) == 1, "same operation key leaked or collided between tenants")
    assert_row(a_rows[0], tenant_a, operation, actor_a, input_a, result_a, [object_a])
    assert_row(b_rows[0], tenant_b, operation, actor_b, input_b, result_b, [object_b])
    require(not read_original(fixture, database, A, operation, "AND tenant_id=" + q(tenant_b))
            and not read_original(fixture, database, A, operation, "AND owner=" + q(OTHER_OWNER)),
            "the ordinary read was not limited to tenant and RMA Owner")
    error_sql(fixture, database, append_a, A, "duplicate-original", "23505")

    # A rollback proves the definer function participates in the caller's native transaction.
    close_key = token()
    closed = ('{"OperationId":"' + close_key + '", "State":"CLOSED"}').encode("utf-8")
    close = write_original(tenant_a, close_key, actor_a, None, closed, [])
    fixture.sql(database, "BEGIN; " + close + " ROLLBACK;", role=A, label="close-rolled-back")
    require(not read_original(fixture, database, A, close_key), "the definer committed outside its caller transaction")
    fixture.sql(database, "BEGIN; " + close + " COMMIT;", role=A, label="close-committed")
    closed_rows = read_original(fixture, database, A, close_key)
    require(len(closed_rows) == 1, "the same close key could not commit once after rollback")
    assert_row(closed_rows[0], tenant_a, close_key, actor_a, None, closed, [])

    error_sql(fixture, database, journal(tenant_a, RMA_OWNER, token()), A, "ordinary-direct-insert")
    error_sql(fixture, database, "SELECT audit_json FROM {};".format(OPERATIONS), A, "ordinary-audit-column")
    error_sql(fixture, database, "SET ROLE {};".format(ident(EXECUTOR)), A, "ordinary-set-executor")
    error_sql(fixture, database, "SET ROLE {};".format(ident(B)), A, "ordinary-set-other-tenant")
    error_sql(fixture, database, old_sql, OWNER, "old-rma-journal-after", detail="RMA_SHARED_AUTHORITY_RETIRED")
    error_sql(fixture, database, "BEGIN; SET LOCAL ROLE {}; {} COMMIT;".format(
        ident(EXECUTOR), journal(tenant_a, RMA_OWNER, token())), OWNER, "old-session-pretends-executor",
        detail="Tenant login binding rejected")
    error_sql(fixture, database, "BEGIN; SELECT cp6_authentication_v1.acquire(); " + old_business(tenant_a) + " COMMIT;",
              OWNER, "old-rma-business", detail="RMA_SHARED_AUTHORITY_RETIRED")
    error_sql(fixture, database, "BEGIN; SELECT cp6_authentication_v1.acquire(); TRUNCATE cp6_rma_application_v1.heads, cp6_rma_application_v1.versions; COMMIT;",
              OWNER, "old-rma-business-truncate", detail="RMA_SHARED_AUTHORITY_RETIRED")
    error_sql(fixture, database, "BEGIN; SELECT cp6_authentication_v1.acquire(); UPDATE {} SET state=state; COMMIT;".format(OPERATIONS),
              OWNER, "journal-immutable", "23514", "MAIN_ORIGINAL_OPERATION_IMMUTABLE")

    other_key = token()
    fixture.sql(database, "BEGIN; SELECT cp6_authentication_v1.acquire(); " + journal(tenant_a, OTHER_OWNER, other_key) + " COMMIT;",
                label="other-owner-after-overlay")
    admin_rows = read_original(fixture, database, OWNER, operation)
    require(len(admin_rows) == 3 and [r for r in admin_rows if r["owner"] == OTHER_OWNER] == other_before
            and all(row in admin_rows for row in a_rows + b_rows)
            and len(read_original(fixture, database, OWNER, other_key)) == 1,
            "the administration path lost an original or another Owner")
    require(read_original(fixture, database, A, operation) == a_rows, "a failed attempt modified A's original")

    with cli_from(fixture, fixture.args.old_cli):
        fixture.invoke("old-cli-commission-after-journal", COMMISSION, commission, 1, "shared-rma-not-empty")
    require(fixture.invoke("commission-compatible-replay", COMMISSION, commission) == commission_original
            and [fixture.state(database, tenant) for tenant in (tenant_a, tenant_b)] == initial_ready
            and fixture.invoke("overlay-replay-after-journal", COMMAND, request) == recovered,
            "current recovery did not return the initial and overlay original bytes")

    fixture.sql(database, "ALTER FUNCTION {}({}) SECURITY INVOKER;".format(STORE, SIGNATURE))
    try:
        fixture.invoke("commission-overlay-drift", COMMISSION, commission, 1, "journal-overlay-mismatch")
        fixture.invoke("overlay-same-key-drift", COMMAND, request, 1, "journal-overlay-mismatch")
    finally:
        fixture.sql(database, "ALTER FUNCTION {}({}) SECURITY DEFINER;".format(STORE, SIGNATURE))
    require(fixture.invoke("commission-replay-after-repair", COMMISSION, commission) == commission_original,
            "restoring the exact overlay did not recover the original commissioning")
    fixture.save("final.json", {"A": a_rows, "B": b_rows, "Closed": closed_rows, "Control": admin_rows,
                              "Ready": initial_ready, "Install": json.loads(recovered)})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--pg-bin", type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--cli", type=Path, required=True)
    parser.add_argument("--old-cli", type=Path, required=True)
    parser.add_argument("--webapi", type=Path, required=True)
    parser.add_argument("--webapi-source", required=True)
    args = parser.parse_args()
    for item in ("root", "pg_bin", "dotnet", "cli", "old_cli", "webapi"):
        setattr(args, item, getattr(args, item).resolve(strict=True))
    args.expected = expected_histories(args.webapi_source)
    require(len(args.expected["Core"]) > 1 and all(args.expected.values()), "published migration inventory missing")
    args.root /= "rma-original-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8]
    args.root.mkdir(mode=0o700)
    print(args.root, flush=True)
    fixture = Fixture(args, "journal")
    fixture.save("source.json", {"WebApiSource": args.webapi_source,
                                 "WebApiSha256": hashlib.sha256(args.webapi.read_bytes()).hexdigest(),
                                 "OldCliSha256": hashlib.sha256(args.old_cli.read_bytes()).hexdigest(),
                                 "CliSha256": hashlib.sha256(args.cli.read_bytes()).hexdigest(),
                                 "CoreSha256": hashlib.sha256(args.cli.with_name("CP6.Core.dll").read_bytes()).hexdigest()})
    with fixture.running():
        exercise(fixture)
    print("native RMA original journal and W353 receipt recovery passed", flush=True)


if __name__ == "__main__":
    try:
        main()
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
