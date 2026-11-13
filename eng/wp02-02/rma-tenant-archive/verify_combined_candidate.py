#!/usr/bin/env python3
"""Combine the existing Stock, ERP, Core and FileContent importers on one closed v11 candidate."""

import argparse
import csv
import hashlib
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
import stock_adjust_candidate as stock
import verify_stock_adjust_candidate as stock_checks
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
    if module is stock:
        argv += ["--effects-sha256", opts.stock_effects_sha256]
    if action.startswith("import"):
        argv += ["--bundle", opts.bundle, "--file-content-root", opts.file_content_root]
        if module is stock:
            argv += ["--effects", opts.stock_effects]
        else:
            argv += ["--include-file-content-history"]
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


def capture_stock(f, label, tenant):
    slices = []
    for part in stock.EFFECTS:
        key = "tenant_id" if part is stock.EFFECTS[3] else '"TenantId"'
        order = "sequence" if part is stock.EFFECTS[3] else '"Id"'
        slices.append((part.member, f"SELECT {stock.columns(part)} FROM {part.relation} WHERE {key}={q(tenant)} ORDER BY {order}"))
    for part in stock.STOCK.TABLES:
        slices.append((part.member, stock.STOCK.rows_sql(part, tenant, stock.STOCK.root(tenant)["id"])))
    return capture_native(f, label, slices)


def capture_native(f, label, slices):
    # Use COPY TO STDOUT like archive.py so Windows writes the same record terminators.
    directory = f.root / label
    archive.private_directory(directory)
    rows = {}
    for name, query in slices:
        with f.psql(DB) as proc:
            sql = ("BEGIN READ ONLY;\n" + archive.JOURNAL_COPY_SETTINGS +
                   f"\nCOPY ({query}) TO STDOUT WITH (FORMAT csv, HEADER true);\nCOMMIT;")
            raw, err = f.collect(proc, sql.encode("utf-8"))
        require(proc.returncode == 0 and not err, label + " native COPY failed for " + name)
        (directory / name).write_bytes(raw)
        rows[name] = raw
    return rows


def capture_erp(f, label, tenant):
    return capture_native(f, label, [(part.member, erp.erp.rows_sql(part, tenant)) for part in erp.erp.TABLES])


def require_stock_originals(observed, effects, queue, *, prefix=""):
    for part in stock.EFFECTS:
        require(observed[part.member] == (effects / part.member).read_bytes(), part.member + " Stock original differs")
    for part in stock.STOCK.TABLES:
        original = (queue / (prefix + part.member)).read_bytes()
        require(stock_checks.without_xmin(observed[part.member], part) == stock_checks.without_xmin(original, part),
                part.member + " Stock queue original differs")


def seed_stock_fixture(f, opts, target, candidate_id):
    # Fixture data is inserted only into the newly created test database.
    require(archive.file_digest(opts.stock_fixture_sql)["sha256"] == opts.stock_fixture_sha256,
            "combined Stock fixture pin differs")
    key = dict(target=target, candidateId=candidate_id, tenantId=opts.tenant,
               manifestSha256=opts.manifest_sha256)
    sql = "BEGIN;\n" + core.target_sql({"key": key}, False) + stock.closed_sql()
    sql += opts.stock_fixture_sql.read_text(encoding="utf-8") + "\nCOMMIT;"
    f.sql(DB, sql, label="seed-closed-stock-prerequisites", seconds=55)


def check_file_bytes(copied_root, source, bundle):
    with (bundle / archive.file_content_assets.MEMBER).open(encoding="utf-8", newline="") as stream:
        for row in csv.DictReader(stream):
            relative = row["store_path"]
            expected = {"bytes": int(row["byte_length"]), "sha256": row["digest"]}
            original = source / relative
            copied = copied_root / relative
            require(copied != original and archive.file_digest(copied) == archive.file_digest(original) == expected,
                    "materialized file bytes differ")


def check_file_history(f, label, tenant, bundle):
    queries = history.rows_sql(tenant)
    data = capture_native(f, label, [(history.filename(key), queries[key] + " ORDER BY " + ",".join(part.key))
                                     for key, part in history.MEMBERS.items()])
    for name, value in data.items():
        require(value == (bundle / name).read_bytes(), name + " FileContent original differs")


def check_rma(f, opts):
    schema = "t_" + archive.guid(opts.tenant).hex
    prefix = ["--data-only", "--no-owner", "--no-acl", "--schema=" + schema, "--table=heads", "--table=versions"]
    dump = f.root / "rma-current.pgcustom"
    command(f, "dump-rma-readback", [f.pg("pg_dump"), "-w", "-h", "127.0.0.1", "-p", opts.port, "-U", OWNER,
            "-d", DB, "--schema=" + schema, "--format=custom", "--file=" + str(dump)],
            env={**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]})

    def data(label, file):
        text = command(f, label, [f.pg("pg_restore"), *prefix, "--file=-", file])
        blocks = []
        active = False
        for line in text.splitlines():
            if line.startswith(b"COPY "):
                active = True
            if active:
                blocks.append(line)
                active = line != b"\\."
        require(not active and sum(line.startswith(b"COPY ") for line in blocks) == 2,
                "native RMA head/version COPY is incomplete")
        return blocks

    require(data("rma-restored-data", dump) == data("rma-original-data", opts.bundle / "rma.pgcustom"),
            "original RMA heads or versions differ")
    name = archive.JOURNAL_MEMBER
    journal = capture_native(f, "rma-original-journal",
                             [(name, archive.original_journal_rows(opts.tenant) + " ORDER BY operation_id")])
    require(journal[name] == (opts.bundle / name).read_bytes(), "RMA original operation bytes differ")


def verify_combined_originals(f, opts, *, stock_before, erp_before):
    observed = capture_stock(f, "stock-a-restored", opts.tenant)
    require_stock_originals(observed, opts.stock_effects, opts.bundle, prefix=archive.STOCK_MEMBER_PREFIX)
    require(capture_stock(f, "stock-b-after", opts.foreign_tenant) == stock_before,
            "foreign Stock originals or xmin changed")
    erp_a = capture_erp(f, "erp-a-restored", opts.tenant)
    require(all(value == (opts.bundle / name).read_bytes() for name, value in erp_a.items()),
            "original ERP rows differ")
    require(capture_erp(f, "erp-b-after", opts.foreign_tenant) == erp_before, "foreign ERP originals changed")
    check_rma(f, opts)
    check_file_history(f, "file-content-restored", opts.tenant, opts.bundle)


def verify_closed_controls(f, candidate_id):
    result = json.loads(psql(f, "closed-native-controls", f"""
      SELECT jsonb_build_object(
        'marker',(SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()),
        'stockPending',(SELECT count(*) FROM {stock.STOCK.relation(stock.STOCK.NATIVE)} WHERE "Status"=0 AND "AttemptCount"=0),
        'erpPending',(SELECT count(*) FROM erp_integration."Cp6_OutboxMessage" WHERE "Status"=0 AND "AttemptCount"=0),
        'runs',(SELECT count(*) FROM {stock.STOCK.relation(stock.STOCK.RUN)}),
        'claims',(SELECT count(*) FROM {stock.STOCK.relation(stock.STOCK.CLAIM)}),
        'sends',(SELECT count(*) FROM {stock.STOCK.relation(stock.STOCK.SEND)}),
        'admission',(SELECT count(*) FROM cp6_privacy_disposition_v1.restore_admission),
        'environment',(SELECT count(*) FROM cp6_scope_authorization_v1.environment));
    """))
    require(result == dict(marker=core.MARKER + candidate_id, stockPending=2, erpPending=2,
                           runs=0, claims=0, sends=0, admission=0, environment=0),
            "unexpected combined candidate marker, admission or queue activity")
    return result


def verify_stock_worker(f, opts, target, candidate_id):
    probe, runtime = opts.stock_worker_probe, opts.stock_worker_runtime
    f.save("stock-worker-input.json", dict(target=target, candidateId=candidate_id, pgData=f.data.as_posix(),
                                           username=OWNER, tenantA=opts.tenant, tenantB=opts.foreign_tenant))
    raw = command(f, "stock-worker-closed", [opts.dotnet, "exec", "--runtimeconfig",
                  runtime / "CP6.Tests.runtimeconfig.json", "--depsfile", runtime / "CP6.Tests.deps.json",
                  probe, f.root / "stock-worker-input.json", runtime],
                  env={**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]})
    result = json.loads(raw)
    require(result["Target"] == target["Database"] and result["Oid"] == target["DatabaseOid"] and
            result["PublisherCalls"] == 0 and result["EqualAfter"] and
            {entry["Tenant"]: entry["Message"] for entry in result["Outcomes"]} ==
            {tenant: "STOCK_OUTBOX_RESTORE_DISPATCH_FORBIDDEN" for tenant in (opts.tenant, opts.foreign_tenant)},
            "actual Stock worker did not keep the candidate closed")
    return result


def recover_stock_closed(f, opts, target_file, candidate_id, original):
    # RMA commissioning grants precisely the two isolated test logins CONNECT.
    # Quiesce those logins for the owner-only Stock recovery, then restore the exact ACL.
    def acl(label):
        return psql(f, label, """
            SELECT coalesce(jsonb_agg(jsonb_build_array(r.rolname,a.privilege_type,a.is_grantable)
                                      ORDER BY r.rolname,a.privilege_type),'[]'::jsonb)
            FROM pg_database d CROSS JOIN LATERAL aclexplode(d.datacl) a
            LEFT JOIN pg_roles r ON r.oid=a.grantee
            WHERE d.datname=current_database() AND a.grantee<>d.datdba;
        """)

    before = acl("before-stock-recover-acl")
    require(json.loads(before) == [[role, "CONNECT", False] for role in sorted((A, B))],
            "unexpected ordinary candidate ACL before Stock recovery")
    roles = ",".join(map(ident, (A, B)))
    f.sql("postgres", f"REVOKE CONNECT ON DATABASE {ident(DB)} FROM {roles};")
    try:
        require(importer(f, opts, stock, "stock-recover-after-composition", target_file, candidate_id, "recover") == original,
                "original Stock receipt changed after other imports")
    finally:
        f.sql("postgres", f"GRANT CONNECT ON DATABASE {ident(DB)} TO {roles};")
    require(acl("after-stock-recover-acl") == before, "ordinary ACL changed across Stock recovery")


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
    source_files = opts.file_content_root
    with_stock = getattr(opts, "stock_effects", None) is not None
    original_inputs = {str(p): archive.file_digest(p) for p in
                       (opts.bundle / "manifest.json", opts.tenant_rows, opts.parent_heads,
                        opts.product_heads, opts.product_versions, opts.cli, opts.journal_cli)}
    if with_stock:
        original_inputs.update({str(p): archive.file_digest(p) for p in
                                (opts.stage_receipt, opts.stock_effects / "manifest.json", opts.stock_fixture_sql)})
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
        if with_stock:
            seed_stock_fixture(f, opts, target, candidate_id)
            stock_b = capture_stock(f, "stock-b-before", foreign)
            require_stock_originals(stock_b, opts.stock_b_effects, opts.stock_b_queue)
            erp_b = capture_erp(f, "erp-b-before", foreign)
            require(all(value == (opts.stock_b_erp / name).read_bytes() for name, value in erp_b.items()),
                    "foreign ERP fixture did not retain native source originals")
            stock_receipt = importer(f, opts, stock, "stock-import", target_file, candidate_id, "import")
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
        if with_stock:
            recover_stock_closed(f, opts, target_file, candidate_id, stock_receipt)
            recovered["stock"] = stock_receipt
            verify_combined_originals(f, opts, stock_before=stock_b, erp_before=erp_b)
            check_file_bytes(opts.file_content_root, source_files, opts.bundle)
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
        if with_stock:
            closed = verify_closed_controls(f, candidate_id)
            for receipt in recovered.values():
                value = json.loads(receipt)
                binding = value["binding"]
                require(value["recoveryReady"] is False and binding["target"] == target
                        and binding["tenantId"] == opts.tenant and binding["candidateId"] == candidate_id
                        and binding["manifestSha256"] == opts.manifest_sha256,
                        "combined receipt did not bind the same closed candidate and v11 cut")
            require(json.loads(recovered["stock"])["binding"]["effectsSha256"] == opts.stock_effects_sha256,
                    "combined Stock receipt did not bind the same-cut effect manifest")
            require(all(archive.file_digest(Path(path)) == digest for path, digest in original_inputs.items()),
                    "a pinned composition input changed")
        actual = dict(Result="PASS", Target=target, CandidateId=candidate_id,
               Manifest=opts.manifest_sha256, RecoveryReady=False, AHeadCount=int(a_count), BHeadCount=int(b_count),
               ReceiptProfiles={key: json.loads(value)["profile"] for key, value in recovered.items()},
               IndependentRecoveries=len(recovered), OrdinaryIsolation=True, ForeignPreserved=True,
               MaterializedFiles=json.loads(materialized)["fileContentAssetCount"],
               CandidateFileRoot=opts.file_content_root.as_posix(),
               WrongOrderRejected=True, NativeLateFileRollback=True,
               OriginalPreconditions=original_inputs)
        if with_stock:
            actual.update(ExactStockErpRmaFileOriginals=True, StockEffectsSha256=opts.stock_effects_sha256,
                          ReceiptSha256={key: hashlib.sha256(value).hexdigest() for key, value in recovered.items()},
                          ClosedControls=closed)
            if opts.stock_worker_probe is not None:
                actual["StockWorker"] = verify_stock_worker(f, opts, target, candidate_id)
        f.save("actual.json", actual)
    require(not (f.data / "postmaster.pid").exists(), "test postmaster remained")
    return json.loads((f.root / "actual.json").read_bytes())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "dotnet", "webapi", "cli", "journal-cli", "bundle", "file-content-root",
                 "tenant-rows", "parent-heads", "product-heads", "product-versions", "stage-receipt"):
        parser.add_argument("--" + name, type=Path, required=True)
    for name in ("webapi-source", "tenant", "foreign-tenant", "manifest-sha256"):
        parser.add_argument("--" + name, required=True)
    stock_inputs = ("stock-effects", "stock-fixture-sql", "stock-b-effects", "stock-b-queue", "stock-b-erp")
    for name in stock_inputs:
        parser.add_argument("--" + name, type=Path)
    parser.add_argument("--stock-effects-sha256")
    parser.add_argument("--stock-fixture-sha256")
    parser.add_argument("--stock-worker-probe", type=Path)
    parser.add_argument("--stock-worker-runtime", type=Path)
    parser.add_argument("--port", type=int, required=True)
    opts = parser.parse_args()
    stock_args = [getattr(opts, name.replace("-", "_")) for name in stock_inputs]
    stock_args.extend((opts.stock_effects_sha256, opts.stock_fixture_sha256))
    if any(stock_args) and not all(stock_args):
        parser.error("the Stock composition requires the effect pins and all retained fixture inputs")
    if bool(opts.stock_worker_probe) != bool(opts.stock_worker_runtime) or opts.stock_worker_probe and not all(stock_args):
        parser.error("the Stock worker probe requires the sealed runtime and the Stock composition inputs")
    opts.root.mkdir(parents=True, exist_ok=True)
    print(json.dumps(verify(opts), indent=2))


if __name__ == "__main__":
    main()
