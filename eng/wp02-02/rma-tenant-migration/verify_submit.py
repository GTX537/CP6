#!/usr/bin/env python3
"""Verify hosted native RMA submit, additive writer recovery and both archive profiles."""
import argparse
import csv
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import secrets
import sys

sys.dont_write_bytecode = True
from verify import A, B, BASE_ENV, OWNER, Fixture, ident, q, require, schema, token, wait_for
from verify_commission import COMMISSION, database_for, expected_histories
from verify_draft import COMMAND as DRAFT, DRAFT_SCHEMA, run_http
from verify_original_journal import COMMAND as JOURNAL, cli_from

sys.path.append(str(Path(__file__).resolve().parent.parent / "rma-tenant-archive"))
import archive

COMMAND = "install-rma-core-submit-writer-v1"
DATABASE = "w372_live"
SUBMIT_SCHEMA = "cp6_rma_submit_writer_v1"
EXECUTOR = "cp6_rma_original_journal_executor_v1"
TEST = ("FullyQualifiedName=CP6.Tests.Persistence.Tenancy.RmaTenantReadHttpPostgresTests."
        "Submit_post_keeps_the_original_native_transaction_and_IfMatch_replay")


def prepare_archive_reader(f, commissioning):
    reader = "w363_backup"
    f.passwords[reader] = secrets.token_hex(24)
    f.sql("postgres", f"SET log_statement='none'; CREATE ROLE {ident(reader)} LOGIN IN ROLE pg_read_all_data PASSWORD {q(f.passwords[reader])};")
    f.sql(DATABASE, f"""
        GRANT CONNECT ON DATABASE {ident(DATABASE)} TO {ident(reader)};
        GRANT EXECUTE ON FUNCTION pg_catalog.pg_control_system() TO {ident(reader)};
    """)
    target = archive.PhysicalTarget.from_mapping(commissioning["Target"])
    tenant = commissioning["Tenants"][0]["TenantId"]
    draft_only = archive.export_bundle(f.args.pg_bin, target, reader, commissioning["InstallationId"], tenant,
                                       f.root / "archive-v1-draft-only",
                                       environ={**BASE_ENV, "PGPASSWORD": f.passwords[reader]})
    require(archive.verify_bundle(draft_only, tenant)["counts"] == {"heads": 0, "versions": 0, "history": 1, "outbounds": 0},
            "v1 backup could not consume the existing draft-only permission profile")
    return reader


def archived(f, commissioning, reader):
    target = archive.PhysicalTarget.from_mapping(commissioning["Target"])
    installation = commissioning["InstallationId"]
    tenant = commissioning["Tenants"][0]["TenantId"]
    tenant_schema = schema(tenant)

    def export(label, role=OWNER, journal=False):
        return archive.export_bundle(f.args.pg_bin, target, role, installation, tenant, f.root / label,
                                     include_original_journal=journal,
                                     environ={**BASE_ENV, "PGPASSWORD": f.passwords[role]})

    def rejects_acl(label):
        try:
            export(label)
        except archive.ArchiveError as error:
            require(str(error) == "rma-schema-acl-rejected", label + " failed at the wrong boundary")
            require(not (f.root / label / "bundle/manifest.json").exists(), "rejected export published a manifest")
        else:
            raise RuntimeError(label + " accepted an unsafe submit ACL")

    v1 = archive.verify_bundle(export("archive-v1-submit-reader", role=reader), tenant)
    v2_path = export("archive-v2-submit", journal=True)
    v2 = archive.verify_bundle(v2_path, tenant)
    require(v1["format"] == archive.FORMAT and v2["format"] == archive.FORMAT_WITH_JOURNAL
            and v1["counts"] == v2["counts"] == {"heads": 1, "versions": 2, "history": 1, "outbounds": 1}
            and v2["journal"]["counts"] == {"rows": 2, "committed": 2, "closed": 0},
            "archive missed the native submission, history or originals")
    originals = json.loads(f.sql(DATABASE, f"""
        SELECT json_agg(json_build_object('operation_id',operation_id,'input_bytes',input_bytes,'result_bytes',result_bytes)
                        ORDER BY operation_id)
        FROM cp6_main_owner_v1.operations WHERE tenant_id={q(tenant)} AND owner={q(archive.JOURNAL_OWNER)};
    """))
    with (v2_path / archive.JOURNAL_MEMBER).open(encoding="utf-8", newline="") as source:
        rows = list(csv.DictReader(source))
    require(len(rows) == len(originals) == 2 and all(row["tenant_id"] == tenant and
            all(row[key] == value for key, value in original.items()) for row, original in zip(rows, originals)),
            "v2 archive changed the RMA draft/submit original bytes")

    f.sql(DATABASE, f"GRANT UPDATE (actor_id) ON {tenant_schema}.heads TO {EXECUTOR};")
    try:
        rejects_acl("archive-extra-head-update")
    finally:
        f.sql(DATABASE, f"REVOKE UPDATE (actor_id) ON {tenant_schema}.heads FROM {EXECUTOR};")
    f.save("archive-after-submit.json", {"v1": v1["counts"], "v2": v2["journal"]["counts"],
                                       "NonOwnerV1": reader, "OriginalBytesMatch": True, "ExtraUpdateRejected": True})
    return rejects_acl


def runtime_permissions(f, tenant_a, tenant_b):
    a_schema = schema(tenant_a)
    f.sql(DATABASE, f"UPDATE {a_schema}.heads SET version=version;", role=A, error="42501", label="ordinary-head-update")
    f.sql(DATABASE, f"SELECT {SUBMIT_SCHEMA}.submit_draft({q(tenant_b)},NULL,NULL,NULL,1,NULL,NULL,NULL,NULL,NULL);",
          role=A, error="42501", label="ordinary-cross-tenant-submit")
    require(b"RMA_DRAFT_TENANT_BINDING_REJECTED" in (f.root / "ordinary-cross-tenant-submit.stderr").read_bytes(),
            "cross-tenant submit missed the native session guard")
    f.sql(DATABASE, f"SET ROLE {EXECUTOR};", role=A, error="42501", label="ordinary-set-executor")


def native_evidence(f, tenant):
    records = re.split(r"(?m)(?=^\d{4}-\d\d-\d\d )", f.log.read_text(encoding="utf-8", errors="replace"))
    chosen = []
    for role in (A, B):
        statements = [entry for entry in records if " " + role + "@" + DATABASE + " " in entry.split("\n", 1)[0]]
        writes = [entry for entry in statements if SUBMIT_SCHEMA + ".submit_draft(" in entry]
        commits = [entry for entry in statements if re.search(r"\bstatement: COMMIT\b", entry)]
        require(writes and commits, "no physical ordinary submit and commit for a commissioned login")
        chosen.extend(writes + commits)
    faults = [entry for entry in records if "ERROR:" in entry and "W372_FIXTURE_JOURNAL_FAILURE_AFTER_CAS_AND_VERSION" in entry]
    require(len(faults) == 1, "journal fault did not observe both prior submit changes")
    f.save("native-submit-statements.log", "\n".join(chosen + faults).encode("utf-8"))

    submitted = json.loads(f.sql(DATABASE, f"SELECT row_to_json(h) FROM {schema(tenant)}.heads h;"))
    rejected = token()
    require(f.sql(DATABASE, f"""
        SELECT {SUBMIT_SCHEMA}.submit_draft({q(tenant)},{q(rejected)},{q(submitted["id"])},
            {q(submitted["actor_id"])},2,NULL,NULL,NULL,NULL,NULL);
    """, role=A, label="ordinary-submitted-state-rejected") == "f", "native SQL accepted an already submitted head")
    require(f.sql(DATABASE, f"SELECT count(*) FROM cp6_main_owner_v1.operations WHERE operation_id={q(rejected)};") == "0",
            "native state rejection stored a new original")


def exercise(f):
    tenant_a, tenant_b = token(), token()
    with cli_from(f, f.args.commission_cli):
        commissioning, _ = database_for(f, DATABASE, (tenant_a, tenant_b))
        print("published W351 DatabaseInit exited and migration histories matched", flush=True)
        commission_result = f.invoke("commission-original", COMMISSION, commissioning)

    submit = {**commissioning, "OperationId": token()}
    journal = {**commissioning, "OperationId": token()}
    draft = {**commissioning, "OperationId": token()}
    with cli_from(f, f.args.journal_cli):
        journal_result = f.invoke("journal-original", JOURNAL, journal)
    f.invoke("submit-missing-draft", COMMAND, submit, 1, "submit-draft-required")
    with cli_from(f, f.args.draft_cli):
        draft_result = f.invoke("draft-original", DRAFT, draft)
        require(f.invoke("old-draft-before-submit", DRAFT, draft) == draft_result, "previous CLI did not recover its own initial bytes")
    require(f.invoke("current-draft-before-submit", DRAFT, draft) == draft_result, "current CLI did not recover the unextended draft")
    backup_reader = prepare_archive_reader(f, commissioning)
    wrong = {**submit, "Target": {**submit["Target"], "DatabaseOid": submit["Target"]["DatabaseOid"] + 1}}
    f.invoke("submit-wrong-physical", COMMAND, wrong, 1, "target-identity-rejected")
    installed = f.invoke("submit-installed", COMMAND, submit)
    result = json.loads(installed)
    require(result["Outcome"] == "INSTALLED"
            and result["DraftCatalogBefore"] == json.loads(draft_result)["CatalogSha256"]
            and result["DraftCatalogAfter"] != result["DraftCatalogBefore"]
            and f.invoke("submit-install-replayed", COMMAND, submit) == installed, "submit did not persist the exact additive transition")
    f.invoke("submit-install-different-key", COMMAND, {**submit, "OperationId": token()}, 1, "operation-conflict")
    with cli_from(f, f.args.draft_cli):
        f.invoke("old-draft-after-submit", DRAFT, draft, 1, "draft-layout-mismatch")
    runtime_permissions(f, tenant_a, tenant_b)

    connection = ("Host=127.0.0.1;Port={};Database={};Username={};Password={};"
                  "SSL Mode=Disable;Pooling=false;Search Path=public;Timeout=4").format(
                      f.args.port, DATABASE, OWNER, f.passwords[OWNER])
    fixture = {"Connection": connection, "CloneConnection": "", "LegacyConnection": "",
               "InstallationId": commissioning["InstallationId"],
               "A": {"TenantId": tenant_a, "Login": A, "Password": f.passwords[A]},
               "B": {"TenantId": tenant_b, "Login": B, "Password": f.passwords[B]}}
    initial_ready = [f.state(DATABASE, tenant) for tenant in (tenant_a, tenant_b)]
    run_http(f, fixture, variable="CP6_RMA_SUBMIT_FIXTURE", test=TEST, name="rma-submit")
    native_evidence(f, tenant_a)
    require(f.invoke("current-draft-after-submit", DRAFT, draft) == draft_result
            and f.invoke("submit-install-after-use", COMMAND, submit) == installed,
            "current CLI changed a prior original during replay")
    with cli_from(f, f.args.journal_cli):
        require(f.invoke("journal-after-submit", JOURNAL, journal) == journal_result and
                f.invoke("commission-after-submit", COMMISSION, commissioning) == commission_result,
                "the previous W361 executable lost the original journal or commissioning bytes")
    require([f.state(DATABASE, tenant) for tenant in (tenant_a, tenant_b)] == initial_ready, "submit changed Ready/history")

    rejects_acl = archived(f, commissioning, backup_reader)
    f.sql(DATABASE, f"UPDATE {SUBMIT_SCHEMA}.installation SET draft_catalog_before=repeat('0',64) WHERE singleton;")
    try:
        f.invoke("draft-transition-drift-rejected", DRAFT, draft, 1, "submit-layout-mismatch")
        f.invoke("submit-transition-drift-rejected", COMMAND, submit, 1, "submit-layout-mismatch")
        rejects_acl("archive-unbound-transition")
    finally:
        f.sql(DATABASE, f"UPDATE {SUBMIT_SCHEMA}.installation SET draft_catalog_before={q(result['DraftCatalogBefore'])} WHERE singleton;")
    require(f.invoke("draft-after-transition-repair", DRAFT, draft) == draft_result, "exact transition recovery changed")
    f.save("final.json", {"Submit": result, "Ready": initial_ready, "ExactDraftReplay": True,
                          "OldDraftRejected": True, "PriorJournalReplayed": True, "ArchiveTransitionRejected": True})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "dotnet", "cli", "commission-cli", "journal-cli", "draft-cli", "webapi", "test-dll"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--webapi-source", required=True)
    args = parser.parse_args()
    for name in ("root", "pg_bin", "dotnet", "cli", "commission_cli", "journal_cli", "draft_cli", "webapi", "test_dll"):
        setattr(args, name, getattr(args, name).resolve(strict=True))
    args.expected = expected_histories(args.webapi_source)
    require(len(args.expected["Core"]) > 1 and all(args.expected.values()), "published migration inventory missing")
    args.root /= "rma-submit-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8]
    args.root.mkdir(mode=0o700)
    print(args.root, flush=True)
    f = Fixture(args, "submit")
    artifacts = {"DatabaseInit": args.webapi, "CommissionCli": args.commission_cli, "JournalCli": args.journal_cli,
                 "DraftCli": args.draft_cli, "Cli": args.cli, "CliCore": args.cli.with_name("CP6.Core.dll"),
                 "Tests": args.test_dll, "WebApi": args.test_dll.with_name("CP6.WebApi.dll"),
                 "Core": args.test_dll.with_name("CP6.Core.dll")}
    f.save("source.json", {"DatabaseInitSource": args.webapi_source,
                           "Sha256": {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in artifacts.items()}})
    with f.running():
        f.sql("postgres", "ALTER SYSTEM SET log_line_prefix TO " + q("%m [%p] %a %u@%d xid=%x ") + ";")
        f.sql("postgres", "SELECT pg_reload_conf();")
        wait_for(lambda: "%u@%d" in f.sql("postgres", "SHOW log_line_prefix;"), "PG role logging did not reload")
        exercise(f)
    print("hosted RMA submit, original recovery and exact archive profiles passed", flush=True)


if __name__ == "__main__":
    try:
        main()
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
