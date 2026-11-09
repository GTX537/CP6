#!/usr/bin/env python3
"""Verify hosted original-operation lookup and close against the actual PostgreSQL journal."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
from verify import A, B, OWNER, Fixture, q, require, token, wait_for
from verify_commission import COMMISSION, database_for, expected_histories
from verify_draft import run_http

DATABASE = "w375_live"
TEST = ("FullyQualifiedName=CP6.Tests.Persistence.Tenancy.RmaTenantReadHttpPostgresTests."
        "Original_operations_use_current_authority_and_serialize_close_with_late_writes")
INSTALLERS = (
    ("journal", "install-rma-core-original-journal-v1"),
    ("draft", "install-rma-core-draft-writer-v1"),
    ("submit", "install-rma-core-submit-writer-v1"),
)


def native_evidence(f, tenant_a, tenant_b):
    records = re.split(r"(?m)(?=^\d{4}-\d\d-\d\d )", f.log.read_text(encoding="utf-8", errors="replace"))
    chosen = []
    for role in (A, B):
        statements = [entry for entry in records if " " + role + "@" + DATABASE + " " in entry.split("\n", 1)[0]]
        readonly = [entry for entry in statements if "SET TRANSACTION READ ONLY" in entry]
        journal = [entry for entry in statements if "FROM cp6_main_owner_v1.operations" in entry]
        closed = [entry for entry in statements if "cp6_rma_original_journal_v1.store(" in entry
                  and "'CLOSED'" in entry and "NULL::bytea" in entry and "NULL::text" in entry]
        require(readonly and journal and (len(closed) == 3 if role == A else not closed),
                "ordinary native read or typed close SQL missed the expected role")
        chosen.extend(readonly + journal + closed)
    f.save("native-original-operation-statements.log", "\n".join(chosen).encode("utf-8"))
    rows = json.loads(f.sql(DATABASE, f"""
        SELECT json_object_agg(tenant_id,counts) FROM (
            SELECT tenant_id,json_build_object('closed',count(*) FILTER (WHERE state='CLOSED'),
                'committed',count(*) FILTER (WHERE state='COMMITTED')) AS counts
            FROM cp6_main_owner_v1.operations WHERE owner='Main.Wms.RmaApplication.v1'
                AND tenant_id IN ({q(tenant_a)},{q(tenant_b)})
            GROUP BY tenant_id) observed;
    """))
    require(rows == {tenant_a: {"closed": 3, "committed": 1}, tenant_b: {"closed": 0, "committed": 2}},
            "ordinary close or late writes changed the expected native journal rows")
    return rows


def exercise(f):
    tenant_a, tenant_b = token(), token()
    commissioning, _ = database_for(f, DATABASE, (tenant_a, tenant_b))
    print("published W351 DatabaseInit exited and migration histories matched", flush=True)
    original_commission = f.invoke("commission-original", COMMISSION, commissioning)
    original_installations = []
    for label, command in INSTALLERS:
        request = {**commissioning, "OperationId": token()}
        result = f.invoke(label + "-original", command, request)
        original_installations.append((label, command, request, result))

    connection = ("Host=127.0.0.1;Port={};Database={};Username={};Password={};"
                  "SSL Mode=Disable;Pooling=false;Search Path=public;Timeout=4").format(
                      f.args.port, DATABASE, OWNER, f.passwords[OWNER])
    fixture = {"Connection": connection, "CloneConnection": "", "LegacyConnection": "",
               "InstallationId": commissioning["InstallationId"],
               "A": {"TenantId": tenant_a, "Login": A, "Password": f.passwords[A]},
               "B": {"TenantId": tenant_b, "Login": B, "Password": f.passwords[B]}}
    ready = [f.state(DATABASE, tenant) for tenant in (tenant_a, tenant_b)]
    run_http(f, fixture, variable="CP6_RMA_OPERATIONS_FIXTURE", test=TEST, name="rma-operations")
    rows = native_evidence(f, tenant_a, tenant_b)
    require(f.invoke("commission-after-operations", COMMISSION, commissioning) == original_commission,
            "hosted close changed the original commissioning bytes")
    for label, command, request, original in original_installations:
        require(f.invoke(label + "-after-operations", command, request) == original,
                "hosted close changed the original " + label + " installation bytes")
    require([f.state(DATABASE, tenant) for tenant in (tenant_a, tenant_b)] == ready,
            "hosted close changed the original tenant Ready records")
    f.save("final.json", {"Rows": rows, "Ready": ready, "OriginalControlReceiptsReplayed": True})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "dotnet", "cli", "webapi", "test-dll"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--webapi-source", required=True)
    args = parser.parse_args()
    for name in ("root", "pg_bin", "dotnet", "cli", "webapi", "test_dll"):
        setattr(args, name, getattr(args, name).resolve(strict=True))
    args.expected = expected_histories(args.webapi_source)
    require(len(args.expected["Core"]) > 1 and all(args.expected.values()), "published migration inventory missing")
    args.root /= "rma-operations-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8]
    args.root.mkdir(mode=0o700)
    print(args.root, flush=True)
    f = Fixture(args, "operations")
    artifacts = {"DatabaseInit": args.webapi, "SealedW372Cli": args.cli, "Tests": args.test_dll,
                 "WebApi": args.test_dll.with_name("CP6.WebApi.dll"), "Core": args.test_dll.with_name("CP6.Core.dll")}
    f.save("source.json", {"DatabaseInitSource": args.webapi_source,
                           "Sha256": {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in artifacts.items()}})
    with f.running():
        f.sql("postgres", "ALTER SYSTEM SET log_line_prefix TO " + q("%m [%p] %a %u@%d xid=%x ") + ";")
        f.sql("postgres", "SELECT pg_reload_conf();")
        wait_for(lambda: "%u@%d" in f.sql("postgres", "SHOW log_line_prefix;"), "PG role logging did not reload")
        exercise(f)
    print("hosted RMA lookup, current authority and both close race directions passed", flush=True)


if __name__ == "__main__":
    try:
        main()
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
