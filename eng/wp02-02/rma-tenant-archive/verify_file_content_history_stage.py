"""Exercise the v11 history stage against isolated, empty PostgreSQL fixtures."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
from types import SimpleNamespace
from unittest.mock import patch

import archive
import file_content_history as history
import stage


def persist(path, data):
    with path.open("xb") as stream:
        stream.write(data)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def file_hashes(path):
    return {str(p.relative_to(path)): archive.file_digest(p)["sha256"]
            for p in sorted(path.rglob("*")) if p.is_file()}


def verify(args):
    out = args.output.absolute()
    archive.private_directory(out)
    files = {name: args.targets / (name + ".json") for name in ("history", "old", "fault")}
    targets = {k: archive.PhysicalTarget.from_mapping(json.loads(p.read_bytes())) for k, p in files.items()}
    assert all(t.database.startswith("cp6_history_test_") for t in targets.values()), "Use dedicated fixture databases"
    assert len({(t.system_identifier, t.database_oid) for t in targets.values()}) == 3
    manifest = json.loads((args.v11 / "manifest.json").read_bytes())
    tenant = str(archive.guid(manifest["tenantId"]))
    old_tenant = str(archive.guid(json.loads((args.v10 / "manifest.json").read_bytes())["tenantId"]))
    assert archive.file_digest(args.v11 / "manifest.json")["sha256"] == args.v11_pin
    assert archive.file_digest(args.v10 / "manifest.json")["sha256"] == args.v10_pin
    originals = file_hashes(args.v11)
    assets = file_hashes(args.v11_assets)
    env = archive.client_environment(os.environ, "cp6-v11-stage-test")

    def process(name, command, cwd=None):
        result = subprocess.run(command, cwd=cwd, env=env, stdin=subprocess.DEVNULL,
                                capture_output=True, timeout=90, creationflags=archive.FLAGS)
        persist(out / (name + ".stdout"), result.stdout)
        persist(out / (name + ".stderr"), result.stderr)
        persist(out / (name + ".exit"), str(result.returncode).encode("ascii"))
        return result

    def psql(key, name, *argv, cwd=None):
        command = [archive.binary(args.pg_bin, "psql"), "-XqAtw", "-v", "ON_ERROR_STOP=1",
                   "-v", "VERBOSITY=verbose", *archive.connection_args(targets[key], args.username), *argv]
        return process(name, command, cwd)

    def query(key, name, sql):
        result = psql(key, name, "--command=" + sql)
        assert result.returncode == 0, (name, result.stderr.decode(errors="replace"))
        return result.stdout

    def invoke(key, name, *, historical=False, bundle=None, pin=None, asset_root=None, recover=False):
        command = [sys.executable, "-B", str(Path(stage.__file__).resolve()), "recover" if recover else "stage",
                   "--pg-bin", str(args.pg_bin), "--target-file", str(files[key]),
                   "--username", args.username, "--tenant", old_tenant if key == "old" else tenant,
                   "--manifest-sha256", pin or (args.v10_pin if key == "old" else args.v11_pin),
                   "--output", str(out / (name + "-driver"))]
        if not recover:
            command.extend(["--bundle", str(bundle or args.v11), "--file-content-root", str(asset_root or args.v11_assets)])
        if historical:
            command.append("--include-file-content-history")
        return process(name, command)

    def reject(result, name, code, reason):
        assert result.returncode == code and reason in result.stderr, (name, result.returncode, result.stderr.decode(errors="replace"))
        assert not (out / (name + "-driver/driver.sql")).exists(), name

    def empty(key, name):
        wanted = old_tenant if key == "old" else tenant
        sql = ("SELECT to_regnamespace('rma_archive_stage') IS NULL "
               "AND to_regnamespace(" + archive.literal("t_" + wanted.replace("-", "")) + ") IS NULL")
        assert query(key, name, sql).strip() == b"t", name

    native_version = query("history", "server-version", "SHOW server_version").decode().strip()
    no_flag = invoke("history", "default-reject")
    reject(no_flag, "default-reject", 1, b"rma-stage-bundle-member-rejected")
    empty("history", "before-explicit-stage")
    if args.original:
        missing = invoke("history", "original-explicit", historical=True)
        reject(missing, "original-explicit", 2, b"unrecognized arguments: --include-file-content-history")
        empty("history", "after-original-rejection")
        result = {"result": "ORIGINAL_STAGE_UNAVAILABLE", "postgresql": native_version,
                  "v11_manifest_sha256": args.v11_pin, "originals_unchanged": originals == file_hashes(args.v11),
                  "assets_unchanged": assets == file_hashes(args.v11_assets)}
        assert result["originals_unchanged"] and result["assets_unchanged"]
        persist(out / "actual.json", (json.dumps(result, indent=2) + "\n").encode())
        print(json.dumps(result))
        return

    wrong = invoke("history", "bad-pin", historical=True, pin="0" * 64)
    reject(wrong, "bad-pin", 1, b"rma-stage-manifest-digest-mismatch")
    empty("history", "after-bad-pin")
    different = invoke("old", "wrong-format", historical=True, bundle=args.v10, asset_root=args.v10_assets)
    reject(different, "wrong-format", 1, b"rma-stage-bundle-member-rejected")
    empty("old", "after-wrong-format")

    positive = invoke("history", "stage-v11", historical=True)
    assert positive.returncode == 0, positive.stderr.decode(errors="replace")
    receipt = json.loads(positive.stdout)
    assert receipt["outcome"] == "STAGED" and receipt["recoveryReady"] is False
    assert receipt["format"] == archive.FORMAT_WITH_FILE_CONTENT_HISTORY and receipt["tenantId"] == tenant
    assert receipt["target"] == targets["history"].mapping() and receipt["manifestSha256"] == args.v11_pin
    assert receipt["fileContentHistory"] == manifest["fileContentHistory"]
    assert receipt["stockOutbox"] == manifest["stockOutbox"]
    recovered = invoke("history", "recover-v11", recover=True)
    assert recovered.returncode == 0 and recovered.stdout == positive.stdout, recovered.stderr.decode(errors="replace")

    checked = {}
    for key, part in history.MEMBERS.items():
        table = "rma_archive_stage.file_content_history_" + key.replace("-", "_")
        select = ", ".join('"' + col + '"' for col in part.columns)
        order = ", ".join('"' + col + '"' for col in part.key)
        raw = query("history", "readback-" + key,
                    f"SET TIME ZONE 'UTC'; COPY (SELECT {select} FROM {table} ORDER BY {order}) TO STDOUT WITH (FORMAT CSV, HEADER TRUE)")
        original = (args.v11 / history.filename(key)).read_bytes()
        assert raw == original, (key, len(raw), digest(raw), len(original), digest(original))
        metadata = query("history", "types-" + key, """
            SELECT json_agg(json_build_array(attname,format_type(atttypid,atttypmod)) ORDER BY attnum)
            FROM pg_attribute WHERE attrelid=""" + archive.literal(table) + "::regclass AND attnum>0 AND NOT attisdropped")
        assert json.loads(metadata) == [list(p) for p in zip(part.columns, part.types)]
        checked[key] = {"bytes": len(raw), "sha256": digest(raw), "rows": manifest["fileContentHistory"]["members"][history.filename(key)]["rows"]}
    assert all(v["rows"] > 0 for v in checked.values()), "Use the actual nonempty source history"
    assert query("history", "neutral-acl", """
        SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        CROSS JOIN LATERAL aclexplode(coalesce(c.relacl,acldefault('r',c.relowner))) a
        WHERE n.nspname='rma_archive_stage' AND c.relname LIKE 'file_content_history_%' AND a.grantee<>c.relowner
        """).strip() == b"0"

    old = invoke("old", "stage-v10", bundle=args.v10, asset_root=args.v10_assets)
    assert old.returncode == 0, old.stderr.decode(errors="replace")
    old_receipt = json.loads(old.stdout)
    assert old_receipt["format"] == archive.FORMAT_WITH_STOCK_OUTBOX and old_receipt["outcome"] == "STAGED"
    assert old_receipt["recoveryReady"] is False and "fileContentHistory" not in old_receipt
    assert query("old", "v10-no-history-tables", """
        SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname='rma_archive_stage' AND c.relname LIKE 'file_content_history_%'
        """).strip() == b"0"

    # Test-only SQL error after executing the unmodified driver, still in its real transaction.
    def late_fault(_pg_bin, target, username, output, action):
        assert target == targets["fault"] and username == args.username and action == "stage"
        failed = psql("fault", "late-native", "--single-transaction", "--file=driver.sql", "--command=SELECT 1/0", cwd=output)
        assert failed.returncode != 0 and b"22012" in failed.stderr
        assert json.loads(failed.stdout)["outcome"] == "STAGED"
        return failed.stdout

    with patch.object(stage, "run_driver", late_fault):
        stage.execute(SimpleNamespace(action="stage", pg_bin=args.pg_bin, target_file=files["fault"],
                                     username=args.username, tenant=tenant, manifest_sha256=args.v11_pin,
                                     output=out / "fault-driver", bundle=args.v11, file_content_root=args.v11_assets,
                                     include_file_content_history=True))
    empty("fault", "after-late-native-rollback")
    absent = invoke("fault", "recover-rolled-back", recover=True)
    assert absent.returncode == 1 and b"rma-stage-receipt-not-found" in absent.stderr
    assert originals == file_hashes(args.v11) and assets == file_hashes(args.v11_assets)
    result = {"result": "PASS", "postgresql": native_version, "v11_manifest_sha256": args.v11_pin,
              "target": targets["history"].mapping(), "staged_members": checked, "recovery_ready": False,
              "receipt_exact_recovery": True, "receipt_sha256": digest(positive.stdout),
              "old_v10_stage": "PASS", "rollback_after_driver": "22012", "originals_unchanged": True, "assets_unchanged": True}
    persist(out / "actual.json", (json.dumps(result, indent=2) + "\n").encode())
    print(json.dumps(result))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ("pg-bin", "targets", "output", "v11", "v11-assets", "v10", "v10-assets"):
        parser.add_argument("--" + key, type=Path, required=True)
    for key in ("username", "v11-pin", "v10-pin"):
        parser.add_argument("--" + key, required=True)
    parser.add_argument("--original", action="store_true")
    verify(parser.parse_args())


if __name__ == "__main__":
    main()
