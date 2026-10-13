"""Verify selected real S01-11 runs and bind their bytes; never rerun or rewrite tests."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[4]
EVIDENCE = ROOT / "docs/development/evidence/S01-11"
parser = argparse.ArgumentParser()
parser.add_argument("--dotnet-run", required=True)
parser.add_argument("--ts-run", required=True)
parser.add_argument("--typecheck-run", required=True)
parser.add_argument("--design-run", required=True)
parser.add_argument("--source-sha")
parser.add_argument("--out", required=True, type=Path)
args = parser.parse_args()


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(*values):
    return subprocess.check_output(["git", "-c", "core.longpaths=true", *values], cwd=ROOT)


def relevant(path, kind):
    if kind == "design":
        return path in ["eng/contracts/s01-11/verify_design.py", "eng/contracts/s01-11/provenance.json",
                        "eng/contracts/s01-11/fixtures/current-chain.json"] or path.startswith(
                            ("eng/contracts/s01-11/assets/", "eng/contracts/s01-11/fixtures/current-bodies/"))
    if kind == "typecheck":
        return path.startswith("cp6.web/")
    private_inputs = path.startswith("eng/contracts/s01/") or (
        path.startswith("eng/contracts/s01-11/") and path.endswith(".json"))
    if kind == "csharp":
        return private_inputs or path.startswith(("CP6.Entity/", "eng/contracts/exact-values-tests/")) or "/" not in path
    return private_inputs or path.startswith("cp6.web/")


def read_run(name, kind):
    directory = (EVIDENCE / "implementation" / name).resolve()
    assert directory.parent == (EVIDENCE / "implementation").resolve(), name
    metadata = json.loads((directory / "metadata.json").read_text(encoding="utf-8"))
    assert metadata["exit_code"] == 0, (name, metadata["exit_code"])
    before = {path: value for path, value in metadata["inputs_before"].items() if relevant(path, kind)}
    after = {path: value for path, value in metadata["inputs_after"].items() if relevant(path, kind)}
    assert before == after, (name, "Relevant inputs changed during execution")
    for path, expected in metadata["evidence_files"].items():
        assert digest(directory / path) == expected, (name, path)
    for path, expected in after.items():
        assert digest(ROOT / path) == expected, (name, path, "Execution inputs no longer match")
    metadata["checked_inputs"] = after
    metadata["input_check_scope"] = kind
    return directory, metadata


dotnet_dir, dotnet = read_run(args.dotnet_run, "csharp")
ts_dir, ts = read_run(args.ts_run, "typescript")
typecheck_dir, typecheck = read_run(args.typecheck_run, "typecheck")
design_dir, design = read_run(args.design_run, "design")
assert "FullyQualifiedName~ErpCreditCodecTests" in dotnet["command"]
assert "src/contracts/erp-credit/creditCodec.test.ts" in ts["command"]
assert "type-check" in typecheck["command"]
design_result = json.loads((design_dir / "design.json").read_text(encoding="utf-8"))
assert design_result["tests"] == design_result["passed"] > 0
assert design_result["failed"] == design_result["skipped"] == 0
assert design_result["runtime_Owner_Store_acceptance"] == "NOT_RUN"

trx = ET.parse(dotnet_dir / "results.trx").getroot()
counters = {key: int(value) for key, value in trx.find("{*}ResultSummary/{*}Counters").attrib.items()}
cs_results = [{"name": node.attrib["testName"], "test_id": node.attrib["testId"], "outcome": node.attrib["outcome"]}
              for node in trx.findall(".//{*}UnitTestResult")]
for row in cs_results:
    if ", item: " in row["name"]:
        # xUnit truncates/escapes the display id argument, but keeps the complete JsonElement.
        assert row["name"].endswith(")")
        row["case"] = json.loads(row["name"].split(", item: ", 1)[1][:-1])
assert counters["total"] == counters["executed"] == counters["passed"] == len(cs_results) > 0
assert counters["failed"] == counters["notExecuted"] == 0
assert all(row["outcome"] == "Passed" for row in cs_results)
report = json.loads((ts_dir / "vitest.json").read_text(encoding="utf-8"))
ts_results = [{"name": row["fullName"], "outcome": row["status"]}
              for suite in report["testResults"] for row in suite["assertionResults"]]
assert report["numTotalTests"] == report["numPassedTests"] == len(ts_results) > 0
assert report["numFailedTests"] == report["numPendingTests"] == 0
assert all(row["outcome"] == "passed" for row in ts_results)

vectors = json.loads((ROOT / "eng/contracts/s01/vectors.json").read_text(encoding="utf-8"))
fixed_ids = [row["id"] for row in vectors["cases"]
             if row["profile_id"] in ["erp-credit-current-v1", "erp-credit-frozen-r8"]]
assert len(fixed_ids) == 10 and len(set(fixed_ids)) == 10
fixture_rows = json.loads(
    (ROOT / "eng/contracts/s01-11/fixtures/cases.json").read_text(encoding="utf-8"))["cases"]
fixture_by_id = {row["id"]: row for row in fixture_rows}
fixture_ids = [row["id"] for row in fixture_rows]
assert len(fixture_ids) == len(set(fixture_ids)) > 0
expected_cases = {**{row["id"]: row for row in vectors["cases"] if row["id"] in fixed_ids}, **fixture_by_id}
executed_cases = [row for row in cs_results if "case" in row]
assert len(executed_cases) == len(expected_cases)
assert len({row["case"]["id"] for row in executed_cases}) == len(expected_cases)
for row in executed_cases:
    assert row["case"] == expected_cases[row["case"]["id"]], row["case"]["id"]


def matching(rows, case_id):
    if rows is cs_results:
        matches = [row for row in rows if row.get("case", {}).get("id") == case_id]
        assert len(matches) == 1, (case_id, len(matches))
        return {"test_id": matches[0]["test_id"], "method": matches[0]["name"].split("(", 1)[0],
                "case_id_from_complete_item": case_id, "outcome": matches[0]["outcome"]}
    pattern = re.compile(r"(?<![A-Za-z0-9_-])" + re.escape(case_id) + r"(?![A-Za-z0-9_-])")
    matches = [row["name"] for row in rows if pattern.search(row["name"])]
    assert len(matches) == 1, (case_id, matches)
    return matches[0]


coverage = [{"id": case_id, "kind": "FROZEN_INPUT" if case_id in fixed_ids else
             fixture_by_id[case_id].get("origin", "CONSTRUCTED_CONTRACT_UNIT_INPUT"),
             "source_pointer": fixture_by_id.get(case_id, {}).get("source_pointer"),
             "csharp": matching(cs_results, case_id), "typescript": matching(ts_results, case_id),
             "result": "PASS"} for case_id in fixed_ids + fixture_ids]
artifacts = []
for path, expected in dotnet["compiled_artifacts_after"].items():
    assert digest(ROOT / path) == expected, (path, "Compiled artifact changed")
    artifacts.append({"path": path, "sha256_file_bytes": expected})
assert artifacts

entry = json.loads((EVIDENCE / "entry.json").read_text(encoding="utf-8"))
for row in entry["input_checks"]:
    if row["basis"] == "git_blob_bytes":
        assert hashlib.sha256(git("show", "HEAD:" + row["path"])).hexdigest() == row["sha256"]
owners = json.loads((EVIDENCE / "owner-boundary.json").read_text(encoding="utf-8"))
for row in owners["read_only_existing_owner_paths"]:
    assert digest(ROOT / row["path"]) == row["sha256_worktree_file"]

source_binding = None
if args.source_sha:
    source_sha = git("rev-parse", args.source_sha + "^{commit}").decode().strip()
    changed = set(git("diff", "--name-only", source_sha).decode().splitlines())
    tracked = {}
    for line in git("ls-tree", "-rz", source_sha).decode().split("\0"):
        if not line:
            continue
        header, path = line.split("\t", 1)
        tracked[path] = header.split()[2]
    inputs = {**dotnet["checked_inputs"], **ts["checked_inputs"], **typecheck["checked_inputs"], **design["checked_inputs"]}
    assert not changed.intersection(inputs), changed.intersection(inputs)
    owned_prefixes = ("CP6.Entity/Contracts/ErpCredit/", "cp6.web/src/contracts/erp-credit/", "eng/contracts/s01-11/")
    assert all(path in tracked for path in inputs if path.startswith(owned_prefixes)), "Uncommitted task inputs"
    assert "eng/contracts/exact-values-tests/ErpCreditCodecTests.cs" in tracked
    source_binding = {"source_sha": source_sha, "basis": "Raw executed input hashes + Git filtered diff equality",
                      "tracked_inputs": [{"path": path, "sha256_file_bytes": value, "git_blob": tracked[path]}
                                         for path, value in sorted(inputs.items()) if path in tracked],
                      "generated_inputs": [{"path": path, "sha256_file_bytes": value}
                                           for path, value in sorted(inputs.items()) if path not in tracked]}

history = []
for path in sorted((EVIDENCE / "implementation").glob("*/metadata.json")):
    metadata = json.loads(path.read_text(encoding="utf-8"))
    history.append({"path": path.relative_to(ROOT).as_posix(), "exit_code": metadata.get("exit_code"),
                    "counts": metadata.get("counts"), "started_utc": metadata["started_utc"],
                    "ended_utc": metadata.get("ended_utc")})
receipt = {"task": "S01-11", "observed_at": datetime.now(timezone.utc).isoformat(), "result": "PASS",
           "scope": "Independent evidence readback and source binding; not a new test execution",
           "selected_runs": {"csharp": args.dotnet_run, "typescript": args.ts_run,
                             "typecheck": args.typecheck_run, "design": args.design_run},
           "input_check_scope": "Inputs checked per actual command: C# compilation/native data, TS/config/native data, frontend typecheck, or design audit. Other language snapshots and fixture generators are observed context, not implicit test dependencies.",
           "evidence_reader_note": "The initial reader exited 1 on an escaped xUnit display ID, without writing a receipt. Reader corrected to parse each full item JSON and compare it with the complete expected case. No test rerun or test-result modification.",
           "design_fixture_subassertions": design_result,
           "counts": {"csharp": counters, "typescript": {key: report[key] for key in
                      ["numTotalTests", "numPassedTests", "numFailedTests", "numPendingTests"]}},
           "fixed_vector_count_per_language": len(fixed_ids), "private_cases_per_language": len(fixture_ids),
           "selected_design_cases_per_language": sum(row["kind"] == "SELECTED_CURRENT_DESIGN_PROJECTION" for row in coverage),
           "constructed_cases_per_language": sum(row["kind"] == "CONSTRUCTED_CONTRACT_UNIT_INPUT" for row in coverage),
           "coverage": coverage, "artifacts": artifacts, "source_binding": source_binding,
           "history": history, "shared_inputs_and_existing_owner_unchanged": True,
           "whole_business_owner_database_http_acceptance": "NOT_RUN", "merge_and_state_owner": "COORD"}
assert not args.out.exists(), "Never overwrite evidence"
args.out.write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
print(json.dumps({"result": "PASS", "csharp": counters["passed"], "typescript": report["numPassedTests"],
                  "fixed_vectors_each": len(fixed_ids), "out": str(args.out)}))
