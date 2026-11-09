#!/usr/bin/env python3
"""Verify isolated Core candidate import and lost native COMMIT confirmation."""

import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
from threading import Thread
import time
from unittest.mock import patch

sys.dont_write_bytecode = True
from verify_stage import A, B, BASE_ENV, FLAGS, OWNER, Fixture, ident, pinned, put_json, q, require, token
from verify import schema
from verify_commission import COMMISSION, PREPARE, expected_histories, initialize
import archive
import candidate_core as candidate
import privacy_evidence
import stage


CLI = Path(candidate.__file__)
DB = "w406_candidate"


class CoreCandidateFixture(Fixture):
    def run_pg(self, label, tool, arguments, expected=0):
        if label == "pg-start" and os.name == "nt":
            with socket.socket() as probe:
                probe.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
                probe.bind(("127.0.0.1", self.args.port))
        return super().run_pg(label, tool, arguments, expected)


def memory_sample():
    from ctypes import Structure, WinError, byref, c_ulonglong, sizeof, windll
    from ctypes.wintypes import DWORD
    import psutil

    class MemoryStatus(Structure):
        _fields_ = [("length", DWORD), ("load", DWORD)] + [(name, c_ulonglong) for name in (
            "physicalTotal", "physicalAvailable", "commitTotal", "commitAvailable", "virtualTotal",
            "virtualAvailable", "virtualExtended")]

    status = MemoryStatus()
    status.length = sizeof(status)
    if not windll.kernel32.GlobalMemoryStatusEx(byref(status)):
        raise WinError()
    heavy = []
    for process in psutil.process_iter(["name"]):
        if (process.info["name"] or "").lower() in ("dotnet.exe", "msbuild.exe", "vbcscompiler.exe"):
            try:
                if process.memory_info().rss >= 384 * 2 ** 20:
                    heavy.append(process.pid)
            except psutil.NoSuchProcess:
                continue
            except psutil.AccessDenied:
                heavy.append(process.pid)
    return dict(utc=datetime.now(timezone.utc).isoformat(), physicalMiB=status.physicalAvailable // 2 ** 20,
                commitMiB=status.commitAvailable // 2 ** 20, heavyDotnetPids=heavy)


def initialize_owned(f):
    deadline = time.monotonic() + f.args.gate_seconds
    samples, consecutive = [], 0
    while True:
        sample = memory_sample()
        physical = sample["physicalMiB"]
        allowed = physical >= 1200 and sample["commitMiB"] >= 10240 and not sample["heavyDotnetPids"]
        consecutive = consecutive + 1 if allowed else 0
        samples.append({**sample, "allowed": allowed, "consecutive": consecutive})
        f.save("init-memory.json", samples)
        if consecutive == 2:
            print(f"DatabaseInit resource gate passed at {physical} MiB physical", flush=True)
            break
        if len(samples) == 1:
            print(f"DatabaseInit resource gate waiting at {physical} MiB physical", flush=True)
        require(time.monotonic() < deadline, "DatabaseInit resource gate elapsed")
        time.sleep(2.5)
    dll = f.args.webapi
    runtime, deps = dll.with_suffix(".runtimeconfig.json"), dll.with_suffix(".deps.json")
    pins = {str(p): archive.file_digest(p) for p in (dll, runtime, deps, f.args.cli)}
    if physical >= 1650:
        f.save("init-artifacts.json", {"artifacts": pins, "gcOverride": None})
        initialize(f, DB)
        return
    configuration = json.loads(runtime.read_bytes())
    properties = configuration["runtimeOptions"]["configProperties"]
    require(properties["System.GC.Server"] is True, "unexpected published GC configuration")
    properties["System.GC.Server"] = False
    private = f.root / "w351-workstation.runtimeconfig.json"
    put_json(private, configuration)
    f.save("init-artifacts.json", {"artifacts": pins, "gcOverride": archive.file_digest(private)})
    popen = subprocess.Popen

    def use_private_gc(command, *args, **kwargs):
        if command[:2] == [str(f.args.dotnet), str(dll)]:
            command = [command[0], "exec", "--runtimeconfig", str(private), "--depsfile", str(deps), *command[1:]]
        return popen(command, *args, **kwargs)

    with patch.object(subprocess, "Popen", side_effect=use_private_gc):
        initialize(f, DB)


def evidence_state(f, a, b, include_stock):
    stock_state = ""
    if include_stock:
        stock = archive.stock_outbox_module()
        counts = [f"{q(table.name)},(SELECT count(*) FROM {stock.relation(table)})" for table in stock.STOCK_TABLES]
        counts.append(f"{q(stock.JOURNAL.name)},(SELECT count(*) FROM {stock.relation(stock.JOURNAL)}"
                      f" WHERE owner={q(stock.JOURNAL_OWNER)})")
        stock_state = ", 'stockCounts',jsonb_build_object(" + ",".join(counts) + ")"
    return json.loads(f.sql(DB, f"""
        SELECT jsonb_build_object(
          'marker',shobj_description((SELECT oid FROM pg_database WHERE datname=current_database()),'pg_database'),
          'binding',(SELECT jsonb_agg(to_jsonb(t) ORDER BY tenant_id) FROM cp6_rma_tenant_v1.tenant_migrations t),
          'install',(SELECT to_jsonb(i) FROM cp6_rma_tenant_v1.installation i),
          'commission',(SELECT to_jsonb(i) FROM cp6_rma_core_commission_v1.installation i),
          'directory',(SELECT jsonb_agg(to_jsonb(l) ORDER BY tenant_id) FROM cp6_tenancy_v1.login_tenant l),
          'aHistory',(SELECT to_jsonb(h) FROM {schema(a)}.rma_migration_history_v1 h),
          'bHistory',(SELECT to_jsonb(h) FROM {schema(b)}.rma_migration_history_v1 h),
          'aCounts',jsonb_build_array((SELECT count(*) FROM {schema(a)}.heads),(SELECT count(*) FROM {schema(a)}.versions)),
          'bHead',(SELECT to_jsonb(h) FROM {schema(b)}.heads h),
          'bVersion',(SELECT to_jsonb(v) FROM {schema(b)}.versions v),
          'acl',(SELECT jsonb_agg(jsonb_build_array(n.nspname,n.nspacl,c.relname,c.relacl) ORDER BY n.nspname,c.relname)
              FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
              WHERE n.nspname IN ({q(schema(a))},{q(schema(b))}) AND c.relkind='r'),
          'sourceAuthEnvironment',(SELECT to_jsonb(e) FROM cp6_scope_authorization_v1.environment e),
          'admission',(SELECT count(*) FROM cp6_privacy_disposition_v1.restore_admission),
          'journal',(SELECT count(*) FROM cp6_main_owner_v1.operations WHERE owner={q(archive.JOURNAL_OWNER)}),
          'sharedHeads',(SELECT count(*) FROM cp6_rma_application_v1.heads),
          'sharedVersions',(SELECT count(*) FROM cp6_rma_application_v1.versions),
          'receiptSchema',to_regnamespace('{candidate.RECEIPTS}') IS NOT NULL{stock_state});
    """))


def invocation(f, label, target, candidate_id, commission, source, action="import"):
    bundle, tenant, digest = source
    request = f.root / (label + "-target.json")
    put_json(request, target)
    return argparse.Namespace(action=action, pg_bin=f.args.pg_bin, target_file=request, username=OWNER, tenant=tenant,
                              candidate_id=candidate_id, manifest_sha256=digest, commission_receipt=commission,
                              output=f.root / (label + "-native"), bundle=bundle, file_content_root=f.args.file_content_root)


def run_cli(f, label, args, rejection=None):
    command = [sys.executable, "-B", str(CLI), args.action]
    for field in ("pg_bin", "target_file", "commission_receipt", "output", "username", "tenant", "candidate_id", "manifest_sha256"):
        command += ["--" + field.replace("_", "-"), str(getattr(args, field))]
    if args.action == "import":
        command += ["--bundle", str(args.bundle)]
        if args.file_content_root is not None:
            command += ["--file-content-root", str(args.file_content_root)]
    result = subprocess.run(command, env={**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, cwd=f.root,
                            timeout=150, capture_output=True, creationflags=FLAGS)
    f.save(label + ".stdout", result.stdout)
    f.save(label + ".stderr", result.stderr)
    f.save(label + ".process.json", {"ExitCode": result.returncode})
    if rejection:
        require(result.returncode == 1 and not result.stdout and rejection.encode("ascii") in result.stderr, label + " wrong rejection")
    else:
        require(result.returncode == 0 and not result.stderr, label + " failed; see native evidence")
    return result.stdout.rstrip(b"\r\n")


def expect_internal_rejection(f, label, args, rejection):
    with patch.dict(os.environ, {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, clear=True):
        try:
            candidate.execute(args)
        except stage.StageError as error:
            f.save(label + ".error.json", {"Error": str(error)})
            require(str(error) == rejection, label + " wrong rejection")
        else:
            raise RuntimeError(label + " unexpectedly returned a receipt")


def late_fk(f, args, counts):
    run_driver = stage.run_driver
    def inject(pg_bin, target, username, output, action):
        driver = output / "driver.sql"
        body = driver.read_text(encoding="utf-8")
        marker = "SET CONSTRAINTS ALL IMMEDIATE;"
        require(body.count(marker) == 1 and body.index(r"\ir tenant-rows.sql") < body.index(marker), "late boundary changed")
        heads = schema(args.tenant) + ".heads"
        versions = schema(args.tenant) + ".versions"
        fault = f"""DO $$ BEGIN
            IF (SELECT count(*) FROM {heads})<>{counts["heads"]} OR (SELECT count(*) FROM {versions})<>{counts["versions"]} THEN
                RAISE EXCEPTION 'late fixture did not see the two original COPY blocks'; END IF;
            RAISE NOTICE 'fixture saw both original COPY blocks'; END $$;
            UPDATE {heads} SET version=99999;
        """
        driver.write_text(body.replace(marker, fault + marker, 1), encoding="utf-8")
        return run_driver(pg_bin, target, username, output, action)
    with patch.object(stage, "run_driver", side_effect=inject):
        expect_internal_rejection(f, "late-fk", args, "rma-stage-native-failed (see native logs)")
    raw = (args.output / "stage.stderr").read_bytes()
    require(b"23503" in raw and b"rma_application_head_version" in raw and b"fixture saw both original COPY blocks" in raw,
            "late PostgreSQL FK evidence missing")


def reject_existing_rows(f, args):
    run_driver = stage.run_driver
    def hide_receipt(pg_bin, target, username, output, action):
        driver = output / "driver.sql"
        body = driver.read_text(encoding="utf-8")
        marker = "SELECT to_regnamespace('{}') IS NOT NULL".format(candidate.RECEIPTS)
        require(body.count(marker) == 1, "receipt branch boundary changed")
        # Only the test changes receipt visibility, inside the transaction that must roll back.
        change = f"ALTER SCHEMA {candidate.RECEIPTS} RENAME TO w406_hidden_receipt;\n"
        driver.write_text(body.replace(marker, change + marker, 1), encoding="utf-8")
        return run_driver(pg_bin, target, username, output, action)
    with patch.object(stage, "run_driver", side_effect=hide_receipt):
        expect_internal_rejection(f, "existing-rows", args, "rma-stage-candidate-not-empty")


def relay(source, destination):
    try:
        while True:
            data = source.recv(65536)
            if not data:
                break
            destination.sendall(data)
    except OSError:
        pass


@contextmanager
def cut_commit_reply(port):
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        listener.listen(1)
        listener.settimeout(30)
        observation = {"ProxyPort": listener.getsockname()[1], "BackendPort": port}
        def serve():
            try:
                client, _ = listener.accept()
                with client, socket.create_connection(("127.0.0.1", port), timeout=30) as server, server.makefile("rb") as frames:
                    forward = Thread(target=relay, args=(client, server), daemon=True)
                    forward.start()
                    while True:
                        header = frames.read(5)
                        if not header:
                            break
                        length = int.from_bytes(header[1:], "big")
                        body = frames.read(length - 4)
                        if header[:1] == b"K":
                            observation["BackendPid"] = int.from_bytes(body[:4], "big")
                        if header[:1] == b"C" and body == b"COMMIT\0":
                            observation["CutCommandComplete"] = "COMMIT"
                            client.shutdown(socket.SHUT_RDWR)
                            break
                        client.sendall(header + body)
            except OSError as error:
                observation["ProxyError"] = str(error)
        worker = Thread(target=serve, daemon=True)
        worker.start()
        try:
            yield observation
        finally:
            worker.join(timeout=35)
        require(not worker.is_alive() and observation.get("CutCommandComplete") == "COMMIT"
                and observation.get("BackendPid"), "native COMMIT frame was not cut")


def exercise(f, alternate, primary, expected_format, counts):
    tenant, tenant_b, candidate_id = primary[1], token(), token()
    include_stock = expected_format == archive.FORMAT_WITH_STOCK_OUTBOX
    target = f.create_target(DB)
    request = dict(Target=target, InstallationId=token(), OperationId=token(),
                   Tenants=[dict(TenantId=tenant, Login=A), dict(TenantId=tenant_b, Login=B)])
    f.save("commission-request.json", request)
    f.invoke("prepare", PREPARE, request)
    initialize_owned(f)
    original_commission = f.invoke("commission", COMMISSION, request)
    commission = f.root / "commission.stdout"
    f.sql(DB, f"COMMENT ON DATABASE {ident(DB)} IS {q(candidate.MARKER + candidate_id)};")
    b_schema, b_app = schema(tenant_b), token()
    f.sql(DB, f"""BEGIN;
        INSERT INTO {b_schema}.heads VALUES ({q(tenant_b)},{q(b_app)},{q(token())},{q(token())},1);
        INSERT INTO {b_schema}.versions VALUES ({q(tenant_b)},{q(b_app)},1,'DRAFT','{{"sentinel":"B"}}'); COMMIT;
    """)
    before = evidence_state(f, tenant, tenant_b, include_stock)
    f.save("before-import.json", before)
    if include_stock:
        require(all(count == 0 for count in before["stockCounts"].values()), "candidate Stock must start empty")
        wrong_pin = invocation(f, "wrong-pin", target, candidate_id, commission, (*primary[:2], "0" * 64))
        run_cli(f, "wrong-pin", wrong_pin, "rma-stage-manifest-digest-mismatch")
        wrong_assets = invocation(f, "wrong-assets", target, candidate_id, commission, primary)
        wrong_assets.file_content_root = f.root / "missing-assets"
        run_cli(f, "wrong-assets", wrong_assets, "file-content-root-rejected")
        require(not any((args.output / "driver.sql").exists() for args in (wrong_pin, wrong_assets)),
                "bad manifest or asset root reached the native driver")
    wrong = f.create_target("w406_wrong_physical")
    run_cli(f, "wrong-physical", invocation(f, "wrong-physical", {**target, "Database": wrong["Database"]},
        candidate_id, commission, primary), "rma-stage-candidate-target-rejected")
    require(f.sql(wrong["Database"], f"SELECT to_regnamespace('{candidate.RECEIPTS}') IS NULL;") == "t",
            "wrong physical database acquired an import receipt")
    run_cli(f, "wrong-marker", invocation(f, "wrong-marker", target, token(), commission, primary),
            "rma-stage-candidate-marker-rejected")
    rewritten = f.root / "rewritten-commission.json"
    put_json(rewritten, json.loads(original_commission))
    run_cli(f, "wrong-commission", invocation(f, "wrong-commission", target, candidate_id, rewritten, primary),
            "rma-stage-candidate-ready-rejected")
    late_fk(f, invocation(f, "late-fk", target, candidate_id, commission, primary), counts)
    run_cli(f, "rolled-back-recover", invocation(f, "rolled-back-recover", target, candidate_id, commission, primary, "recover"),
            "rma-stage-candidate-receipt-not-found")
    rollback = evidence_state(f, tenant, tenant_b, include_stock)
    f.save("after-rollback.json", rollback)
    require(before == rollback, "failed import changed candidate or B state")

    args = invocation(f, "unknown-confirmation", target, candidate_id, commission, primary)
    original_connection_args = archive.connection_args
    wire = {}
    try:
        with cut_commit_reply(f.args.port) as wire:
            def through_proxy(physical, username):
                return ["--port=" + str(wire["ProxyPort"]) if part.startswith("--port=") else part
                        for part in original_connection_args(physical, username)]
            with patch.object(archive, "connection_args", side_effect=through_proxy):
                expect_internal_rejection(f, "unknown-confirmation", args, "rma-stage-outcome-unknown (use recover)")
    finally:
        f.save("commit-reply-cut.json", wire)
    raw = run_cli(f, "recover", invocation(f, "recover", target, candidate_id, commission, primary, "recover"))
    same = run_cli(f, "same-import", invocation(f, "same-import", target, candidate_id, commission, primary))
    reject_existing_rows(f, invocation(f, "existing-rows", target, candidate_id, commission, primary))
    persisted = f.sql(DB, f"SELECT result_json FROM {candidate.RECEIPTS}.receipt;", label="persisted-receipt").encode("utf-8")
    receipt = json.loads(raw)
    _, commission_sha = candidate.commissioning_pin(commission, archive.PhysicalTarget.from_mapping(target))
    require(raw == same == persisted and receipt["recoveryReady"] is False and receipt["outcome"] == "IMPORTED"
            and receipt["profile"] == candidate.PROFILE and receipt["format"] == expected_format
            and receipt["counts"] == counts
            and receipt["binding"] == dict(target=target, tenantId=tenant, manifestSha256=primary[2], candidateId=candidate_id,
                                          commissionReceiptSha256=commission_sha), "original result changed")
    run_cli(f, "different-archive", invocation(f, "different-archive", target, candidate_id, commission, alternate),
            "rma-stage-candidate-receipt-key-rejected")
    after = evidence_state(f, tenant, tenant_b, include_stock)
    f.save("after-import.json", after)
    require(after == {**before, "aCounts": [counts["heads"], counts["versions"]], "receiptSchema": True},
            "import changed protected state or B")
    for table, order in (("heads", "id"), ("versions", "id,version")):
        f.sql(DB, f"COPY (SELECT * FROM {schema(tenant)}.{table} ORDER BY {order}) TO STDOUT;", label="imported-" + table)
    copies = (args.output / "tenant-rows.sql").read_bytes().splitlines()
    for i, table in ((0, "heads"), (1, "versions")):
        start = [n for n, row in enumerate(copies) if row.startswith(b"COPY ")][i]
        original = copies[start + 1:copies.index(b"\\.", start + 1)]
        require(sorted(original) == sorted((f.root / ("imported-" + table + ".stdout")).read_bytes().splitlines()),
                "native COPY row bytes changed for " + table)
    require(f.invoke("commission-replay", COMMISSION, request) == original_commission, "commissioning receipt changed")
    f.sql(DB, f"SELECT id FROM {schema(tenant)}.heads;", role=B, error="42501", label="ordinary-b-denied")
    f.sql(DB, f"SELECT result_json FROM {candidate.RECEIPTS}.receipt;", role=A, error="42501", label="ordinary-a-receipt-denied")
    f.save("actual.json", {"Target": target, "CandidateId": candidate_id, "Receipt": archive.file_digest(f.root / "recover.stdout"),
                           "ManifestSha256": primary[2], "CoreCounts": after["aCounts"]})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "dotnet", "webapi", "cli"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--webapi-source", required=True)
    for version in (1, 2, 9, 10):
        parser.add_argument(f"--v{version}-bundle", type=Path)
        parser.add_argument(f"--v{version}-sha256")
    parser.add_argument("--file-content-root", type=Path)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--gate-seconds", type=int, default=600)
    args = parser.parse_args()
    args.root = args.root.resolve(strict=True)
    for name in ("pg_bin", "dotnet", "webapi", "cli"):
        setattr(args, name, getattr(args, name).resolve(strict=True))
    args.expected = expected_histories(args.webapi_source)
    legacy = (args.v1_bundle, args.v1_sha256, args.v2_bundle, args.v2_sha256)
    extended = (args.v9_bundle, args.v9_sha256, args.v10_bundle, args.v10_sha256, args.file_content_root)
    if all(legacy) and not any(extended):
        expected_format, versions = archive.FORMAT_WITH_JOURNAL, 2
        alternate = pinned(args.v1_bundle, args.v1_sha256, archive.FORMAT)
        primary = pinned(args.v2_bundle, args.v2_sha256, expected_format)
    elif all(extended) and not any(legacy):
        expected_format, versions = archive.FORMAT_WITH_STOCK_OUTBOX, 1
        alternate = pinned(args.v9_bundle, args.v9_sha256, privacy_evidence.FORMAT)
        primary = pinned(args.v10_bundle, args.v10_sha256, expected_format)
        args.file_content_root = args.file_content_root.resolve(strict=True)
        v9_source, v10_source = (json.loads((bundle / "manifest.json").read_bytes())["source"]
                                 for bundle in (alternate[0], primary[0]))
        require(v9_source == v10_source, "v9 and v10 fixture sources differ")
    else:
        parser.error("supply either v1/v2 bundles and pins, or v9/v10 bundles and pins with the original asset root")
    require(alternate[1] == primary[1], "fixture original tenants differ")
    files = lambda: {str(p): archive.file_digest(p) for bundle in (alternate[0], primary[0]) for p in sorted(bundle.iterdir())}
    source = files()
    f = CoreCandidateFixture(args, "rma-core-candidate-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    print("evidence:", f.root, flush=True)
    with f.running():
        exercise(f, alternate, primary, expected_format, {"heads": 1, "versions": versions})
    require(source == files(), "original archive bytes changed")
    f.save("source-archive-digests.json", source)
    print("native rollback, original rows, cut COMMIT and exact isolated Core candidate recovery passed", flush=True)


if __name__ == "__main__":
    main()
