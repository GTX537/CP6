#!/usr/bin/env python3
"""Stage and recover a pinned v7 archive with its original local asset bytes."""

import argparse
import csv
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import sys
from unittest.mock import patch

sys.dont_write_bytecode = True

from verify_stage import (BASE_ENV, FLAGS, OWNER, STAGE, Fixture, assert_empty, check_acl, copy_staged, invoke,
                          new_target, pinned, put_json, require, token)
import archive
import file_content_assets
import stage


def invoke_stage(f, label, target, source, asset_root, error=None, discard=False):
    bundle, tenant, digest = source
    request = f.root / (label + "-target.json")
    put_json(request, target)
    output = f.root / (label + "-native")
    command = [sys.executable, "-B", str(STAGE), "stage", "--pg-bin", str(f.args.pg_bin),
               "--target-file", str(request), "--username", OWNER, "--tenant", tenant,
               "--manifest-sha256", digest, "--output", str(output), "--bundle", str(bundle)]
    if asset_root is not None:
        command += ["--file-content-root", str(asset_root)]
    process = subprocess.run(command, env={**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, cwd=f.root,
                             timeout=95, capture_output=True, creationflags=FLAGS)
    f.save(label + ".stderr", process.stderr)
    if not discard:
        f.save(label + ".stdout", process.stdout)
    f.save(label + ".process.json", {"ExitCode": process.returncode,
                                    "DiscardedStdoutBytes": len(process.stdout) if discard else 0})
    if error:
        require(process.returncode == 1 and error.encode("utf-8") in process.stderr and not process.stdout
                and not (output / "driver.sql").exists() and not (output / "pg-restore-list.stdout").exists(),
                label + " did not reject before native restore or connection")
    else:
        require(process.returncode == 0, label + " failed")


def stage_with_count_fault(f, target, source, asset_root):
    bundle, tenant, digest = source
    request = f.root / "v7-late-target.json"
    put_json(request, target)
    args = argparse.Namespace(action="stage", pg_bin=f.args.pg_bin, target_file=request, username=OWNER,
                              tenant=tenant, manifest_sha256=digest, bundle=bundle, file_content_root=asset_root,
                              output=f.root / "v7-late-native")
    run_driver = stage.run_driver

    def inject(pg_bin, physical, username, output, action):
        driver = output / "driver.sql"
        script = driver.read_text(encoding="utf-8")
        marker = "DO $cp6_stage_file_content_assets$"
        require(script.count(marker) == 1, "missing native asset count check")
        fault = "DELETE FROM rma_archive_stage.file_content_asset_snapshot;\n"
        driver.write_text(script.replace(marker, fault + marker, 1), encoding="utf-8")
        return run_driver(pg_bin, physical, username, output, action)

    with patch.dict(os.environ, {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, clear=True), \
            patch.object(stage, "run_driver", inject):
        try:
            stage.execute(args)
        except stage.StageError as error:
            require(str(error) == "rma-stage-file-content-count-mismatch", "wrong late native asset count rejection")
        else:
            raise AssertionError("late removal of staged asset metadata was accepted")
    stderr = (args.output / "stage.stderr").read_bytes()
    require(b"P0001" in stderr and b"rma-stage-file-content-count-mismatch" in stderr, "missing native asset count error")


def exercise(f, source):
    bundle, tenant, digest = source
    manifest = archive.verify_bundle(bundle, tenant, file_content_root=f.args.file_content_root)
    member = bundle / file_content_assets.MEMBER
    with member.open(encoding="utf-8", newline="") as stream:
        row, = csv.DictReader(stream)
    original_file = f.args.file_content_root / row["store_path"]
    original_bytes = original_file.read_bytes()
    asset_root = f.root / "supplied-local-store"
    asset_root.mkdir()
    copied = asset_root / row["store_path"]
    copied.parent.mkdir(parents=True)
    target = new_target(f, "w394_assets")
    database = target["Database"]

    invoke_stage(f, "v7-no-root", target, source, None, "file-content-root-required")
    invoke_stage(f, "v7-missing-file", target, source, asset_root, "file-content-asset-not-found")
    copied.write_bytes(bytes((original_bytes[0] ^ 1,)) + original_bytes[1:])
    invoke_stage(f, "v7-corrupt-file", target, source, asset_root, "file-content-asset-digest-mismatch")
    assert_empty(f, database)
    copied.write_bytes(original_bytes)

    stage_with_count_fault(f, target, source, asset_root)
    assert_empty(f, database)
    invoke(f, "v7-rollback-no-receipt", "recover", target, tenant, digest, None, 1, "rma-stage-receipt-not-found")
    invoke_stage(f, "v7-stage-discarded", target, source, asset_root, discard=True)
    original = f.sql(database, "SELECT result_json FROM rma_archive_stage.receipt;").encode("utf-8")

    owned = f.root.resolve(strict=True)
    supplied = asset_root.resolve(strict=True)
    retained = (owned / "retained-supplied-assets").resolve()
    require(supplied.parent == retained.parent == owned and not retained.exists(), "private asset move escaped fixture")
    supplied.rename(retained)
    require(not asset_root.exists() and original_file.read_bytes() == original_bytes, "original asset was moved or changed")
    recovered = invoke(f, "v7-recover-without-root", "recover", target, tenant, digest)
    require(recovered == original, "v7 recover changed persisted receipt bytes")
    receipt = json.loads(recovered)
    require(receipt["format"] == file_content_assets.FORMAT and receipt["target"] == target and receipt["tenantId"] == tenant
            and receipt["manifestSha256"] == digest and receipt["outcome"] == "STAGED" and receipt["recoveryReady"] is False
            and receipt["fileContentAssetCount"] == manifest["fileContentAssets"]["count"] == 1
            and receipt["iamReferenceCounts"] == manifest["rmaIamReferences"]["counts"], "wrong original v7 stage receipt")
    copy_staged(f, database, "file_content_asset_snapshot", "file_id", member)
    check_acl(f, database, tenant)
    require(f.sql(database, """
        SELECT pg_typeof(file_id)='uuid'::regtype AND pg_typeof(parent_version)='text'::regtype
            AND pg_typeof(byte_length)='bigint'::regtype AND pg_typeof(state_version)='bigint'::regtype
            AND pg_typeof(state_etag)='text'::regtype AND to_regnamespace('cp6_file_content_v1') IS NULL
            AND to_regnamespace('cp6_engineering_revision_v1') IS NULL
        FROM rma_archive_stage.file_content_asset_snapshot;
    """, label="v7-native-asset-types") == "t", "v7 changed native types or activated FileContent")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "bundle", "file-content-root"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--manifest-sha256", required=True)
    args = parser.parse_args()
    args.root = args.root.resolve(strict=True)
    args.pg_bin = args.pg_bin.resolve(strict=True)
    source = pinned(args.bundle, args.manifest_sha256, file_content_assets.FORMAT)
    fixture = Fixture(args, "rma-stage-v7-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    fixture.save("source.json", {"Bundle": str(source[0]), "TenantId": source[1], "ManifestSha256": source[2]})
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        exercise(fixture, source)
    print("native v7 staging, late asset rollback and original recovery without asset root passed", flush=True)


if __name__ == "__main__":
    main()
