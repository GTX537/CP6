"""Task-local prepare/build/probe runner. Never starts Main or the database probe."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time

sys.dont_write_bytecode = True
from snapshot import (CONFIG_HASH, MAIN_SHA, MAIN_TREE, PLATFORM_SHA, PLATFORM_TREE,
                      ROOTS, apply_overlay, dump, export_git, git, no_links,
                      owner_root, project_closure, sha256, validate_plan, verify_files,
                      native, logical, walk_files)

TASK = Path(__file__).resolve().parent
WORKTREE = TASK.parents[2]


def read(path):
    return json.loads(native(path).read_text(encoding="utf-8"))


def environment(root: Path, dotnet: Path) -> dict:
    # Preserve caller credential-provider variables without inspecting or recording them.
    env = os.environ.copy()
    env.update({"DOTNET_ROOT": str(dotnet.parent), "DOTNET_ROOT_X64": str(dotnet.parent),
                "DOTNET_MULTILEVEL_LOOKUP": "0", "DOTNET_CLI_HOME": str(root / "cli-home"),
                "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1", "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
                "DOTNET_NOLOGO": "1", "NUGET_PACKAGES": str(root / "n"),
                "NUGET_HTTP_CACHE_PATH": str(native(root / "h")),
                "NUGET_PLUGINS_CACHE_PATH": str(native(root / "p")),
                "TEMP": str(root / "tmp"), "TMP": str(root / "tmp"), "TMPDIR": str(root / "tmp")})
    for name in ("cli-home", "n", "h", "p", "tmp"):
        native(root / name).mkdir(exist_ok=True)
    return env


def command(root: Path, label: str, args: list[str], env: dict) -> dict:
    logs = root / "logs"
    native(logs).mkdir(exist_ok=True)
    log = logs / f"{label}.log"
    if native(log).exists():
        raise ValueError(f"Refusing to overwrite prior command evidence: {label}")
    record = {"label": label, "argv": args, "cwd": str(root), "log": log.relative_to(root).as_posix(), "exitCode": None}
    record_path = logs / f"{label}.command.json"
    dump(record_path, record)
    started = time.monotonic()
    with native(log).open("xb") as output:
        result = subprocess.run(args, cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT, timeout=1800)
    record.update(exitCode=result.returncode, elapsedSeconds=round(time.monotonic() - started, 3), logSha256=sha256(native(log).read_bytes()))
    dump(record_path, record)
    print(json.dumps({"command": label, "exitCode": result.returncode, "log": record["log"]}), flush=True)
    return record


def assert_command(record):
    if record["exitCode"] != 0:
        raise ValueError(f"Command failed: {record['label']}; see {record['log']}")


def prepare(root: Path, args, plan: dict):
    for repo, sha, tree in ((args.main_repo, MAIN_SHA, MAIN_TREE), (args.platform_repo, PLATFORM_SHA, PLATFORM_TREE)):
        actual = git(repo, "rev-parse", f"{sha}^{{tree}}").decode().strip()
        if actual != tree:
            raise ValueError("Fixed Git tree mismatch")
    projects = project_closure(args.main_repo, MAIN_SHA, ROOTS)
    prefixes = [str(Path(project).parent).replace("\\", "/") + "/" for project in projects]
    root_inputs = {"Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.config", "global.json", ".editorconfig", "README.md"}
    def main_selected(path):
        return path in root_inputs or path.startswith("contracts/") or any(path.startswith(prefix) for prefix in prefixes)
    def platform_selected(path):
        return path in root_inputs or path.startswith(("src/", "contracts/", "deploy/p09/"))
    main = root / "snapshots/main"
    platform = root / "snapshots/platform"
    sources = {"main": export_git(args.main_repo, MAIN_SHA, main, main_selected),
               "platform": export_git(args.platform_repo, PLATFORM_SHA, platform, platform_selected)}
    if sha256(native(main / "NuGet.config").read_bytes()) != CONFIG_HASH:
        raise ValueError("Main NuGet config bytes differ from admission pin")
    overlays = apply_overlay(main, platform, root / "originals/main")
    # Keep every original lock unchanged; generated graph locks get a distinct filename.
    for key in ("main", "platform"):
        for row in sources[key]["files"]:
            if row["exported"] and row["path"].endswith("packages.lock.json"):
                saved = root / "originals" / key / row["path"]
                native(saved.parent).mkdir(parents=True, exist_ok=True)
                native(saved).write_bytes(native(root / "snapshots" / key / row["path"]).read_bytes())
    probe = root / "probe"
    native(probe).mkdir()
    probe_files = []
    for source in sorted(walk_files(TASK / "probe")):
        if any(part in ("bin", "obj", "__pycache__") for part in source.relative_to(TASK / "probe").parts):
            continue
        no_links(source)
        relative = source.relative_to(TASK / "probe")
        target = probe / relative
        native(target.parent).mkdir(parents=True, exist_ok=True)
        data = native(source).read_bytes()
        native(target).write_bytes(data)
        probe_files.append({"path": relative.as_posix(), "sha256": sha256(data)})
    if not native(probe / "Probe.csproj").is_file():
        raise ValueError("Private probe source is missing")
    packages = native(platform / "Directory.Packages.props").read_bytes()
    native(probe / "Directory.Packages.props").write_bytes(packages)
    probe_files = [row for row in probe_files if row["path"] != "Directory.Packages.props"]
    probe_files.append({"path": "Directory.Packages.props", "sha256": sha256(packages)})
    dump(root / "global.json", {"sdk": {"version": "8.0.424", "rollForward": "disable", "allowPrerelease": False}})
    dump(root / "input-manifest.json", plan)
    manifest = {"schemaVersion": 1, "owner": args.owner, "sources": sources, "mainProjectClosure": projects,
                "overlay": overlays, "probeFiles": probe_files, "sdk": "8.0.424", "nugetConfigSha256": CONFIG_HASH,
                "formalPackageTrust": "BLOCKED", "mainConsumption": "NOT_RUN"}
    dump(root / "snapshot-manifest.json", manifest)
    state = {"schemaVersion": 1, "owner": args.owner, "phase": "prepare", "source": "PASS", "build": "NOT_RUN", "runtime": "NOT_RUN",
             "sourceAdoptionPlan": "CANDIDATE_ONLY", "formalPackageTrust": "BLOCKED", "mainConsumption": "NOT_RUN",
             "commands": [], "failures": [], "snapshotManifestSha256": sha256(native(root / "snapshot-manifest.json").read_bytes())}
    dump(root / "state.json", state)
    verify(root, args.owner)
    return state


def verify(root: Path, owner: str):
    no_links(root)
    # Generated bin/obj/cache are also forbidden from redirecting writes through junctions.
    for _ in walk_files(root):
        pass
    state = read(root / "state.json")
    if state.get("formalPackageTrust") != "BLOCKED" or state.get("mainConsumption") != "NOT_RUN":
        raise ValueError("Formal trust/Main consumption status is outside this window")
    if read(root / "global.json") != {"sdk": {"version": "8.0.424", "rollForward": "disable", "allowPrerelease": False}}:
        raise ValueError("Owner SDK selection changed")
    if state["owner"] != owner or state["snapshotManifestSha256"] != sha256(native(root / "snapshot-manifest.json").read_bytes()):
        raise ValueError("Owner marker or source manifest changed")
    manifest = read(root / "snapshot-manifest.json")
    validate_plan(read(root / "input-manifest.json"))
    changed = {row["project"]: row["afterSha256"] for row in manifest["overlay"]}
    verify_files(root / "snapshots/main", manifest["sources"]["main"]["files"], changed)
    verify_files(root / "snapshots/platform", manifest["sources"]["platform"]["files"])
    verify_files(root / "probe", [{**row, "exported": True} for row in manifest["probeFiles"]])
    if sha256(native(root / "snapshots/main/NuGet.config").read_bytes()) != CONFIG_HASH:
        raise ValueError("External NuGet config changed")
    return state


def capture_identities(root: Path):
    rows = []
    for scope in (root / "snapshots", root / "probe"):
        if not native(scope).is_dir():
            continue
        for path in sorted(walk_files(scope)):
            if path.name == "project.assets.json" or path.name.endswith((".deps.json", ".dll", ".runtimeconfig.json", ".wp02-02.lock.json")):
                rows.append({"path": path.relative_to(root).as_posix(), "sha256": sha256(native(path).read_bytes()), "size": native(path).stat().st_size})
    dump(root / "build-identities.json", {"schemaVersion": 1, "owner": root.name, "files": rows, "formalPackageTrust": "BLOCKED", "mainConsumption": "NOT_RUN"})


def build(root: Path, args):
    state = verify(root, args.owner)
    if state["build"] != "NOT_RUN":
        raise ValueError("Build already attempted for this owner; use a new owner")
    state.update(phase="build", build="RUNNING", compatibility="UNKNOWN", buildRoots={})
    dump(root / "state.json", state)
    env = environment(root, args.dotnet)
    try:
        record = command(root, "sdk-version", [str(args.dotnet), "--version"], env)
        state["commands"].append(record)
        assert_command(record)
        sdk = native(root / record["log"]).read_text(encoding="utf-8-sig").strip()
        if sdk != "8.0.424":
            raise ValueError(f"Required SDK8.0.424 was not selected: {sdk}")
        record = command(root, "runtime-list", [str(args.dotnet), "--list-runtimes"], env)
        state["commands"].append(record)
        assert_command(record)
        projects = [("main-webapi", root / "snapshots/main" / ROOTS[0]), ("database-probe-build-only", root / "snapshots/main" / ROOTS[1]), ("private-probe", root / "probe/Probe.csproj")]
        common = ["-p:RestoreLockedMode=false", "-p:RestorePackagesWithLockFile=true", "-p:NuGetLockFilePath=packages.wp02-02.lock.json", f"-p:PlatformSnapshot={root / 'snapshots/platform'}"]
        for label, project in projects:
            outcome = {"restore": "NOT_RUN", "build": "NOT_RUN", "restoreExitCode": None, "buildExitCode": None}
            state["buildRoots"][label] = outcome
            restore = [str(args.dotnet), "restore", str(project), "--configfile", str(root / "snapshots/main/NuGet.config"), "--packages", str(root / "n"), *common]
            record = command(root, label + "-restore", restore, env)
            state["commands"].append(record)
            outcome.update(restore="PASS" if record["exitCode"] == 0 else "FAIL", restoreExitCode=record["exitCode"])
            dump(root / "state.json", state)
            if record["exitCode"] != 0:
                state["failures"].append(f"{label} restore failed; root build NOT_RUN; see {record['log']}")
                verify(root, args.owner)
                continue
            record = command(root, label + "-build", [str(args.dotnet), "build", str(project), "-c", "Release", "--no-restore", "-warnaserror:MSB3277", *common], env)
            state["commands"].append(record)
            outcome.update(build="PASS" if record["exitCode"] == 0 else "FAIL", buildExitCode=record["exitCode"])
            if record["exitCode"] != 0:
                state["failures"].append(f"{label} build failed; see {record['log']}")
            dump(root / "state.json", state)
            verify(root, args.owner)
        if any(outcome["restore"] != "PASS" or outcome["build"] != "PASS" for outcome in state["buildRoots"].values()):
            raise ValueError("One or more independent build roots failed; all root outcomes and logs retained")
        state["build"] = "PASS"
    except Exception as error:
        state["build"] = "FAIL"
        state["failures"].append(str(error))
        raise
    finally:
        capture_identities(root)
        dump(root / "state.json", state)
    return state


def probe(root: Path, args):
    state = verify(root, args.owner)
    private = state.get("buildRoots", {}).get("private-probe", {})
    if state["build"] not in ("PASS", "FAIL") or private.get("restoreExitCode") != 0 or private.get("buildExitCode") != 0 or private.get("restore") != "PASS" or private.get("build") != "PASS" or state["runtime"] != "NOT_RUN":
        raise ValueError("Probe requires successful private-probe restore/build, completed build phase and no prior runtime attempt")
    state.update(phase="probe", runtime="RUNNING", compatibility="UNKNOWN", runtimeScope="ALL_THREE_BUILD_ROOTS" if state["build"] == "PASS" else "PRIVATE_PROBE_ONLY_MAIN_BUILD_FAILED")
    dump(root / "state.json", state)
    try:
        native(root / "runtime").mkdir(exist_ok=False)
        binary = root / "probe/bin/Release/net8.0/Probe.dll"
        record = command(root, "private-probe-run", [str(args.dotnet), str(binary), "--owner", args.owner, "--output", str(root / "runtime/runtime.json")], environment(root, args.dotnet))
        state["commands"].append(record)
        assert_command(record)
        result = read(root / "runtime/runtime.json")
        if result.get("status") != "PASS" or result.get("owner") != args.owner:
            raise ValueError("Runtime probe did not report owner-bound PASS")
        state["runtime"] = "OBSERVED_PASS_PENDING_POLICY"
        verify(root, args.owner)
    except Exception as error:
        state["runtime"] = "FAIL"
        state["failures"].append(str(error))
        raise
    finally:
        dump(root / "state.json", state)
    return state


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--phase", required=True, choices=("verify-plan", "prepare", "build", "probe"))
    parser.add_argument("--manifest", type=Path, default=TASK / "input-manifest.json")
    parser.add_argument("--owner")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--main-repo", type=Path)
    parser.add_argument("--platform-repo", type=Path)
    parser.add_argument("--dotnet", type=Path)
    args = parser.parse_args()
    root = None
    try:
        plan = read(args.manifest)
        validate_plan(plan)
        if args.phase == "verify-plan":
            print(json.dumps({"plan": "PASS", "formalPackageTrust": "BLOCKED", "mainConsumption": "NOT_RUN"}))
            return 0
        if not args.owner or args.output is None:
            raise ValueError("Every execution phase requires explicit --owner and --output")
        if args.phase == "prepare" and (not args.main_repo or not args.platform_repo):
            raise ValueError("prepare requires explicit --main-repo and --platform-repo")
        if args.phase in ("build", "probe") and (not args.dotnet or not args.dotnet.is_absolute() or not native(args.dotnet).is_file()):
            raise ValueError("build/probe require an explicit absolute SDK8 dotnet executable")
        root = owner_root(WORKTREE, args.owner, args.output, create=args.phase == "prepare")
        state = prepare(root, args, plan) if args.phase == "prepare" else build(root, args) if args.phase == "build" else probe(root, args)
        print(json.dumps({key: state[key] for key in ("owner", "phase", "source", "build", "runtime", "formalPackageTrust", "mainConsumption")}))
        return 0
    except Exception as error:
        failure = {"phase": args.phase, "failure": str(error), "formalPackageTrust": "BLOCKED", "mainConsumption": "NOT_RUN"}
        if root is not None:
            path = root / f"{args.phase}-failure-{time.time_ns()}.json"
            dump(path, failure)
        print(json.dumps(failure), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
