#!/usr/bin/env python3
"""Verify the hosted RMA draft POST and its exact PostgreSQL original on an owned database."""
import argparse
import csv
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

sys.dont_write_bytecode = True
from verify import A, B, BASE_ENV, FLAGS, OWNER, Fixture, q, require, schema, token, wait_for
from verify_commission import COMMISSION, database_for, expected_histories
from verify_original_journal import COMMAND as JOURNAL, cli_from

COMMAND = "install-rma-core-draft-writer-v1"
DATABASE = "w364_live"
DRAFT_SCHEMA = "cp6_rma_draft_writer_v1"
TEST = ("FullyQualifiedName=CP6.Tests.Persistence.Tenancy.RmaTenantReadHttpPostgresTests."
        "Draft_post_uses_native_ordinary_transaction_and_recovers_the_original")


def run_http(f, fixture):
    env = {name: os.environ[name] for name in os.environ if name.upper() in
           ("SYSTEMROOT", "WINDIR", "PATH", "TEMP", "TMP", "LOCALAPPDATA", "APPDATA", "USERPROFILE")}
    env.update({"DOTNET_ROOT": str(f.args.dotnet.parent), "DOTNET_CLI_HOME": str(f.root / "dotnet-home"),
                "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "CP6_PERMISSION_PG_ROOT": str(f.root),
                "CP6_RMA_DRAFT_FIXTURE": json.dumps(fixture)})
    args = [str(f.args.dotnet), "vstest", str(f.args.test_dll), "--TestCaseFilter:" + TEST,
            "--Logger:trx;LogFileName=rma-draft.trx", "--ResultsDirectory:" + str(f.root)]
    with subprocess.Popen(args, cwd=f.root, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          creationflags=FLAGS) as proc:
        out, err = f.collect(proc, seconds=180)
        f.save("http.stdout", out)
        f.save("http.stderr", err)
        f.save("http.process.json", {"Pid": proc.pid, "ExitCode": proc.returncode})
        require(proc.returncode == 0, "hosted draft test failed; see http.stdout")
    trx = (f.root / "rma-draft.trx").read_bytes()
    require(f.scrub(trx) == trx and b'passed="1"' in trx and b'executed="1"' in trx,
            "the exact draft scenario was not executed once or the receipt contains a private fixture value")


def assert_runtime_permissions(f, tenant_a, tenant_b):
    a_schema = schema(tenant_a)
    f.sql(DATABASE, f"INSERT INTO {a_schema}.heads(tenant_id,id,actor_id,outbound_id,version) VALUES "
          f"({q(tenant_a)},{q(token())},{q(token())},{q(token())},1);", role=A, error="42501", label="ordinary-head-insert")
    f.sql(DATABASE, f"SELECT {DRAFT_SCHEMA}.create_draft({q(tenant_b)},NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);",
          role=A, error="42501", label="ordinary-cross-tenant-draft")
    require(b"RMA_DRAFT_TENANT_BINDING_REJECTED" in (f.root / "ordinary-cross-tenant-draft.stderr").read_bytes(),
            "cross-tenant draft missed the native tenant guard")
    f.sql(DATABASE, f"SELECT {DRAFT_SCHEMA}.append_original(NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);",
          role=A, error="42501", label="ordinary-bridge-execute")
    f.sql(DATABASE, "SET ROLE cp6_rma_original_journal_executor_v1;", role=A, error="42501", label="ordinary-set-executor")


def assert_native_execution(f):
    records = re.split(r"(?m)(?=^\d{4}-\d\d-\d\d )", f.log.read_text(encoding="utf-8", errors="replace"))
    chosen = []
    for role in (A, B):
        statements = [entry for entry in records if " " + role + "@" + DATABASE + " " in entry.split("\n", 1)[0]]
        locks = [entry for entry in statements if DRAFT_SCHEMA + ".lock_draft(" in entry]
        writes = [entry for entry in statements if DRAFT_SCHEMA + ".create_draft(" in entry]
        commits = [entry for entry in statements if re.search(r"\bstatement: COMMIT\b", entry)]
        require(locks and writes and commits, "no physical ordinary lock/write/commit for a commissioned login")
        chosen.extend(locks + writes + commits)
    faults = [entry for entry in records if "ERROR:" in entry and "W364_FIXTURE_JOURNAL_FAILURE_AFTER_BOTH_ROWS" in entry]
    require(len(faults) == 1, "the journal fault did not observe both earlier aggregate writes")
    chosen.extend(faults)
    f.save("native-draft-statements.log", "\n".join(chosen).encode("utf-8"))


def exercise(f):
    tenant_a, tenant_b = token(), token()
    with cli_from(f, f.args.commission_cli):
        commissioning, _ = database_for(f, DATABASE, (tenant_a, tenant_b))
        commission_result = f.invoke("commission-original", COMMISSION, commissioning)

    draft = {**commissioning, "OperationId": token()}
    journal = {**commissioning, "OperationId": token()}
    f.invoke("draft-missing-journal", COMMAND, draft, 1, "draft-journal-required")
    with cli_from(f, f.args.journal_cli):
        journal_result = f.invoke("journal-original", JOURNAL, journal)
    wrong = {**draft, "Target": {**draft["Target"], "DatabaseOid": draft["Target"]["DatabaseOid"] + 1}}
    f.invoke("draft-wrong-physical", COMMAND, wrong, 1, "target-identity-rejected")
    installed = f.invoke("draft-installed", COMMAND, draft)
    require(json.loads(installed)["Outcome"] == "INSTALLED" and
            f.invoke("draft-install-replayed", COMMAND, draft) == installed, "draft installation did not recover its original")
    f.invoke("draft-install-different-key", COMMAND, {**draft, "OperationId": token()}, 1, "operation-conflict")
    assert_runtime_permissions(f, tenant_a, tenant_b)
    connection = ("Host=127.0.0.1;Port={};Database={};Username={};Password={};"
                  "SSL Mode=Disable;Pooling=false;Search Path=public;Timeout=4").format(
                      f.args.port, DATABASE, OWNER, f.passwords[OWNER])
    fixture = {"Connection": connection, "CloneConnection": "", "LegacyConnection": "",
               "InstallationId": commissioning["InstallationId"],
               "A": {"TenantId": tenant_a, "Login": A, "Password": f.passwords[A]},
               "B": {"TenantId": tenant_b, "Login": B, "Password": f.passwords[B]}}
    initial_ready = [f.state(DATABASE, tenant) for tenant in (tenant_a, tenant_b)]
    run_http(f, fixture)
    assert_native_execution(f)
    require(f.invoke("draft-install-after-use", COMMAND, draft) == installed,
            "draft installer replay changed after the ordinary writes")
    with cli_from(f, f.args.journal_cli):
        require(f.invoke("journal-after-drafts", JOURNAL, journal) == journal_result and
                f.invoke("commission-after-drafts", COMMISSION, commissioning) == commission_result,
                "the existing W361 executable no longer recovers its original journal or commissioning bytes")
    require([f.state(DATABASE, tenant) for tenant in (tenant_a, tenant_b)] == initial_ready,
            "drafts changed one of the original Ready/history records")
    require(f.sql(DATABASE, "SELECT (SELECT count(*) FROM cp6_rma_application_v1.heads)+"
                  "(SELECT count(*) FROM cp6_rma_application_v1.versions);") == "0", "legacy aggregate was used")
    f.save("final.json", {"Draft": json.loads(installed), "Ready": initial_ready,
                          "PreviousJournalReplayed": True, "PreviousCommissionReplayed": True})


def archive_after_draft(f):
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "rma-tenant-archive"))
    import archive

    commissioning = json.loads((f.root / (DATABASE + "-request.json")).read_text(encoding="utf-8"))
    target = archive.PhysicalTarget.from_mapping(commissioning["Target"])
    installation = commissioning["InstallationId"]
    tenant = commissioning["Tenants"][0]["TenantId"]
    tenant_schema = schema(tenant)
    environ = {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}

    def export(label, journal=False):
        return archive.export_bundle(f.args.pg_bin, target, OWNER, installation, tenant, f.root / label,
                                     include_original_journal=journal, environ=environ)

    def rejects_acl(label):
        try:
            export(label)
        except archive.ArchiveError as error:
            require(str(error) == "rma-schema-acl-rejected", label + " failed at the wrong boundary")
            require(not (f.root / label / "bundle/manifest.json").exists(), "rejected export published a manifest")
        else:
            raise RuntimeError(label + " accepted an unsafe draft ACL")

    v1 = archive.verify_bundle(export("archive-v1-after-draft"), tenant)
    v2_path = export("archive-v2-after-draft", journal=True)
    v2 = archive.verify_bundle(v2_path, tenant)
    require(v1["format"] == archive.FORMAT and v2["format"] == archive.FORMAT_WITH_JOURNAL
            and v1["counts"] == v2["counts"] == {"heads": 1, "versions": 1, "history": 1, "outbounds": 1}
            and v2["journal"]["counts"] == {"rows": 1, "committed": 1, "closed": 0},
            "archive did not select exactly the committed A draft and its original")
    with (v2_path / archive.JOURNAL_MEMBER).open(encoding="utf-8", newline="") as source:
        rows = list(csv.DictReader(source))
    original = json.loads(f.sql(DATABASE, f"""
        SELECT json_build_object('operation_id',operation_id,'input_bytes',input_bytes,'result_bytes',result_bytes)
        FROM cp6_main_owner_v1.operations WHERE tenant_id={q(tenant)} AND owner={q(archive.JOURNAL_OWNER)};
    """))
    require(len(rows) == 1 and rows[0]["tenant_id"] == tenant
            and all(rows[0][key] == value for key, value in original.items()),
            "v2 archive changed the committed draft original bytes")

    executor = "cp6_rma_original_journal_executor_v1"
    f.sql(DATABASE, f"GRANT UPDATE (version) ON {tenant_schema}.heads TO {executor};")
    try:
        rejects_acl("archive-extra-executor-update")
    finally:
        f.sql(DATABASE, f"REVOKE UPDATE (version) ON {tenant_schema}.heads FROM {executor};")

    f.sql(DATABASE, f"UPDATE {DRAFT_SCHEMA}.installation SET installation_id={q(token())} WHERE singleton;")
    try:
        rejects_acl("archive-wrong-writer-marker")
    finally:
        f.sql(DATABASE, f"UPDATE {DRAFT_SCHEMA}.installation SET installation_id={q(installation)} WHERE singleton;")
    f.save("archive-after-draft.json", {"v1": v1["counts"], "v2": v2["journal"]["counts"],
                                       "OriginalBytesMatch": True, "UnexpectedUpdateRejected": True,
                                       "WrongWriterMarkerRejected": True})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "dotnet", "cli", "commission-cli", "journal-cli", "webapi", "test-dll"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--webapi-source", required=True)
    args = parser.parse_args()
    for name in ("root", "pg_bin", "dotnet", "cli", "commission_cli", "journal_cli", "webapi", "test_dll"):
        setattr(args, name, getattr(args, name).resolve(strict=True))
    args.expected = expected_histories(args.webapi_source)
    require(len(args.expected["Core"]) > 1 and all(args.expected.values()), "published migration inventory missing")
    args.root /= "rma-draft-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8]
    args.root.mkdir(mode=0o700)
    print(args.root, flush=True)
    f = Fixture(args, "draft")
    artifacts = {"DatabaseInit": args.webapi, "CommissionCli": args.commission_cli, "JournalCli": args.journal_cli,
                 "Cli": args.cli, "CliCore": args.cli.with_name("CP6.Core.dll"), "Tests": args.test_dll,
                 "WebApi": args.test_dll.with_name("CP6.WebApi.dll"),
                 "Core": args.test_dll.with_name("CP6.Core.dll")}
    f.save("source.json", {"DatabaseInitSource": args.webapi_source,
                           "Sha256": {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in artifacts.items()}})
    with f.running():
        f.sql("postgres", "ALTER SYSTEM SET log_line_prefix TO " + q("%m [%p] %a %u@%d xid=%x ") + ";")
        f.sql("postgres", "SELECT pg_reload_conf();")
        wait_for(lambda: "%u@%d" in f.sql("postgres", "SHOW log_line_prefix;"), "PG role logging did not reload")
        exercise(f)
        archive_after_draft(f)
    print("hosted RMA draft POST, original recovery and source archives passed", flush=True)


if __name__ == "__main__":
    try:
        main()
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
