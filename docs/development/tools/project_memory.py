"""在四份项目记忆页中更新台账摘要，保留人工记录的原始字节。"""
import os
from pathlib import Path
import posixpath
from shutil import copymode
from tempfile import NamedTemporaryFile
from urllib.parse import quote

START = b"<!-- cp6:roadmap -->"
END = b"<!-- /cp6:roadmap -->"


def evidence_link(label, source, anchor=""):
    if source.startswith("https://"):
        return f"[{label}]({source}{anchor})"
    return f"[{label}]({quote(posixpath.relpath(source, 'docs/project-memory'), safe='/')}{anchor})"


def sections(data, counts):
    policy = data["dispatch_policy"]["card_execution_policy"]
    tasks = {task["id"]: task for task in data["implementation_tasks"]}
    log = []
    for tid in reversed(policy["closed_cards"]):
        task = tasks[tid]
        execution = task["card_execution"]
        result = execution["result"]
        if result == "SPLIT":
            result = "拆卡，余项 " + "、".join(execution["new_splits"])
        report = evidence_link(tid + " " + task["title"], execution["report"])
        log.append(f"- {report}（{result}），核对时间 `{execution['verified_at']}`。")

    targets = []
    for target in data["targets"]:
        if target["delivery_state"] != "DONE":
            continue
        audit = target.get("completion_audit", {})
        source = audit.get("report") or target["delivery_evidence"][0]["path"]
        link = evidence_link(target["id"] + " " + target["name"], source)
        note = "（测试支撑）" if audit.get("classification") == "TEST_ONLY" else ""
        targets.append("- " + link + note)

    total = counts["original_total"]
    next_card = policy.get("next_card")
    if next_card:
        task = tasks[next_card]
        anchor = "#" + task["card_anchor"] if task.get("card_anchor") else ""
        link = evidence_link(next_card + " " + task["title"], task["plan_path"], anchor)
        todo = f"台账指定的下一卡为 {link}。直接前置为 {'、'.join(task['dependencies']) or '无'}。"
    else:
        todo = "台账尚未指定下一卡。"
    previous = policy.get("next_card_selection", {}).get("after_card")
    transfers = policy.get("supplemental_budget_transfers", [])
    if previous in {transfer["target_task_id"] for transfer in transfers}:
        report = evidence_link(previous, tasks[previous]["card_execution"]["report"])
        todo += f" 最近的获批补正及剩余出口见 {report}。"
    progress = (f"原号 DONE {counts['original_done']}/{total}，完整 DONE {counts['fully_done']}/{total}。"
                f"已关闭目标 {len(targets)}/{len(data['targets'])}。")
    if counts.get("supplemental_total"):
        progress += f"获批补正完整 DONE {counts['supplemental_done']}/{counts['supplemental_total']}。"
    return {
        "PROJECT_STATE.md": "## 单卡进度\n\n" + progress,
        "05-Completed.md": "## 最近交付\n\n" + (log[0] if log else "尚无单卡交付。")
                           + "\n\n### 已关闭目标\n\n" + ("\n".join(targets) or "尚无关闭目标。"),
        "06-Todo.md": "## 下一卡\n\n" + todo,
        "CHANGELOG-AI.md": "## 单卡交付记录\n\n" + ("\n".join(log) or "尚无单卡交付。"),
    }


def replace_section(original, section):
    first, separator, rest = original.partition(b"\n")
    newline = b"\r\n" if first.endswith(b"\r") else b"\n"
    block = newline.join([START, *[line.encode("utf-8") for line in section.split("\n")], END])
    if START not in original and END not in original:
        if not separator or not first.removeprefix(b"\xef\xbb\xbf").startswith(b"# "):
            raise ValueError("找不到用于插入摘要的一级标题")
        return first + separator + newline + block + newline + rest
    before, _, tail = original.partition(START)
    if original.count(START) != 1 or original.count(END) != 1 or END not in tail:
        raise ValueError("台账摘要的起止标记不完整或重复")
    return before + block + tail.partition(END)[2]


def synchronize(directory: Path, data, counts, *, write):
    policy = data.get("dispatch_policy", {}).get("card_execution_policy", {})
    if "closed_cards" not in policy:
        return []  # 历史台账尚未采用单卡交付记录。

    pending = {}
    errors = []
    try:
        summaries = sections(data, counts)
    except (KeyError, IndexError, TypeError) as exc:
        return [f"单卡摘要缺少对应的台账记录或交付报告（{exc}）"]
    for name, section in summaries.items():
        path = directory / name
        try:
            old = path.read_bytes()
            new = replace_section(old, section)
            if new != old:
                pending[path] = new
        except (OSError, ValueError) as exc:
            errors.append(f"{name} 无法更新台账摘要（{exc}）")
    if errors:
        return errors
    if not write:
        return [f"project-memory/{path.name} 摘要待更新，请执行 --write" for path in pending]

    # 同目录原子替换保留历史原件。中断后可用相同台账继续生成剩余页。
    for path, content in pending.items():
        with NamedTemporaryFile(dir=directory, delete=False) as temporary:
            staging = Path(temporary.name)
        try:
            staging.write_bytes(content)
            copymode(path, staging)
            os.replace(staging, path)
        finally:
            staging.unlink(missing_ok=True)
    return []
