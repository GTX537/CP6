"""Verify X4 metadata identity, exact text reuse, and package membership; execute no source code."""
import argparse
import collections
import hashlib
import json
from pathlib import Path
import zipfile


def leaves(value, pointer=""):
    if isinstance(value, dict):
        if not value:
            yield pointer, {}
        for key, child in value.items():
            yield from leaves(child, pointer + "/" + key)
    elif isinstance(value, list):
        if not value:
            yield pointer, []
        for index, child in enumerate(value):
            yield from leaves(child, pointer + "/" + str(index))
    else:
        yield pointer, value


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--handbook", required=True, type=Path)
    args = parser.parse_args()
    handbook = args.handbook
    reading = json.loads((handbook / "evidence/root-specialty-reading.json").read_text(encoding="utf-8"))["files"]
    known = json.loads((handbook / "indexes/materialized-content-paths.json").read_text(encoding="utf-8"))
    targets = ["WMS-TOOL-01", "WMS-INK-01", "WMS-VMI-01", "WMS-SAMPLE-01", "WMS-AUTO-01", "WMS-ANALYTICS-01"]
    errors = []

    def get(prefix):
        return next(item for item in reading if item["sha256"].startswith(prefix))

    def body(item):
        return json.loads(Path(item["source_path"]).read_text(encoding="utf-8-sig"))

    def check(condition, message):
        if not condition:
            errors.append(message)

    scopes = body(get("996ff8c6"))
    criteria = body(get("e694f81b"))
    index = body(get("be2f000f"))
    members = body(get("72a6dbfa"))
    result = {"schema": "CP6-X4-LOCAL-METADATA-RECONCILIATION-1", "business_tests_executed": False,
              "interpretation": "Structural equality reuses already-read exact source text. Package hashing is byte identity, not semantic audit of all source code or dependencies.",
              "targets": [], "metadata_differential": [], "package_members": [], "errors": errors}
    for target in targets:
        current_item = next(item for item in reading if item["targets"] == [target] and "acceptance_entry_provenance_not_body" in item["roles"])
        ua_item = next(item for item in reading if item["targets"] == [target] and "required_acceptance_and_decision" in item["roles"])
        current, ua = body(current_item), body(ua_item)
        normative_sha = current["Normative"]["Sha256"]
        raw = Path(known[normative_sha]).read_bytes()
        check(hashlib.sha256(raw).hexdigest() == normative_sha, target + " normative hash")
        check(len(raw) == current["Normative"]["Bytes"], target + " normative bytes")
        check(ua["Normative"]["Sha256"] == normative_sha, target + " UA selector")
        check(current["Acceptance"]["Sha256"] == ua_item["sha256"], target + " CURRENT UA selector")
        scope = scopes[int(ua["OriginalScopeJsonPointer"].lstrip("/"))]
        check(scope == current["OriginalScope"], target + " exact original scope")
        criterion = next(item for item in criteria["Targets"] if item["Target"] == target)
        check(criterion["Normative"] == ua["Normative"], target + " criteria selector")
        for field in ("Status", "ExecutedAcCount", "DirectR2AndR2_1Propagation"):
            check(criterion[field] == criteria["Targets"][0][field], target + " criteria shared " + field)
        lines = raw.decode("utf-8-sig").splitlines()
        start, end = criterion["SectionStartLine"], criterion["SectionEndLine"]
        expected = "\n".join(lines[start - 1:end]) + "\n"
        section_equal = criterion["ExactTaskAndAcSectionText"] == expected
        check(section_equal, target + " exact AC/task section")
        rows_equal = all(item["ExactRow"] == lines[item["Line"] - 1] for item in criterion["DevelopmentTasks"])
        check(rows_equal, target + " exact task rows")
        check(criterion["OriginalAcIds"] == ua["OriginalAcIds"], target + " AC IDs")
        check([item["TaskId"] for item in criterion["DevelopmentTasks"]] == ua["DevelopmentTaskIds"], target + " task IDs")
        ix = next(item for item in index["Targets"] if item["Target"] == target)
        check(ix["Normative"] == ua["Normative"] and ix["Acceptance"]["Sha256"] == ua_item["sha256"], target + " index selectors")
        result["targets"].append({"target": target, "current_path": current_item["source_path"], "current_sha256": current_item["sha256"],
                                  "ua_path": ua_item["source_path"], "ua_sha256": ua_item["sha256"], "normative_sha256": normative_sha,
                                  "scope_equal_current": scope == current["OriginalScope"], "ac_section_equal_current": section_equal,
                                  "ac_section_lines": [start, end], "task_rows_equal_current": rows_equal,
                                  "ac_count": criterion["OriginalAcCount"], "task_count": criterion["TaskCount"]})
        for role in ("required_acceptance_and_decision", "acceptance_entry_provenance_not_body"):
            baseline = next(item for item in reading if item["targets"] == [targets[0]] and role in item["roles"])
            item = ua_item if role == "required_acceptance_and_decision" else current_item
            original, actual = dict(leaves(body(baseline))), dict(leaves(body(item)))
            result["metadata_differential"].append({"source_path": item["source_path"], "sha256": item["sha256"], "baseline_path": baseline["source_path"],
                "baseline_sha256": baseline["sha256"], "shared_equal_leaf_count": sum(k in original and original[k] == v for k, v in actual.items()),
                "total_leaf_count": len(actual), "changed_or_added": {k: v for k, v in actual.items() if k not in original or original[k] != v},
                "removed": sorted(original.keys() - actual.keys())})
    package = members["Package"]
    package_path = Path(known[package["Sha256"]])
    package_raw = package_path.read_bytes()
    check(len(package_raw) == package["Bytes"] and hashlib.sha256(package_raw).hexdigest() == package["Sha256"], "original X4 ZIP identity")
    with zipfile.ZipFile(package_path) as archive:
        names = [i.filename for i in archive.infolist() if not i.is_dir()]
        check(set(names) == {item["Member"] for item in members["Members"]}, "original X4 complete member set")
        check(len(names) == package["MemberCount"], "original X4 member count")
        for item in members["Members"]:
            name = item["Member"]
            raw = archive.read(name)
            ok = len(raw) == item["Bytes"] and hashlib.sha256(raw).hexdigest() == item["Sha256"]
            check(ok, "member identity " + name)
            if name.startswith("current/manuscripts/"):
                role = "current_body_fully_read"
            elif name.startswith("dependencies/"):
                role = "owner_dependency_cross_domain_reading_ledger_required"
            elif name.startswith(("sources/code/", "source-delta/")):
                role = "fixed_historical_code_reference_not_new_code_audit"
            elif name.startswith("history/"):
                role = "historical_superseded_or_review_basis"
            else:
                role = "scope_provenance_or_selection_metadata"
            result["package_members"].append(dict(item, identity_verified=ok, disposition=role, local_path=known.get(item["Sha256"])))
    result["package"] = dict(package, local_path=str(package_path))
    result["member_role_counts"] = dict(collections.Counter(item["disposition"] for item in result["package_members"]))
    result["validation_passed"] = not errors
    out = handbook / "evidence/x4-metadata-reconciliation.json"
    out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(out), "targets": len(result["targets"]), "member_count": len(result["package_members"]), "errors": errors}, ensure_ascii=False))
    raise SystemExit(bool(errors))


if __name__ == "__main__":
    main()
