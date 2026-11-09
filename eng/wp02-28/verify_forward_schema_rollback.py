"""Verify a forward full-Core PostgreSQL migration and a local application rollback."""

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import secrets
import subprocess
from urllib.request import HTTPSHandler, ProxyHandler, build_opener
from uuid import uuid4

import pg_resource as pg
import published_artifact as artifact
from verify_host import digest, write
from verify_published_host import isolated_environment
from verify_service_token_pair import (
    DRIVERS, ISSUER, RELEASES, http, issue, issue_certificate, observe, prepare_host,
    revoke, runtime_host, stop_database, verified_release, verify_revocation,
)


CURRENT_SOURCE = "c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40"
TENANT = "00000000-0000-0000-0000-0000000000a1"
VERSION = "local-forward-schema-rollback"
HISTORIES = {
    "Core": "__EFMigrationsHistory",
    "Space": "__EFMigrationsHistory_Space",
    "IdentityPriority": "__EFMigrationsHistory_IdentityPriority",
    "ErpIntegration": "__EFMigrationsHistory_ErpIntegration",
}


def now():
    return datetime.now(timezone.utc).isoformat()


def migrations(source):
    root = "CP6.Persistence.PostgreSql/Migrations"
    paths = artifact.git("ls-tree", "-rz", "--name-only", source, root).decode().split("\0")
    return {
        context: sorted(Path(path).stem for path in paths
                        if path.startswith(f"{root}/{context}/")
                        and re.fullmatch(r"\d{14}_[^.]+\.cs", Path(path).name))
        for context in HISTORIES
    }


def histories(control, database):
    entries = ", ".join(f"""'{context}', (SELECT json_agg(h ORDER BY "MigrationId") FROM public."{table}" h)"""
                        for context, table in HISTORIES.items())
    return json.loads(pg.sql(control, f"SELECT json_build_object({entries});", database))


def check_histories(actual, expected):
    ids = {context: [row["MigrationId"] for row in rows] for context, rows in actual.items()}
    pg.require(ids == expected, "Physical PostgreSQL migration lineage differs from the source")


def stock_dispatch(control, database):
    definition = pg.sql(control, """
        SELECT pg_get_functiondef(to_regprocedure('cp6_operations_owner_v1.mapping_validate()'));
        """, database)
    if definition:
        pg.require("NEW.object_kind='stock' THEN" in definition, "Forward Stock mapping branch is missing")
        return digest(definition.encode())
    return None


def configuration(control, database, client, signing, certificate, root):
    return {
        "Startup": {"SkipDatabaseInitialization": True, "SkipHostedServices": True},
        "Database": {"Provider": "PostgreSql"},
        "ConnectionStrings": {
            "DefaultConnection": f'Host=127.0.0.1;Port={pg.PORT};Database={database};'
                                 f'Username={control["Admin"]};Password={control["AdminPassword"]};'
                                 'SSL Mode=Disable;Timeout=3',
            "Redis": "",
        },
        "JWT": {"Issuer": "CP6", "Audience": "CP6.Web", "Secret": secrets.token_hex(32)},
        "CrmOidc": {
            "Enabled": True, "Issuer": ISSUER, "ActiveKeyId": "w351",
            "Keys": [{"Kid": "w351", "Pem": signing}],
            "Clients": [{"ClientId": "CP6.Web", "SecretSha256": digest(secrets.token_bytes(32)),
                         "RedirectUris": [ISSUER + "/callback"]}],
            "Organizations": [{"TenantId": TENANT, "Slug": "w351-local", "Region": "us", "CrmEnabled": True}],
            "ServiceClients": [{"ClientId": client["id"], "TenantId": TENANT, "Enabled": True,
                                "SecretSha256": digest(client["secret"].encode()),
                                "AllowedScopes": ["cp6.services"]}],
        },
        "CrmIdentity": {"Enabled": True, "Tenants": {TENANT: "us"},
                        "ProjectionReaderClientIds": [client["id"]]},
        "Kestrel": {"Certificates": {"Default": certificate}},
        "Storage": {"Provider": "Local", "LocalRoot": str(root / "uploads")},
        "Space": {"Files": {"RootPath": str(root / "space-files")}},
        "ErpIntegration": {"Enabled": False},
        "SpaceObservability": {"MetricsEnabled": False},
        "Kafka": {"BootstrapServers": ""},
        "OperLog": {"IncludeGet": False},
    }


def initialize(root, name, release, config, processes):
    config = config | {
        "Startup": {"Mode": "DatabaseInit", "SkipHostedServices": True},
        "CrmOidc": {"Enabled": False},
        "CrmIdentity": {"Enabled": False},
    }
    content, command = prepare_host(root, name, release, config, VERSION)
    stdout = content / "stdout.log"
    stderr = content / "stderr.log"
    run = {"sourceSha": release["sourceSha"], "command": command, "startedUtc": now(),
           "processStopped": False, "stdout": str(stdout), "stderr": str(stderr)}
    processes[name] = run
    with stdout.open("wb") as output, stderr.open("wb") as error:
        with subprocess.Popen(command, cwd=content, env=isolated_environment(),
                              stdout=output, stderr=error, creationflags=pg.NO_WINDOW) as process:
            run["pid"] = process.pid
            try:
                process.wait(timeout=300)
            finally:
                if process.poll() is None:
                    process.kill()
                    process.wait()
                run.update(exitCode=process.returncode, processStopped=True, stoppedUtc=now())
    pg.require(run["exitCode"] == 0 and b"Database initialization completed;" in stdout.read_bytes(),
               f'{name} exited {run["exitCode"]}; see private logs')


def release_health(opener, report, name, base, release, latest):
    body = http(opener, report["http"], name, base, "/health/release", 200)
    report["http"][name]["body"] = body
    pg.require(body["gitSha"] == release["sourceSha"] and body["latestMigration"] == latest,
               f"{name} did not report the observed migration")


def exercise(root, control, releases, report):
    prior, current = releases["prior"], releases["current"]
    prior_lineage = migrations(prior["sourceSha"])
    current_lineage = migrations(current["sourceSha"])
    pg.require([len(prior_lineage[key]) for key in HISTORIES] == [1, 1, 1, 1]
               and len(current_lineage["Core"]) == 224
               and current_lineage["Core"][:1] == prior_lineage["Core"]
               and all(current_lineage[key] == prior_lineage[key] for key in HISTORIES if key != "Core"),
               "Unexpected fixed source migration range")
    database = "w351_core_" + control["Owner"][:8]
    pg.verify(control)
    pg.sql(control, f'CREATE DATABASE "{database}" OWNER "{control["Admin"]}" TEMPLATE template0;', "postgres")
    physical = pg.identity(control, database)
    server_version = pg.sql(control, "SHOW server_version;", database)
    pg.require(server_version == "18.6", "Expected actual PostgreSQL 18.6")
    report["database"] = physical | {"serverVersion": server_version, "profile": "full-core-postgresql"}
    client = {"id": "w351-service", "tenant": TENANT, "secret": secrets.token_hex(24)}
    signing, certificate, context = issue_certificate(root)
    config = configuration(control, database, client, signing, certificate, root)
    opener = build_opener(ProxyHandler({}), HTTPSHandler(context=context))
    processes = report["processes"]
    report["stage"] = "prior_database_init"
    initialize(root, "prior_database_init", prior, config, processes)
    before = histories(control, database)
    report["history"]["before"] = before
    check_histories(before, prior_lineage)
    pg.require(stock_dispatch(control, database) is None, "Forward Stock mapping already exists")
    start = observe(control, database)
    report["state"]["initial"] = start
    report["stage"] = "prior_runtime"
    with runtime_host(root, "prior_runtime", prior, config, processes, VERSION) as base:
        release_health(opener, report, "prior_release", base, prior, prior_lineage["Core"][-1])
        jti, issued = issue(opener, report, control, "prior_issue", base, client, start,
                            database=database, key_id="w351")
    report["stage"] = "current_database_init"
    pg.zero_sessions(control, database)
    initialize(root, "current_database_init", current, config, processes)
    after = histories(control, database)
    report["history"]["afterForward"] = after
    check_histories(after, current_lineage)
    pg.require(after | {"Core": after["Core"][:1]} == before
               and pg.identity(control, database) == physical,
               "Forward migration changed original EF rows or physical database identity")
    stock = stock_dispatch(control, database)
    pg.require(stock is not None, "The latest applied migration did not install its native function")
    report["stockDispatchSha256"] = stock
    migrated = observe(control, database)
    report["state"]["afterForward"] = migrated
    pg.require(migrated == issued, "Forward migration changed the original C02 business state")
    latest = current_lineage["Core"][-1]
    report["stage"] = "current_runtime"
    with runtime_host(root, "current_runtime", current, config, processes, VERSION) as base:
        release_health(opener, report, "current_release", base, current, latest)
        revoke(opener, report, "current_revoke_prior", base, client, jti)
        revoked = observe(control, database)
        report["state"]["current_revoke_prior"] = revoked
        verify_revocation(migrated, revoked, TENANT, jti)
        new_jti, new_issue = issue(opener, report, control, "current_issue", base, client, revoked,
                                    database=database, key_id="w351")
    report["stage"] = "prior_rollback"
    pg.zero_sessions(control, database)
    with runtime_host(root, "prior_rollback", prior, config, processes, VERSION) as base:
        release_health(opener, report, "rollback_release", base, prior, latest)
        revoke(opener, report, "prior_revoke_current", base, client, new_jti)
        final = observe(control, database)
        report["state"]["prior_revoke_current"] = final
        verify_revocation(new_issue, final, TENANT, new_jti)
        for label, original in (("prior_replay_prior", jti), ("prior_replay_current", new_jti)):
            revoke(opener, report, label, base, client, original)
            repeat = observe(control, database)
            report["state"][label] = repeat
            pg.require(repeat == final, f"{label} changed original rows or event bytes")
    rolled_back = histories(control, database)
    report["history"]["afterApplicationRollback"] = rolled_back
    pg.require(rolled_back == after and pg.identity(control, database) == physical
               and stock_dispatch(control, database) == stock,
               "Application rollback changed the advanced schema or physical database")
    report["stage"] = "verified"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("private-root", "prior-root", "current-receipt", "package-cache"):
        parser.add_argument("--" + name, required=True, type=Path)
    parser.add_argument("--current-receipt-sha256", required=True)
    parser.add_argument("--settings", type=Path, help="An already running, fresh task-owned PG resource")
    args = parser.parse_args()
    pg.ROOT = args.private_root.absolute()
    control = pg.load_settings(args.settings) if args.settings else None
    root = Path(control["RunDirectory"]) if control else pg.ROOT / uuid4().hex
    result_path = root / "w351-results.json"
    pg.require(not result_path.exists(), "Use a fresh owned resource; existing evidence is retained")
    print(f"W351 private evidence: {root}", flush=True)
    report = {"completed": False, "startedUtc": now(), "stage": "setup", "releases": {},
              "scope": "LOCAL_EVIDENCE_ONLY", "driverDeps": DRIVERS, "processes": {},
              "history": {}, "http": {}, "state": {}}
    try:
        prior_source, prior_pin = RELEASES["prior"]
        prior = verified_release(args.prior_root / "prior", args.prior_root / "prior.receipt.json",
                                 prior_pin, prior_source, args.package_cache)
        current = verified_release(args.current_receipt.with_name("current"), args.current_receipt,
                                   args.current_receipt_sha256, CURRENT_SOURCE, args.package_cache)
        pg.require(prior["driverFiles"] == current["driverFiles"], "Driver assembly bytes changed")
        report["releases"] = {"prior": prior, "current": current}
        report["rollbackDirection"] = {"fromSource": CURRENT_SOURCE, "toSource": prior_source}
        if control is None:
            pg.create(root.name)
            control = pg.load_settings(root / "settings.json")
        exercise(root, control, report["releases"], report)
    except BaseException as error:
        report["failure"] = type(error).__name__
        raise
    finally:
        try:
            stop_database(root, control)
            report["postgresStopped"] = control is not None
        except Exception as error:
            report["cleanupFailure"] = type(error).__name__
            if "failure" not in report:
                raise
        finally:
            report["completed"] = (report["stage"] == "verified" and report.get("postgresStopped", False)
                                   and all(run["processStopped"] and run.get("listenerClosed", True)
                                           for run in report["processes"].values()))
            report["finishedUtc"] = now()
            if root.is_dir():
                write(result_path, report)
    pg.require(report["completed"], "Forward schema and rollback validation incomplete")
    print("W351 actual forward migration and application rollback passed")


if __name__ == "__main__":
    main()
