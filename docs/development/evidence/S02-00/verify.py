"""Read-only S02-00 document/source checks. Never connect to a database or mark runtime cases PASS."""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import unquote, urlsplit


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def git(root: Path, *args: str) -> bytes:
    return subprocess.check_output(["git", "-c", "core.longpaths=true", "-C", str(root), *args], stderr=subprocess.PIPE)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[4])
    parser.add_argument("--crm-root", type=Path, required=True)
    parser.add_argument("--platform-root", type=Path, required=True)
    parser.add_argument("--archive-root", type=Path, required=True, help="Directory containing originals/")
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    root = args.root.resolve()
    base = root / "docs/development/evidence/S02-00"
    inventory = json.loads((base / "source-inventory.json").read_text(encoding="utf-8-sig"))
    matrix_path = root / "docs/development/acceptance/postgresql-tenancy.json"
    matrix = json.loads(matrix_path.read_text(encoding="utf-8-sig"))
    repos = {"Main": root, "CRM": args.crm_root, "Platform": args.platform_root}
    errors: list[str] = []
    checks: list[dict] = []

    def check(name: str, ok: bool, detail: str = "") -> None:
        checks.append({"check": name, "result": "PASS" if ok else "FAIL", "detail": detail})
        if not ok:
            errors.append(name + (": " + detail if detail else ""))

    check("task_and_design_identity", matrix["task_id"] == inventory["task_id"] == "S02-00"
          and matrix["design_version"] == inventory["design_version"] == "cp6-pg-tenancy-v1")
    check("postgresql_only", matrix["database_target"] == "PostgreSQL")
    cases = matrix["cases"]
    check("unique_case_ids", len({c["id"] for c in cases}) == len(cases))
    check("planned_case_set", {c["id"] for c in cases} == {f"PGT-{i:03}" for i in range(1, 28)})
    check("all_runtime_cases_not_run", matrix["execution_status"] == "NOT_RUN"
          and all(c["status"] == "NOT_RUN" and c["execution_evidence"] == [] for c in cases))
    task_ids = {t["id"] for t in json.loads((root / "docs/development/roadmap.json").read_text(encoding="utf-8-sig"))["implementation_tasks"]}
    for case in cases:
        check(case["id"] + "_complete", all(case.get(k) for k in (
            "actor", "preconditions", "request", "steps", "expected", "evidence_required", "source_scope")))
        check(case["id"] + "_implementation_task", case["implementation_task"] in task_ids)

    for item in inventory["source_files"]:
        try:
            data = git(repos[item["repository"]], "show", item["source_sha"] + ":" + item["path"])
            check("source:" + item["repository"] + ":" + item["path"],
                  digest(data) == item["blob_sha256"] and len(data) == item["bytes"])
        except (subprocess.CalledProcessError, OSError) as exc:
            check("source:" + item["path"], False, str(exc))

    for item in inventory["normative_inputs"]:
        source = args.archive_root / item["archive_path"]
        ok = source.is_file()
        if ok:
            data = source.read_bytes()
            ok = digest(data) == item["sha256"] and len(data) == item["bytes"]
        check("normative:" + item["target_id"] + ":" + item["role"], ok, item["archive_path"])

    authored = [root / matrix["design_path"], matrix_path, base / "source-inventory.json",
                base / "README.md", base / "review.md", base / "verify.py"]
    files = []
    for path in authored:
        check("authored_exists:" + path.name, path.is_file())
        if not path.is_file():
            continue
        text = path.read_text(encoding="utf-8-sig")
        files.append({"path": path.relative_to(root).as_posix(),
                      "sha256_utf8_lf": digest(text.replace("\r\n", "\n").encode("utf-8"))})
        if path.suffix != ".md":
            continue
        for href in re.findall(r"(?<!!)\[[^\]]*\]\(([^)]+)\)", text):
            url = urlsplit(href)
            if url.scheme or not url.path:
                continue
            target = (path.parent / unquote(url.path)).resolve()
            check("link:" + path.name + ":" + href, target.is_relative_to(root) and target.exists())
        check("fences_balanced:" + path.name, len(re.findall(r"^```", text, re.M)) % 2 == 0)

    diff = subprocess.run(["git", "-c", "core.longpaths=true", "-C", str(root), "diff", "--check"], capture_output=True, text=True)
    check("git_diff_check", diff.returncode == 0, diff.stdout + diff.stderr)
    staged = subprocess.run(["git", "-c", "core.longpaths=true", "-C", str(root), "diff", "--cached", "--check"], capture_output=True, text=True)
    check("git_staged_diff_check", staged.returncode == 0, staged.stdout + staged.stderr)
    result = {
        "task_id": "S02-00", "scope": "Document structure, local links, exact source and normative input identities only",
        "observed_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "interpreter": sys.version.split()[0], "command": sys.argv,
        "source_sha": inventory["repositories"][0]["main_sha"],
        "result": "FAIL" if errors else "PASS", "checks": checks, "errors": errors,
        "authored_files": files, "runtime_cases": {"total": len(cases), "NOT_RUN": len(cases)},
        "not_run": ["build", "business tests", "database connections/schema/roles/migrations", "runtime package verification", "capacity/backup/restore", "GitHub Actions", "deployment"],
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({"result": result["result"], "checks": len(checks), "runtime_NOT_RUN": len(cases), "errors": errors}, ensure_ascii=False))
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
