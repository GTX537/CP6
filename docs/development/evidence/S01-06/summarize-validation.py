"""Build a per-ID matrix from actual retained xUnit/Vitest results, not a fresh test run."""
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
OUT = Path(__file__).resolve().parent
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def load(name):
    return json.loads((OUT / name).read_text(encoding="utf-8"))


def write(name, value):
    (OUT / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


vectors_raw = (ROOT / "eng/contracts/s01/vectors.json").read_bytes()
assert hashlib.sha256(vectors_raw).hexdigest() == "b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01"
cases = [case for case in json.loads(vectors_raw)["cases"] if case["profile_id"] == "atp-stableid-r1"]
dispatch = json.loads((ROOT / "docs/development/dispatch/20261012-wave3/dispatch.json").read_bytes())
assigned = next(task for task in dispatch["tasks"] if task["id"] == "S01-06")["case_ids"]
assert len(cases) == 22 and sorted(assigned) == sorted(case["id"] for case in cases)

summary = {}
for stage, counts in [("red", (3, 57, 40, 41)), ("green", (60, 0, 81, 0))]:
    tree = ET.parse(OUT / f"{stage}-csharp.trx")
    counters = {key: int(value) for key, value in tree.find(".//t:Counters", NS).attrib.items()}
    ts = load(f"{stage}-ts.json")
    assert counters["total"] == counters["executed"] == 60
    assert counters["notExecuted"] == counters["pending"] == counters["notRunnable"] == 0
    assert ts["numTotalTests"] == 81 and ts["numPendingTests"] == 0
    assert (counters["passed"], counters["failed"], ts["numPassedTests"], ts["numFailedTests"]) == counts
    summary[stage] = {"csharp": {"total": 60, "passed": counts[0], "failed": counts[1], "skipped": 0,
                                "report": f"{stage}-csharp.trx", "command_exit_code": int(stage == "red")},
                      "typescript_and_schema": {"total": 81, "passed": counts[2], "failed": counts[3], "skipped": 0,
                                                "report": f"{stage}-ts.json", "command_exit_code": int(stage == "red")}}

tree = ET.parse(OUT / "green-csharp.trx")
csharp = {}
for result in tree.findall(".//t:UnitTestResult", NS):
    match = re.search(r'\.Fixed_vector\(id: "([^"]+)"', result.get("testName", ""))
    if match:
        assert match[1] not in csharp
        csharp[match[1]] = result.get("outcome")
typescript = {}
schema = {}
for result in load("green-ts.json")["testResults"][0]["assertionResults"]:
    match = re.search(r"'([^']+)' / (TypeScript lexical|Schema fragment assertions)$", result["fullName"])
    if match:
        selected = typescript if match[2] == "TypeScript lexical" else schema
        assert match[1] not in selected
        selected[match[1]] = result["status"]
assert set(csharp) == set(typescript) == set(schema) == set(assigned)
assert set(csharp.values()) == {"Passed"} and set(typescript.values()) == set(schema.values()) == {"passed"}
matrix = [{"id": case["id"], "profile_id": case["profile_id"], "operation": case["operation"],
           "input": case["input"], "expected_accepted": case["expected"]["accepted"],
           "csharp_assertions": csharp[case["id"]], "typescript_assertions": typescript[case["id"]],
           "schema_fragment_assertions": schema[case["id"]]} for case in cases]
write("case-matrix.json", {"vectors_sha256": hashlib.sha256(vectors_raw).hexdigest(),
                          "observation_basis": "Per-ID passing assertions in green-csharp.trx and green-ts.json against unchanged fixed expectations.",
                          "schema_scope": "Four extracted scalar assertions evaluated with ECMA-262 regexp and code-point length; not a general Schema engine or full ATP validation.",
                          "cases": matrix})
summary.update({"fixed_cases": 22, "fixed_assertions_per_endpoint": {"csharp": 22, "typescript": 22, "schema_fragment": 22},
                "type_check": {"command": "npm --prefix cp6.web run type-check", "exit_code": 0,
                               "evidence": "green-type-check.log", "observation_basis": "Actual local command exit recorded by execution tool; not inferred from an empty log."},
                "scope": "CONTRACT_UNIT scalar tests only. No SQL, migration, Owner adoption, remote Actions, full ATP wire/business acceptance, or production release."})
write("validation-summary.json", summary)
print("PASS: retained red/green counters verified; all 22 fixed IDs matched C#, TS and Schema assertions without missing, duplicate or skipped cases")
