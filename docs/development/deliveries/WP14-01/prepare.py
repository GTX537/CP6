"""WP14-01 private documentation snapshot; never executes or records business tests.

Python 3.10+. Default verifies; --write regenerates only this task's five CSVs and
input manifest from the fixed Git baseline. No ledger or shared views are edited.
"""
import argparse
import csv
import hashlib
import io
import json
import subprocess
from collections import Counter, defaultdict
from pathlib import Path

BASE = "df5553fd04ec4fc516602a4e2843650c38ae5006"
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
ROADMAP = "docs/development/roadmap.json"
DEFINITIONS = "docs/development/task-definitions.json"
CATALOG = "docs/design-handbook/remediation/08-acceptance-catalog.json"
ASSESSMENT = "docs/development/assessments/20261011/implementation-assessment.json"
CACHE = {}
PARSED = {}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def blob(path):
    if path not in CACHE:
        CACHE[path] = subprocess.check_output(
            ["git", "show", f"{BASE}:{path}"], cwd=ROOT)
    return CACHE[path]


def read(path):
    if path not in PARSED:
        PARSED[path] = json.loads(blob(path))
    return PARSED[path]


def ref(path, pointer=""):
    return path + "#" + pointer


def resolve(reference):
    path, pointer = reference.split("#", 1)
    obj = read(path)
    for part in pointer.strip("/").split("/") if pointer else []:
        key = part.replace("~1", "/").replace("~0", "~")
        obj = obj[int(key)] if isinstance(obj, list) else obj[key]
    return obj


def compact(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def csv_bytes(rows):
    out = io.StringIO(newline="")
    writer = csv.DictWriter(out, fieldnames=list(rows[0]), lineterminator="\n")
    writer.writeheader()
    writer.writerows(rows)
    return out.getvalue().encode("utf-8")


def source_key(case):
    # IDs such as 001 / AC01 recur in other sources. JSON pointers and workbook /
    # archive-member locators are part of identity, not optional title metadata.
    locator_fields = (
        "source_path", "source_sha256", "source_case_id", "source_line",
        "source_end_line", "source_json_pointer", "source_xml_member",
        "source_zip_member", "source_member_sha256", "source_zip_sha256",
        "source_cells", "source_location", "source_step_id", "source_ac_id",
        "source_supplement_id",
    )
    locator = {k: case[k] for k in locator_fields if k in case}
    return "SRC-" + digest(compact(locator).encode("utf-8")), locator


def build():
    ledger = read(ROADMAP)
    tasks = read(DEFINITIONS)["tasks"]
    catalog = read(CATALOG)
    read(ASSESSMENT)
    read("docs/development/assessments/20261011/original-acceptance-catalog.json")
    read("docs/development/assessments/20261011/acceptance-source-index.json")
    for path in ("AGENTS.md", "docs/development/08-postgresql-baseline.md",
                 "docs/development/09-parallel-development.md"):
        blob(path)
    targets = {t["id"]: t for t in ledger["targets"]}
    coverage = {t["target_id"]: t["implementation_task_ids"]
                for t in ledger["target_task_coverage"]}
    task_by_id = {t["id"]: t for t in tasks}
    revisions = {c["id"]: c for c in ledger["revision_acceptances"]}
    requirements = json.loads((HERE / "requirements.json").read_text(encoding="utf-8-sig"))
    profiles = requirements["data_profiles"]
    assert set(profiles) == {t["package_id"] for t in targets.values()}
    assert requirements["database_target"] == "PostgreSQL"
    assert requirements["business_execution_state"] == "NOT_RUN"

    def repositories(tid):
        return sorted({task_by_id[i]["repository"] for i in coverage[tid]})

    def owner(tid):
        return f"{targets[tid]['package_id']}/{tid}:Owner"

    def chain_refs(tids):
        packages = {targets[t]["package_id"] for t in tids}
        return ";".join(c["task_id"] for c in requirements["chains"]
                        if packages.intersection(c["participant_packages"]))

    target_rows, originals, runtime, historical, chains = [], [], [], [], []
    groups = defaultdict(list)
    for ti, t in enumerate(ledger["targets"]):
        tid, inv = t["id"], t["original_acceptance_inventory"]
        definition = ref(inv["source"], inv["definition_pointer"])
        full_target = resolve(definition)
        assert full_target["id"] == tid
        assert len(full_target["cases"]) == len(inv["cases"])
        blob(t["design_module"])
        target_rows.append({
            "target_id": tid, "name": t["name"], "owner_role": owner(tid),
            "executor_person": "UNASSIGNED", "repositories": ";".join(repositories(tid)),
            "implementation_tasks": ";".join(coverage[tid]),
            "inventory_state": inv["state"], "original_assignments": len(inv["cases"]),
            "complete_definition_ref": definition,
            "catalog_overlays_ref": ref(inv["source"]),
            "module_ref": t["design_module"],
            "revision_case_ids": ";".join(t["runtime_case_ids"]),
            "data_profile": t["package_id"], "chain_candidates": chain_refs([tid]),
            "failure_plan": "requirements.json#/fault_plans/F-SOURCE",
            "evidence_contract": "EVIDENCE.md#run-record",
            "remaining_scope": compact(inv["remaining_scope"]), "business_state": "NOT_RUN",
        })
        for ci, c in enumerate(inv["cases"]):
            cr = ref(c["definition_ref"]["path"], c["definition_ref"]["json_pointer"])
            actual = resolve(cr)
            assert actual["id"] == c["id"] and actual["kind"] == c["kind"]
            assert c["state"] == actual["state"] == "NOT_RUN"
            key, locator = source_key(actual)
            row = {
                "assignment_id": c["id"], "source_key": key,
                "target_id": tid, "source_case_id": c["source_case_id"], "kind": c["kind"],
                "owner_role": owner(tid), "data_profile": t["package_id"],
                "complete_definition_ref": cr,
                "target_expectations_ref": definition,
                "catalog_overlays_ref": ref(inv["source"]),
                "source_locator": compact(locator),
                "failure_plan": "F-SOURCE", "evidence_contract": "EVIDENCE.md#run-record",
                "business_state": "NOT_RUN",
            }
            originals.append(row)
            groups[key].append(row)
    # Count a shared source record once only after all its target-specific
    # expectations have evidence. Distinct revisions / locators never coalesce.
    for rows in groups.values():
        participants = sorted({r["target_id"] for r in rows})
        packages = {targets[t]["package_id"] for t in participants}
        coordinator = next(iter(packages)) if len(packages) == 1 else "WP14"
        for row in rows:
            row["coordinator_package"] = coordinator
            row["witness_targets"] = ";".join(participants)

    for ci, c in enumerate(catalog["cases"]):
        recorded = revisions[c["id"]]
        assert set(c["consumer_targets"]) == set(recorded["consumer_targets"])
        if recorded["kind"] == "HISTORICAL_STATIC":
            historical.append({"case_id": c["id"], "definition_ref": ref(CATALOG, f"/cases/{ci}"),
                               "historical_result": c["result"], "state": "REFERENCE_ONLY",
                               "runtime_count": 0})
            continue
        consumers = c["consumer_targets"]
        assert recorded["state"] == c["result"] == "NOT_RUN"
        runtime.append({
            "case_id": c["id"], "definition_ref": ref(CATALOG, f"/cases/{ci}"),
            "coordinator_package": recorded["coordinator_package"],
            "consumer_targets": ";".join(consumers),
            "execution_owners": ";".join(owner(t) for t in consumers),
            "executor_person": "UNASSIGNED",
            "repositories": ";".join(sorted({r for t in consumers for r in repositories(t)})),
            "data_profiles": ";".join(sorted({targets[t]["package_id"] for t in consumers})),
            "chain_candidates": chain_refs(consumers),
            "source_issue_id": c["issue_id"], "scenario": c["scenario"], "input": c["input"],
            "expected": c["expected"], "expected_writes": c["expected_writes"],
            "owner_design_refs": ";".join(targets[t]["design_module"] for t in consumers),
            "failure_plan": "F-SOURCE;EVIDENCE.md#fault-selection",
            "evidence_contract": "EVIDENCE.md#run-record", "business_state": "NOT_RUN",
        })
    for c in requirements["chains"]:
        task = task_by_id[c["task_id"]]
        chains.append({
            "task_id": c["task_id"], "title": task["title"],
            "task_definition_ref": ref(DEFINITIONS, f"/tasks/{tasks.index(task)}"),
            "dependencies": ";".join(task["dependencies"]),
            "card_targets": ";".join(task["target_ids"]),
            "participant_packages": ";".join(c["participant_packages"]),
            "source_facts": c["source_facts"], "owner_witnesses": c["owner_witnesses"],
            "exclusive_data": c["exclusive_data"], "fault_plans": ";".join(c["fault_plans"]),
            "input_gate": c["input_gate"], "business_state": "NOT_RUN",
        })
    metrics = {
        "targets": len(target_rows),
        "inventories_complete": sum(t["inventory_state"] == "COMPLETE" for t in target_rows),
        "original_assignments": len(originals), "unique_source_keys": len(groups),
        "original_kind_counts": dict(Counter(r["kind"] for r in originals)),
        "revision_runtime_cases": len(runtime),
        "revision_consumer_assignments": sum(len(c["consumer_targets"].split(";")) for c in runtime),
        "historical_reference_cases": len(historical), "chain_tasks": len(chains),
        "business_tests_executed": False,
    }
    assert (len(target_rows), metrics["inventories_complete"], len(originals), len(runtime), len(historical)) == (125, 123, 13404, 247, 41)
    assert {t["target_id"] for t in target_rows if t["inventory_state"] != "COMPLETE"} == {"PLM-ITER-01", "PLM-REL-01"}
    assert len({r["assignment_id"] for r in originals}) == len(originals)
    assert len({r["case_id"] for r in runtime}) == len(runtime)
    assert {r["case_id"] for r in runtime} == {x for t in target_rows for x in t["revision_case_ids"].split(";") if x}
    for t in target_rows:
        assert set(filter(None, t["revision_case_ids"].split(";"))) == {
            r["case_id"] for r in runtime if t["target_id"] in r["consumer_targets"].split(";")}
    manifest = {
        "task_id": "WP14-01", "preparation_version": "WP14-01-PREP/1.0",
        "main_baseline_sha": BASE, "scope": "Documentation snapshot; no business execution or delivery claim",
        "database_target": "PostgreSQL", "metrics": metrics,
        "sources": [{"path": p, "sha256": digest(b), "git_commit": BASE} for p, b in sorted(CACHE.items())],
        "source_policy": "Complete embedded catalog definitions are indexed; external archive bytes and current cross-repository runtime artifacts were not revalidated. Source records are candidate run groups, not a new accepted test denominator.",
    }
    return {
        "targets.csv": csv_bytes(target_rows), "original-assignments.csv": csv_bytes(originals),
        "revision-runtime.csv": csv_bytes(runtime), "historical-reference.csv": csv_bytes(historical),
        "chains.csv": csv_bytes(chains),
        "input-manifest.json": (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode("utf-8"),
    }, metrics


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    outputs, metrics = build()
    errors = []
    for name, content in outputs.items():
        path = HERE / name
        if args.write:
            path.write_bytes(content)
        if not path.is_file() or path.read_bytes() != content:
            errors.append(f"Generated documentation differs: {name}")
    print(json.dumps({"result": "FAIL" if errors else "PASS", "scope": "WP14-01 documentation mapping only",
                      "metrics": metrics, "errors": errors}, ensure_ascii=False))
    return bool(errors)


if __name__ == "__main__":
    raise SystemExit(main())
