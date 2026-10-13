"""Bind real S01-03 test evidence to current files and, optionally, committed sources."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[4]
HERE = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument("--stage", default="green")
parser.add_argument("--source-ref")
parser.add_argument("--out", type=Path, required=True)
args = parser.parse_args()
assert not args.out.exists(), "Use a unique receipt; never overwrite validation evidence"


def git(*values):
    return subprocess.check_output(["git", "-c", "core.longpaths=true", *values], cwd=ROOT)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_run(stage):
    metadata_path = HERE / "implementation" / f"{stage}-metadata.json"
    trx_path = HERE / "implementation" / f"{stage}.trx"
    metadata = json.loads(metadata_path.read_text(encoding="utf-8-sig"))
    tree = ET.parse(trx_path)
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counters = {key: int(value) for key, value in tree.find("t:ResultSummary/t:Counters", ns).attrib.items()}
    results = [{"name": item.attrib["testName"], "outcome": item.attrib["outcome"]}
               for item in tree.findall("t:Results/t:UnitTestResult", ns)]
    return metadata, counters, results


metadata, counters, results = read_run(args.stage)
assert metadata["exit_code"] == 0 and metadata["sources_unchanged_during_run"]
assert counters["total"] == counters["executed"] == counters["passed"] == len(results) > 25
assert counters["failed"] == counters["notExecuted"] == 0
assert all(result["outcome"] == "Passed" for result in results)
dispatch = json.loads((ROOT / "docs/development/dispatch/20261012-wave3/dispatch.json").read_bytes())
task = next(task for task in dispatch["tasks"] if task["id"] == "S01-03")
expected_ids = set(task["case_ids"])
fixed_results = []
for case_id in task["case_ids"]:
    matches = [result for result in results
               if re.search(r"(?<![A-Za-z0-9-])" + re.escape(case_id) + r"(?![A-Za-z0-9-])", result["name"])]
    assert len(matches) == 1, (case_id, matches)
    fixed_results.append({"id": case_id, "result": "PASS", "test_name": matches[0]["name"]})
sources = []
source_ref = git("rev-parse", args.source_ref).decode().strip() if args.source_ref else None
for source in metadata["sources"]:
    path = source["path"]
    assert digest(ROOT / path) == source["sha256_file_bytes"], path
    receipt = dict(source)
    if source_ref:
        # hash-object applies Git text filters; recorded raw execution bytes remain distinct.
        actual_blob = git("hash-object", "--", path).decode().strip()
        committed_blob = git("rev-parse", f"{source_ref}:{path}").decode().strip()
        assert actual_blob == committed_blob, path
        receipt["git_blob"] = committed_blob
        receipt["sha256_git_blob"] = hashlib.sha256(git("show", f"{source_ref}:{path}")).hexdigest()
        receipt["committed_source_matches_execution_file_after_git_filters"] = True
    sources.append(receipt)
for artifact in metadata["artifacts"]:
    assert digest(ROOT / artifact["path"]) == artifact["sha256_file_bytes"], artifact["path"]
copied_vectors = ROOT / "eng/contracts/exact-values-tests/bin/Debug/net8.0/Inputs/vectors.json"
assert copied_vectors.read_bytes() == (ROOT / "eng/contracts/s01/vectors.json").read_bytes()
red_runs = []
pre_execution_failures = []
for path in sorted((HERE / "implementation").glob("red*-metadata.json")):
    stage = path.name.removesuffix("-metadata.json")
    if not (HERE / "implementation" / f"{stage}.trx").exists():
        failed_metadata = json.loads(path.read_text(encoding="utf-8-sig"))
        assert failed_metadata["exit_code"] != 0
        pre_execution_failures.append({"stage": stage, "exit_code": failed_metadata["exit_code"],
                                       "classification": "No TRX; not counted as executed TDD RED."})
        continue
    red_meta, red_count, _ = read_run(stage)
    assert red_meta["exit_code"] != 0 and red_count["executed"] > 0 and red_count["failed"] > 0
    red_runs.append({"stage": stage, "exit_code": red_meta["exit_code"], "counters": red_count})
assert red_runs, "An actual compiled-runtime RED is required"
entry = json.loads((HERE / "entry-check.json").read_bytes())
for pin in entry["shared_read_only_pins"]:
    assert hashlib.sha256(git("show", f"HEAD:{pin['path']}")).hexdigest() == pin["sha256_git_blob"]
    assert git("hash-object", "--", pin["path"]).decode().strip() == git("rev-parse", f"HEAD:{pin['path']}").decode().strip()
allowed = task["write_paths"]
if source_ref:
    changed = git("diff", "--name-only", entry["head"], source_ref).decode().splitlines()
    assert all(any(path == scope or (scope.endswith("/") and path.startswith(scope)) for scope in allowed)
               for path in changed), changed
report = {"task": "S01-03", "result": "PASS", "observed_at_utc": datetime.now(timezone.utc).isoformat(),
          "input_package_version": dispatch["input_package_version"], "source_sha": source_ref,
          "execution_base_head": metadata["base_head"], "stage": args.stage,
          "scope": "Read-only binding of existing real test execution, not a new test run.",
          "counters": counters, "fixed_cases": fixed_results, "additional_case_count": len(results) - 25,
          "all_test_results": results, "sources": sources, "artifacts": metadata["artifacts"],
          "copied_input_bytes_match": True, "runtime_red_records": red_runs,
          "pre_execution_failure_records": pre_execution_failures,
          "shared_read_only_inputs_unchanged": True, "consumer_adoption": "NOT_RUN",
          "business_acceptance": "NOT_RUN", "database": "NOT_RUN", "shared_state_and_merge_owner": "COORD"}
args.out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"result": "PASS", "stage": args.stage, "counters": counters,
                  "fixed_case_count": len(fixed_results), "source_sha": source_ref, "out": str(args.out)}))
