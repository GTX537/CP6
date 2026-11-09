#!/usr/bin/env python3
"""Exercise the offline materializer with the retained W412 nonempty native cut."""
import argparse
import csv
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
from uuid import uuid4

sys.dont_write_bytecode = True
import archive
import file_content_assets as assets

CLI = Path(__file__).resolve().with_name("materialize_files.py")
BEFORE_PUBLISH = """
import sys
import materialize_files as m
def fault(candidate, output):
    assert (candidate / 'receipt.json').is_file() and (candidate / 'files').is_dir()
    assert not output.exists()
    raise OSError('fixture interrupted before reservation')
m.publish = fault
sys.exit(m.main())
"""
AFTER_RESERVATION = """
import sys
from pathlib import Path
import materialize_files as m
original = Path.rename
def fault(path, target):
    if path.name == 'ready' and target.name == 'ready':
        assert target.parent.is_dir() and not any(target.parent.iterdir())
        raise OSError('fixture interrupted after reservation')
    return original(path, target)
Path.rename = fault
sys.exit(m.main())
"""


def require(condition, error):
    if not condition:
        raise AssertionError(error)


class Fixture:
    def __init__(self, args):
        self.args = args
        self.root = args.root / ("materialize-" + uuid4().hex[:12])
        archive.private_directory(self.root)

    def command(self, action, output, *, source=None, tenant=None, pin=None, bundle=None, fault=None):
        command = [sys.executable, "-B", str(CLI)] if fault is None else [sys.executable, "-B", "-c", fault]
        command += [action, "--bundle", str(bundle or self.args.bundle), "--tenant", str(tenant or self.args.tenant),
                    "--stage-receipt", str(self.args.stage_receipt), "--stage-receipt-sha256",
                    pin or self.args.stage_receipt_sha256, "--output", str(output)]
        if action == "materialize":
            command += ["--source-root", str(source or self.args.source_root)]
        return command

    def record(self, label, exit_code, stdout, stderr, *, discard=False):
        if not discard:
            (self.root / (label + ".stdout")).write_bytes(stdout)
        (self.root / (label + ".stderr")).write_bytes(stderr)
        (self.root / (label + ".json")).write_text(json.dumps(dict(exit=exit_code,
            discardedStdoutBytes=len(stdout) if discard else 0)) + "\n", encoding="utf-8")

    def call(self, label, action, output, *, error=None, discard=False, **options):
        result = subprocess.run(self.command(action, output, **options), cwd=CLI.parent, capture_output=True,
                                timeout=30, creationflags=archive.FLAGS)
        self.record(label, result.returncode, result.stdout, result.stderr, discard=discard)
        if error:
            require(result.returncode == 1 and not result.stdout and error.encode("utf-8") in result.stderr,
                    label + " wrong rejection")
        else:
            require(result.returncode == 0 and not result.stderr, label + " unexpected failure")
        return result.stdout

    def race(self, output, source):
        processes = []
        results = []
        try:
            for _ in range(2):
                processes.append(subprocess.Popen(self.command("materialize", output, source=source), cwd=CLI.parent,
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=archive.FLAGS))
            for index, process in enumerate(processes):
                out, err = process.communicate(timeout=30)
                self.record("race-" + str(index), process.returncode, out, err)
                require((process.returncode == 0 and not err) or
                        (process.returncode == 1 and not out and b"file-materialize-output-incomplete" in err),
                        "wrong competing caller outcome")
                results.append((process.returncode, out))
        finally:
            for process in processes:
                if process.poll() is None:
                    process.kill()
                    process.communicate(timeout=10)
        require(any(code == 0 for code, _ in results), "neither caller published")
        return results


def run(f):
    args = f.args
    with (args.bundle / assets.MEMBER).open(encoding="utf-8", newline="") as stream:
        row, = csv.DictReader(stream)
    key = Path(*row["store_path"].split("/"))
    original = args.source_root / key
    wrong = args.bad_source_root / key
    payload, altered = original.read_bytes(), wrong.read_bytes()
    require(len(payload) == len(altered) == 41 and payload != altered and row["tenant_id"] == str(args.tenant),
            "expected actual nonempty W412 inputs")
    binding = json.loads(args.stage_receipt.read_bytes())
    source_pins = {p: archive.file_digest(p) for p in
                   (original, wrong, args.stage_receipt, args.bundle / "manifest.json", args.bundle / assets.MEMBER)}
    valid = f.root / "materialized-a"
    first = f.call("good", "materialize", valid)
    receipt = json.loads(first)
    require(receipt["outcome"] == "MATERIALIZED" and receipt["recoveryReady"] is False
            and receipt["fileContentAssetCount"] == 1 and receipt["stageTarget"] == binding["target"]
            and receipt["manifestSha256"] == binding["manifestSha256"]
            and receipt["stageReceiptSha256"] == args.stage_receipt_sha256
            and Path(receipt["fileContentRoot"]) == valid / "ready/files"
            and (Path(receipt["fileContentRoot"]) / key).read_bytes() == payload,
            "actual W412 materialization or original bytes differ")
    require(first == (valid / "ready/receipt.json").read_bytes() == f.call("recover-good", "recover", valid)
            == f.call("same-key", "materialize", valid), "exact same-key original receipt differs")

    for label, options, error in (
        ("wrong-bytes", {"source": args.bad_source_root}, "file-content-asset-digest-mismatch"),
        ("wrong-stage-pin", {"pin": "0" * 64}, "file-materialize-stage-pin-mismatch"),
        ("foreign-tenant", {"tenant": args.b_tenant}, "file-materialize-stage-binding-rejected"),
    ):
        output = f.root / label
        f.call(label, "materialize", output, error=error, **options)
        require(not output.exists(), label + " left a final output")
    changed = f.root / "changed-index"
    archive.private_directory(changed)
    shutil.copyfile(args.bundle / "manifest.json", changed / "manifest.json")
    (changed / assets.MEMBER).write_bytes((args.bundle / assets.MEMBER).read_bytes() + b"\n")
    changed_output = f.root / "changed-index-output"
    f.call("wrong-index", "materialize", changed_output, bundle=changed, error="file-materialize-asset-member-mismatch")
    require(not changed_output.exists(), "changed pinned index left a final output")

    supplied = f.root / "a-and-b-supplied"
    archive.private_directory(supplied)
    first_asset = supplied / key
    first_asset.parent.mkdir(parents=True)
    first_asset.write_bytes(payload)
    foreign = supplied / key.parts[0] / args.b_tenant.hex / key.name
    foreign.parent.mkdir()
    foreign.write_bytes(altered)
    f.call("overlap-child", "materialize", supplied / "child", source=supplied, error="file-materialize-root-overlap")
    f.call("overlap-parent", "materialize", f.root, source=supplied, error="file-materialize-root-overlap")
    f.call("parent-traversal", "materialize", supplied / "..", error="file-materialize-output-rejected")
    link = f.root / "reparse-source"
    if os.name == "nt":
        subprocess.run(["pwsh", "-NoProfile", "-NonInteractive", "-Command",
                        "New-Item -ItemType Junction -Path $env:CP6_W414_LINK -Target $env:CP6_W414_TARGET | Out-Null"],
                       env=dict(os.environ, CP6_W414_LINK=str(link), CP6_W414_TARGET=str(supplied)), check=True,
                       capture_output=True, timeout=10, creationflags=archive.FLAGS)
    else:
        link.symlink_to(supplied, target_is_directory=True)
    try:
        f.call("reparse-source", "materialize", f.root / "reparse-output", source=link, error="file-content-root-rejected")
        f.call("reparse-target-parent", "materialize", link / "output", error="file-content-root-rejected")
    finally:
        if os.name == "nt":
            link.rmdir()
        else:
            link.unlink()
    occupied = f.root / "unrelated-existing"
    occupied.mkdir()
    sentinel = occupied / "sentinel"
    sentinel.write_bytes(b"unrelated private directory")
    f.call("occupied", "materialize", occupied, error="file-materialize-output-occupied")
    require(sentinel.read_bytes() == b"unrelated private directory" and list(occupied.iterdir()) == [sentinel],
            "existing unrelated root changed")

    race_output = f.root / "competing-candidate"
    race = f.race(race_output, supplied)
    persisted = (race_output / "ready/receipt.json").read_bytes()
    require(all(out == persisted for code, out in race if code == 0)
            and (race_output / "ready/files" / key).read_bytes() == payload
            and not (race_output / "ready/files" / key.parts[0] / args.b_tenant.hex).exists()
            and foreign.read_bytes() == altered, "race changed original bytes or materialized B")
    retained = f.root / "retained-source-with-b"
    supplied.rename(retained)
    require(f.call("race-recover-without-source", "recover", race_output) == persisted,
            "completed recovery unexpectedly depended on source root")

    delayed = f.root / "late-fault-candidate"
    f.call("late-fault", "materialize", delayed, error="fixture interrupted before reservation", fault=BEFORE_PUBLISH)
    require(not delayed.exists(), "interrupted scratch published a final target")
    f.call("discarded-reply", "materialize", delayed, discard=True)
    lost = (delayed / "ready/receipt.json").read_bytes()
    require(f.call("recover-discarded", "recover", delayed) == lost, "lost reply did not recover original bytes")
    copied = delayed / "ready/files" / key
    copied.write_bytes(altered)
    f.call("recover-corrupt", "recover", delayed, error="file-content-asset-digest-mismatch")

    reserved = f.root / "interrupted-reservation"
    f.call("reservation-fault", "materialize", reserved, error="fixture interrupted after reservation",
           fault=AFTER_RESERVATION)
    require(reserved.is_dir() and not any(reserved.iterdir()), "interrupted reservation exposed partial files")
    f.call("reservation-recover", "recover", reserved, error="file-materialize-output-incomplete")
    f.call("reservation-retry", "materialize", reserved, error="file-materialize-output-incomplete")
    require(not any(reserved.iterdir()) and not list(f.root.glob(".cp6-file-materialize-*")),
            "unexpected incomplete directory writes or leaked normal scratch")

    require(source_pins == {p: archive.file_digest(p) for p in source_pins}, "original W412 inputs changed")
    summary = dict(materialized=receipt, actualPayload=archive.file_digest(valid / "ready/files" / key),
                   raceExitCodes=[code for code, _ in race], recoveredRaw=True, wrongAssetZeroEffect=True,
                   beforePublicationZeroEffect=True, interruptedReservationClosed=True, originalInputsUnchanged=True)
    (f.root / "actual.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for field in ("root", "bundle", "stage-receipt", "source-root", "bad-source-root"):
        parser.add_argument("--" + field, type=Path, required=True)
    for field in ("tenant", "b-tenant"):
        parser.add_argument("--" + field, type=archive.guid, required=True)
    parser.add_argument("--stage-receipt-sha256", required=True)
    args = parser.parse_args()
    args.root = args.root.resolve(strict=True)
    fixture = Fixture(args)
    print("evidence:", fixture.root, flush=True)
    run(fixture)


if __name__ == "__main__":
    main()
