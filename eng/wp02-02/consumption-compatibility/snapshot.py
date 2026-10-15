"""Bounded Git-object projection. No checkout bytes or shared outputs are used."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import xml.etree.ElementTree as ET

MAIN_SHA = "1129953156b7775152a74ad2f51cb95ef430ee56"
PLATFORM_SHA = "b28db763f70ab6f164cb7c87d6ce49a6d97267f1"
MAIN_TREE = "fcf79cd38055adddab4a69e4910808c691d442b2"
PLATFORM_TREE = "4ae98434d6e87f3fdd852a156fd057c03e336e43"
CONFIG_HASH = "23ab29f2a03952fcf859213fe42a1e2c2a8eee933e8f92dd74c18f68f2b7be45"
ROOTS = ["CP6.WebApi/CP6.WebApi.csproj", "tools/CP6.DatabaseCompatibility.Probe/CP6.DatabaseCompatibility.Probe.csproj"]
OVERLAY = [
    ("CP6.Core/CP6.Core.csproj", "CP6.Platform.Messaging"),
    ("CP6.Core/CP6.Core.csproj", "CP6.Platform.EntityFramework"),
    ("CP6.WebApi/CP6.WebApi.csproj", "CP6.Platform.AspNetCore"),
    ("tools/CP6.DatabaseCompatibility.Probe/CP6.DatabaseCompatibility.Probe.csproj", "CP6.Platform.EntityFramework"),
]


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def logical(path: Path) -> Path:
    """Ordinary absolute path for identity, containment and human-readable evidence."""
    value = str(path)
    if os.name == "nt":
        if value.startswith("\\\\?\\UNC\\"):
            value = "\\\\" + value[8:]
        elif value.startswith("\\\\?\\"):
            value = value[4:]
    return Path(value)


def native(path: Path) -> Path:
    """Filesystem spelling that does not depend on Windows process MAX_PATH opt-in."""
    path = logical(path)
    if os.name != "nt":
        return path
    value = os.path.abspath(path)
    return Path("\\\\?\\UNC\\" + value[2:] if value.startswith("\\\\") else "\\\\?\\" + value)


def walk_files(root: Path):
    """Walk extended paths, checking each node once; never follow reparse points."""
    root = logical(root)
    no_links(root)
    pending = [root]
    while pending:
        directory = pending.pop()
        with os.scandir(native(directory)) as entries:
            for entry in entries:
                metadata = entry.stat(follow_symlinks=False)
                path = directory / entry.name
                if stat.S_ISLNK(metadata.st_mode) or getattr(metadata, "st_file_attributes", 0) & 0x400:
                    raise ValueError(f"Link/reparse point forbidden: {path}")
                if stat.S_ISDIR(metadata.st_mode):
                    pending.append(path)
                elif stat.S_ISREG(metadata.st_mode):
                    yield path
                else:
                    raise ValueError(f"Nonregular owner entry forbidden: {path}")


def dump(path: Path, data) -> None:
    native(path).write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def safe_relative(value: str) -> PurePosixPath:
    path = PurePosixPath(value)
    if not value or "\\" in value or ":" in value or path.is_absolute() or any(part in ("", ".", "..") for part in value.split("/")):
        raise ValueError(f"Unsafe Git relative path: {value!r}")
    return path


def no_links(path: Path) -> None:
    """Reject symlinks and all Windows reparse points, including junctions."""
    path = logical(path)
    for candidate in (path, *path.parents):
        try:
            metadata = native(candidate).lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(metadata.st_mode) or getattr(metadata, "st_file_attributes", 0) & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400):
            raise ValueError(f"Link/reparse point forbidden: {candidate}")


def owner_root(worktree: Path, owner: str, output: Path, *, create: bool) -> Path:
    if not re.fullmatch(r"[0-9a-f]{32}", owner):
        raise ValueError("owner must be exactly 32 lowercase hexadecimal characters")
    worktree = logical(worktree).absolute()
    output = logical(output)
    if not output.is_absolute() or ".." in output.parts:
        raise ValueError("output must be an absolute canonical owner path")
    expected = worktree / "artifacts/wp02-02/consumption-compatibility" / owner
    no_links(worktree)
    no_links(output)
    if output != expected or output.resolve() != expected.absolute():
        raise ValueError("output is outside the exact task worktree owner root")
    if create:
        native(expected.parent).mkdir(parents=True, exist_ok=True)
        no_links(expected.parent)
        try:
            native(expected).mkdir(exist_ok=False)
        except FileExistsError as error:
            raise ValueError("owner directory already exists; use a fresh owner") from error
    elif not native(output).is_dir():
        raise ValueError("prepared owner directory does not exist")
    return output


def validate_plan(data: dict) -> None:
    expected = {
        "schemaVersion": 1, "scope": "SOURCE_COMPATIBILITY_PREPARATION_ONLY", "sdk": "8.0.424",
        "main": {"sha": MAIN_SHA, "tree": MAIN_TREE, "nugetConfigSha256": CONFIG_HASH},
        "platform": {"sha": PLATFORM_SHA, "tree": PLATFORM_TREE}, "buildRoots": ROOTS,
        "overlay": [{"project": project, "package": package, "version": "[0.10.2]", "sourceProject": f"src/{package}/{package}.csproj"} for project, package in OVERLAY],
        "formalPackageTrust": "BLOCKED", "mainConsumption": "NOT_RUN",
    }
    if data != expected:
        raise ValueError("Input plan differs from the exact authorized source/tree/SDK/config/four-reference contract")


def git(repo: Path, *args: str) -> bytes:
    return subprocess.check_output(["git", "-C", str(repo), *args], stderr=subprocess.PIPE)


def project_closure(repo: Path, sha: str, roots: list[str]) -> list[str]:
    pending = list(roots)
    result = set()
    while pending:
        current = pending.pop()
        safe_relative(current)
        if current in result:
            continue
        result.add(current)
        xml = ET.fromstring(git(repo, "show", f"{sha}:{current}"))
        for element in xml.iter("ProjectReference"):
            reference = element.attrib["Include"].replace("\\", "/")
            if "$" in reference or Path(reference).is_absolute():
                raise ValueError(f"Nonliteral project reference: {current}: {reference}")
            normalized = os.path.normpath(str(PurePosixPath(current).parent / reference)).replace("\\", "/")
            safe_relative(normalized)
            pending.append(normalized)
    return sorted(result)


def export_git(repo: Path, sha: str, destination: Path, selected) -> dict:
    tree = git(repo, "rev-parse", f"{sha}^{{tree}}").decode().strip()
    rows = []
    for entry in git(repo, "ls-tree", "-rlz", sha).split(b"\0"):
        if not entry:
            continue
        header, name = entry.split(b"\t", 1)
        mode, kind, blob, length = header.decode("ascii").split()
        path = name.decode("utf-8")
        safe_relative(path)
        included = bool(selected(path))
        if included and (kind != "blob" or mode not in ("100644", "100755")):
            raise ValueError(f"Selected source is not an ordinary Git blob: {path}")
        rows.append({"path": path, "mode": mode, "kind": kind, "gitBlob": blob, "size": int(length) if length != "-" else None, "exported": included})
    native(destination).mkdir(parents=True, exist_ok=False)
    # Batch Git-object streaming avoids command-line quoting and checkout filters/LFS smudging.
    process = subprocess.Popen(["git", "-C", str(repo), "cat-file", "--batch"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    try:
        for row in rows:
            if not row["exported"]:
                continue
            process.stdin.write((row["gitBlob"] + "\n").encode("ascii"))
            process.stdin.flush()
            header = process.stdout.readline().decode("ascii").strip().split()
            if header != [row["gitBlob"], "blob", str(row["size"])]:
                raise ValueError(f"Git object identity mismatch: {row['path']}")
            data = process.stdout.read(row["size"])
            if len(data) != row["size"] or process.stdout.read(1) != b"\n":
                raise ValueError("Truncated Git object stream")
            if data.startswith(b"version https://git-lfs.github.com/spec/v1\n"):
                raise ValueError(f"Selected build closure contains unresolved LFS pointer: {row['path']}")
            target = destination / row["path"]
            native(target.parent).mkdir(parents=True, exist_ok=True)
            no_links(target)
            native(target).write_bytes(data)
            row["sha256"] = sha256(data)
        process.stdin.close()
        if process.wait(timeout=30) != 0:
            raise ValueError("Git cat-file failed")
    finally:
        if process.poll() is None:
            process.kill()
            process.wait()
        process.stdout.close()
        process.stderr.close()
    return {"sha": sha, "tree": tree, "projection": "selected-build-closure; full Git tree inventory retained", "files": rows}


def replace_reference(data: bytes, package: str, target: str) -> bytes:
    expected = f'<PackageReference Include="{package}" Version="[0.10.2]" />'.encode()
    if data.count(expected) != 1 or data.count(f'Include="{package}"'.encode()) != 1:
        raise ValueError(f"Expected exactly one original reference to {package}")
    if '"' in target or "<" in target or "&" in target:
        raise ValueError("Unsafe XML reference target")
    return data.replace(expected, f'<ProjectReference Include="{target}" />'.encode())


def verify_files(root: Path, rows: list[dict], overlays: dict | None = None) -> None:
    overlays = overlays or {}
    expected = {row["path"] for row in rows if row["exported"]}
    for actual in walk_files(root):
        relative = actual.relative_to(root)
        if relative.as_posix() not in expected and not any(part in ("bin", "obj") for part in relative.parts) and actual.name != "packages.wp02-02.lock.json":
            raise ValueError(f"Unexpected snapshot input: {relative.as_posix()}")
    for row in rows:
        if not row["exported"]:
            continue
        path = root / row["path"]
        if not native(path).is_file() or sha256(native(path).read_bytes()) != overlays.get(row["path"], row["sha256"]):
            raise ValueError(f"Snapshot byte invariant failed: {row['path']}")


def apply_overlay(main: Path, platform: Path, originals: Path) -> list[dict]:
    records = []
    native(originals).mkdir(parents=True, exist_ok=False)
    for project, package in OVERLAY:
        source = main / project
        target = platform / f"src/{package}/{package}.csproj"
        if not native(target).is_file():
            raise ValueError(f"Missing fixed Platform source: {package}")
        original = native(source).read_bytes()
        saved = originals / project
        if not native(saved).exists():
            native(saved.parent).mkdir(parents=True, exist_ok=True)
            native(saved).write_bytes(original)
        relative = os.path.relpath(target, source.parent).replace("\\", "/")
        changed = replace_reference(original, package, relative)
        native(source).write_bytes(changed)
        records.append({"project": project, "package": package, "sourceProject": relative, "beforeSha256": sha256(original), "afterSha256": sha256(changed)})
    return records
