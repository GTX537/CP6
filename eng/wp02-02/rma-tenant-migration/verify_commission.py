#!/usr/bin/env python3
"""Exercise the Core RMA cutover against DatabaseInit from a real published host."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys

sys.dont_write_bytecode = True
from verify import Fixture, FLAGS, A, B, OWNER, ident, q, require, schema, token, wait_for

PREPARE = "prepare-rma-core-tenant-storage-v1"
COMMISSION = "commission-rma-core-tenant-storage-v1"
CORE_CONTROL = "cp6_rma_core_commission_v1"
RMA_OWNER = "Main.Wms.RmaApplication.v1"
OTHER_OWNER = "Main.Wms.StockMovement"
HISTORIES = {"Core": "__EFMigrationsHistory", "Space": "__EFMigrationsHistory_Space",
             "IdentityPriority": "__EFMigrationsHistory_IdentityPriority", "ErpIntegration": "__EFMigrationsHistory_ErpIntegration"}


def initialize(fixture, database):
    content = fixture.root / ("init-" + database)
    content.mkdir()
    env = {name: os.environ[name] for name in os.environ
           if name.upper() in ("SYSTEMROOT", "WINDIR", "PATH", "TEMP", "TMP", "LOCALAPPDATA", "APPDATA", "USERPROFILE")}
    env.update({
        "ASPNETCORE_ENVIRONMENT": "Development", "Database__Provider": "PostgreSql",
        "Startup__Mode": "DatabaseInit", "Startup__SkipDatabaseInitialization": "false",
        "Startup__SkipHostedServices": "true", "CrmOidc__Enabled": "false", "CrmIdentity__Enabled": "false",
        "ErpIntegration__Enabled": "false", "Kafka__BootstrapServers": "",
        "JWT__Issuer": "rma-core-fixture", "JWT__Audience": "rma-core-fixture",
        "JWT__Secret": secrets.token_hex(32), "ConnectionStrings__Redis": "",
        "Storage__Provider": "Local", "Storage__LocalRoot": str(content / "uploads"),
        "Space__Files__RootPath": str(content / "space-files"),
        "ConnectionStrings__DefaultConnection": (
            "Host=127.0.0.1;Port={};Database={};Username={};Password={};SSL Mode=Disable;Timeout=4"
            .format(fixture.args.port, database, OWNER, fixture.passwords[OWNER]))
    })
    command = [str(fixture.args.dotnet), str(fixture.args.webapi), "--contentRoot", str(content)]
    with subprocess.Popen(command, cwd=content, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          creationflags=FLAGS) as process:
        output, error = fixture.collect(process, seconds=180)
        output = output.replace(env["JWT__Secret"].encode(), b"[private fixture value]")
        error = error.replace(env["JWT__Secret"].encode(), b"[private fixture value]")
        fixture.save(database + "-dbinit.stdout", output)
        fixture.save(database + "-dbinit.stderr", error)
        fixture.save(database + "-dbinit.process.json", {"Pid": process.pid, "ExitCode": process.returncode})
        require(process.returncode == 0 and b"Database initialization completed;" in output,
                "published DatabaseInit failed for " + database)

    actual = {}
    for context, table in HISTORIES.items():
        values = fixture.sql(database, 'SELECT "MigrationId" FROM public.{} ORDER BY "MigrationId" COLLATE "C";'.format(ident(table)))
        actual[context] = values.splitlines()
    fixture.save(database + "-histories.json", actual)
    require(actual == fixture.args.expected, "physical Core/Space/Identity/ERP histories differ from published source")


def expected_histories(source):
    repo = Path(__file__).resolve().parents[3]
    paths = subprocess.check_output(["git", "-c", "core.longpaths=true", "ls-tree", "-rz", "--name-only",
                                     source, "CP6.Persistence.PostgreSql/Migrations"], cwd=repo,
                                    creationflags=FLAGS).decode().split("\0")
    return {context: sorted(Path(path).stem for path in paths
                            if path.startswith("CP6.Persistence.PostgreSql/Migrations/" + context + "/")
                            and re.fullmatch(r"\d{14}_[^.]+\.cs", Path(path).name)) for context in HISTORIES}


def snapshot(fixture, database, tenant_ids):
    state = json.loads(fixture.sql(database, """
        SELECT json_build_object(
          'old_heads',(SELECT count(*) FROM cp6_rma_application_v1.heads),
          'old_versions',(SELECT count(*) FROM cp6_rma_application_v1.versions),
          'rma_journal',(SELECT count(*) FROM cp6_main_owner_v1.operations WHERE owner='Main.Wms.RmaApplication.v1'),
          'other_journal',(SELECT count(*) FROM cp6_main_owner_v1.operations WHERE owner='Main.Wms.StockMovement'),
          'old_guards',(SELECT count(*) FROM pg_trigger WHERE NOT tgisinternal AND tgname='rma_core_legacy_disabled'),
          'tenant_control',to_regnamespace('cp6_rma_tenant_v1')::oid::bigint,
          'tenant_directory',to_regnamespace('cp6_tenancy_v1')::oid::bigint,
          'tenant_a',to_regnamespace({})::oid::bigint,'tenant_b',to_regnamespace({})::oid::bigint);
        """.format(*(q(schema(tenant)) for tenant in tenant_ids))))
    return state


def old_business(tenant):
    app, actor, outbound = token(), token(), token()
    return """
        INSERT INTO cp6_rma_application_v1.heads (tenant_id,id,actor_id,outbound_id,version)
          VALUES ({tenant},{app},{actor},{outbound},1);
        INSERT INTO cp6_rma_application_v1.versions (tenant_id,id,version,state,payload)
          VALUES ({tenant},{app},1,'DRAFT','{{}}'::jsonb);
    """.format(tenant=q(tenant), app=q(app), actor=q(actor), outbound=q(outbound))


def journal(tenant, owner, operation):
    contract = "RMA-NATIVE-APPLICATION/1" if owner == RMA_OWNER else "Main.Wms.StockOperation.v1"
    body = json.dumps({"OperationId": operation, "State": "CLOSED"}, separators=(",", ":"))
    return """
      INSERT INTO cp6_main_owner_v1.operations
        (tenant_id,owner,operation_id,actor_id,contract,state,input_bytes,input_sha256,result_bytes,object_ids,audit_json)
        VALUES ({tenant},{owner},{op},{actor},{contract},'CLOSED',NULL,NULL,convert_to({body},'UTF8'),
                ARRAY[]::uuid[],'{{"action":"close-uncommitted-operation"}}'::jsonb);
    """.format(tenant=q(tenant), owner=q(owner), op=q(operation), actor=q(token()), contract=q(contract), body=q(body))


def database_for(fixture, name, tenant_ids):
    request = {"Target": fixture.create_target(name), "InstallationId": token(), "OperationId": token(),
               "Tenants": [{"TenantId": tenant_ids[0], "Login": A}, {"TenantId": tenant_ids[1], "Login": B}]}
    fixture.save(name + "-request.json", request)
    before = fixture.invoke(name + "-prepare", PREPARE, request)
    if name == "rma_core_fresh":
        fixture.invoke(name + "-no-history", COMMISSION, request, 1, "core-history-required")
    initialize(fixture, name)
    return request, before


def negative(fixture, database, request, tenant_ids, make_nonempty):
    fixture.sql(database, "BEGIN; SELECT cp6_authentication_v1.acquire();\n" + make_nonempty + "\nCOMMIT;")
    before = snapshot(fixture, database, tenant_ids)
    fixture.invoke(database + "-commission-denied", COMMISSION, request, 1, "shared-rma-not-empty")
    require(snapshot(fixture, database, tenant_ids) == before and before["old_guards"] == 0
            and before["tenant_control"] is None, "a rejected commission changed legacy state")
    fixture.save(database + "-preserved.json", before)


def exercise(fixture):
    tenant_ids = (token(), token())
    tenant = tenant_ids[0]
    positive, prepared = database_for(fixture, "rma_core_fresh", tenant_ids)
    negative_heads, _ = database_for(fixture, "rma_core_business_nonempty", tenant_ids)
    negative_journal, _ = database_for(fixture, "rma_core_journal_nonempty", tenant_ids)
    operation = positive["OperationId"]
    negative(fixture, "rma_core_business_nonempty", negative_heads, tenant_ids, old_business(tenant))
    negative(fixture, "rma_core_journal_nonempty", negative_journal, tenant_ids, journal(tenant, RMA_OWNER, operation))

    database = "rma_core_fresh"
    initial = snapshot(fixture, database, tenant_ids)
    require(initial["old_heads"] == initial["old_versions"] == initial["rma_journal"] == initial["old_guards"] == 0,
            "published new Core did not start with an empty shared RMA")
    fixture.invoke("fresh-wrong-oid", COMMISSION, {**positive, "Target": {**positive["Target"],
                   "DatabaseOid": positive["Target"]["DatabaseOid"] + 1}}, 1, "target-identity-rejected")
    fixture.invoke("fresh-wrong-installation", COMMISSION, {**positive, "InstallationId": token()}, 1, "core-preparation-conflict")
    unprepared = fixture.create_target("rma_core_unprepared", template=database)
    copied = {**positive, "Target": unprepared}
    fixture.invoke("fresh-clone", COMMISSION, copied, 1, "core-preparation-conflict")
    fixture.sql(unprepared["Database"], "DROP TABLE {}.installation; DROP SCHEMA {};".format(CORE_CONTROL, CORE_CONTROL))
    fixture.invoke("fresh-unprepared", COMMISSION, copied, 1, "core-preparation-required")
    fixture.invoke("fresh-nonempty-prepare", PREPARE, copied, 1, "fresh-layout-required")
    fixture.invoke("fresh-prepare-after-core-replay", PREPARE, positive)
    require((fixture.root / "fresh-prepare-after-core-replay.stdout").read_bytes() == prepared, "prepare bytes differed")

    # Fail when the second registered schema is created, after the old write
    # barriers and the first tenant's objects and Ready history have been staged.
    second_schema = schema(sorted(tenant_ids)[1])
    fixture.sql(database, """
        CREATE SCHEMA w353_fixture;
        CREATE FUNCTION w353_fixture.fail_second() RETURNS event_trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF EXISTS (SELECT FROM pg_event_trigger_ddl_commands() WHERE object_identity={}) THEN
            RAISE EXCEPTION 'W353_ABORT_SECOND_TENANT' USING ERRCODE='P0001';
          END IF;
        END $$;
        CREATE EVENT TRIGGER w353_fail_second ON ddl_command_end WHEN TAG IN ('CREATE SCHEMA')
          EXECUTE FUNCTION w353_fixture.fail_second();
    """.format(q(second_schema)))
    fixture.invoke("fresh-interrupted", COMMISSION, positive, 1, "P0001")
    fixture.save("fresh-after-abort.json", snapshot(fixture, database, tenant_ids))
    require(snapshot(fixture, database, tenant_ids) == initial, "failed commission left authority or tenant half-state")
    fixture.sql(database, "DROP EVENT TRIGGER w353_fail_second; DROP FUNCTION w353_fixture.fail_second(); DROP SCHEMA w353_fixture;")

    old_probe = "BEGIN; SELECT cp6_authentication_v1.acquire();\n" + old_business(tenant) + journal(tenant, RMA_OWNER, operation) + "\nROLLBACK;"
    fixture.sql(database, old_probe, label="fresh-old-authority-after-abort")
    require(snapshot(fixture, database, tenant_ids) == initial, "old rollback probe changed data")

    # A legacy Owner has already passed the auth root and looked up its old
    # operation. It must finish without waiting on relations held by cutover.
    holder = fixture.psql(database, app="W353-old-inflight")
    commission = None
    try:
        holder.stdin.write(("BEGIN; SET LOCAL lock_timeout='4s'; SELECT cp6_authentication_v1.acquire(); "
                            "SELECT count(*) FROM cp6_main_owner_v1.operations WHERE owner="
                            + q(RMA_OWNER) + ";\n").encode("utf-8"))
        holder.stdin.flush()
        held = wait_for(lambda: next((r for r in fixture.activity(database)
                                      if r["application_name"] == "W353-old-inflight"
                                      and r["state"] == "idle in transaction"), None), "legacy root not held")
        commission = fixture.start_cli(COMMISSION, positive)
        waiting = wait_for(lambda: next((r for r in fixture.activity(database)
                                         if held["pid"] in r["blockers"]
                                         and "cp6_authentication_v1.acquire()" in r["query"]), None),
                           "commission did not wait on the old authentication root")
        fixture.save("fresh-inflight-locks.json", {"Legacy": held, "Commission": waiting, "CliPid": commission.pid})
        out, err = fixture.collect(holder, (old_business(tenant) + journal(tenant, RMA_OWNER, operation)
                                           + "\nROLLBACK;\n").encode("utf-8"))
        fixture.save("fresh-inflight-legacy.stdout", out)
        fixture.save("fresh-inflight-legacy.stderr", err)
        require(holder.returncode == 0 and not err, "old in-flight write could not finish while commission waited")
        original = fixture.receipt("fresh-commission", commission, 0)
    finally:
        for process in (holder, commission):
            if process is not None and process.poll() is None:
                process.kill()
                fixture.collect(process)
    result = json.loads(original)
    require(result["Outcome"] == "COMMISSIONED" and result["OperationId"] == operation and len(result["Tenants"]) == 2,
            "commission result omitted a tenant or original key")
    ready = snapshot(fixture, database, tenant_ids)
    require(ready["old_guards"] == 3 and ready["tenant_a"] and ready["tenant_b"] and ready["old_heads"] == 0
            and ready["old_versions"] == 0 and ready["rma_journal"] == 0, "the committed cutover is incomplete")
    for tenant_id, login in zip(tenant_ids, (A, B)):
        state = fixture.state(database, tenant_id)
        persisted = json.loads(state["local"]["result_json"])
        require(state["control"]["state"] == "Ready" and state["control"]["result_json"] == state["local"]["result_json"]
                and persisted["TenantId"] == tenant_id and persisted["OperationId"] == operation
                and persisted in result["Tenants"], "tenant history/control/original disagree")
        identity = fixture.sql(database, "SELECT session_user||'|'||cp6_tenancy_v1.current_tenant_id()||'|'||"
                               "(SELECT count(*) FROM {}.heads);".format(schema(tenant_id)), login)
        require(identity == "{}|{}|0".format(login, tenant_id), "ordinary role could not read its empty RMA schema")
        fixture.sql(database, "INSERT INTO {}.heads VALUES ({},{},{},{},1);".format(
                    schema(tenant_id), q(tenant_id), q(token()), q(token()), q(token())),
                    login, "42501", "fresh-runtime-write-denied-" + tenant_id[:8])
        fixture.sql(database, "SELECT * FROM cp6_main_owner_v1.operations;", login, "42501",
                    "fresh-runtime-journal-denied-" + tenant_id[:8])

    fixture.sql(database, old_probe, error="42501", label="fresh-old-business-rejected")
    fixture.sql(database, "TRUNCATE cp6_rma_application_v1.heads,cp6_rma_application_v1.versions;",
                error="42501", label="fresh-old-truncate-rejected")
    fixture.sql(database, "BEGIN; SELECT cp6_authentication_v1.acquire();\n" + journal(tenant, RMA_OWNER, operation) + "\nCOMMIT;",
                error="42501", label="fresh-old-close-rejected")
    for label in ("fresh-old-business-rejected", "fresh-old-truncate-rejected", "fresh-old-close-rejected"):
        require(b"RMA_SHARED_AUTHORITY_RETIRED" in (fixture.root / (label + ".stderr")).read_bytes(),
                "old writer did not reach the exact RMA fence")
    fixture.sql(database, "BEGIN; SELECT cp6_authentication_v1.acquire();\n" + journal(tenant, OTHER_OWNER, operation) + "\nCOMMIT;",
                label="fresh-unrelated-owner-committed")
    final = snapshot(fixture, database, tenant_ids)
    require(final == {**ready, "other_journal": ready["other_journal"] + 1}, "legacy refusal or other Owner mutation differed")
    require(fixture.invoke("fresh-original-replay", COMMISSION, positive) == original,
            "same commission returned different original bytes")
    fixture.invoke("fresh-changed-operation", COMMISSION, {**positive, "OperationId": token()}, 1, "core-preparation-conflict")
    fixture.save("fresh-final.json", {"State": final, "OriginalSha256": hashlib.sha256(original).hexdigest()})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--pg-bin", type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--cli", type=Path, required=True)
    parser.add_argument("--webapi", type=Path, required=True)
    parser.add_argument("--webapi-source", required=True)
    args = parser.parse_args()
    for item in ("root", "pg_bin", "dotnet", "cli", "webapi"):
        setattr(args, item, getattr(args, item).resolve(strict=True))
    args.expected = expected_histories(args.webapi_source)
    require(len(args.expected["Core"]) > 1 and all(args.expected.values()), "published migration inventory missing")
    args.root /= "rma-core-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8]
    args.root.mkdir(mode=0o700)
    print(args.root, flush=True)
    fixture = Fixture(args, "commission")
    fixture.save("source.json", {"WebApiSource": args.webapi_source,
                 "WebApiSha256": hashlib.sha256(args.webapi.read_bytes()).hexdigest(),
                 "CliSha256": hashlib.sha256(args.cli.read_bytes()).hexdigest()})
    with fixture.running():
        exercise(fixture)
    print("full Core commission passed", flush=True)


if __name__ == "__main__":
    try:
        main()
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
