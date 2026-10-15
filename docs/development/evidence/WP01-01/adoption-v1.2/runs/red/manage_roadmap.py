"""Validate the planning ledger and render views; never run or infer business tests."""
from __future__ import annotations

import argparse
import collections
import json
from pathlib import Path
import re
import subprocess
from urllib.parse import quote


STATES = {"PLANNED", "READY", "IN_PROGRESS", "VERIFYING", "BLOCKED", "DONE"}
ASSESSMENTS = {"PENDING", "REUSE_CONFIRMED", "CHANGE_REQUIRED", "NEW_REQUIRED", "DECISION_REQUIRED"}
LABELS = {"PLANNED": "待安排", "ASSESSING": "评估中", "READY": "可开工", "IN_PROGRESS": "开发中", "VERIFYING": "验收中", "BLOCKED": "受阻", "DONE": "已交付"}


def read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def unique(rows, key, errors, label):
    values = [row[key] for row in rows]
    if len(values) != len(set(values)):
        errors.append(f"{label}: duplicate {key}")
    return {row[key]: row for row in rows}


def evidence_ok(items, root: Path, *, execution=False):
    if not isinstance(items, list) or not items:
        return False
    for item in items:
        if not isinstance(item, dict):
            return False
        path = root / item.get("path", "")
        if not path.resolve().is_relative_to(root.resolve()) or not path.is_file():
            return False
        if not re.fullmatch(r"[0-9a-f]{40}", item.get("source_sha", "")):
            return False
        required = ("command", "environment", "observed_at", "scope") if execution else ("scope",)
        if any(not isinstance(item.get(k), str) or not item[k].strip() for k in required):
            return False
        if execution and item.get("result") != "PASS":
            return False
    return True


def validate(data, root: Path, *, check_pins=True):
    errors = []
    handbook = root / "docs/design-handbook/remediation"
    source_targets = {t["target_id"]: t for t in read_json(handbook / "10-target-coverage.json")["targets"]}
    source_cases = {c["id"]: c for c in read_json(handbook / "08-acceptance-catalog.json")["cases"]}
    source_issues = read_json(handbook / "00-disposition-register.json")
    packages = unique(data["work_packages"], "id", errors, "packages")
    targets = unique(data["targets"], "id", errors, "targets")
    cases = unique(data["revision_acceptances"], "id", errors, "cases")
    if set(packages) != {f"WP{i:02}" for i in range(1, 15)}:
        errors.append("package IDs must preserve the approved 14-package baseline")
    if set(targets) != set(source_targets):
        errors.append("target coverage differs from the authoritative design coverage")
    if set(cases) != set(source_cases):
        errors.append("acceptance coverage differs from the authoritative revision catalog")
    if errors:
        return errors
    required_sources = {f"docs/design-handbook/remediation/{name}" for name in ("10-target-coverage.json", "00-disposition-register.json", "08-acceptance-catalog.json")}
    if {s["path"] for s in data["sources"]} != required_sources or len(data["sources"]) != 3:
        errors.append("source pin set is incomplete or duplicated")
    if check_pins:
        for source in data["sources"]:
            proc = subprocess.run(["git", "-c", "core.longpaths=true", "hash-object", "--path=" + source["path"], source["path"]], cwd=root, capture_output=True, text=True)
            if proc.returncode or proc.stdout.strip() != source["git_blob"]:
                errors.append(f"source pin changed: {source['path']}; rebase the ledger explicitly")
    expected_totals = {"work_packages": 14, "targets": len(source_targets), "source_findings": source_issues["source_finding_count"], "issue_families": source_issues["issue_count"], "revision_runtime_cases": sum(c["verification_level"].startswith("REQUIRED_RUNTIME") for c in source_cases.values()), "historical_static_cases": sum(not c["verification_level"].startswith("REQUIRED_RUNTIME") for c in source_cases.values())}
    if data["baseline_totals"] != expected_totals:
        errors.append("baseline totals disagree with authoritative inputs")
    owners = collections.Counter(t for p in packages.values() for t in p["target_ids"])
    if set(owners) != set(targets) or any(n != 1 for n in owners.values()):
        errors.append("each target must have exactly one owning package")
    for p in packages.values():
        if p["state"] not in STATES | {"ASSESSING"}:
            errors.append(f"{p['id']}: unknown state")
        if set(p["integration_partners"]) - set(packages) or p["id"] in p["integration_partners"]:
            errors.append(f"{p['id']}: invalid integration partner")
        if p["state"] == "BLOCKED" and not p["blockers"]:
            errors.append(f"{p['id']}: BLOCKED needs a concrete blocker")
        if p["state"] == "DONE":
            owned = [targets[t] for t in p["target_ids"]]
            if owned and any(t["delivery_state"] != "DONE" for t in owned):
                errors.append(f"{p['id']}: package DONE with unfinished targets")
            if not evidence_ok(p["evidence"], root):
                errors.append(f"{p['id']}: DONE requires completion evidence")
            if p["id"] == "WP01" and any(t["assessment"] in {"PENDING", "DECISION_REQUIRED"} for t in targets.values()):
                errors.append("WP01: detailed assessment or decision remains open")
            if p["id"] == "WP01" and any(t["original_acceptance_inventory"]["state"] != "COMPLETE" for t in targets.values()):
                errors.append("WP01: original acceptance inventory remains open")
            if p["id"] == "WP14" and any(t["delivery_state"] != "DONE" for t in targets.values()):
                errors.append("WP14: full-baseline closeout requires all target deliveries")
    for c in cases.values():
        original = source_cases[c["id"]]
        runtime = original["verification_level"].startswith("REQUIRED_RUNTIME")
        participants = sorted({targets[t]["package_id"] for t in original["consumer_targets"]})
        coordinator = participants[0] if len(participants) == 1 else "WP14"
        if c["consumer_targets"] != original["consumer_targets"] or c["source_issue_id"] != original["issue_id"] or c["source_result"] != original["result"]:
            errors.append(f"{c['id']}: immutable source mapping changed")
        if c["participant_packages"] != participants or c["coordinator_package"] != coordinator:
            errors.append(f"{c['id']}: consumer/coordinator mapping differs")
        if c["kind"] != ("RUNTIME" if runtime else "HISTORICAL_STATIC"):
            errors.append(f"{c['id']}: static/runtime classification changed")
        if not runtime:
            if c["state"] != "REFERENCE_ONLY" or c["execution_evidence"]:
                errors.append(f"{c['id']}: historical static check cannot become new runtime execution")
        elif c["state"] not in {"NOT_RUN", "PASS", "FAIL", "BLOCKED"}:
            errors.append(f"{c['id']}: unknown runtime state")
        elif c["state"] == "PASS" and not evidence_ok(c["execution_evidence"], root, execution=True):
            errors.append(f"{c['id']}: runtime PASS lacks execution evidence")
        elif c["state"] in {"FAIL", "BLOCKED"} and not c.get("reason"):
            errors.append(f"{c['id']}: failed/blocked runtime case requires a reason")
    for t in targets.values():
        source = source_targets[t["id"]]
        if t["package_id"] not in packages or t["id"] not in packages[t["package_id"]]["target_ids"]:
            errors.append(f"{t['id']}: owner mismatch")
        if t["design_issue_ids"] != source["issue_ids"] or t["design_module"] != "docs/design-handbook/" + source["source_module"]:
            errors.append(f"{t['id']}: design traceability changed")
        if t["revision_outputs"] != ["docs/design-handbook/" + p for p in source["outputs"]]:
            errors.append(f"{t['id']}: required revision output mapping changed")
        expected_cases = sorted(c["id"] for c in cases.values() if c["kind"] == "RUNTIME" and t["id"] in c["consumer_targets"])
        if sorted(t["runtime_case_ids"]) != expected_cases:
            errors.append(f"{t['id']}: runtime case coverage differs")
        if t["assessment"] not in ASSESSMENTS or t["delivery_state"] not in STATES:
            errors.append(f"{t['id']}: unknown assessment or delivery state")
        if t["assessment"] != "PENDING" and not evidence_ok(t["assessment_evidence"], root):
            errors.append(f"{t['id']}: assessment needs source evidence")
        if t["delivery_state"] in {"READY", "IN_PROGRESS", "VERIFYING", "DONE"} and t["assessment"] in {"PENDING", "DECISION_REQUIRED"}:
            errors.append(f"{t['id']}: implementation entered before assessment/decision")
        if t["delivery_state"] == "BLOCKED" and not t["blockers"]:
            errors.append(f"{t['id']}: BLOCKED requires a concrete blocker")
        inventory = t["original_acceptance_inventory"]
        original_cases = inventory["cases"]
        if inventory["state"] not in {"NOT_INVENTORIED", "COMPLETE"}:
            errors.append(f"{t['id']}: invalid original inventory state")
        if inventory["state"] == "COMPLETE" and (not original_cases or len({x["id"] for x in original_cases}) != len(original_cases)):
            errors.append(f"{t['id']}: original acceptance inventory empty/duplicated")
        for original_case in original_cases:
            if original_case["state"] not in {"NOT_RUN", "PASS", "FAIL", "BLOCKED"}:
                errors.append(f"{t['id']}: invalid original acceptance state")
            if original_case["state"] == "PASS" and not evidence_ok(original_case.get("evidence"), root, execution=True):
                errors.append(f"{t['id']}: original acceptance PASS lacks evidence")
        if t["delivery_state"] == "DONE":
            if inventory["state"] != "COMPLETE" or not original_cases or any(c["state"] != "PASS" for c in original_cases):
                errors.append(f"{t['id']}: DONE requires all original acceptance cases")
            if any(cases[cid]["state"] != "PASS" for cid in expected_cases):
                errors.append(f"{t['id']}: DONE with required revision cases not passed")
            if not evidence_ok(t["implementation_evidence"], root, execution=True):
                errors.append(f"{t['id']}: DONE lacks implementation/validation evidence")
            delivery = t["delivery_evidence"]
            if not isinstance(delivery, dict) or not evidence_ok([delivery], root) or not re.fullmatch(r"[0-9a-f]{40}", delivery.get("remote_main_sha", "")) or not delivery.get("pr_url", "").startswith("https://github.com/"):
                errors.append(f"{t['id']}: DONE lacks audited remote-main delivery receipt")
    for t in targets.values():
        for path in [t["design_module"], *t["revision_outputs"], t["original_acceptance_inventory"]["source"]]:
            if not (root / path).is_file():
                errors.append(f"missing source link: {path}")
    errors.extend(validate_tasks(data, root, targets))
    return errors


def validate_tasks(data, root: Path, targets):
    """Check task records independently; task delivery never advances business cases."""
    errors = []
    rows = data.get("implementation_tasks", [])
    if not rows:
        return ["task inventory must not be empty"]
    tasks = unique(rows, "id", errors, "tasks")
    if any(tid.startswith("WP") for tid in tasks) and not data.get("dispatch_policy", {}).get("definition_path"):
        errors.append("WP tasks require dispatch policy and definitions")
    for task in tasks.values():
        tid = task["id"]
        if task.get("state") not in STATES or task.get("kind") not in {"CODE", "DESIGN", "VALIDATION"}:
            errors.append(f"{tid}: invalid task state/kind")
        if task.get("repository") not in {"Main", "CRM", "Platform"}:
            errors.append(f"{tid}: invalid task repository")
        coordinator = task.get("package_id") in {"WP01", "WP14"} and task.get("coverage_role") in {"COORDINATION", "VALIDATION"}
        if (not task.get("target_ids") and not coordinator) or set(task.get("target_ids", [])) - set(targets):
            errors.append(f"{tid}: invalid task target")
        plan = (root / task.get("plan_path", "")).resolve()
        generated_card = (
            "definition_path" in data.get("dispatch_policy", {})
            and tid.startswith("WP")
            and task.get("plan_path") == f"docs/development/task-plans/{task.get('package_id')}.md"
        )
        # Generated cards may be absent on the first --write. Normal CLI checks
        # still require their exact rendered contents after definition validation.
        if not plan.is_relative_to(root.resolve()) or (not plan.is_file() and not generated_card):
            errors.append(f"{tid}: task plan missing or outside repository")
        deps = task.get("dependencies", [])
        if len(set(deps)) != len(deps) or set(deps) - set(tasks) or tid in deps:
            errors.append(f"{tid}: invalid task dependency")
        if task["state"] in {"READY", "IN_PROGRESS", "VERIFYING", "DONE"} and any(d not in tasks or tasks[d]["state"] != "DONE" for d in deps):
            errors.append(f"{tid}: unfinished task dependencies")
        if task["state"] == "BLOCKED" and not task.get("blockers"):
            errors.append(f"{tid}: task BLOCKED needs a reason")
        if task["state"] == "DONE":
            if not evidence_ok(task.get("validation_evidence"), root, execution=True):
                errors.append(f"{tid}: task DONE lacks validation evidence")
            delivery = task.get("delivery_evidence")
            if not isinstance(delivery, dict) or not evidence_ok([delivery], root) or not re.fullmatch(r"[0-9a-f]{40}", delivery.get("remote_main_sha", "")) or not delivery.get("pr_url", "").startswith("https://github.com/"):
                errors.append(f"{tid}: task DONE lacks audited remote-main delivery receipt")
    visiting, visited = set(), set()

    def visit(tid):
        if tid in visiting:
            errors.append(f"{tid}: task dependency cycle")
            return
        if tid in visited:
            return
        visiting.add(tid)
        for dep in tasks[tid].get("dependencies", []):
            if dep in tasks:
                visit(dep)
        visiting.remove(tid)
        visited.add(tid)

    for tid in tasks:
        visit(tid)
    if data.get("dispatch_policy"):
        errors.extend(validate_dispatch(data, targets))
        errors.extend(validate_definitions(data, root, targets))
    return errors


def validate_dispatch(data, targets):
    """Validate the coordinator's snapshot; this is not a distributed lock service."""
    errors = []
    tasks = data["implementation_tasks"]
    packages = {p["id"] for p in data["work_packages"]}
    owning = {tid: [] for tid in targets}
    active_resources, active_claims = {}, {}
    for task in tasks:
        tid = task["id"]
        if task.get("package_id") not in packages:
            errors.append(f"{tid}: invalid dispatch package")
        if task.get("coverage_role") not in {"IMPLEMENTATION", "FOUNDATION", "DESIGN", "VALIDATION", "COORDINATION"}:
            errors.append(f"{tid}: invalid task coverage role")
        if task.get("entry_gate") not in {"NOT_CHECKED", "CONFIRMED"} or not task.get("entry_conditions"):
            errors.append(f"{tid}: missing task entry conditions")
        if not task.get("exclusive_resources") or len(set(task["exclusive_resources"])) != len(task["exclusive_resources"]):
            errors.append(f"{tid}: missing/duplicate exclusive resources")
        if task.get("coverage_role") == "IMPLEMENTATION" and task.get("kind") == "CODE":
            for target in task.get("target_ids", []):
                if target in targets and targets[target]["package_id"] == task.get("package_id"):
                    owning[target].append(tid)
        if task["state"] in {"READY", "IN_PROGRESS", "VERIFYING", "DONE"}:
            if task.get("entry_gate") != "CONFIRMED":
                errors.append(f"{tid}: entry gate not confirmed")
            claim = task.get("assignment")
            required = ("chat_label", "branch", "worktree", "claimed_at")
            valid_claim = isinstance(claim, dict) and all(isinstance(claim.get(k), str) and claim[k].strip() for k in required)
            if not valid_claim:
                errors.append(f"{tid}: assignment missing")
            elif task["state"] in {"IN_PROGRESS", "VERIFYING"}:
                for key in ("chat_label", "worktree", "branch"):
                    # Branch names can coincide across repositories; paths/chat labels cannot.
                    claimed_value = str(Path(claim[key]).resolve()) if key == "worktree" else claim[key]
                    value = (key, task["repository"] if key == "branch" else "", claimed_value.casefold().replace("\\", "/").rstrip("/"))
                    if value in active_claims:
                        errors.append(f"{tid}: active assignment conflict with {active_claims[value]} ({key})")
                    active_claims[value] = tid
        if task["state"] in {"IN_PROGRESS", "VERIFYING"}:
            for resource in task.get("exclusive_resources", []):
                if resource in active_resources:
                    errors.append(f"{tid}: active resource conflict with {active_resources[resource]} ({resource})")
                active_resources[resource] = tid
    coverage = unique(data.get("target_task_coverage", []), "target_id", errors, "target task coverage")
    if set(coverage) != set(targets):
        errors.append("target task coverage differs from 125-target baseline")
    states = {t["id"]: t["state"] for t in tasks}
    for target, ids in owning.items():
        if not ids:
            errors.append(f"{target}: no owning implementation task")
        if target not in coverage or sorted(coverage[target].get("implementation_task_ids", [])) != sorted(ids):
            errors.append(f"{target}: target task coverage differs from owning tasks")
        if targets[target]["delivery_state"] == "DONE" and any(states[i] != "DONE" for i in ids):
            errors.append(f"{target}: target DONE with unfinished implementation tasks")
    return errors


DEFINITION_METADATA = (
    "id", "package_id", "title", "repository", "kind", "target_ids",
    "relative_size", "dependencies", "exclusive_resources",
)
BINDING_MODES = {"TASK_DELIVERY", "CONDITIONAL_CONSUMER", "EXTERNAL_EVIDENCE"}


def string_list(value, *, nonempty=False):
    return (isinstance(value, list) and (bool(value) or not nonempty)
            and all(isinstance(item, str) and item.strip() for item in value))


def validate_definitions(data, root: Path, targets):
    """Definitions own card content; roadmap alone owns delivery/assignment state."""
    policy = data.get("dispatch_policy", {})
    if "definition_path" not in policy:
        return []
    errors = []
    definition_path = policy["definition_path"]
    if definition_path != "docs/development/task-definitions.json":
        return ["invalid task definition path"]
    try:
        definitions = read_json(root / definition_path)
    except (OSError, ValueError) as error:
        return [f"cannot read task definitions: {error}"]
    if (not isinstance(definitions, dict)
            or type(definitions.get("schema_version")) is not int
            or definitions["schema_version"] != 1):
        return ["unsupported task definition schema"]
    rows = definitions.get("tasks")
    if not isinstance(rows, list):
        return ["task definitions require a tasks array"]
    tasks = {task["id"]: task for task in data.get("implementation_tasks", [])}
    new_tasks = {tid: task for tid, task in tasks.items() if tid.startswith("WP")}
    entries = {}
    for row in rows:
        if not isinstance(row, dict) or not isinstance(row.get("id"), str):
            errors.append("invalid task definition record")
            continue
        tid = row["id"]
        if tid in entries:
            errors.append(f"{tid}: duplicate definition ID")
        entries[tid] = row
    if set(entries) != set(new_tasks):
        errors.append("definition task IDs differ from roadmap WP tasks")
    plans = policy.get("package_plans", {})
    for package in data["work_packages"]:
        pid = package["id"]
        if plans.get(pid) != f"docs/development/task-plans/{pid}.md":
            errors.append(f"{pid}: definition package plan differs")
    for tid, definition in entries.items():
        task = new_tasks.get(tid)
        if task is None:
            continue
        for key in DEFINITION_METADATA:
            if key not in definition or definition[key] != task.get(key):
                errors.append(f"{tid}: definition metadata differs: {key}")
        for key in ("state", "assignment", "validation_evidence", "delivery_evidence", "entry_gate", "blockers"):
            if key in definition:
                errors.append(f"{tid}: mutable ledger field in definition: {key}")
        pid = definition.get("package_id")
        if not re.fullmatch(r"WP[0-9]{2}-[0-9]{2,}", tid) or not tid.startswith(f"{pid}-"):
            errors.append(f"{tid}: invalid definition task identity")
        if task.get("card_anchor") != tid.lower() or task.get("plan_path") != f"docs/development/task-plans/{pid}.md":
            errors.append(f"{tid}: definition card locator differs")
        if not string_list(definition.get("target_ids")) or set(definition["target_ids"]) - set(targets):
            errors.append(f"{tid}: invalid definition targets")
        for key in ("scope", "outputs", "acceptance_positive", "acceptance_negative"):
            if not string_list(definition.get(key), nonempty=True):
                errors.append(f"{tid}: definition requires nonempty {key}")
        for key in ("code_paths", "new_paths", "external_prerequisites"):
            if not string_list(definition.get(key)):
                errors.append(f"{tid}: invalid definition {key}")
        notes = definition.get("notes")
        if not (isinstance(notes, str) or string_list(notes)):
            errors.append(f"{tid}: invalid definition notes")
        refs = definition.get("source_refs")
        if not string_list(refs, nonempty=True):
            errors.append(f"{tid}: invalid definition source reference")
        else:
            for ref in refs:
                resolved = (root / ref).resolve()
                if (not ref.startswith(("docs/", "tools/")) or "\\" in ref or ".." in Path(ref).parts
                        or not resolved.is_relative_to(root.resolve()) or not resolved.is_file()):
                    errors.append(f"{tid}: invalid definition source reference: {ref}")
        bindings = definition.get("prerequisite_bindings")
        if not isinstance(bindings, list):
            errors.append(f"{tid}: invalid prerequisite bindings")
            continue
        for binding in bindings:
            if (not isinstance(binding, dict) or binding.get("mode") not in BINDING_MODES
                    or not all(isinstance(binding.get(k), str) and binding[k].strip()
                               for k in ("requirement", "condition"))
                    or not string_list(binding.get("task_ids"))):
                errors.append(f"{tid}: invalid prerequisite binding")
                continue
            refs = binding["task_ids"]
            if len(refs) != len(set(refs)) or set(refs) - set(tasks) or tid in refs:
                errors.append(f"{tid}: invalid prerequisite task reference")
            if binding["mode"] == "TASK_DELIVERY":
                if not refs or set(refs) - set(task.get("dependencies", [])):
                    errors.append(f"{tid}: TASK_DELIVERY binding missing dependency")
    return errors


def render_cards(definitions, data):
    """Return all 14 derived cards without copying any mutable ledger state."""
    tasks = {task["id"]: task for task in data.get("implementation_tasks", [])}

    def doc_link(path, label=None, anchor=""):
        return f"[{label or path}](../../../{quote(path, safe='/')}{anchor})"

    def task_link(tid):
        task = tasks[tid]
        anchor = "#" + task["card_anchor"] if task.get("card_anchor") else ""
        return doc_link(task["plan_path"], tid, anchor)

    def section(lines, title, values):
        lines.extend([f"### {title}", ""])
        lines.extend(f"- {value}" for value in values)
        if not values:
            lines.append("无。")
        lines.append("")

    cards = {}
    for package in data["work_packages"]:
        pid = package["id"]
        lines = [f"# {pid} {package['title']} · 任务卡", "",
                 "由 task-definitions.json 生成；内容修改后运行管理脚本重新生成。",
                 "任务状态、领取和交付证据只在 [roadmap.json](../roadmap.json) 登记，"
                 "见[任务进度](../07-detailed-tasks.md)。任务交付不等于整目标或业务验收完成。", ""]
        if pid == "WP02":
            existing = [task for tid, task in tasks.items() if tid.startswith("S01-") or tid == "S02-00"]
            section(lines, "既有S01与S02-00任务", [task_link(task["id"]) + "：" + task["title"] for task in existing])
            lines.extend(["沿既有计划执行，不在本页重复定义。", ""])
        group = [task for task in definitions["tasks"] if task["package_id"] == pid]
        for definition in group:
            tid = definition["id"]
            lines.extend([f'<a id="{tid.lower()}"></a>', "", f"## {tid} {definition['title']}", "",
                          f"仓库：{definition['repository']}；类型：{definition['kind']}；相对规模：{definition['relative_size']}。",
                          f"目标：{', '.join(definition['target_ids']) or '全局协调，不另占业务目标'}。", ""])
            section(lines, "直接前置", [task_link(dep) for dep in definition["dependencies"]])
            section(lines, "设计与验收来源", [doc_link(ref) for ref in definition["source_refs"]])
            section(lines, "实施范围", definition["scope"])
            section(lines, "交付输出", definition["outputs"])
            # Foreign-repository code paths are deliberately not linked into Main.
            section(lines, "已有源码入口", [f"`{path}`" for path in definition["code_paths"]])
            section(lines, "拟新增路径", [f"`{path}`" for path in definition["new_paths"]])
            section(lines, "正向验收", definition["acceptance_positive"])
            section(lines, "反向验收", definition["acceptance_negative"])
            section(lines, "跨域及外部前置", definition["external_prerequisites"])
            section(lines, "前置绑定与生效条件", [
                f"**{binding['mode']}**：{binding['requirement']}；条件：{binding['condition']}；"
                f"任务：{', '.join(task_link(dep) for dep in binding['task_ids']) or '无任务替代，核对外部证据'}。"
                for binding in definition["prerequisite_bindings"]])
            section(lines, "共享互斥资源", [f"`{resource}`" for resource in definition["exclusive_resources"]])
            notes = definition["notes"]
            section(lines, "备注", [notes] if isinstance(notes, str) and notes else notes or [])
        if not group:
            lines.extend(["本包无新增任务定义；已有任务沿台账中的原计划执行。", ""])
        cards[f"task-plans/{pid}.md"] = "\n".join(lines)
    return cards


def metrics(data):
    runtime = [c for c in data["revision_acceptances"] if c["kind"] == "RUNTIME"]
    return {"packages": len(data["work_packages"]), "targets": len(data["targets"]), "targets_assessed": sum(t["assessment"] not in {"PENDING", "DECISION_REQUIRED"} for t in data["targets"]), "targets_delivered": sum(t["delivery_state"] == "DONE" for t in data["targets"]), "original_inventories_complete": sum(t["original_acceptance_inventory"]["state"] == "COMPLETE" for t in data["targets"]), "revision_runtime": len(runtime), "runtime_states": dict(sorted(collections.Counter(c["state"] for c in runtime).items())), "historical_static": sum(c["kind"] == "HISTORICAL_STATIC" for c in data["revision_acceptances"])}


def render(data):
    m = metrics(data)
    overview = ["# 开发进度", "", f"台账日期：{data['as_of']}；基线：`{data['baseline_id']}`。由 roadmap.json 生成，按实际记录更新。", "", "| 指标 | 当前记录 |", "|---|---:|", f"| 大任务及目标归属已建立 | {m['packages']} 个 / {m['targets']} 个 |", f"| 逐目标实现差异已确认 | {m['targets_assessed']}/{m['targets']} |", f"| 原模块完整验收清单已登记 | {m['original_inventories_complete']}/{m['targets']} |", f"| 已确认完成本轮目标交付 | {m['targets_delivered']}/{m['targets']} |", f"| 修订运行场景已有真实 PASS | {m['runtime_states'].get('PASS', 0)}/{m['revision_runtime']} |", f"| 历史静态检查引用 | {m['historical_static']} 条，不计业务运行进度 |", "", "上述是本轮台账的证据登记数。0项已确认交付不表示既有代码为零；目标大小不同，不以目标数或大任务数计算总工作量百分比。247条仅为修订场景，原模块业务验收另行登记。", "", "| 大任务 | 目标数 | 状态 | 已确认交付 | 初步规模 |", "|---|---:|---|---:|---|"]
    details = ["# 开发大任务评估", "", "14项是业务工作包。WP01负责全局评估，WP14负责跨域验收协调，其余12项各自拥有目标；跨域合同可以有多个参与方，但一个Target只有一个归属。", "", "规模L/XL是相对范围与风险，不是工期；置信度均为LOW。已有实现入口是复用候选，未代替逐目标语义核对和实际测试。", ""]
    for p in data["work_packages"]:
        own = [t for t in data["targets"] if t["package_id"] == p["id"]]
        done = sum(t["delivery_state"] == "DONE" for t in own)
        delivered = f"{done}/{len(own)}" if own else "按工作包完成条件"
        overview.append(f"| {p['id']} {p['title']} | {len(own)} | {LABELS[p['state']]} | {delivered} | {p['relative_size']} |")
        details += [f"## {p['id']} {p['title']}", "", f"**成果：** {p['outcome']}。", "", f"**现有基础：** {p['existing_baseline']}。", "", f"**规模与置信度：** {p['relative_size']} / LOW；{p['estimate_basis']}。", "", "**分批业务范围：**", "", *[f"- {s}" for s in p["business_slices"]], "", f"**进入条件：** {p['entry_gate']}。", "", f"**完成条件：** {p['exit_gate']}。", "", "**重点风险：** " + "；".join(p["risk_focus"]) + "。", "", "**协作包：** " + (", ".join(p["integration_partners"]) or "全局基线输入") + "。这些是接口协作关系，不表示必须等待对方整包完成。", "", "**目标归属：** " + (", ".join(p["target_ids"]) or "全局协调工作，不另占125目标名额") + "。", ""]
    active = [p for p in data["work_packages"] if p["state"] in {"ASSESSING", "READY", "IN_PROGRESS", "VERIFYING", "BLOCKED"}]
    overview += ["", "当前活动工作包：" + ("；".join(f"{p['id']} {p['title']}（{LABELS[p['state']]}）" for p in active) or "暂无已登记活动任务") + "。", "", "具体批次及准入见[依赖与批次](04-batches.md)。本视图按记录生成，不启动后台监控或业务任务。", "", "受阻项只在有具体事实时登记；PLANNED/PENDING不自动等同BLOCKED。更新步骤见[台账使用说明](README.md)。", ""]
    target_table = ["# 125目标归属与评估登记", "", "每个目标恰好归属一个大任务；设计来源和修订场景完整ID保存在roadmap.json。状态均针对本轮目标交付。", "", "| 目标 | 名称 | 大任务 | 实现评估 | 交付状态 | 修订运行场景数 |", "|---|---|---|---|---|---:|"]
    for t in data["targets"]:
        link = "../../" + t["design_module"]
        target_table.append(f"| [{t['id']}]({link}) | {t['name']} | {t['package_id']} | {t['assessment']} | {LABELS[t['delivery_state']]} | {len(t['runtime_case_ids'])} |")
    target_table += ["", "同一跨域场景可能列入多个目标，整体验收进度按唯一场景ID去重。表内场景数不得直接求和当作总数；场景数为0也必须盘点并通过该模块原始业务验收。", ""]
    tasks = data.get("implementation_tasks", [])
    s01 = [t for t in tasks if t["id"].startswith("S01-")]
    delivered = sum(t["state"] == "DONE" for t in s01)
    task_view = ["# 细分任务进度", "", f"台账日期：{data['as_of']}。由 roadmap.json 生成；S01 已交付：{delivered}/{len(s01)}。S02-00 多租户设计前置保留。", "", f"全部已登记任务 {len(tasks)} 项，已交付 {sum(t['state']=='DONE' for t in tasks)} 项。任务大小和类型不同，这不是总工时百分比。设计任务完成不代表业务代码或运行验收通过。", "", "任务卡和目标主体责任见[工作包任务索引](10-work-package-tasks.md)，分配步骤见[多对话协作](09-parallel-development.md)。所有状态/领取/证据只在JSON登记；未领取的候选不自动READY。", ""]
    for package in data["work_packages"]:
        group = [t for t in tasks if t.get("package_id", "WP02") == package["id"]]
        if not group:
            continue
        task_view += [f"## {package['id']} {package['title']}", "", "| 任务 | 仓库 | 状态 | 直接前置 | 类型/规模 | 对话 |", "|---|---|---|---|---|---|"]
        for t in group:
            link = "../../" + t["plan_path"]
            if t.get("card_anchor"):
                link += "#" + t["card_anchor"]
            deps = t["dependencies"]
            dependency_summary = (f"共{len(deps)}项，详见任务卡" if len(deps) > 12
                                  else ", ".join(deps) or "无任务前置，仍需准入核对")
            task_view.append(f"| [{t['id']} {t['title']}]({link}) | {t['repository']} | {LABELS[t['state']]} | {dependency_summary} | {t['kind']} / {t['relative_size']} | {(t.get('assignment') or {}).get('chat_label','未领取')} |")
        task_view += [""]
    task_view += ["[PostgreSQL基线](08-postgresql-baseline.md)：后续新增开发不要求SQL Server/双库。单任务通过不关闭整目标。CRM可以在所需合同/身份/事务子集完成后推进，不等待全部财务任务。", ""]
    overview += ["", f"全部细分任务已交付 {sum(t['state']=='DONE' for t in tasks)}/{len(tasks)}；其中S01 {delivered}/{len(s01)}，S02-00 多租户设计前置保留。见[细分任务进度](07-detailed-tasks.md)，不计入业务目标交付。", ""]
    views = {"01-work-packages.md": "\n".join(details), "02-target-coverage.md": "\n".join(target_table), "03-progress.md": "\n".join(overview), "07-detailed-tasks.md": "\n".join(task_view)}
    if data.get("dispatch_policy"):
        index = ["# 工作包任务与125目标主体责任", "", "由roadmap.json生成。原14个工作包和125目标未增减；任务清单是本轮范围拆分，执行对话仍需按最新源码细化步骤。", "", "| 工作包 | 任务数 | 主体目标数 | 任务卡 |", "|---|---:|---:|---|"]
        for p in data["work_packages"]:
            group = [t for t in tasks if t["package_id"] == p["id"]]
            path = data["dispatch_policy"]["package_plans"][p["id"]]
            index.append(f"| {p['id']} {p['title']} | {len(group)} | {len(p['target_ids'])} | [查看卡片](../../{path}) |")
        index += ["", "## 目标关闭门", "", "下表列出每目标所属工作包的全部主体实现任务。公共基础、其他仓消费者和跨域验证另见卡片与修订场景；仅把目标挂在设计/基础任务下不算主体实现覆盖。全部主体任务DONE仍需原模块完整验收、必需修订场景、真实执行及远端交付证据，才能关闭目标。", "", "| 目标 | 主体实现任务 |", "|---|---|"]
        index += [f"| {c['target_id']} | {', '.join(c['implementation_task_ids'])} |" for c in data["target_task_coverage"]]
        index += ["", "原验收目录、具体case定义、任务状态和证据在JSON中保留准确引用。共享case不能重复计数，原件不可恢复不等于免验收。", ""]
        views["10-work-package-tasks.md"] = "\n".join(index)
    return views


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[3])
    parser.add_argument("--write", action="store_true", help="regenerate derived Markdown views after validation")
    parser.add_argument("--report", type=Path, help="write the structural check result, not business execution evidence")
    args = parser.parse_args()
    root = args.root.resolve()
    data = read_json(root / "docs/development/roadmap.json")
    errors = validate(data, root)
    views = render(data) if not errors else {}
    if not errors and "definition_path" in data.get("dispatch_policy", {}):
        definitions = read_json(root / data["dispatch_policy"]["definition_path"])
        views.update(render_cards(definitions, data))
    for name, text in views.items():
        path = root / "docs/development" / name
        if args.write:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
        elif not path.exists() or path.read_text(encoding="utf-8") != text:
            errors.append(f"derived view out of date: {name}; run with --write")
    result = {"result": "FAIL" if errors else "PASS", "scope": "ledger/source traceability, structural evidence requirements and generated view consistency only", "business_tests_executed": False, "metrics": metrics(data), "errors": errors}
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False))
    return bool(errors)


if __name__ == "__main__":
    raise SystemExit(main())
