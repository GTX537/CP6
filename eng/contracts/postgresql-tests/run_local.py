"""Run the task's local tests once, retaining failures and exact source/artifact identities."""
import argparse
import datetime
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def sources():
    paths = [ROOT / "CP6.Persistence.PostgreSql/Contracts/ExactValueStorage.cs"]
    paths += [p for p in (ROOT / "eng/contracts/postgresql-tests").rglob("*") if p.is_file()
              and not any(x in p.relative_to(ROOT).parts for x in ("bin", "obj", "__pycache__"))]
    paths += [ROOT / p for p in ("CP6.Persistence.PostgreSql/CP6.Persistence.PostgreSql.csproj", "CP6.Persistence.PostgreSql/packages.lock.json",
                               "eng/contracts/s01/vectors.json", "eng/contracts/s01/profile-inputs.json")]
    mapping = ROOT / "eng/contracts/s01/postgresql-mappings.json"
    paths += [mapping if mapping.exists() else ROOT / "docs/development/evidence/S01-10/postgresql-mappings.proposed.json"]
    return {p.relative_to(ROOT).as_posix(): sha(p) for p in sorted(set(paths))}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("run_id")
    parser.add_argument("--filter", default="FullyQualifiedName~ExactValueStorageTests")
    args = parser.parse_args()
    if not re.fullmatch(r"[a-z0-9-]+", args.run_id): raise ValueError("Invalid evidence run ID")
    output = ROOT / "docs/development/evidence/S01-10/runs" / args.run_id
    output.mkdir(parents=True, exist_ok=False)
    before = sources()
    start = datetime.datetime.now(datetime.timezone.utc)
    lifecycle = ROOT / "docs/development/evidence/S01-10/lifecycle"
    old_receipts = set(lifecycle.glob("*.json")) if lifecycle.exists() else set()
    command = ["dotnet", "test", "eng/contracts/postgresql-tests/CP6.ExactValues.PostgreSqlTests.csproj", "--no-restore",
               "--filter", args.filter, "--logger", "trx;LogFileName=s01-10-postgresql.trx", "--results-directory", str(output), "--verbosity", "minimal"]
    with (output / "command.log").open("wb") as log:
        process = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
    counters, cases = {}, []
    trx = output / "s01-10-postgresql.trx"
    if trx.exists():
        document = ET.parse(trx)
        ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
        counters = document.find(".//t:Counters", ns).attrib
        cases = [{"name": e.attrib["testName"], "outcome": e.attrib["outcome"]} for e in document.findall(".//t:UnitTestResult", ns)]
    new_receipts = sorted(set(lifecycle.glob("*.json")) - old_receipts) if lifecycle.exists() else []
    receipt_data = [{"path": p.relative_to(ROOT).as_posix(), "sha256": sha(p), "status": json.loads(p.read_text())["status"]} for p in new_receipts]
    artifact_dir = ROOT / "eng/contracts/postgresql-tests/bin/Debug/net8.0"
    artifacts = {name: sha(artifact_dir / name) for name in ["CP6.ExactValues.PostgreSqlTests.dll", "CP6.Persistence.PostgreSql.dll", "CP6.Entity.dll", "Npgsql.dll"] if (artifact_dir / name).exists()}
    valid = process.returncode == 0 and before == sources() and int(counters.get("total", 0)) > 0 and all(c["outcome"] == "Passed" for c in cases)
    valid = valid and int(counters.get("notExecuted", 1)) == 0 and len(new_receipts) == 1 and receipt_data[0]["status"] == "CLEANED"
    report = {"task": "S01-10", "run_id": args.run_id, "started_utc": start.isoformat(), "finished_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "source_head": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
              "source_identity": "Exact working-file hashes; uncommitted implementation is identified independently from baseline HEAD",
              "command": command, "exit_code": process.returncode, "counters": counters, "cases": cases, "source_sha256": before,
              "source_unchanged_during_run": before == sources(), "artifact_sha256": artifacts, "lifecycle": receipt_data,
              "local_gate": "PASS" if valid else "FAIL_OR_BLOCKED", "scope": "Owned fixture only; draft mapping remains proposed until COORD decision; no production Owner/tenant/migration acceptance"}
    (output / "run.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: report[k] for k in ["run_id", "exit_code", "counters", "local_gate", "lifecycle"]}))
    lines = (output / "command.log").read_text(encoding="utf-8-sig", errors="replace").splitlines()
    print("\n".join(lines[-22:]))
    return 0 if valid else 1


if __name__ == "__main__":
    sys.exit(main())
