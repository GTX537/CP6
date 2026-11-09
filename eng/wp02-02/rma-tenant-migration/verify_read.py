#!/usr/bin/env python3
"""Run the original RMA HTTP GET against an owned, commissioned Core PostgreSQL fixture."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

sys.dont_write_bytecode = True
from verify import Fixture, FLAGS, A, B, OWNER, q, require, schema, token, wait_for
from verify_commission import PREPARE, COMMISSION, expected_histories, initialize


def setup(f):
    target = f.create_target("w356_live")
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
    env = {name: os.environ[name] for name in os.environ
           if name.upper() in ("SYSTEMROOT", "WINDIR", "PATH", "TEMP", "TMP", "LOCALAPPDATA", "APPDATA", "USERPROFILE")}
    env.update({"DOTNET_ROOT": str(f.args.dotnet.parent), "DOTNET_CLI_HOME": str(f.root / "dotnet-home"),
                "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "CP6_PERMISSION_PG_ROOT": str(f.root),
                "CP6_RMA_READ_FIXTURE": json.dumps(fixture)})
    args = [str(f.args.dotnet), "vstest", str(f.args.test_dll),
            "--TestCaseFilter:FullyQualifiedName~RmaTenantRead", "--Logger:trx;LogFileName=rma-read.trx",
            "--ResultsDirectory:" + str(f.root)]
    with subprocess.Popen(args, cwd=f.root, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          creationflags=FLAGS) as proc:
        out, err = f.collect(proc, seconds=180)
        f.save("03-vstest.stdout", out)
        f.save("03-vstest.stderr", err)
        f.save("03-vstest.process.json", {"Pid": proc.pid, "ExitCode": proc.returncode})
        require(proc.returncode == 0, "real bearer/EF test failed; see 03-vstest.stdout")
    # TRX includes only safe scenario outputs. Check that no fixture credential was serialized.
    trx = f.root / "rma-read.trx"
    raw = trx.read_bytes()
    require(f.scrub(raw) == raw, "test receipt contained a private fixture value")


def verify_native_sql(f, fixture):
    records = re.split(r"(?m)(?=^\d{4}-\d\d-\d\d )", f.log.read_text(encoding="utf-8", errors="replace"))
    selected = []
    for name in ("A", "B"):
        binding = fixture[name]
        role = binding["Login"]
        tenant_schema = schema(binding["TenantId"])
        ordinary = [entry for entry in records if " " + role + "@w356_live " in entry.split("\n", 1)[0]]
        readonly = [entry for entry in ordinary if "SET TRANSACTION READ ONLY" in entry]
        require(readonly, "tenant EF transaction was not actually read-only")
        selected.extend(readonly)
        for table in ("heads", "versions"):
            expression = r'\bFROM\s+"?{}"?\."?{}"?(?:\s|$)'.format(tenant_schema, table)
            matches = [entry for entry in ordinary if re.search(expression, entry, re.IGNORECASE)]
            require(matches, "no actual EF SELECT for {}.{} using its tenant login".format(tenant_schema, table))
            selected.extend(matches)
        other = schema(fixture["B" if name == "A" else "A"]["TenantId"])
        require(not any(other + "." in entry or '"' + other + '".' in entry
                        or "cp6_rma_application_v1." in entry for entry in ordinary),
                "ordinary EF issued SQL against another schema")
    f.save("04-ordinary-rma-sql.log", "\n".join(selected).encode("utf-8"))


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
        f.sql("postgres", "SELECT pg_reload_conf();")
        wait_for(lambda: "%u@%d" in f.sql("postgres", "SHOW log_line_prefix;"), "PG role logging did not reload")
        fixture = setup(f)
        run_tests(f, fixture)
        verify_native_sql(f, fixture)
    print("real bearer RMA GET, EF model, native SQL and failure boundaries passed", flush=True)


if __name__ == "__main__":
    try:
        main()
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
