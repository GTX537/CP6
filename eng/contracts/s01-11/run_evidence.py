"""Capture local S01-11 execution without rewriting earlier failures or successes."""
from pathlib import Path
import datetime, hashlib, json, os, platform, subprocess, sys, uuid

ROOT = Path(__file__).resolve().parents[3]
def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
def snapshot():
    paths = set()
    for pattern in ["CP6.Entity/**/*.cs", "CP6.Entity/*.csproj", "CP6.Entity/packages.lock.json", "CP6.Entity/obj/project.assets.json",
                    "eng/contracts/exact-values-tests/*.cs", "eng/contracts/exact-values-tests/*.csproj",
                    "eng/contracts/exact-values-tests/obj/project.assets.json", "eng/contracts/exact-values-tests/packages.lock.json", "eng/contracts/s01/*.json",
                    "eng/contracts/s01-11/**/*", "cp6.web/src/contracts/erp-credit/*",
                    "cp6.web/package*.json", "cp6.web/*config*", "global.json", "Directory.*", "NuGet.Config", "nuget.config"]:
        paths.update(p for p in ROOT.glob(pattern) if p.is_file() and "__pycache__" not in str(p) and not (p.suffix == '.cs' and ('obj' in p.parts or 'bin' in p.parts)))
    return {p.relative_to(ROOT).as_posix(): sha(p) for p in sorted(paths)}
def artifacts():
    return {p.relative_to(ROOT).as_posix(): sha(p) for pattern in ["CP6.Entity/bin/Debug/net8.0/*.dll", "CP6.Entity/bin/Debug/net8.0/*.deps.json", "eng/contracts/exact-values-tests/bin/Debug/net8.0/CP6*.dll", "eng/contracts/exact-values-tests/bin/Debug/net8.0/*.deps.json", "eng/contracts/exact-values-tests/bin/Debug/net8.0/*.runtimeconfig.json"] for p in sorted(ROOT.glob(pattern))}
def output(args):
    return subprocess.run(args, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT).stdout.decode("utf-8", "replace")

mode = sys.argv[1]
run = ROOT / "docs/development/evidence/S01-11/implementation" / (datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + mode + "-" + uuid.uuid4().hex[:8])
run.mkdir(parents=True, exist_ok=False)
commands = {
    "restore-ts": ["cmd.exe", "/d", "/c", "npm.cmd", "--prefix", "cp6.web", "ci", "--ignore-scripts", "--no-audit", "--no-fund"],
    "typecheck": ["cmd.exe", "/d", "/c", "npm.cmd", "--prefix", "cp6.web", "run", "type-check"],
    "design": [sys.executable, "-X", "utf8", "eng/contracts/s01-11/verify_design.py", str(run / "design.json")],
}
if mode.startswith("dotnet"):
    command = ["dotnet", "test", "eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj", "--filter", "FullyQualifiedName~ErpCreditCodecTests", "--logger", "trx;LogFileName=results.trx", "--results-directory", str(run)]
elif mode.startswith("ts"):
    command = ["cmd.exe", "/d", "/c", "npm.cmd", "--prefix", "cp6.web", "test", "--", "src/contracts/erp-credit/creditCodec.test.ts", "--reporter=default", "--reporter=json", "--outputFile=" + str(run / "vitest.json")]
else:
    command = commands[mode]
meta = {"command": command, "cwd": str(ROOT), "started_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "source_baseline": output(["git", "-c", "core.longpaths=true", "rev-parse", "HEAD"]).strip(),
        "execution_identity": "uncommitted raw source hashes; baseline is not the tested source tree identity",
        "environment": {"os": platform.platform(), "python": sys.version, "dotnet": output(["dotnet", "--info"]), "node": output(["node", "--version"]), "npm": output(["cmd.exe", "/d", "/c", "npm.cmd", "--version"])},
        "inputs_before": snapshot(), "compiled_artifacts_before": artifacts(), "verification_level": "LOCAL_CONTRACT_UNIT_ONLY", "business_execution": "NOT_RUN"}
(run / "metadata.json").write_text(json.dumps(meta, indent=2), encoding="utf-8", newline="\n")
with (run / "output.log").open("wb") as log:
    result = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
meta.update(exit_code=result.returncode, ended_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(), inputs_after=snapshot(), compiled_artifacts_after=artifacts())
trx = run / "results.trx"
if trx.exists():
    import xml.etree.ElementTree as ET
    root = ET.parse(trx).getroot()
    meta["counts"] = root.find("{*}ResultSummary/{*}Counters").attrib
if (run / "vitest.json").exists():
    report = json.loads((run / "vitest.json").read_text("utf-8"))
    meta["counts"] = {k: report[k] for k in ["numTotalTests", "numPassedTests", "numFailedTests", "numPendingTests"]}
if (run / "design.json").exists():
    meta["counts"] = json.loads((run / "design.json").read_text("utf-8"))
meta["evidence_files"] = {p.name: sha(p) for p in run.iterdir() if p.name != "metadata.json"}
(run / "metadata.json").write_text(json.dumps(meta, indent=2), encoding="utf-8", newline="\n")
print(str(run))
print(json.dumps({"exit_code": result.returncode, "counts": meta.get("counts")}))
print((run / "output.log").read_text("utf-8", errors="replace")[-12000:])
sys.exit(result.returncode)
