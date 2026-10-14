"""Build bounded WP01-01 preparation evidence from exact Git/raw source bytes.

No source program, Office formulas, business services or database are executed.
Only the fixed WP01-01 preparation directory is writable. Never changes adoption,
roadmap, manager or original assets. Full source text stays normative; extracted
matrix dimensions are review navigation, not new contract terms or execution.
"""
import argparse
import collections
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

sys.dont_write_bytecode = True
BASELINE = "1ab2d031e3c501662ecb89a33839fac1cba55d33"
BUNDLE = "docs/development/task-deliveries/WP05-01/"
SUPPLEMENT = BUNDLE + "supplements/review-r1/"
OUT = "docs/development/evidence/WP01-01/preparation/"
BASE_HASH = "979cb7f4fc779f6200e16b2919a89afc244681d122683036378e068931fcd3ee"
TARGETS = {"PLM-ITER-01", "PLM-REL-01"}
BASE_FILES = {"README.md", "sources.json", "cases.json", "normative-coverage.json", "coverage.md", "adoption.json"}


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def parse(raw):
    def unique(pairs):
        result = {}
        for k, v in pairs:
            if k in result:
                raise ValueError("duplicate JSON key: " + k)
            result[k] = v
        return result
    def bad_constant(value):
        raise ValueError("non-JSON number: " + value)
    return json.loads(raw.decode("utf-8-sig"), object_pairs_hook=unique, parse_constant=bad_constant)


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8")


def pointer(document, path):
    if path and not path.startswith("/"):
        raise ValueError("invalid pointer")
    for key in path.split("/")[1:]:
        if re.search(r"~(?![01])", key):
            raise ValueError("invalid pointer escape")
        key = key.replace("~1", "/").replace("~0", "~")
        document = document[int(key)] if isinstance(document, list) else document[key]
    return document


def git_bytes(root, revision, path):
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise ValueError("full commit required")
    return subprocess.check_output(["git", "-c", "core.longpaths=true", "--no-optional-locks", "cat-file", "blob", revision + ":" + path], cwd=root)


def workbook(raw):
    ns = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
    def string(node):
        if node is None:
            return ""
        return "".join(n.text or "" for n in node.findall("s:t", ns) + node.findall("s:r/s:t", ns))
    with zipfile.ZipFile(io.BytesIO(raw)) as z:
        strings = [string(n) for n in ET.fromstring(z.read("xl/sharedStrings.xml"))] if "xl/sharedStrings.xml" in z.namelist() else []
        rels = {n.attrib["Id"]: n.attrib["Target"] for n in ET.fromstring(z.read("xl/_rels/workbook.xml.rels"))}
        result = {}
        for sheet in ET.fromstring(z.read("xl/workbook.xml")).findall("s:sheets/s:sheet", ns):
            path = rels[sheet.attrib["{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id"]]
            path = path.lstrip("/") if path.startswith("/") else "xl/" + path
            cells = {}
            for c in ET.fromstring(z.read(path)).findall(".//s:c", ns):
                v, f = c.find("s:v", ns), c.find("s:f", ns)
                if c.get("t") == "inlineStr":
                    value = string(c.find("s:is", ns))
                elif c.get("t") == "s" and v is not None:
                    value = strings[int(v.text)]
                elif f is not None:
                    value = "=" + (f.text or "")
                else:
                    value = v.text if v is not None else ""
                cells[c.attrib["r"]] = value
            result[sheet.attrib["name"]] = cells
        return result


def source_references(obj, at=""):
    if isinstance(obj, dict):
        if "path" in obj and "sha256" in obj:
            yield at, obj
        elif "source_path" in obj and "source_sha256" in obj:
            yield at, {"path": obj["source_path"], "sha256": obj["source_sha256"], "line_start": obj.get("source_line"), "anchored_text": obj.get("observation", obj.get("assertion"))}
        for k, v in obj.items():
            yield from source_references(v, at + "/" + k)
    elif isinstance(obj, list):
        for i, v in enumerate(obj):
            yield from source_references(v, at + "/" + str(i))


def paragraphs(text):
    return [p.strip() for p in re.split(r"\n\s*\n", text) if p.strip() and not p.lstrip().startswith("<!-- SOURCE_END")]


def body_dimensions(row):
    """Lossless per-source segmentation, with kind-specific observation channels."""
    text, kind, title = row["source_obligation"], row["kind"], row["title"]
    parts = paragraphs(text)
    owner = row["applicability"]
    if kind == "REQ":
        owner = parts[0] + "；" + owner
        operation, facts = parts[1], parts[2:]
        obj = title
    elif kind == "BR":
        obj, operation, facts = title, "触发并尝试违反此规则：" + parts[0], parts
    elif kind == "FLD":
        obj = re.search(r"绑定：`([^`]+)`", text).group(1)
        operation = "在" + parts[0] + "；按以下字段控制读写：" + parts[1]
        facts = ["逐值回读 " + obj + "；逐项核对：" + p for p in parts]
        facts += ["只读注入/越权拒绝；合法null/省略保留；类型建议未批准时不得作为存储上限。"]
    elif kind == "PROC":
        obj, operation = title, parts[0] + "；" + next(p for p in parts if p.startswith("输入："))
        owner += "；" + next(p for p in parts if p.startswith("授权："))
        facts = [p for p in parts if not p.startswith("输入：")]
    elif kind == "LOGICAL_DATA":
        obj = title.strip("`")
        operation = "在已批准PostgreSQL表示中写入、原样回读、历史读取及违反下列约束：" + text
        facts = [text, "合法域/精度/外域Owner身份无损；可空、关联、唯一性、Tenant和CAS按本字段实际约束核对，未定物理表示继续阻塞。"]
    elif kind == "OPEN_DECISION":
        obj, operation, facts = title, "取得此项准确后继决定并逐点比对：" + parts[0], parts
    elif kind == "CURRENT_ACTION":
        obj, operation = title, "按此具名动作的全部输入/前置执行正反及恢复路径：" + parts[0]
        owner = parts[0] + "；" + owner
        facts = parts
    elif kind == "CURRENT_PAGE_FIELDS":
        obj, operation = title, "逐行查询/受控动作/撤权后重读；每个字段保原Owner和读写条件。"
        facts = [line.strip() for line in text.splitlines() if line.strip().startswith("| `PG01-FLD-")]
    else:
        raise ValueError("unhandled kind " + kind)
    return obj, operation, facts, owner


def matrix_rows(cases, body, supplement):
    rows = []
    for row in cases:
        assertion = row["source_assertion"]
        if isinstance(assertion, str):
            ps = paragraphs(assertion)
            title = ps[0].split("｜", 1)[-1]
            context = next((p for p in ps if p.startswith(("前置：", "Given：", "Given:"))), "")
            title += ("；" + context) if context else ""
            operation = next((p for p in ps if p.startswith(("动作：", "When：", "When:", "操作："))), row["observation"])
            facts = [p for p in ps if p.startswith(("预期：", "Then：", "Then:", "期待："))] or [row["observation"]]
        else:
            title = assertion["scenario"]
            operation = assertion.get("input", title)
            facts = [assertion["expected"]] + ([assertion["expected_writes"]] if "expected_writes" in assertion else [])
        rows.append({"id": row["id"], "target": row["target"], "kind": row["kind"], "object": title, "operation": operation, "observations": facts, "owner": row["applicability"], "owner_authority": row.get("assignment_authority"), "source_refs": [row[k] for k in ("catalog_ref", "source_ref") if k in row], "source_assertion": assertion, "overlays": row.get("overlays", []), "runtime_state": "NOT_RUN", "coverage_state": "SOURCE_ASSERTION_PRESERVED_NOT_EXECUTED"})
    for row in body:
        obj, op, observations, owner = body_dimensions(row)
        rows.append({"id": row["id"], "target": row["target"], "kind": row["kind"], "object": obj, "operation": op, "observations": observations, "owner": owner, "source_refs": [row["source_ref"]], "source_assertion": row["source_obligation"], "supporting_ids": row.get("supporting_case_ids", []) + row.get("supporting_obligation_ids", []), "overlays": row.get("overlays", []), "runtime_state": "NOT_RUN", "coverage_state": "UNRESOLVED" if row["kind"] == "OPEN_DECISION" else "SOURCE_ASSERTION_PRESERVED_NOT_EXECUTED"})
    for row in supplement:
        rows.append({"id": row["id"], "target": row["target"], "kind": "SUPPLEMENT_MAPPING", "object": row["title"], "operation": row["scenario"], "observations": [row["required_observation"]], "owner": row["owner_scope"], "source_refs": row["source_refs"], "source_assertion": row["required_observation"], "supporting_ids": row["prior_basis_ids"], "revision_action": row["revision_action"], "runtime_state": "NOT_RUN", "coverage_state": "SOURCE_MAPPING_NOT_NEW_ORIGINAL_AC"})
    return rows


def build(root, archive_root):
    errors, checks, inputs, refs = [], [], [], []
    def check(ok, label):
        (checks if ok else errors).append(label)
    def pinned(path, revision=BASELINE, role="BASIS_ARTIFACT"):
        raw = git_bytes(root, revision, path)
        inputs.append({"role": role, "path": path, "git_revision": revision, "representation": "GIT_BLOB_BYTES", "sha256": sha(raw), "bytes": len(raw)})
        return raw
    combined_raw = pinned(SUPPLEMENT + "manifest.json")
    combined = parse(combined_raw)
    base_raw = pinned(BUNDLE + "basis-manifest.json")
    base = parse(base_raw)
    check(sha(base_raw) == BASE_HASH == combined["base_manifest"]["sha256"], "immutable base manifest")
    check({x["path"] for x in base["artifacts"]} == BASE_FILES and len(base["artifacts"]) == 6, "exact original six artifacts")
    docs = {}
    for prefix, manifest in [(BUNDLE, base), (SUPPLEMENT, combined)]:
        for item in manifest["artifacts"]:
            raw = pinned(prefix + item["path"])
            check(sha(raw) == item["sha256"], "artifact byte hash: " + prefix + item["path"])
            if item["path"].endswith(".json"):
                docs[item["path"]] = parse(raw)
    registry = docs["sources.json"]["sources"]
    raw_sources = {}
    source_map = {x["path"]: x for x in registry}
    check(len(source_map) == len(registry) == 39, "all 39 registered inputs unique")
    for source in registry:
        path = source["path"]
        if source["representation"] == "GIT_BLOB_BYTES":
            raw = pinned(path, source["git_revision"], "REGISTERED_INPUT")
        elif source["representation"] == "RAW_FILE_BYTES":
            resolved = (archive_root / path).resolve()
            if not resolved.is_relative_to(archive_root.resolve()):
                raise ValueError("archive path escapes root")
            raw = resolved.read_bytes()
            inputs.append({"role": "REGISTERED_INPUT", "path": path, "git_revision": None, "representation": "RAW_FILE_BYTES", "sha256": sha(raw), "bytes": len(raw)})
        else:
            raise ValueError("unsupported source representation")
        check(sha(raw) == source["sha256"], "registered byte hash: " + path)
        raw_sources[path] = raw
    books = {}
    for name in ("cases.json", "normative-coverage.json", "wp05-supplement-proposed.json"):
        for location, ref in source_references(docs[name]):
            path = ref["path"]
            raw = raw_sources[path]
            before = len(errors)
            check(ref["sha256"] == sha(raw), name + location + ": source identity")
            if "git_revision" in ref:
                check(ref["git_revision"] == source_map[path]["git_revision"], name + location + ": git pin")
            if "json_pointer" in ref:
                pointer(parse(raw), ref["json_pointer"])
            if ref.get("line_start") is not None:
                lines = raw.decode("utf-8-sig").splitlines()
                start, end = ref["line_start"], ref.get("line_end", ref["line_start"])
                check(type(start) is int and 1 <= start <= end <= len(lines), name + location + ": valid line range")
                if "excerpt" in ref:
                    check(ref["excerpt"] == "\n".join(lines[start-1:end]), name + location + ": exact excerpt")
                if ref.get("anchored_text"):
                    check("\n".join(lines[start-1:]).startswith(ref["anchored_text"]), name + location + ": exact assignment/interpretation text at anchor")
            if "sheet" in ref:
                if path not in books:
                    books[path] = workbook(raw)
                check(len(books[path]) == 37, "37-sheet workbook")
                for cell, expected in ref["cells"].items():
                    check(books[path][ref["sheet"]].get(cell) == expected, name + location + ": " + ref["sheet"] + "!" + cell)
            refs.append({"artifact": name, "json_pointer": location, "path": path, "result": "PASS" if len(errors) == before else "FAIL", "locator": {k: v for k, v in ref.items() if k not in {"path", "sha256", "anchored_text", "excerpt", "cells"}}, "cells_checked": list(ref.get("cells", {}))})
    cases = docs["cases.json"]["cases"]
    body = docs["normative-coverage.json"]["obligations"]
    supplement = docs["wp05-supplement-proposed.json"]
    check(collections.Counter(c["kind"] for c in cases) == {"RECOVERED_SCENARIO": 221, "R1_REQUIRED_SCENARIO": 8, "R1_DETAIL_VECTOR": 10}, "separate case counts 221 / 8 / 10")
    check(len(body) == 295 and len(supplement["rows"]) == 43, "295 body / 43 supplemental mappings")
    check(supplement["status"] == "PROPOSED" and supplement["runtime_state"] == "NOT_RUN" and supplement["decision_record"] is None, "supplement not adopted/executed")
    check(combined["status"] == "PROPOSED" and combined["decision_record"] is None and combined["gate_implementation"] == "NOT_CHANGED", "combined boundary")
    check({r["id"] for r in supplement["rows"]} == {f"WP05-01-REVIEW/{k}-{n:02}" for k, count in [("WB", 18), ("CP22", 15), ("R1", 10)] for n in range(1, count+1)}, "exact supplement identities")
    check(collections.Counter(r["revision_action"] for r in supplement["rows"]) == supplement["summary"], "supplement reuse/addition separate")
    catalog = parse(raw_sources["docs/development/assessments/20261011/catalogs/engineering-oa.json"])
    r1 = parse(raw_sources["docs/design-handbook/remediation/08-acceptance-catalog.json"])
    detail = parse(raw_sources["docs/design-handbook/remediation/assets/shared-consumers/acceptance.json"])
    check({(r["target"], r["source_case_id"]) for r in cases if r["kind"] == "RECOVERED_SCENARIO"} == {(t["id"], c["source_case_id"]) for t in catalog["targets"] if t["id"] in TARGETS for c in t["cases"]}, "exact recovered source membership")
    check({(r["target"], r["source_case_id"]) for r in cases if r["kind"] == "R1_REQUIRED_SCENARIO"} == {(t, c["id"]) for c in r1["cases"] for t in c["consumer_targets"] if t in TARGETS}, "exact target R1 membership")
    check({r["source_case_id"] for r in cases if r["kind"] == "R1_DETAIL_VECTOR"} == {c["id"] for c in detail["cases"] if c["id"].startswith(("OA-", "ENG-"))}, "exact detail membership")
    for row in cases:
        ref = row["catalog_ref"]
        original = pointer(parse(raw_sources[ref["path"]]), ref["json_pointer"])
        if row["kind"] == "RECOVERED_SCENARIO":
            check(row["source_assertion"] == original["assertion"] and row["source_case_id"] == original["source_case_id"] and row["source_aliases"] == original.get("source_aliases", []) and row["assignment_authority"] == original.get("assignment_authority"), row["id"] + ": exact source assertion/alias/Owner")
            ref = row["source_ref"]
            text = raw_sources[ref["path"]].decode("utf-8-sig").replace("\r\n", "\n")
            check(ref["line_start"] == original["source_line"] and "\n".join(text.splitlines()[ref["line_start"]-1:]).startswith(row["source_assertion"].rstrip("\n")), row["id"] + ": assertion at exact raw anchor")
        else:
            check(row["source_assertion"] == original, row["id"] + ": exact R1/detail body")
        check(row["runtime_state"] == "NOT_RUN" and not row["execution_evidence"] and row["legacy_30_48_equivalence"] == "UNPROVABLE", row["id"] + ": unexecuted / no legacy equivalence")
    check(docs["cases.json"]["interpretation_overlays"] == catalog["current_interpretation_overlays"], "PG05 interpretation preserved")
    expected_body = set()
    for path, raw in raw_sources.items():
        if path.endswith("__02_ITER_当前阅读主册.md"):
            text = raw.decode("utf-8-sig")
            expected_body.update("WP05-01/1/BODY/" + x for x in re.findall(r"^### ((?:REQ|BR|FLD|PROC)-PLM-ITER-01-\d{3}|DP-ITER-\d{2}) ", text, re.M))
            expected_body.update("WP05-01/1/BODY/" + x for x in re.findall(r"^- (DATA-PLM-ITER-01-\d{3}) ", text, re.M))
        if path.endswith("__05_REL_当前阅读主册.md"):
            text = raw.decode("utf-8-sig")
            expected_body.update("WP05-01/1/BODY/" + x for x in re.findall(r"^#### (PG01-ACT-R\d{2}-\d{2})｜", text, re.M))
            expected_body.update(f"WP05-01/1/BODY/REL-PG01-{i:02}-FIELDS" for i in range(1, len(re.findall(r"^### 字段／信息项目录", text, re.M))+1))
    check({r["id"] for r in body} == expected_body, "exact body membership from original headings")
    known_ids = {r["id"] for r in cases + body}
    for row in body:
        ref = row["source_ref"]
        lines = raw_sources[ref["path"]].decode("utf-8-sig").splitlines()
        check(row["source_obligation"] in "\n".join(lines[ref["line_start"]-1:ref["line_end"]]), row["id"] + ": exact obligation in source range")
        check(set(row["supporting_case_ids"] + row.get("supporting_obligation_ids", [])) <= known_ids, row["id"] + ": existing supports")
        check(row["runtime_state"] == "NOT_RUN" and not row["execution_evidence"], row["id"] + ": body unexecuted")
    for row in supplement["rows"]:
        check(set(row["prior_basis_ids"]) <= known_ids and row["runtime_state"] == "NOT_RUN" and row["approval_state"] == "PROPOSED", row["id"] + ": known prior IDs and no runtime/approval")
    adoption = docs["adoption.json"]
    check(adoption["status"] == "PROPOSED" and adoption["decision_record"] is None and not adoption["approval_evidence"] and adoption["gate_implementation"] == "NOT_CHANGED", "base adoption remains proposed")
    for gap in adoption["legacy_gaps"]:
        check(gap["historical_inventory_state"] == "NOT_INVENTORIED" and gap["recovery_state"] == "UNRECOVERABLE" and gap["equivalence"] == "UNPROVABLE" and gap["legacy_case_ids"] is None and gap["missing_original_sha256"] is None, gap["id"] + ": historical boundary")
    current_paths = ["AGENTS.md", "docs/development/roadmap.json", "docs/development/tools/manage_roadmap.py", "docs/development/task-plans/WP01.md", "docs/development/task-plans/WP05.md", "docs/development/dispatch/20261013-wave6/WP01-01.md", "docs/development/evidence/wave6-dispatch-20261013/admission-plm.json"]
    current = {path: pinned(path, BASELINE, "CURRENT_CONTEXT") for path in current_paths}
    drift = []
    for source in registry:
        if source["representation"] == "GIT_BLOB_BYTES":
            raw = git_bytes(root, BASELINE, source["path"])
            drift.append({"path": source["path"], "registered_sha256": source["sha256"], "current_sha256": sha(raw), "changed": sha(raw) != source["sha256"]})
    rows = matrix_rows(cases, body, supplement["rows"])
    check(len(rows) == len({r["id"] for r in rows}) == 577, "577 distinct mapping rows, NOT unique business obligations")
    check(all(r["object"] and r["operation"] and r["observations"] and r["owner"] and r["source_refs"] for r in rows), "all matrix dimensions populated")
    matrix = {"scope": "PREPARATION_ONLY", "basis_version": combined["version"], "combined_manifest_sha256": sha(combined_raw), "counting_rule": "577 is matrix row count only; 221 recovered, 295 body locations, 43 source mappings, 8 R1, 10 detail are separate overlapping views, not unique obligations or legacy completeness.", "rows": rows}
    matrix_raw = encoded(matrix)
    roadmap = parse(current["docs/development/roadmap.json"])
    contract = {"scope": "PROPOSED_PREPARATION_MODEL_NOT_PRODUCTION_SCHEMA", "version": combined["version"], "pins": {"combined_manifest": sha(combined_raw), "base_manifest": sha(base_raw), "matrix": sha(matrix_raw)}, "historical_gaps": adoption["legacy_gaps"], "targets": {}}
    for target in sorted(TARGETS):
        tasks = [r["id"] for r in roadmap["implementation_tasks"] if target in r["target_ids"] and r["coverage_role"] == "IMPLEMENTATION"]
        contract["targets"][target] = {"obligations": [r["id"] for r in rows if r["target"] == target and r["kind"] != "OPEN_DECISION"], "decisions": [r["id"] for r in rows if r["target"] == target and r["kind"] == "OPEN_DECISION"], "r1": next(t["runtime_case_ids"] for t in roadmap["targets"] if t["id"] == target), "tasks": tasks, "repositories": sorted({r["repository"] for r in roadmap["implementation_tasks"] if r["id"] in tasks}), "cross_repository_scope_state": "UNRESOLVED", "baseline_inventory_not_dependency_approval": ["Main", "Platform", "CRM"], "cross_repository_scope": "Known implementation tasks establish Main only. Exact external provider/consumer repository closure must be reviewed from accepted contracts and actual task prerequisites before successor gate implementation. Baseline presence does not impose a universal CRM dependency. All applicable repository remote-main/package/contract/adoption receipts remain required; current unresolved closure blocks proposal conditions."}
    unsigned = {"schema": "WP01-01-PREPARATION-UNSIGNED-DECISION/1", "status": "UNSIGNED", "adoption_id": None, "actor": None, "time": None, "authority": None, "effective_at": None, "decision_text": None, "proposed_wording_not_signed": "以当前可知义务建立新标准，无法证明旧30/48逐项等价", "version": combined["version"], "targets": sorted(TARGETS), "pins": contract["pins"], "artifact_hashes": [x for x in inputs if x["role"] == "BASIS_ARTIFACT"], "historical_gaps": adoption["legacy_gaps"], "reviewed_coverage_conclusion": None, "unresolved_decisions": [r for r in rows if r["kind"] == "OPEN_DECISION"], "exceptions": None, "expiry_conditions": None, "approval_evidence": [], "actual_adoption": False, "gate_implementation": "NOT_CHANGED", "note": "Template is not a signature. Append a separately identified decision after review; never edit original WP05/adoption or claim assignment approves this version."}
    lock = {"scope": "PREPARATION_ONLY", "source_revision": BASELINE, "product_baseline": "9a0155dec1c3366df8a6631f3d8fdb086aa32fc8", "repository_baselines": {"Main": BASELINE, "Platform": "e5c63a28c94bae4cc66ec50fbe6079553c03d0d0", "CRM": "1e4eea61c31d7547c3b3495d392eefbed99b7339"}, "inputs": inputs, "historical_git_input_drift_to_current": drift}
    report = {"scope": "PREPARATION_STATIC_IDENTITY_LOCATORS_AND_ASSERTION_PRESERVATION", "result": "FAIL" if errors else "PASS", "source_revision": BASELINE, "checker_sha256": sha(Path(__file__).read_bytes()), "input_count": len(inputs), "registered_sources": 39, "reference_occurrences_checked": len(refs), "checks_passed": len(checks), "errors": errors, "separate_counts": {"recovered": dict(collections.Counter(r["target"] for r in cases if r["kind"] == "RECOVERED_SCENARIO")), "body": dict(collections.Counter(r["kind"] for r in body)), "supplement": dict(collections.Counter(r["id"].split("/")[1].split("-")[0] for r in supplement["rows"])), "r1": 8, "detail": 10, "matrix_rows_not_unique_obligations": len(rows)}, "actual_adoption": False, "business_runtime": "NOT_RUN", "references": refs, "limitations": ["No original30/48 equivalence proof; no original member IDs guessed.", "All registered source bytes and all structured references checked; quoted field locators are not Office rendering/formula recalculation or exhaustive semantic proof of every external document.", "No real Owner, API/UI/worker, database, deployment, provider or cross-repository runtime acceptance."]}
    output = root / OUT
    output.mkdir(parents=True, exist_ok=True)
    (output / "source-verification.json").write_bytes(encoded(report))
    if errors:
        print(json.dumps({"result": "FAIL", "errors": errors}, ensure_ascii=False, indent=2))
        return 1
    for name, value in [("input-lock.json", lock), ("obligation-matrix.json", matrix), ("gate-contract.proposed.json", contract), ("decision.UNSIGNED.json", unsigned)]:
        (output / name).write_bytes(encoded(value))
    headings = ["# WP01-01 逐 ID 复核矩阵", "", "PREPARATION_ONLY；所有业务 NOT_RUN。以下是577行定位导航，绝非577个独立义务/原AC。完整源断言、全部观察条件、Owner及准确引用在 obligation-matrix.json 同ID下。正文每个子条款和PG01字段行均须留证，不能仅执行摘要。", "", "| ID | 对象 | 操作 | 必须观察（全部条件见JSON） | Owner | 来源 |", "|---|---|---|---|---|---|"]
    def cell(value):
        return str(value).replace("|", "\\|").replace("\n", "<br>")
    for row in rows:
        source = row["source_refs"][-1]
        location = source.get("json_pointer", source.get("sheet", "L" + str(source.get("line_start", ""))))
        headings.append("| " + " | ".join(cell(v) for v in (row["id"], row["object"], row["operation"], "<br>".join(row["observations"]), row["owner"], source["path"] + " " + location)) + " |")
    (output / "obligation-matrix.md").write_bytes(("\n".join(headings) + "\n").encode("utf-8"))
    print(json.dumps({k: v for k, v in report.items() if k not in {"references", "limitations"}}, ensure_ascii=False, indent=2))
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--archive-root", type=Path, required=True)
    args = parser.parse_args()
    return build(args.root.resolve(), args.archive_root.resolve())


if __name__ == "__main__":
    sys.exit(main())
