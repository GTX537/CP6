"""Exercise actual v11 and v10 FileContent materialization with pinned stage receipts."""

import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

import archive
import file_content_assets as assets


def sha(path):
    return archive.file_digest(path)["sha256"]


def snapshot(root):
    return {str(p.relative_to(root)): sha(p) for p in sorted(root.rglob("*")) if p.is_file()}


def save(path, data):
    with path.open("xb") as stream:
        stream.write(data)


def verify(args):
    out = args.output.resolve()
    archive.private_directory(out)
    manifest = args.v11 / "manifest.json"
    index = args.v11 / assets.MEMBER
    native = json.loads(args.v11_receipt.read_bytes())
    tenant = archive.guid(native["tenantId"])
    assert sha(args.v11_receipt) == args.v11_pin and sha(manifest) == native["manifestSha256"]
    assert native["format"] == archive.FORMAT_WITH_FILE_CONTENT_HISTORY and native["outcome"] == "STAGED"
    [(key, size, content_hash)] = list(assets.member_files(index, tenant, 1))
    source_path = args.v11_assets.joinpath(*key.split("/"))
    payload = source_path.read_bytes()
    assert len(payload) == size and hashlib.sha256(payload).hexdigest() == content_hash
    source_state = snapshot(args.v11_assets)
    archive_state = snapshot(args.v11)
    old_state = snapshot(args.v10_assets)
    old = json.loads(args.v10_receipt.read_bytes())
    assert sha(args.v10_receipt) == args.v10_pin and sha(args.v10 / "manifest.json") == old["manifestSha256"]
    program = Path(__file__).resolve().parent / "materialize_files.py"

    def invoke(name, target, *, action="materialize", bundle=None, receipt=None, pin=None, source=None, tenant_id=None):
        command = [sys.executable, "-B", str(program), action, "--bundle", str(bundle or args.v11),
                   "--stage-receipt", str(receipt or args.v11_receipt), "--stage-receipt-sha256", pin or args.v11_pin,
                   "--tenant", str(tenant_id or tenant), "--output", str(target)]
        if action == "materialize":
            command.extend(["--source-root", str(source or args.v11_assets)])
        result = subprocess.run(command, stdin=subprocess.DEVNULL, capture_output=True, timeout=30, creationflags=archive.FLAGS)
        save(out / (name + ".stdout"), result.stdout)
        save(out / (name + ".stderr"), result.stderr)
        save(out / (name + ".exit"), str(result.returncode).encode())
        return result

    target = out / "candidate-v11"
    positive = invoke("materialize-v11", target)
    if args.original:
        assert positive.returncode == 1 and b"file-materialize-stage-binding-rejected" in positive.stderr
        assert not os.path.lexists(target) and source_state == snapshot(args.v11_assets)
        result = {"result": "ORIGINAL_V11_REJECTED", "stage_pin": args.v11_pin, "source_unchanged": True}
        save(out / "actual.json", (json.dumps(result, indent=2) + "\n").encode())
        print(json.dumps(result))
        return
    assert positive.returncode == 0, positive.stderr.decode(errors="replace")
    receipt = json.loads(positive.stdout)
    assert receipt["outcome"] == "MATERIALIZED" and receipt["recoveryReady"] is False
    assert receipt["archiveFormat"] == archive.FORMAT_WITH_FILE_CONTENT_HISTORY
    assert receipt["stageTarget"] == native["target"] and receipt["stageReceiptSha256"] == args.v11_pin
    assert receipt["manifestSha256"] == native["manifestSha256"] and receipt["tenantId"] == str(tenant)
    files = target / "ready/files"
    assert receipt["fileContentRoot"] == files.as_posix()
    assert snapshot(files) == {str(Path(key)): content_hash}
    assert files.joinpath(*key.split("/")).read_bytes() == payload
    assert (target / "ready/receipt.json").read_bytes() == positive.stdout
    recovered = invoke("recover-v11", target, action="recover")
    again = invoke("repeat-v11", target)
    assert recovered.returncode == again.returncode == 0
    assert recovered.stdout == again.stdout == positive.stdout

    def rejected(name, text, **kw):
        path = out / name
        result = invoke(name, path, **kw)
        assert result.returncode == 1 and text in result.stderr and not os.path.lexists(path), name

    rejected("wrong-stage-pin", b"file-materialize-stage-pin-mismatch", pin="0" * 64)
    # This fixture holds the original manifest pin, but lies about the staged history.
    changed = json.loads(args.v11_receipt.read_bytes())
    changed["fileContentHistory"]["members"]["file-content-events.csv"]["rows"] = 0
    bad_receipt = out / "fixture-wrong-history.json"
    save(bad_receipt, (json.dumps(changed, indent=2) + "\n").encode())
    rejected("wrong-history-binding", b"file-materialize-history-binding-rejected", receipt=bad_receipt, pin=sha(bad_receipt))
    bad_root = out / "fixture-wrong-file"
    bad_file = bad_root.joinpath(*key.split("/"))
    bad_file.parent.mkdir(parents=True)
    save(bad_file, bytes([payload[0] ^ 1]) + payload[1:])
    rejected("wrong-file", b"file-content-asset-digest-mismatch", source=bad_root)

    race_target = out / "race-v11"
    with ThreadPoolExecutor(max_workers=2) as pool:
        contenders = [pool.submit(invoke, "race-" + str(i), race_target) for i in range(2)]
        race = [job.result() for job in contenders]
    successes = [x.stdout for x in race if x.returncode == 0]
    assert successes and all(x.returncode in (0, 1) for x in race)
    for failure in (x for x in race if x.returncode):
        assert b"file-materialize-output-incomplete" in failure.stderr
    race_recovery = invoke("recover-race", race_target, action="recover")
    assert race_recovery.returncode == 0 and all(x == race_recovery.stdout for x in successes)
    assert snapshot(race_target / "ready/files") == {str(Path(key)): content_hash}

    old_target = out / "candidate-v10"
    old_result = invoke("materialize-v10", old_target, bundle=args.v10, receipt=args.v10_receipt,
                        pin=args.v10_pin, source=args.v10_assets, tenant_id=old["tenantId"])
    assert old_result.returncode == 0, old_result.stderr.decode(errors="replace")
    old_recovery = invoke("recover-v10", old_target, action="recover", bundle=args.v10,
                          receipt=args.v10_receipt, pin=args.v10_pin, tenant_id=old["tenantId"])
    old_files = snapshot(old_target / "ready/files")
    [(old_key, old_size, old_hash)] = list(assets.member_files(args.v10 / assets.MEMBER, archive.guid(old["tenantId"]), 1))
    assert old_files == {str(Path(old_key)): old_hash}
    assert (old_target / "ready/files").joinpath(*old_key.split("/")).stat().st_size == old_size
    assert old_recovery.returncode == 0 and old_recovery.stdout == old_result.stdout
    assert json.loads(old_result.stdout)["archiveFormat"] == archive.FORMAT_WITH_STOCK_OUTBOX
    assert archive_state == snapshot(args.v11) and source_state == snapshot(args.v11_assets) and old_state == snapshot(args.v10_assets)
    assert not list(out.glob(".cp6-file-materialize-*"))
    result = {"result": "PASS", "archive_format": receipt["archiveFormat"], "stage_pin": args.v11_pin,
              "manifest_pin": receipt["manifestSha256"], "candidate_root": receipt["fileContentRoot"], "file_key": key,
              "file_bytes": size, "file_sha256": content_hash, "recovery_ready": False,
              "receipt_exact_recovery": True, "receipt_sha256": hashlib.sha256(positive.stdout).hexdigest(),
              "old_v10": {"bytes": old_size, "exact_recovery": True},
              "source_unchanged": True, "wrong_history_rejected": True, "wrong_file_rejected": True,
              "race_exits": [x.returncode for x in race], "race_exact_recovery": True}
    save(out / "actual.json", (json.dumps(result, indent=2) + "\n").encode())
    print(json.dumps(result))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ("output", "v11", "v11-receipt", "v11-assets", "v10", "v10-receipt", "v10-assets"):
        parser.add_argument("--" + key, type=Path, required=True)
    for key in ("v11-pin", "v10-pin"):
        parser.add_argument("--" + key, required=True)
    parser.add_argument("--original", action="store_true")
    verify(parser.parse_args())


if __name__ == "__main__":
    main()
