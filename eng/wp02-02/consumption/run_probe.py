#!/usr/bin/env python3
"""Offline archive signature / synthetic plumbing probe; never Main adoption.

Approval is a separately pinned consumer-lock-shaped document with repository,
sourceGitSha, and seven packages (packageId/version/sha256/sourceGitSha).
The caller must obtain its SHA-256 from independent committed evidence, not from
an inspected package or this program. This hash authorizes local inspection only.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

PACKAGE_NAMES = {"CP6.Platform." + name for name in (
    "Contracts", "Abstractions", "AspNetCore", "Messaging", "EntityFramework", "Deployment", "Release")}
PROBE_NAMES = {"CP6.Platform." + name for name in ("Contracts", "Abstractions", "AspNetCore", "Messaging")}


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def save(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def require(value: bool, message: str) -> None:
    if not value:
        raise ValueError(message)


def extract_approved(approval_path: Path, approved_hash: str, packages: Path, output: Path) -> list[dict]:
    raw = approval_path.read_bytes()
    require(digest(raw) == approved_hash, "independent approval document hash mismatch")
    approval = json.loads(raw)
    require(approval.get("repository") == "https://github.com/GTX537/CP6.Platform", "repository mismatch")
    source = approval.get("sourceGitSha", "")
    require(re.fullmatch(r"[0-9a-f]{40}", source) is not None, "full source SHA required")
    rows = approval["packages"]
    require(len(rows) == 7 and {row["packageId"] for row in rows} == PACKAGE_NAMES, "exact seven-package approval required")
    require(len({row["version"] for row in rows}) == 1, "mixed approved versions")
    result = []
    for row in sorted(rows, key=lambda value: value["packageId"]):
        name, version = row["packageId"], row["version"]
        require(re.fullmatch(r"[0-9A-Za-z.+-]+", version) is not None, "invalid package version")
        require(row["sourceGitSha"] == source, "mixed approved source")
        require(re.fullmatch(r"[0-9a-f]{64}", row["sha256"]) is not None, "invalid approved hash")
        archive_path = packages / f"{name}.{version}.nupkg"
        raw_archive = archive_path.read_bytes()
        require(digest(raw_archive) == row["sha256"], f"archive hash mismatch: {name}")
        import io
        with zipfile.ZipFile(io.BytesIO(raw_archive)) as archive:
            names = archive.namelist()
            require(len(names) == len(set(names)), f"duplicate ZIP member: {name}")
            specs = [item for item in names if item.endswith(".nuspec")]
            require(len(specs) == 1, f"one nuspec required: {name}")
            root = ET.fromstring(archive.read(specs[0]))
            metadata = root.find("{*}metadata")
            require(metadata is not None, "nuspec metadata missing")
            require(metadata.findtext("{*}id") == name and metadata.findtext("{*}version") == version, "nuspec identity mismatch")
            repo = metadata.find("{*}repository")
            require(repo is not None and repo.get("commit") == source
                    and repo.get("url", "").removesuffix(".git") == approval["repository"], "nuspec repository/source mismatch")
            entry = {**row, "archivePath": str(archive_path.resolve()), "dll": None}
            if name in PROBE_NAMES:
                member = f"lib/net8.0/{name}.dll"
                data = archive.read(member)
                (output / f"{name}.dll").write_bytes(data)
                entry["dll"] = {"member": member, "sha256": digest(data)}
            result.append(entry)
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--approval", type=Path, required=True)
    parser.add_argument("--approval-sha256", required=True)
    parser.add_argument("--packages", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--mode", choices=("runtime", "inspect"), default="runtime")
    parser.add_argument("--inspector", type=Path, help="Already-built task-private Probe.dll, required for metadata-only inspection")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    report = {"scope": "PREPARATION_ONLY", "mainHostAdoption": "NOT_RUN", "formalAdmission": "BLOCKED",
              "runtimeBusinessAcceptance": "NOT_RUN", "packageDependencyCompatibility": "Unknown",
              "mode": args.mode, "result": "FAIL",
              "approvalPath": str(args.approval.resolve()), "approvalSha256": args.approval_sha256,
              "python": sys.version, "pythonExecutable": sys.executable, "commands": []}
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1")

    def command(name: str, command_args: list[str]) -> str:
        completed = subprocess.run(command_args, cwd=output, env=env, text=True, encoding="utf-8", errors="replace",
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
        (output / (name + ".log")).write_text(completed.stdout, encoding="utf-8")
        report["commands"].append({"argv": command_args, "exitCode": completed.returncode, "log": name + ".log",
                                   "warningCodes": sorted(set(re.findall(r"\bwarning ([A-Z]+[0-9]+):", completed.stdout)))})
        require(completed.returncode == 0, f"{name} failed (see retained log)")
        return completed.stdout

    try:
        dlls = output / "archive-dlls"
        dlls.mkdir()
        report["packages"] = extract_approved(args.approval, args.approval_sha256, args.packages, dlls)
        dotnet = str(args.dotnet.resolve())
        # Isolate SDK choice, build outputs and all restore settings in this exclusive attempt.
        save(output / "global.json", {"sdk": {"version": "8.0.424", "rollForward": "disable", "allowPrerelease": False}})
        require(command("sdk-version", [dotnet, "--version"]).strip() == "8.0.424", "SDK must be 8.0.424")
        command("dotnet-info", [dotnet, "--info"])
        if args.mode == "runtime":
            source = Path(__file__).resolve().parent / "probe"
            build = output / "probe-source"
            build.mkdir()
            sources = {}
            for item in sorted(source.iterdir()):
                if item.is_file():
                    shutil.copyfile(item, build / item.name)
                    sources[item.name] = digest(item.read_bytes())
            report["probeSourceSha256"] = sources
            project = str(build / "Probe.csproj")
            prop = "-p:ProbeArchiveDirectory=" + str(dlls)
            command("restore", [dotnet, "restore", project, "--configfile", str(build / "NuGet.Config"), prop])
            command("build", [dotnet, "build", project, "--no-restore", "-c", "Release", prop])
            inspector = build / "bin/Release/net8.0/Probe.dll"
        else:
            require(args.inspector is not None, "metadata-only inspection needs the previously built task probe")
            inspector = args.inspector.resolve()
        report["inspector"] = {"path": str(inspector), "sha256": digest(inspector.read_bytes())}
        metadata = command("archive-metadata", [dotnet, str(inspector), "inspect", str(dlls)])
        metadata_result = json.loads(metadata)
        save(output / "archive-metadata.json", metadata_result)
        if args.mode == "runtime":
            require(not metadata_result["missingRequiredTypes"], "required context API types are missing")
            runtime = command("runtime", [dotnet, str(inspector), "runtime", str(dlls)])
            save(output / "signatures-and-synthetic-runtime.json", json.loads(runtime))
        report["result"] = "PASS"
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    finally:
        report["runnerSha256"] = digest(Path(__file__).read_bytes())
        report["artifacts"] = {str(path.relative_to(output)).replace("\\", "/"): digest(path.read_bytes())
                               for path in sorted(output.rglob("*")) if path.is_file() and path.name != "report.json"}
        save(output / "report.json", report)
    print(json.dumps({"result": report["result"], "report": str(output / "report.json"), "error": report.get("error")}))
    return 0 if report["result"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
