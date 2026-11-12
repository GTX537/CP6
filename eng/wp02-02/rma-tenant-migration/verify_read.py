#!/usr/bin/env python3
"""Run RMA HTTP reads or schema activation against an owned Core PostgreSQL fixture."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

sys.dont_write_bytecode = True
from verify import Fixture, FLAGS, A, B, OWNER, q, require, schema, token, wait_for
from verify_commission import PREPARE, COMMISSION, expected_histories, initialize

LIVE_DATABASE = "w356_live"
HTTP_TEST_CLASS = "CP6.Tests.Persistence.Tenancy.RmaTenantReadHttpPostgresTests."
READ_TESTS = (
    HTTP_TEST_CLASS + "Real_bearer_current_and_historical_RMA_use_two_tenants_and_live_authority",
    "CP6.Tests.Persistence.Tenancy.RmaTenantReadModelTests."
    "Request_read_model_reuses_own_mapping_and_separates_other_schema_version_and_design_time",
)
ACTIVATION_TEST = HTTP_TEST_CLASS + "Revoked_bearer_remains_denied_when_real_RMA_activates_the_commissioned_tenant_schema"


def setup(f):
    target = f.create_target(LIVE_DATABASE)
    a, b = token(), token()
    request = {"Target": target, "InstallationId": token(), "OperationId": token(),
               "Tenants": [{"TenantId": a, "Login": A}, {"TenantId": b, "Login": B}]}
    f.save("commission-request.json", request)
    f.invoke("01-prepare", PREPARE, request)
    initialize(f, target["Database"])
    f.create_target("w356_legacy", template=target["Database"])
    f.invoke("02-commission", COMMISSION, request)
    f.create_target("w356_clone", template=target["Database"])

    def connection(database):
        return ("Host=127.0.0.1;Port={};Database={};Username={};Password={};"
                "SSL Mode=Disable;Pooling=false;Search Path=public;Timeout=4").format(
                    f.args.port, database, OWNER, f.passwords[OWNER])

    return {"Connection": connection(target["Database"]), "CloneConnection": connection("w356_clone"),
            "LegacyConnection": connection("w356_legacy"), "InstallationId": request["InstallationId"],
            "A": {"TenantId": a, "Login": A, "Password": f.passwords[A]},
            "B": {"TenantId": b, "Login": B, "Password": f.passwords[B]}}


def run_tests(f, fixture):
    if f.args.schema_activation:
        variable, tests, log_name = "CP6_RMA_SCHEMA_ACTIVATION_FIXTURE", (ACTIVATION_TEST,), "rma-schema-activation.trx"
    else:
        variable, tests, log_name = "CP6_RMA_READ_FIXTURE", READ_TESTS, "rma-read.trx"
    env = {name: os.environ[name] for name in os.environ
           if name.upper() in ("SYSTEMROOT", "WINDIR", "PATH", "TEMP", "TMP", "LOCALAPPDATA", "APPDATA", "USERPROFILE")}
    env.update({"DOTNET_ROOT": str(f.args.dotnet.parent), "DOTNET_CLI_HOME": str(f.root / "dotnet-home"),
                "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "CP6_PERMISSION_PG_ROOT": str(f.root),
                variable: json.dumps(fixture)})
    test_filter = "|".join("FullyQualifiedName=" + test for test in tests)
    args = [str(f.args.dotnet), "vstest", str(f.args.test_dll),
            "--TestCaseFilter:" + test_filter, "--Logger:trx;LogFileName=" + log_name,
            "--ResultsDirectory:" + str(f.root)]
    with subprocess.Popen(args, cwd=f.root, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          creationflags=FLAGS) as proc:
        out, err = f.collect(proc, seconds=180)
        f.save("03-vstest.stdout", out)
        f.save("03-vstest.stderr", err)
        f.save("03-vstest.process.json", {"Pid": proc.pid, "ExitCode": proc.returncode})
        require(proc.returncode == 0, "real bearer/EF test failed; see 03-vstest.stdout")
    # TRX includes only safe scenario outputs. Check that no fixture credential was serialized.
    raw = (f.root / log_name).read_bytes()
    require(f.scrub(raw) == raw, "test receipt contained a private fixture value")
    counters = ET.fromstring(raw).find("./{*}ResultSummary/{*}Counters")
    expected = str(len(tests))
    require(counters is not None and counters.get("total") == expected and counters.get("passed") == expected,
            "the selected RMA tests did not all run and pass; see " + log_name)


def pg_records(f):
    return re.split(r"(?m)(?=^\d{4}-\d\d-\d\d )", f.log.read_text(encoding="utf-8", errors="replace"))


def reads_relation(entry, namespace, table):
    expression = r'\bFROM\s+"?{}"?\."?{}"?(?:\s|$)'.format(re.escape(namespace), re.escape(table))
    return re.search(expression, entry, re.IGNORECASE)


def verify_native_sql(f, fixture):
    records = pg_records(f)
    selected = []
    for name in ("A", "B"):
        binding = fixture[name]
        role = binding["Login"]
        tenant_schema = schema(binding["TenantId"])
        ordinary = [entry for entry in records if f" {role}@{LIVE_DATABASE} " in entry.split("\n", 1)[0]]
        readonly = [entry for entry in ordinary if "SET TRANSACTION READ ONLY" in entry]
        require(readonly, "tenant EF transaction was not actually read-only")
        selected.extend(readonly)
        for table in ("heads", "versions"):
            matches = [entry for entry in ordinary if reads_relation(entry, tenant_schema, table)]
            require(matches, "no actual EF SELECT for {}.{} using its tenant login".format(tenant_schema, table))
            selected.extend(matches)
        other = schema(fixture["B" if name == "A" else "A"]["TenantId"])
        require(not any(other + "." in entry or '"' + other + '".' in entry
                        or "cp6_rma_application_v1." in entry for entry in ordinary),
                "ordinary EF issued SQL against another schema")
    f.save("04-ordinary-rma-sql.log", "\n".join(selected).encode("utf-8"))


def verify_activation_sql(f, fixture):
    records = pg_records(f)
    role = fixture["A"]["Login"]
    tenant_schema = schema(fixture["A"]["TenantId"])
    legacy_schema = "cp6_rma_application_v1"

    def interval(marker):
        begin, end = (next((i for i, entry in enumerate(records) if f"SELECT '{marker}_{edge}'" in entry), -1)
                      for edge in ("begin", "end"))
        require(0 <= begin < end, "missing ordered PostgreSQL witness for " + marker)
        return records[begin:end + 1]

    def mentions_schema(entry, namespace):
        return namespace + "." in entry or '"' + namespace + '".' in entry

    def ordinary(entry):
        return f" {role}@{LIVE_DATABASE} " in entry.split("\n", 1)[0]

    shared = interval("rma_shared_get")
    denied = interval("rma_revoked_bearer")
    current = interval("rma_tenant_get")
    f.save("04-activation-sql.log", "".join(shared + denied + current).encode("utf-8"))
    require(any(reads_relation(entry, legacy_schema, "heads") for entry in shared)
            and not any(mentions_schema(entry, tenant_schema) for entry in shared),
            "legacy HTTP did not use the shared RMA schema")
    require(any("cp6_authentication_v1.access_sessions" in entry for entry in denied),
            "revoked bearer did not reach the durable access-session gate")
    require(not any(ordinary(entry) or "cp6-tenant-storage-v1" in entry or f" user={role} " in entry
                    or mentions_schema(entry, legacy_schema) or mentions_schema(entry, tenant_schema) for entry in denied),
            "revoked bearer opened ordinary storage or executed RMA SQL")
    require(any(f"connection authorized: user={role} database={LIVE_DATABASE} application_name=cp6-tenant-storage-v1" in entry
                for entry in current), "current bearer did not open the ordinary tenant role")
    require(any(ordinary(entry) and "SET TRANSACTION READ ONLY" in entry for entry in current),
            "native tenant transaction was not read-only")
    for table in ("heads", "versions"):
        require(any(ordinary(entry) and reads_relation(entry, tenant_schema, table) for entry in current),
                "native HTTP did not read the tenant " + table)
    require(not any(mentions_schema(entry, legacy_schema) for entry in current), "native HTTP used the shared RMA schema")


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--root", type=Path, required=True)
    p.add_argument("--pg-bin", type=Path, required=True)
    p.add_argument("--port", type=int, required=True)
    p.add_argument("--dotnet", type=Path, required=True)
    p.add_argument("--cli", type=Path, required=True)
    p.add_argument("--webapi", type=Path, required=True, help="Verified DatabaseInit publish")
    p.add_argument("--webapi-source", required=True)
    p.add_argument("--test-dll", type=Path, required=True, help="Release CP6.Tests.dll")
    p.add_argument("--schema-activation", action="store_true", help="Verify durable bearer revocation during RMA schema activation")
    args = p.parse_args()
    for key in ("root", "pg_bin", "dotnet", "cli", "webapi", "test_dll"):
        setattr(args, key, getattr(args, key).resolve(strict=True))
    args.expected = expected_histories(args.webapi_source)
    require(len(args.expected["Core"]) > 1 and all(args.expected.values()), "published migration inventory missing")
    args.root /= "rma-http-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8]
    args.root.mkdir(mode=0o700)
    print(args.root, flush=True)
    f = Fixture(args, "fixture")
    app = args.test_dll.parent / "CP6.WebApi.dll"
    f.save("source.json", {"DbInitSource": args.webapi_source,
                            "DbInitSha256": hashlib.sha256(args.webapi.read_bytes()).hexdigest(),
                            "CommissionCliSha256": hashlib.sha256(args.cli.read_bytes()).hexdigest(),
                            "HttpSha256": hashlib.sha256(app.read_bytes()).hexdigest(),
                            "TestsSha256": hashlib.sha256(args.test_dll.read_bytes()).hexdigest()})
    with f.running():
        f.sql("postgres", "ALTER SYSTEM SET log_line_prefix TO " + q("%m [%p] %a %u@%d xid=%x ") + ";")
        if args.schema_activation:
            f.sql("postgres", "ALTER SYSTEM SET log_connections TO 'authorization';")
        f.sql("postgres", "SELECT pg_reload_conf();")
        wait_for(lambda: "%u@%d" in f.sql("postgres", "SHOW log_line_prefix;"), "PG role logging did not reload")
        if args.schema_activation:
            wait_for(lambda: f.sql("postgres", "SHOW log_connections;") == "authorization", "PG connection logging did not reload")
        fixture = setup(f)
        run_tests(f, fixture)
        if args.schema_activation:
            verify_activation_sql(f, fixture)
        else:
            verify_native_sql(f, fixture)
    if args.schema_activation:
        print("RMA schema activation, durable bearer revocation and native SQL passed", flush=True)
    else:
        print("real bearer RMA GET, EF model, native SQL and failure boundaries passed", flush=True)


if __name__ == "__main__":
    try:
        main()
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
