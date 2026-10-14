"""Run bounded local checks and retain real command output/exit/source identity."""
import argparse
import datetime
from pathlib import Path
import re
import subprocess
import sys

from prepare import BASELINE, BUNDLE, OUT, encoded, sha


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive-root", type=Path, required=True)
    parser.add_argument("--run-label", default=datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%S%fZ"), help="Fresh alphanumeric/hyphen label; existing run outputs are never overwritten")
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9-]+", args.run_label):
        parser.error("run label must use letters, digits or hyphens")
    root = Path(__file__).resolve().parents[3]
    out = root / OUT
    validation_path = out / ("validation." + args.run_label + ".json")
    if validation_path.exists() or any(out.glob("*." + args.run_label + ".*")):
        parser.error("run label already exists; choose a fresh label")
    # Preserve the previous actual record before regenerating derived outputs.
    prior = out / "source-verification.json"
    if prior.exists():
        (out / ("source-verification." + args.run_label + ".before.json")).write_bytes(prior.read_bytes())
    protected = [root / "docs/development/roadmap.json", root / "docs/development/tools/manage_roadmap.py"]
    protected += list((root / BUNDLE).rglob("*"))
    protected += [root / "docs/project-memory" / name for name in ("PROJECT_STATE.md", "05-Completed.md", "06-Todo.md", "CHANGELOG-AI.md")]
    def identities():
        return {str(p.relative_to(root)).replace("\\", "/"): sha(p.read_bytes()) for p in protected if p.is_file()}
    before = identities()
    commands = [
        ("prepare-final", [sys.executable, "-B", "-X", "utf8", "eng/wp01-01/preparation/prepare.py", "--root", ".", "--archive-root", str(args.archive_root)]),
        ("comparator-tests-final", [sys.executable, "-B", "-X", "utf8", "eng/wp01-01/preparation/test_comparator.py"]),
        ("bundle-tests-final", [sys.executable, "-B", "-X", "utf8", "eng/wp01-01/preparation/test_bundle.py"]),
        ("manage-roadmap-readonly", [sys.executable, "-B", "-X", "utf8", "docs/development/tools/manage_roadmap.py"]),
    ]
    records = []
    for name, command in commands:
        start = datetime.datetime.now(datetime.timezone.utc).isoformat()
        process = subprocess.run(command, cwd=root, capture_output=True)
        stdout, stderr = out / (name + "." + args.run_label + ".stdout.txt"), out / (name + "." + args.run_label + ".stderr.txt")
        stdout.write_bytes(process.stdout)
        stderr.write_bytes(process.stderr)
        record = {"name": name, "command": command, "started_at": start, "finished_at": datetime.datetime.now(datetime.timezone.utc).isoformat(), "exit_code": process.returncode, "stdout": stdout.name, "stdout_sha256": sha(process.stdout), "stderr": stderr.name, "stderr_sha256": sha(process.stderr), "script_sha256": sha((root / command[4]).read_bytes()), "scope": "LOCAL_PREPARATION_STATIC_ONLY"}
        records.append(record)
        print(name, "exit", process.returncode)
        if process.returncode:
            print(process.stdout.decode("utf-8", errors="replace"))
            print(process.stderr.decode("utf-8", errors="replace"))
            break
    after = identities()
    result = {"scope": "PREPARATION_ONLY", "result": "PASS" if len(records) == len(commands) and all(r["exit_code"] == 0 for r in records) and before == after else "FAIL", "source_revision": BASELINE, "python": sys.version, "executable": sys.executable, "platform": sys.platform, "commands": records, "protected_input_hashes_before": before, "protected_input_hashes_after": after, "protected_inputs_unchanged": before == after, "earlier_runs": [{"path": "tdd-red.txt", "result": "EXPECTED_RED", "tests": 20, "assertion_failures": 22}, {"path": "tdd-malformed-red.txt", "result": "EXPECTED_MALFORMED_INPUT_FAILURES", "tests": 22, "failures": 3, "errors": 3}, {"path": "comparator-tests.txt", "result": "PASS", "tests": 22}, {"path": "tdd-repository-scope-red.txt", "result": "EXPECTED_RED", "tests": 23, "failures": 1}, {"path": "prepare-first-run.txt", "result": "PASS", "report": "source-verification.initial.json", "note": "Actual initial source-check output retained; later final run has its own script identity."}], "not_run": ["Office visual rendering/formula recalculation", "Business runtime/UI/API/worker/provider tests", "PostgreSQL/database migration/concurrency runtime", "Signature/authority or remote-main delivery authentication", "GitHub Actions/release/deployment"], "actual_adoption": False, "actual_completion": False, "notes": "No previous result relabeled as newly executed; red/failure outputs retained. No database allocated, no workflow trigger changed, no remote action or cancellation performed by these tools."}
    validation_path.write_bytes(encoded(result))
    print("validation", validation_path.relative_to(root))
    return 0 if result["result"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
