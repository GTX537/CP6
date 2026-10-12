"""Read-only WP05-01 document integrity/coverage check. No business tests."""

import argparse
import collections
import datetime
import hashlib
import json
import pathlib
import re
import subprocess
import sys


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def parse(raw):
    return json.loads(raw.decode("utf-8-sig"), object_pairs_hook=unique_object)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def pointer(obj, value):
    for part in value.split("/")[1:]:
        part = part.replace("~1", "/").replace("~0", "~")
        obj = obj[int(part)] if isinstance(obj, list) else obj[part]
    return obj


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=pathlib.Path, default=pathlib.Path(__file__).resolve().parents[4])
    parser.add_argument("--archive-root", type=pathlib.Path, required=True)
    parser.add_argument("--bundle", type=pathlib.Path, default=pathlib.Path(__file__).resolve().parent)
    parser.add_argument("--out", type=pathlib.Path, required=True)
    args = parser.parse_args()
    errors, checks, source_bytes, json_sources = [], [], {}, {}

    def check(condition, label):
        (checks if condition else errors).append(label)

    def load(name):
        return parse((args.bundle / name).read_bytes())

    sources = load("sources.json")
    data = load("cases.json")
    body = load("normative-coverage.json")
    adoption = load("adoption.json")
    manifest = load("basis-manifest.json")
    expected_version = "CP6-WP05-01-ACCEPTANCE-BASIS/1.0.0-proposed"
    for obj in (sources, data, body, adoption, manifest):
        check(obj["version"] == expected_version, "artifact version: " + str(obj.get("version")))

    manifest_paths = {x["path"] for x in manifest["artifacts"]}
    check(manifest_paths == {"README.md", "sources.json", "cases.json", "normative-coverage.json", "coverage.md", "adoption.json"}, "exact normative artifact set")
    check(len(manifest_paths) == len(manifest["artifacts"]), "manifest paths unique")
    for item in manifest["artifacts"]:
        check(sha((args.bundle / item["path"]).read_bytes()) == item["sha256"], "artifact hash: " + item["path"])

    check(len({s["path"] for s in sources["sources"]}) == len(sources["sources"]), "source paths unique")
    for source in sources["sources"]:
        path = source["path"]
        if source["representation"] == "GIT_BLOB_BYTES":
            raw = subprocess.check_output(["git", "show", source["git_revision"] + ":" + path], cwd=args.root)
        else:
            raw = (args.archive_root / path).read_bytes()
        check(sha(raw) == source["sha256"], "source hash: " + path)
        source_bytes[path] = raw
        if path.endswith(".json"):
            json_sources[path] = parse(raw)

    def ref(ref):
        check(ref["path"] in source_bytes, "registered source: " + ref["path"])
        raw = source_bytes[ref["path"]]
        check(sha(raw) == ref["sha256"], "reference hash: " + ref["path"])
        return raw

    targets = {"PLM-ITER-01", "PLM-REL-01"}
    catalog_path = "docs/development/assessments/20261011/catalogs/engineering-oa.json"
    r1_path = "docs/design-handbook/remediation/08-acceptance-catalog.json"
    detail_path = "docs/design-handbook/remediation/assets/shared-consumers/acceptance.json"
    expected_recovered = {(t["id"], c["source_case_id"]) for t in json_sources[catalog_path]["targets"] if t["id"] in targets for c in t["cases"]}
    expected_r1 = {(t, c["id"]) for c in json_sources[r1_path]["cases"] for t in c["consumer_targets"] if t in targets}
    expected_detail = {c["id"] for c in json_sources[detail_path]["cases"] if c["id"].startswith(("OA-", "ENG-"))}
    actual_recovered, actual_r1, actual_detail = set(), set(), set()
    ids = {c["id"] for c in data["cases"]}
    check(len(ids) == len(data["cases"]), "new case IDs unique")
    for case in data["cases"]:
        check(case["runtime_state"] == "NOT_RUN" and not case["execution_evidence"], "runtime not claimed: " + case["id"])
        check(case["legacy_30_48_equivalence"] == "UNPROVABLE", "legacy equivalence not claimed: " + case["id"])
        check(bool(case["observation"] and case["applicability"]), "observable scope: " + case["id"])
        source_ref = case["catalog_ref"]
        ref(source_ref)
        original = pointer(json_sources[source_ref["path"]], source_ref["json_pointer"])
        if case["kind"] == "RECOVERED_SCENARIO":
            actual_recovered.add((case["target"], case["source_case_id"]))
            target_ref = source_ref["json_pointer"].split("/cases/")[0]
            check(pointer(json_sources[catalog_path], target_ref)["id"] == case["target"], "catalog target: " + case["id"])
            check(case["source_case_id"] == original["source_case_id"], "source case identity: " + case["id"])
            check(case["source_assertion"] == original["assertion"], "complete inherited assertion: " + case["id"])
            check(case["source_aliases"] == original.get("source_aliases", []), "aliases unchanged: " + case["id"])
            check(case["assignment_authority"] == original.get("assignment_authority"), "owner assignment evidence: " + case["id"])
            raw = ref(case["source_ref"]).decode("utf-8-sig").replace("\r\n", "\n")
            check(case["source_ref"]["line_start"] == original["source_line"], "original source line: " + case["id"])
            check(case["source_case_id"] in raw.splitlines()[original["source_line"] - 1], "original line anchor: " + case["id"])
            check(case["source_assertion"] in raw, "assertion exists in original bytes: " + case["id"])
        else:
            check(case["source_assertion"] == original, "R1/detail source unchanged: " + case["id"])
            if case["kind"] == "R1_REQUIRED_SCENARIO":
                actual_r1.add((case["target"], case["source_case_id"]))
            elif case["kind"] == "R1_DETAIL_VECTOR":
                actual_detail.add(case["source_case_id"])
            else:
                check(False, "unknown case kind: " + case["id"])
    check(actual_recovered == expected_recovered and len(actual_recovered) == 221, "all 221 target-qualified recovered scenarios")
    check(actual_r1 == expected_r1 and len(actual_r1) == 8, "all 8 target R1 scenarios")
    check(actual_detail == expected_detail and len(actual_detail) == 10, "all 10 OA/ENG detail vectors")
    check(data["interpretation_overlays"] == json_sources[catalog_path]["current_interpretation_overlays"], "current PG05 interpretation unchanged")
    summary = dict(collections.Counter(c["target"] + "::" + c["kind"] for c in data["cases"]))
    check(summary == data["summary"], "case summary matches actual members")

    body_ids = {r["id"] for r in body["obligations"]}
    check(len(body_ids) == len(body["obligations"]), "body IDs unique")
    expected_body = set()
    iter_path = next(p for p in source_bytes if p.endswith("__02_ITER_当前阅读主册.md"))
    rel_path = next(p for p in source_bytes if p.endswith("__05_REL_当前阅读主册.md"))
    iter_text = source_bytes[iter_path].decode("utf-8-sig")
    rel_text = source_bytes[rel_path].decode("utf-8-sig")
    for key in re.findall(r"^### ((?:REQ|BR|FLD|PROC)-PLM-ITER-01-\d{3}|DP-ITER-\d{2}) ", iter_text, re.M):
        expected_body.add("WP05-01/1/BODY/" + key)
    for key in re.findall(r"^- (DATA-PLM-ITER-01-\d{3}) ", iter_text, re.M):
        expected_body.add("WP05-01/1/BODY/" + key)
    for key in re.findall(r"^#### (PG01-ACT-R\d{2}-\d{2})｜", rel_text, re.M):
        expected_body.add("WP05-01/1/BODY/" + key)
    pages = re.findall(r"^### 字段／信息项目录", rel_text, re.M)
    expected_body.update(f"WP05-01/1/BODY/REL-PG01-{i:02}-FIELDS" for i in range(1, len(pages) + 1))
    check(body_ids == expected_body and len(body_ids) == 295, "all 295 current body locations, including logical fields and open decisions")
    for row in body["obligations"]:
        raw = ref(row["source_ref"]).decode("utf-8-sig").splitlines()
        start, end = row["source_ref"]["line_start"], row["source_ref"]["line_end"]
        excerpt = "\n".join(raw[start - 1:end])
        check(bool(row["source_obligation"]) and row["source_obligation"] in excerpt, "body statement in exact range: " + row["id"])
        check(bool(row["required_observation"] and row["applicability"]), "body observation/scope: " + row["id"])
        check(set(row["supporting_case_ids"]) <= ids, "body case references: " + row["id"])
        check(set(row.get("supporting_obligation_ids", [])) <= body_ids, "body obligation references: " + row["id"])
        check(row["runtime_state"] == "NOT_RUN" and not row["execution_evidence"], "body runtime not claimed: " + row["id"])
    check(dict(collections.Counter(r["target"] + "::" + r["kind"] for r in body["obligations"])) == body["summary"], "body summary matches members")
    check(adoption["status"] == "PROPOSED" and adoption["decision_record"] is None and not adoption["approval_evidence"], "no fabricated adoption")
    check(adoption["gate_implementation"] == "NOT_CHANGED", "existing gate not replaced")
    check([(g["target"], g["declared_legacy_count"]) for g in adoption["legacy_gaps"]] == [("PLM-ITER-01", 30), ("PLM-REL-01", 48)], "two exact historical gap declarations")
    for gap in adoption["legacy_gaps"]:
        check(gap["legacy_case_ids"] is None and gap["missing_original_sha256"] is None and gap["equivalence"] == "UNPROVABLE" and gap["historical_inventory_state"] == "NOT_INVENTORIED", "no fabricated old inventory: " + gap["id"])
    for name in ("README.md", "coverage.md", "delivery.md"):
        text = (args.bundle / name).read_text(encoding="utf-8")
        for target in re.findall(r"\]\(([^)]+)\)", text):
            if re.match(r"https?://", target):
                continue
            path = (args.bundle / target.split("#")[0]).resolve()
            check(path.is_file(), f"local link: {name} -> {target}")
    report = {
        "scope": "STATIC_DOCUMENT_INTEGRITY_AND_COVERAGE_ONLY",
        "result": "PASS" if not errors else "FAIL",
        "checked_at_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "source_main_sha": sources["source_main_sha"],
        "basis_manifest_sha256": sha((args.bundle / "basis-manifest.json").read_bytes()),
        "checker_sha256": sha(pathlib.Path(__file__).read_bytes()),
        "source_count": len(sources["sources"]), "case_summary": summary,
        "body_locations": len(body_ids), "passed_checks": len(checks), "errors": errors,
        "runtime_result": "NOT_RUN", "adoption": "PROPOSED",
        "limitations": ["No business/database/UI/Office visual tests or runtime provider adoption.", "Does not certify legal authority, semantic completeness of missing originals, approval, task entry or remote-main delivery.", "Repository source pins read exact historical Git blobs; final diff and later main changes require a separate task review."]
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_bytes((json.dumps(report, ensure_ascii=False, indent=2) + "\n").encode("utf-8"))
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
