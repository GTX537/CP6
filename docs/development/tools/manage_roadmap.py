"""Validate the planning ledger and render views; never run or infer business tests."""
from __future__ import annotations

import argparse
import collections
import json
from pathlib import Path
import re
import subprocess


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
    return errors


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
    return {"01-work-packages.md": "\n".join(details), "02-target-coverage.md": "\n".join(target_table), "03-progress.md": "\n".join(overview)}


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
    for name, text in views.items():
        path = root / "docs/development" / name
        if args.write:
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
