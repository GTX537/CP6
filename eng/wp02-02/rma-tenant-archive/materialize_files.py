#!/usr/bin/env python3
"""Materialize pinned FileContent bytes in a closed private filesystem candidate."""
import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
import archive
import erp_integration
import file_content_assets as assets
import privacy_evidence
from stage import StageError, digest_pin


FORMAT = "cp6-file-content-materialization-v1"
ASSET_FORMATS = (assets.FORMAT, erp_integration.FORMAT, privacy_evidence.FORMAT, archive.FORMAT_WITH_STOCK_OUTBOX)


def checked_directory(path):
    path = assets.checked_root(path)
    if path.resolve(strict=True) != path:
        raise ValueError("file-materialize-path-rejected")
    return path


def checked_input(path):
    checked_directory(path.parent)
    assets.entry(path, stat.S_ISREG, "file-materialize-input-rejected")
    if path.resolve(strict=True) != path:
        raise ValueError("file-materialize-input-rejected")
    return path


def overlap(first, second):
    return first == second or first in second.parents or second in first.parents


def checked_output(output, bundle, source):
    if not output.name or ".." in output.parts or (os.name == "nt" and (
            output.is_reserved() or output.name.endswith((".", " ")) or ":" in output.name)):
        raise ValueError("file-materialize-output-rejected")
    checked_directory(output.parent)
    if overlap(output, bundle) or source is not None and overlap(output, source):
        raise ValueError("file-materialize-root-overlap")
    return output


def read_binding(args, bundle, tenant, output):
    stage_pin = digest_pin(args.stage_receipt_sha256)
    raw = checked_input(args.stage_receipt).read_bytes()
    if hashlib.sha256(raw).hexdigest() != stage_pin:
        raise ValueError("file-materialize-stage-pin-mismatch")
    stage = json.loads(raw)
    if not isinstance(stage, dict) or stage.get("outcome") != "STAGED" or stage.get("recoveryReady") is not False \
            or stage.get("stageProfile") != "cp6-rma-archive-stage-v1" or stage.get("tenantId") != str(tenant) \
            or stage.get("format") not in ASSET_FORMATS or type(stage.get("fileContentAssetCount")) is not int:
        raise ValueError("file-materialize-stage-binding-rejected")
    target = archive.PhysicalTarget.from_mapping(stage.get("target"))
    manifest_pin = digest_pin(stage.get("manifestSha256"))
    raw = checked_input(bundle / "manifest.json").read_bytes()
    if hashlib.sha256(raw).hexdigest() != manifest_pin:
        raise ValueError("file-materialize-manifest-pin-mismatch")
    manifest = json.loads(raw)
    if not isinstance(manifest, dict) or manifest.get("format") != stage["format"] \
            or manifest.get("tenantId") != str(tenant) or manifest.get("recoveryReady") is not False \
            or not assets.matches_manifest(manifest):
        raise ValueError("file-materialize-manifest-binding-rejected")
    count = manifest["fileContentAssets"]["count"]
    members = manifest.get("members")
    member = members.get(assets.MEMBER) if isinstance(members, dict) else None
    if count != stage["fileContentAssetCount"] or not isinstance(member, dict) \
            or set(member) != {"bytes", "sha256"} or type(member["bytes"]) is not int or member["bytes"] <= 0:
        raise ValueError("file-materialize-asset-binding-rejected")
    receipt = dict(outcome="MATERIALIZED", recoveryReady=False, format=FORMAT, archiveFormat=stage["format"],
                   stageTarget=target.mapping(), tenantId=str(tenant), manifestSha256=manifest_pin,
                   stageReceiptSha256=stage_pin, assetMemberSha256=digest_pin(member["sha256"]),
                   fileContentAssetCount=count, fileContentRoot=(output / "ready" / "files").as_posix())
    return receipt, member


def copy_index(bundle, destination, expected):
    shutil.copyfile(checked_input(bundle / assets.MEMBER), destination, follow_symlinks=False)
    assets.entry(destination, stat.S_ISREG, "file-materialize-input-rejected")
    if archive.file_digest(destination) != expected:
        raise ValueError("file-materialize-asset-member-mismatch")


def completed(output, raw, index, tenant, count):
    if not os.path.lexists(output):
        raise ValueError("file-materialize-output-missing")
    checked_directory(output)
    entries = {entry.name for entry in output.iterdir()}
    if not entries:
        raise ValueError("file-materialize-output-incomplete")
    if entries != {"ready"}:
        raise ValueError("file-materialize-output-occupied")
    ready = checked_directory(output / "ready")
    if {entry.name for entry in ready.iterdir()} != {"files", "receipt.json"}:
        raise ValueError("file-materialize-output-occupied")
    original = checked_input(ready / "receipt.json").read_bytes()
    if original != raw:
        raise ValueError("file-materialize-receipt-key-rejected")
    files = checked_directory(ready / "files")
    expected = {"native-content-v1"} if count else set()
    if {entry.name for entry in files.iterdir()} != expected:
        raise ValueError("file-materialize-output-contents-rejected")
    if count:
        namespace = checked_directory(files / "native-content-v1")
        if {entry.name for entry in namespace.iterdir()} != {tenant.hex} \
                or sum(1 for _ in checked_directory(namespace / tenant.hex).iterdir()) != count:
            raise ValueError("file-materialize-output-contents-rejected")
    assets.verify_member(index, tenant, count, files)
    return original


def publish(candidate, output):
    # Only a caller that creates the exact reservation may publish inside it.
    try:
        archive.private_directory(output)
    except FileExistsError:
        return False
    ready = output / "ready"
    if os.path.lexists(ready):
        raise ValueError("file-materialize-output-occupied")
    candidate.rename(ready)
    return True


def execute(args):
    tenant = archive.guid(args.tenant)
    bundle = checked_directory(args.bundle)
    source = checked_directory(args.source_root) if args.action == "materialize" else None
    output = checked_output(args.output, bundle, source)
    receipt, member = read_binding(args, bundle, tenant, output)
    raw = (json.dumps(receipt, indent=2) + "\n").encode("utf-8")
    count = receipt["fileContentAssetCount"]

    # TemporaryDirectory owns only this unpredictable scratch directory. File bytes
    # and the pinned input snapshot live beneath its explicitly private child.
    with tempfile.TemporaryDirectory(prefix=".cp6-file-materialize-", dir=output.parent) as temporary:
        private = Path(temporary) / "private"
        archive.private_directory(private)
        index = private / assets.MEMBER
        copy_index(bundle, index, member)
        if args.action == "recover" or os.path.lexists(output):
            return completed(output, raw, index, tenant, count)

        candidate = private / "ready"
        files = candidate / "files"
        files.mkdir(parents=True, mode=0o700)
        for key, length, digest in assets.member_files(index, tenant, count):
            path = files.joinpath(*key.split("/"))
            path.parent.mkdir(parents=True, exist_ok=True)
            with path.open("xb") as sink:
                assets.verify_file(source, key, length, digest, sink=sink)
                sink.flush()
                os.fsync(sink.fileno())
        with (candidate / "receipt.json").open("xb") as receipt_file:
            receipt_file.write(raw)
            receipt_file.flush()
            os.fsync(receipt_file.fileno())
        if publish(candidate, output):
            return raw
        return completed(output, raw, index, tenant, count)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="action", required=True)
    for action in ("materialize", "recover"):
        command = commands.add_parser(action)
        for name in ("bundle", "stage-receipt", "output"):
            command.add_argument("--" + name, type=Path, required=True)
        for name in ("tenant", "stage-receipt-sha256"):
            command.add_argument("--" + name, required=True)
        if action == "materialize":
            command.add_argument("--source-root", type=Path, required=True)
    try:
        sys.stdout.buffer.write(execute(parser.parse_args()))
    except (StageError, archive.ArchiveError, OSError, ValueError, csv.Error, subprocess.SubprocessError) as error:
        print("file materialization rejected:", error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
