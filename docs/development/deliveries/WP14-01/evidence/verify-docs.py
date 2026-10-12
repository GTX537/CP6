"""Independent, read-only WP14-01 document checks; Python 3.10+."""
import csv
import hashlib
import json
import re
import subprocess
from collections import defaultdict
from pathlib import Path

TASK = Path(__file__).resolve().parents[1]
ROOT = TASK.parents[3]
manifest = json.loads((TASK / "input-manifest.json").read_text(encoding="utf-8"))
baseline = manifest["main_baseline_sha"]
errors = []


def require(condition, message):
    if not condition:
        errors.append(message)


def rows(name):
    with (TASK / name).open(encoding="utf-8", newline="") as stream:
        return list(csv.DictReader(stream))


def business_rows_valid(records, identity):
    return (all(r["business_state"] == "NOT_RUN" for r in records)
            and len({r[identity] for r in records}) == len(records))


targets = rows("targets.csv")
originals = rows("original-assignments.csv")
runtime = rows("revision-runtime.csv")
historical = rows("historical-reference.csv")
require(business_rows_valid(targets, "target_id"), "Target state/identity drift")
require(business_rows_valid(originals, "assignment_id"), "Original state/identity drift")
require(business_rows_valid(runtime, "case_id"), "Revision state/identity drift")
require(all(r["state"] == "REFERENCE_ONLY" and r["runtime_count"] == "0" for r in historical),
        "Historical static evidence entered runtime count")
require(len({r["case_id"] for r in historical + runtime}) == 288, "Revision identity/count drift")

groups = defaultdict(list)
for r in originals:
    groups[r["source_key"]].append(r)
for key, members in groups.items():
    witnesses = ";".join(sorted({r["target_id"] for r in members}))
    require(all(r["witness_targets"] == witnesses for r in members), f"Missing witness: {key}")
require(sum(int(t["original_assignments"]) for t in targets) == len(originals) == 13404,
        "Original count does not reconcile")

# Negative controls are in-memory metadata checks. They never update a business
# result or claim that the application's runtime gates have executed.
injected_pass = [dict(r) for r in runtime]
injected_pass[0]["business_state"] = "PASS"
require(not business_rows_valid(injected_pass, "case_id"), "Static PASS was not rejected")
require(not business_rows_valid(runtime + [runtime[0]], "case_id"), "Duplicate case not rejected")
all_runtime_pass = [dict(r, business_state="PASS") for r in runtime]
full_scope_complete = (
    all(r["business_state"] == "PASS" for r in all_runtime_pass)
    and all(t["inventory_state"] == "COMPLETE" for t in targets)
    and all(r["business_state"] == "PASS" for r in originals)
)
require(not full_scope_complete, "247 runtime PASS incorrectly implies full acceptance")

link_count = 0
for doc in sorted(TASK.glob("*.md")):
    content = doc.read_text(encoding="utf-8-sig")
    for label, destination in re.findall(r"\[([^\]]+)\]\(([^)]+)\)", content):
        if "://" in destination:
            continue
        path, _, anchor = destination.partition("#")
        target = (doc.parent / path).resolve() if path else doc
        require(target.is_relative_to(ROOT) and target.is_file(), f"Broken local link: {doc.name}: {destination}")
        if anchor and target.is_file():
            text = target.read_text(encoding="utf-8-sig")
            require(f'id="{anchor}"' in text, f"Missing explicit anchor: {destination}")
        link_count += 1

source_drift = []
for s in manifest["sources"]:
    data = subprocess.check_output(["git", "show", f"{baseline}:{s['path']}"], cwd=ROOT)
    require(hashlib.sha256(data).hexdigest() == s["sha256"], f"Fixed source hash mismatch: {s['path']}")
    current = ROOT / s["path"]
    # Git blob comparison ignores checkout newline conversion. Current roadmap
    # status changes are not allowed to rewrite this preparation snapshot.
    current_blob = subprocess.check_output(["git", "hash-object", "--path=" + s["path"], str(current)], cwd=ROOT).decode().strip()
    base_blob = subprocess.check_output(["git", "rev-parse", f"{baseline}:{s['path']}"], cwd=ROOT).decode().strip()
    if current_blob != base_blob:
        source_drift.append(s["path"])
requirements = json.loads((TASK / "requirements.json").read_text(encoding="utf-8"))
for c in requirements["chains"]:
    require(set(c["participant_packages"]) <= set(requirements["data_profiles"]), "Unknown chain owner")
    require(set(c["fault_plans"]) <= set(requirements["fault_plans"]), "Unknown fault plan")

report = {
    "result": "FAIL" if errors else "PASS",
    "scope": "WP14-01 document coverage, identities, fixed inputs and local links only",
    "business_tests_executed": False,
    "source_baseline": baseline,
    "checks": {"local_links": link_count, "fixed_inputs": len(manifest["sources"]),
               "source_groups": len(groups), "runtime_consumer_assignments": sum(len(r["consumer_targets"].split(";")) for r in runtime),
               "in_memory_controls": ["reject_static_PASS", "reject_duplicate_runtime_identity", "preserve_123_complete_inventory_limit"]},
    "current_source_drift_requiring_adoption_review": source_drift,
    "errors": errors,
}
print(json.dumps(report, ensure_ascii=False, indent=2))
raise SystemExit(bool(errors))
