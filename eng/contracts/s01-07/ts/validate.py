"""Run bounded IPQC local validation with immutable logs and exact input fingerprints."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[4]
EVIDENCE = ROOT / "docs/development/evidence/S01-07/ts"


def fingerprint():
    paths = set((ROOT / "cp6.web/src/contracts/quality").rglob("*.ts"))
    paths.update((ROOT / "eng/contracts/s01-07/ts").glob("*"))
    paths.update(ROOT / "cp6.web" / name for name in ("package.json", "package-lock.json", "vitest.config.ts", "tsconfig.json", "tsconfig.app.json", "tsconfig.node.json"))
    paths.update((ROOT / "eng/contracts/s01-07/inputs").rglob("*"))
    paths.add(ROOT / "eng/contracts/s01-07/sources.json")
    return {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(paths) if p.is_file()}


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="backslashreplace")
    started = datetime.now(timezone.utc).isoformat()
    before = fingerprint()
    run_id = sys.argv[1] if len(sys.argv) > 1 else "06-final"
    if not run_id.replace("-", "").isalnum():
        raise ValueError("run identity must be alphanumeric with optional hyphens")
    evidence = EVIDENCE / f"{run_id}-validation.json"
    if evidence.exists():
        raise RuntimeError("Evidence already exists: use a new run identity rather than overwrite history")
    commands = [
        ["node", "eng/contracts/s01-07/ts/generate.mjs", "--check"],
        ["npm.cmd" if os.name == "nt" else "npm", "--prefix", "cp6.web", "test", "--", "src/contracts/quality/ipqc.test.ts", "--reporter=verbose"],
        ["npm.cmd" if os.name == "nt" else "npm", "--prefix", "cp6.web", "run", "type-check"],
    ]
    outcomes = []
    for index, command in enumerate(commands):
        log = EVIDENCE / f"{run_id}-{index}.log"
        if log.exists():
            raise RuntimeError(f"Refusing to overwrite {log}")
        ran_at = datetime.now(timezone.utc).isoformat()
        result = subprocess.run(command, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        log.write_bytes(result.stdout)
        outcomes.append(dict(command=command, observed_at=ran_at, exit_code=result.returncode, log=log.relative_to(ROOT).as_posix(), log_sha256=hashlib.sha256(result.stdout).hexdigest()))
        print(" ".join(command), "exit", result.returncode, flush=True)
        print(result.stdout.decode("utf-8", errors="replace")[-1000:], flush=True)
    after = fingerprint()
    document = dict(started_at=started, finished_at=datetime.now(timezone.utc).isoformat(), execution_head=subprocess.check_output(["git", "-c", "core.longpaths=true", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(), scope="Uncommitted S01-07 sources bound by SHA256; local contract and Web compiler evidence only", node=subprocess.check_output(["node", "--version"], text=True).strip(), npm=subprocess.check_output(["npm.cmd" if os.name == "nt" else "npm", "--version"], text=True).strip(), commands=outcomes, inputs_sha256=before, inputs_unchanged=before == after, business_acceptance="NOT_RUN")
    evidence.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8", newline="\n")
    if before != after or any(o["exit_code"] for o in outcomes):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
