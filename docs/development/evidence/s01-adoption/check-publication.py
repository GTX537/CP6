"""Inspect exact candidate/protected-main workflow trees before a branch push or PR."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[4]
parser = argparse.ArgumentParser()
parser.add_argument("--operation", required=True, choices=["push", "pr"])
parser.add_argument("--out", type=Path)
args = parser.parse_args()


def git(*values):
    return subprocess.check_output(["git", "-c", "core.longpaths=true", *values], cwd=ROOT)


git("-c", "http.proxy=", "fetch", "origin", "main", "--no-tags")
refs = {"candidate": git("rev-parse", "HEAD").decode().strip(),
        "protected_main": git("rev-parse", "origin/main").decode().strip()}
checks = []
for kind, ref in refs.items():
    paths = git("ls-tree", "-r", "--name-only", ref, ".github/workflows").decode().splitlines()
    assert paths, "Missing workflow tree requires independent review"
    for path in paths:
        raw = git("show", f"{ref}:{path}")
        text = raw.decode("utf-8-sig").replace("\r\n", "\n")
        match = re.search(r"(?m)^on:\n((?:[ \t].*\n|\n)*)", text)
        assert match, path
        event_block = match.group(0).strip()
        events = re.findall(r"(?m)^  ([A-Za-z_]+):", event_block)
        if path == ".github/workflows/r2-candidate.yml":
            assert event_block == 'on:\n  push:\n    tags:\n      - "v*.*.*"', event_block
            conclusion = "Protected version-tag candidate only; this operation pushes a branch/creates a PR, no tag."
        else:
            assert events == ["workflow_dispatch"], (path, events)
            conclusion = "Manual entry only; no manual run authorized or requested."
        local_uses = re.findall(r"(?m)^\s*(?:-\s*)?uses:\s*[\"']?(\./[^\s\"']+)", text)
        dependencies = []
        for local in local_uses:
            dependency_paths = git("ls-tree", "-r", "--name-only", ref, local[2:]).decode().splitlines()
            assert dependency_paths, local
            dependencies.extend({"path": p, "sha256_git_blob": hashlib.sha256(git("show", f"{ref}:{p}")).hexdigest()}
                                for p in dependency_paths)
        checks.append({"tree": kind, "sha": ref, "path": path,
                       "sha256_git_blob": hashlib.sha256(raw).hexdigest(),
                       "events": event_block, "local_dependencies": dependencies, "conclusion": conclusion})
candidate = {c["path"]: c["sha256_git_blob"] for c in checks if c["tree"] == "candidate"}
protected = {c["path"]: c["sha256_git_blob"] for c in checks if c["tree"] == "protected_main"}
assert candidate == protected, "Workflow drift requires an explicit dependency/trigger review"
receipt = {"task": "S01-12", "operation": args.operation,
           "checked_at_utc": datetime.now(timezone.utc).isoformat(), **refs,
           "workflow_trees_identical": True, "result": "PASS", "checks": checks,
           "actions_triggered": False, "actions_cancelled": [], "trigger_changes": [],
           "limits": "Read-only inspection; no Actions/setting/branch-protection change, release or deployment."}
if args.out:
    assert not args.out.exists(), "Never overwrite publication evidence"
    args.out.write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"result": "PASS", "operation": args.operation, **refs,
                  "workflows_per_tree": len(candidate), "automatic_branch_or_pr_events": 0,
                  "out": str(args.out) if args.out else "console-only exact current tree check"}))
