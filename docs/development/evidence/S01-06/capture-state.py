"""Record the actual working-tree inputs and local build bytes for one test stage."""
import argparse
import hashlib
import json
import subprocess
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
EVIDENCE = Path(__file__).resolve().parent
FILES = [
    "CP6.Entity/Contracts/PlanAtp/StableId.cs",
    "eng/contracts/exact-values-tests/StableIdTests.cs",
    "cp6.web/src/contracts/plan-atp/stableId.ts",
    "cp6.web/src/contracts/plan-atp/stableId.test.ts",
    "eng/contracts/s01/atp-stable-id.schema.json",
    "eng/contracts/s01/profile-inputs.json",
    "eng/contracts/s01/vectors.json",
    "docs/development/dispatch/20261012-wave3/dispatch.json",
    "CP6.Entity/Contracts/Wire/StrictJson.cs",
    "CP6.Entity/CP6.Entity.csproj",
    "CP6.Entity/packages.lock.json",
    "eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj",
    "Directory.Build.props",
    "cp6.web/package.json",
    "cp6.web/package-lock.json",
    "cp6.web/tsconfig.app.json",
    "cp6.web/vitest.config.ts",
    "eng/contracts/exact-values-tests/bin/Debug/net8.0/CP6.ExactValues.Tests.dll",
    "eng/contracts/exact-values-tests/bin/Debug/net8.0/CP6.Entity.dll",
    "eng/contracts/exact-values-tests/bin/Debug/net8.0/CP6.ExactValues.Tests.deps.json",
    "eng/contracts/exact-values-tests/obj/project.assets.json",
]


def pin(path):
    raw = path.read_bytes()
    return {"path": path.relative_to(ROOT).as_posix(), "bytes": len(raw),
            "sha256": hashlib.sha256(raw).hexdigest()}


parser = argparse.ArgumentParser()
parser.add_argument("stage", choices=["red", "green"])
args = parser.parse_args()
report = {
    "stage": args.stage,
    "snapshot_utc_after_run": datetime.now(timezone.utc).isoformat(),
    "git_head_baseline": subprocess.check_output(
        ["git", "-c", "core.longpaths=true", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
    "source_basis": "Working-tree file bytes, including uncommitted task changes; HEAD alone is not the executed source identity.",
    "environment": {"dotnet_sdk": "8.0.424", "target_framework": "net8.0",
                    "node": "20.19.0", "npm": "10.8.2", "database": "NOT_RUN"},
    "files": [pin(ROOT / name) for name in FILES],
    "run_artifacts": [pin(path) for path in sorted(EVIDENCE.glob(f"{args.stage}-*"))
                      if path.suffix in {".log", ".json", ".trx"} and path.name != f"{args.stage}-state.json"],
}
(EVIDENCE / f"{args.stage}-state.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"PASS: captured {args.stage} source/build inputs and {len(report['run_artifacts'])} raw run artifacts")
