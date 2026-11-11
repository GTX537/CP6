#!/usr/bin/env python3
"""Combine the existing ERP, Core and FileContent importers on one closed v11 candidate."""

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
from unittest.mock import patch

sys.dont_write_bytecode = True
import archive
import candidate_core as core
import erp_terminal_candidate as erp
import file_content_candidate as files
import file_content_history as history
import materialize_files
import stage
from verify_stage import A, B, BASE_ENV, FLAGS, OWNER, Fixture, ident, q, require, token
from verify_commission import COMMISSION, PREPARE, expected_histories, initialize


DB = "combined_v11_candidate"


def command(f, label, argv, *, expected=0, rejection=None, env=None):
    result = subprocess.run(list(map(str, argv)), cwd=f.root, env=env or BASE_ENV,
                            stdin=subprocess.DEVNULL, capture_output=True,
                            timeout=180, creationflags=FLAGS)
    f.save(label + ".stdout", result.stdout)
    f.save(label + ".stderr", result.stderr)
    f.save(label + ".process.json", dict(ExitCode=result.returncode))
    require(result.returncode == expected, label + " failed; see native output")
    if rejection:
        require(not result.stdout and rejection.encode("ascii") in result.stderr, label + " wrong rejection")
    return result.stdout.rstrip(b"\r\n")


def psql(f, label, sql, *, role=OWNER):
    return command(f, label, [f.pg("psql"), "-XqAtw", "-v", "ON_ERROR_STOP=1", "-v", "VERBOSITY=verbose",
                   "-h", "127.0.0.1", "-p", f.args.port, "-U", role, "-d", DB, "-c", sql],
                   env={**BASE_ENV, "PGPASSWORD": f.passwords[role]})


def initialize_bounded(f):
    configuration = json.loads(f.args.webapi.with_suffix(".runtimeconfig.json").read_bytes())
    configuration["runtimeOptions"]["configProperties"]["System.GC.Server"] = False
    runtime = f.root / "workstation.runtimeconfig.json"
    runtime.write_text(json.dumps(configuration), encoding="utf-8")
    popen = subprocess.Popen

    def bounded(argv, *args, **kwargs):
        if argv[:2] == [str(f.args.dotnet), str(f.args.webapi)]:
            argv = [argv[0], "exec", "--runtimeconfig", str(runtime), "--depsfile",
                    str(f.args.webapi.with_suffix(".deps.json")), *argv[1:]]
            kwargs["env"].update(DOTNET_PROCESSOR_COUNT="2", DOTNET_GCHeapHardLimit="0x20000000")
        return popen(argv, *args, **kwargs)

    with patch.object(subprocess, "Popen", side_effect=bounded):
        initialize(f, DB)


def importer(f, opts, module, label, target_file, candidate_id, action, *, commission=None,
             expected=0, rejection=None):
    argv = [sys.executable, "-B", module.__file__, action, "--pg-bin", opts.pg_bin,
            "--target-file", target_file, "--output", f.root / label, "--username", OWNER,
            "--tenant", opts.tenant, "--candidate-id", candidate_id, "--manifest-sha256", opts.manifest_sha256]
    if action.startswith("import"):
        argv += ["--bundle", opts.bundle, "--file-content-root", opts.file_content_root,
                 "--include-file-content-history"]
    elif module is files:
        argv += ["--file-content-root", opts.file_content_root]
    if commission:
        argv += ["--commission-receipt", commission]
    return command(f, label, argv, expected=expected, rejection=rejection,
                   env={**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]})


def snapshot(f, label, tenant):
    tenant_schema = "t_" + archive.guid(tenant).hex
    return json.loads(psql(f, label, f"""
        BEGIN READ ONLY;
        SELECT json_build_object(
          'heads',(SELECT coalesce(jsonb_agg(to_jsonb(h) ORDER BY id),'[]'::jsonb) FROM {tenant_schema}.heads h),
          'versions',(SELECT coalesce(jsonb_agg(to_jsonb(v) ORDER BY id,version),'[]'::jsonb) FROM {tenant_schema}.versions v),
          'erp',(SELECT coalesce(jsonb_agg(to_jsonb(o) ORDER BY "Id"),'[]'::jsonb)
                 FROM erp_integration."Cp6_OutboxMessage" o WHERE "TenantId"={q(tenant)}),
          'parentHeads',(SELECT coalesce(jsonb_agg(to_jsonb(h) ORDER BY revision_id),'[]'::jsonb)
                 FROM {history.PARENT_HEADS} h WHERE tenant_id={q(tenant)}),
          'fileRows',(SELECT count(*) FROM cp6_file_content_v1.contents WHERE tenant_id={q(tenant)}),
          'journal',(SELECT coalesce(jsonb_agg(to_jsonb(o) ORDER BY owner,operation_id),'[]'::jsonb)
                 FROM {archive.JOURNAL_TABLE} o WHERE tenant_id={q(tenant)}),
          'environment',(SELECT count(*) FROM cp6_scope_authorization_v1.environment),
          'admission',(SELECT count(*) FROM cp6_privacy_disposition_v1.restore_admission));
        COMMIT;
    """))


def wrong_order(f, opts):
    database = "combined_v11_wrong_order"
    target = f.create_target(database, template=DB)
    marker = token()
    f.sql("postgres", f"REVOKE ALL ON DATABASE {ident(database)} FROM PUBLIC; "
          f"GRANT CONNECT ON DATABASE {ident(database)} TO {ident(A)};")
    f.sql(database, f"COMMENT ON DATABASE {ident(database)} IS {q(core.MARKER + marker)};")
    target_file = f.root / "wrong-order-target.json"
    f.save(target_file.name, target)
    importer(f, opts, erp, "wrong-order-rejected", target_file, marker, "import", expected=1,
             rejection="rma-stage-erp-candidate-not-isolated")
    require(f.sql(database, f"SELECT count(*) FROM {erp.relation('Cp6_OutboxMessage')};") == "0",
            "wrong order wrote an ERP row")


def late_file_rollback(f, opts, target_file, candidate_id):
    before = snapshot(f, "before-file-fault", opts.tenant)
    original = stage.run_driver

    def fault(pg, target, username, output, action):
        driver = output / "driver.sql"
        sql = driver.read_text(encoding="utf-8")
        marker = f"CREATE SCHEMA {files.RECEIPTS};"
        require(sql.count(marker) == 1, "file receipt fault point changed")
        injected = f"""DO $combined_late$
            BEGIN
              IF NOT EXISTS (SELECT FROM cp6_file_content_v1.contents WHERE tenant_id={q(opts.tenant)})
                 OR NOT EXISTS (SELECT FROM {archive.JOURNAL_TABLE}
                                WHERE tenant_id={q(opts.tenant)} AND owner={q(history.assets.OWNER)}) THEN
                RAISE EXCEPTION 'combined-file-fault-before-original-writes';
              END IF;
              RAISE EXCEPTION 'combined-file-native-late-fault' USING ERRCODE='23514';
            END $combined_late$;
            """
        driver.write_text(sql.replace(marker, injected + marker), encoding="utf-8")
        return original(pg, target, username, output, action)

    args = argparse.Namespace(action="import", pg_bin=opts.pg_bin, target_file=target_file,
        output=f.root / "files-native-late-fault", username=OWNER, tenant=opts.tenant,
        candidate_id=candidate_id, manifest_sha256=opts.manifest_sha256,
        bundle=opts.bundle, file_content_root=opts.file_content_root, include_file_content_history=True)
    with patch.dict(os.environ, {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, clear=True), \
            patch.object(stage, "run_driver", side_effect=fault):
        try:
            files.execute(args)
        except stage.StageError as error:
            require(str(error) == "rma-stage-native-failed (see native logs)", "unexpected late file failure")
        else:
            raise RuntimeError("late file failure committed")
    native_error = (args.output / "stage.stderr").read_bytes()
    require(b"23514" in native_error and b"combined-file-native-late-fault" in native_error,
            "late file failure did not reach native original writes")
    require(snapshot(f, "after-file-fault", opts.tenant) == before, "late file rollback changed original Owners")
    importer(f, opts, files, "uncommitted-file-recover-rejected", target_file, candidate_id, "recover",
             expected=1, rejection="rma-stage-candidate-receipt-not-found")


def verify(opts):
    require(archive.file_digest(opts.bundle / "manifest.json")["sha256"] == opts.manifest_sha256,
            "fixture manifest pin differs")
    manifest = archive.verify_bundle(opts.bundle, opts.tenant, file_content_root=opts.file_content_root)
    require(manifest["format"] == archive.FORMAT_WITH_FILE_CONTENT_HISTORY, "complete v11 fixture required")
    opts.expected = expected_histories(opts.webapi_source)
    f = Fixture(opts, "combined")
    original_inputs = {str(p): archive.file_digest(p) for p in
                       (opts.bundle / "manifest.json", opts.tenant_rows, opts.parent_heads,
                        opts.product_heads, opts.product_versions, opts.cli, opts.journal_cli)}
    materialization = [sys.executable, "-B", materialize_files.__file__, "materialize", "--bundle", opts.bundle,
        "--stage-receipt", opts.stage_receipt, "--stage-receipt-sha256", archive.file_digest(opts.stage_receipt)["sha256"],
        "--tenant", opts.tenant, "--source-root", opts.file_content_root, "--output", f.root / "materialized"]
    materialized = command(f, "materialize-original-files", materialization)
    opts.file_content_root = Path(json.loads(materialized)["fileContentRoot"])
    with f.running():
        target = f.create_target(DB)
        f.sql("postgres", f"REVOKE ALL ON DATABASE {ident(DB)} FROM PUBLIC;")
        foreign = opts.foreign_tenant
        candidate_id = token()
        request = dict(Target=target, InstallationId=token(), OperationId=token(),
                       Tenants=[dict(TenantId=opts.tenant, Login=A), dict(TenantId=foreign, Login=B)])
        f.save("commission-request.json", request)
        f.invoke("prepare", PREPARE, request)
        initialize_bounded(f)
        f.sql(DB, f"COMMENT ON DATABASE {ident(DB)} IS {q(core.MARKER + candidate_id)};")
        target_file = f.root / "target.json"
        f.save("target.json", target)
        # These are original fixture preconditions, not a new Engineering or IAM restore implementation.
        psql(f, "seed-original-tenants", f"\\copy public.\"Sys_Tenants\" FROM {q(opts.tenant_rows.as_posix())} WITH (FORMAT csv,HEADER MATCH)")
        wrong_order(f, opts)
        erp_receipt = importer(f, opts, erp, "erp-import", target_file, candidate_id, "import")
        f.invoke("commission", COMMISSION, request)
        commission = f.root / "commission.stdout"
        other = token()
        foreign_schema = "t_" + archive.guid(foreign).hex
        f.sql(DB, f"""BEGIN;
            INSERT INTO {foreign_schema}.heads VALUES ({q(foreign)},{q(other)},{q(token())},{q(token())},1);
            INSERT INTO {foreign_schema}.versions VALUES ({q(foreign)},{q(other)},1,'DRAFT','{{"fixture":"active B"}}');
            COMMIT;""")
        protected_b = snapshot(f, "foreign-before-core-import", foreign)
        core_receipt = importer(f, opts, core, "core-import", target_file, candidate_id, "import", commission=commission)
        previous_cli = f.args.cli
        f.args.cli = opts.journal_cli
        try:
            f.invoke("journal-install", "install-rma-core-original-journal-v1", {**request, "OperationId": token()})
        finally:
            f.args.cli = previous_cli
        journal_receipt = importer(f, opts, core, "journal-import", target_file, candidate_id,
                                   "import-journal", commission=commission)
        parent = history.MEMBERS["parent-versions"]
        f.sql(DB, "BEGIN;\n" + archive.JOURNAL_COPY_SETTINGS + "\n" +
              f"\\copy cp6_engineering_product_v1.heads FROM {q(opts.product_heads.as_posix())} WITH (FORMAT csv,HEADER MATCH)\n" +
              f"\\copy cp6_engineering_product_v1.versions FROM {q(opts.product_versions.as_posix())} WITH (FORMAT csv,HEADER MATCH)\n" +
              f"\\copy {history.PARENT_HEADS} FROM {q(opts.parent_heads.as_posix())} WITH (FORMAT csv,HEADER MATCH)\n" +
              f"\\copy {parent.relation} ({','.join(parent.columns)}) FROM " +
              f"{q((opts.bundle / history.filename('parent-versions')).as_posix())} WITH (FORMAT csv,HEADER MATCH)\nCOMMIT;",
              label="seed-original-engineering", seconds=30)
        late_file_rollback(f, opts, target_file, candidate_id)
        file_receipt = importer(f, opts, files, "files-import", target_file, candidate_id, "import")
        original_receipts = dict(erp=erp_receipt, core=core_receipt, journal=journal_receipt, files=file_receipt)
        recovered = {
            "erp": importer(f, opts, erp, "erp-recover", target_file, candidate_id, "recover"),
            "core": importer(f, opts, core, "core-recover", target_file, candidate_id, "recover", commission=commission),
            "journal": importer(f, opts, core, "journal-recover", target_file, candidate_id, "recover-journal", commission=commission),
            "files": importer(f, opts, files, "files-recover", target_file, candidate_id, "recover")}
        require(original_receipts == recovered, "combined original receipts changed after commissioning/imports")
        materialization[materialization.index("materialize")] = "recover"
        del materialization[materialization.index("--source-root"):materialization.index("--source-root") + 2]
        require(command(f, "recover-original-files", materialization) == materialized,
                "original materialization receipt changed")
        require(snapshot(f, "foreign-after-recovery", foreign) == protected_b, "foreign tenant or recovery gates changed")
        local_schema = "t_" + archive.guid(opts.tenant).hex
        a_count = f.sql(DB, f"SELECT count(*) FROM {local_schema}.heads;", role=A)
        b_count = f.sql(DB, f"SELECT count(*) FROM {foreign_schema}.heads;", role=B)
        require(int(a_count) == manifest["counts"]["heads"] and b_count == "1", "ordinary tenant reads differ")
        f.sql(DB, f"SELECT * FROM {local_schema}.heads;", role=B, error="42501", label="foreign-direct-rejected")
        state = snapshot(f, "candidate-after", opts.tenant)
        require(state["environment"] == state["admission"] == 0, "combined candidate was opened")
        f.save("actual.json", dict(Result="PASS", Target=target, CandidateId=candidate_id,
               Manifest=opts.manifest_sha256, RecoveryReady=False, AHeadCount=int(a_count), BHeadCount=int(b_count),
               ReceiptProfiles={key: json.loads(value)["profile"] for key, value in recovered.items()},
               IndependentRecoveries=len(recovered), OrdinaryIsolation=True, ForeignPreserved=True,
               MaterializedFiles=json.loads(materialized)["fileContentAssetCount"],
               CandidateFileRoot=opts.file_content_root.as_posix(),
               WrongOrderRejected=True, NativeLateFileRollback=True,
               OriginalPreconditions=original_inputs))
    require(not (f.data / "postmaster.pid").exists(), "test postmaster remained")
    return json.loads((f.root / "actual.json").read_bytes())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "dotnet", "webapi", "cli", "journal-cli", "bundle", "file-content-root",
                 "tenant-rows", "parent-heads", "product-heads", "product-versions", "stage-receipt"):
        parser.add_argument("--" + name, type=Path, required=True)
    for name in ("webapi-source", "tenant", "foreign-tenant", "manifest-sha256"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--port", type=int, required=True)
    opts = parser.parse_args()
    opts.root.mkdir(parents=True, exist_ok=True)
    print(json.dumps(verify(opts), indent=2))


if __name__ == "__main__":
    main()
