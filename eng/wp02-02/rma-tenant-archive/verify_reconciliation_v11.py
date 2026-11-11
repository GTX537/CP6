#!/usr/bin/env python3
"""Exercise all four read-only reconciliation commands on one pinned v11 source."""

import argparse
import csv
import json
from pathlib import Path
import shutil
import subprocess
import sys

import archive
import file_content_assets as assets


ENTRIES = {
    "iam": ("reconcile_iam.py", "iam-reconciliation.jsonl", "iam-original-bundle-rejected"),
    "identity": ("reconcile_identity_priority.py", "reconciliation.jsonl", "priority-v5-bundle-rejected"),
    "erp": ("reconcile_erp_deliveries.py", "reconciliation.jsonl", "erp-v8-bundle-rejected"),
    "stock": ("reconcile_stock_outbox.py", "reconciliation.jsonl", "stock-v10-bundle-rejected"),
}


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def invoke(args, kind, label, *, rejection=None, pin=None, root=None):
    script, report_name, _ = ENTRIES[kind]
    out = args.output / label
    command = [sys.executable, "-B", str(Path(__file__).with_name(script)), "--pg-bin", str(args.pg_bin),
               "--target-file", str(args.target_file), "--username", args.username, "--tenant", args.tenant,
               "--bundle", str(args.bundle), "--manifest-sha256", pin or args.manifest_sha256,
               "--file-content-root", str(root or args.file_content_root), "--output", str(out)]
    result = subprocess.run(command, capture_output=True, timeout=100, creationflags=archive.FLAGS)
    (args.output / (label + ".stdout")).write_bytes(result.stdout)
    (args.output / (label + ".stderr")).write_bytes(result.stderr)
    (args.output / (label + ".exit")).write_text(str(result.returncode), encoding="ascii")
    report = out / report_name
    if rejection:
        require(result.returncode == 1 and not result.stdout and rejection.encode() in result.stderr
                and not report.exists() and not (out / (report_name + ".pending")).exists()
                and not list(out.rglob("holder.stdout")), f"{label} did not reject before the database")
        return None
    require(result.returncode == 0 and not result.stderr and report.is_file(), f"{label} failed; see native logs")
    reply = json.loads(result.stdout)
    require(Path(reply["report"]) == report and reply["recoveryReady"] is False, f"{label} output binding differs")
    return report


def observed(kind, report, manifest, pin):
    header, *rows, summary = (json.loads(line) for line in report.read_bytes().splitlines())
    current = header["current"]
    original = header["original"] if kind == "iam" else dict(
        manifestSha256=header["manifestSha256"], snapshot=header["archived"])
    require(header["type"] == "header" and summary["type"] == "summary"
            and header["source"] == manifest["source"] and header["tenantId"] == manifest["tenantId"]
            and header["installationId"] == manifest["installationId"] and original["manifestSha256"] == pin
            and original["snapshot"] == manifest["snapshot"]
            and current["readOnly"] is True and current["isolation"] == "repeatable read"
            and summary["result"] == "OBSERVED" and summary["recoveryReady"] is False,
            f"{kind} report did not bind the original and current read-only snapshots")
    if kind == "iam":
        counts = manifest["rmaIamReferences"]["counts"]
        expected = sum(counts.values())
        require(original["format"] == manifest["format"] and summary["originalCounts"] == summary["currentCounts"] == counts
                and all(row["decision"] == "UNCHANGED" and row["originalFacts"] == row["currentFacts"] for row in rows),
                "IAM original facts did not match the same source")
    else:
        if kind == "identity":
            tables = manifest["identityPriority"]["tables"]
            keys = ("Cp6_InboxMessage", "Cp6_OutboxMessage")
        elif kind == "erp":
            tables = manifest["erpIntegration"]["tables"]
            keys = ("CommandInbox", "Cp6_OutboxMessage", "OrderBridgeDispatch")
        else:
            tables = manifest["stockOutbox"]["tables"]
            keys = ("stock-outbox-Cp6_OutboxMessage.csv",)
        expected = sum(tables[name]["count"] for name in keys)
        require(all(row["decision"] == "PRESERVE_CURRENT" and
                    row["current"]["state"] == (row["archivedState"] if kind == "identity" else row["archived"]["state"])
                    for row in rows), f"{kind} original keys and current delivery states differ")
    require(expected > 0 and len(rows) == expected and sum(summary["counts"].values()) == expected
            and all(row["type"] == "observation" for row in rows), f"{kind} needs exact nonempty evidence")
    return dict(observations=len(rows), summary=summary["counts"], readOnly=True, isolation=current["isolation"],
                report=archive.file_digest(report))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ("pg-bin", "target-file", "bundle", "file-content-root", "output"):
        parser.add_argument("--" + key, type=Path, required=True)
    for key in ("username", "tenant", "manifest-sha256"):
        parser.add_argument("--" + key, required=True)
    parser.add_argument("--original", action="store_true")
    args = parser.parse_args()
    archive.private_directory(args.output)
    manifest_path = args.bundle / "manifest.json"
    require(archive.file_digest(manifest_path)["sha256"] == args.manifest_sha256, "verification uses the pinned original v11")
    manifest = json.loads(manifest_path.read_bytes())
    require(manifest["format"] == archive.FORMAT_WITH_FILE_CONTENT_HISTORY and manifest["fileContentAssets"]["count"] > 0,
            "verification requires original v11 FileContent assets")
    require(manifest["source"] == json.loads(args.target_file.read_bytes()), "fixture is not the pinned source")
    results = {}
    for kind, (_, _, original_error) in ENTRIES.items():
        report = invoke(args, kind, kind, rejection=original_error if args.original else None)
        results[kind] = dict(exit=1, reason=original_error) if args.original else observed(
            kind, report, manifest, args.manifest_sha256)
    if not args.original:
        invoke(args, "stock", "wrong-manifest-pin", rejection="manifest-digest-mismatch", pin="0" * 64)
        wrong = args.output / "wrong-files"
        shutil.copytree(args.file_content_root, wrong)
        member = args.bundle / assets.MEMBER
        with member.open(encoding="utf-8", newline="") as stream:
            row = next(csv.DictReader(stream))
        key = row["store_path"]
        path = wrong.joinpath(*key.split("/"))
        original = path.read_bytes()
        path.write_bytes(bytes([original[0] ^ 1]) + original[1:])
        invoke(args, "erp", "wrong-original-file", rejection="file-content-asset-digest-mismatch", root=wrong)
    with (args.output / "actual.json").open("x", encoding="utf-8") as stream:
        json.dump(dict(result="ORIGINAL_V11_REJECTED" if args.original else "PASS", manifest=args.manifest_sha256,
                       results=results, recoveryReady=False), stream, indent=2)
        stream.write("\n")


if __name__ == "__main__":
    main()
