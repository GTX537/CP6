"""Read completed task-owner dependency evidence; never restore, build or alter locks."""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
from pathlib import Path
import sys
from urllib.parse import urlsplit

sys.dont_write_bytecode = True
from snapshot import MAIN_SHA, MAIN_TREE, PLATFORM_SHA, PLATFORM_TREE, logical, native, no_links, owner_root, sha256, walk_files

TASK = Path(__file__).resolve().parent
WORKTREE = TASK.parents[2]


def canonical(path: Path) -> Path:
    """Normalize extended Windows names for identity comparisons, never for IO."""
    value = str(path)
    if value.startswith("\\\\?\\UNC\\"):
        raise ValueError("UNC paths are outside this task-local owner contract")
    if value.startswith("\\\\?\\"):
        value = value[4:]
    original = logical(Path(value))
    # Check the caller's spelling before resolve can erase an owner-internal junction.
    no_links(original)
    return original.resolve()


def filesystem(path: Path) -> Path:
    return native(canonical(path))


def recorded_cache(root: Path, state: dict) -> Path | None:
    """Use executed restore argv, not cache folder discovery or environment variables."""
    recorded = set()
    for command in state.get("commands", []):
        args = command.get("argv", [])
        if "restore" not in args:
            continue
        if args.count("--packages") != 1 or args.index("--packages") + 1 >= len(args):
            raise ValueError("Restore command lacks its unambiguous isolated cache argument")
        cache = canonical(Path(args[args.index("--packages") + 1]))
        if cache not in {canonical(root / "nuget-cache"), canonical(root / "n")}:
            raise ValueError("Recorded restore cache is outside the exact old/new owner cache locations")
        no_links(filesystem(cache))
        recorded.add(cache)
    if len(recorded) > 1:
        raise ValueError("A single owner cannot mix distinct package caches")
    return next(iter(recorded)) if recorded else None


def contained(root: Path, path: Path, *, exists=True) -> Path:
    path = canonical(path)
    no_links(filesystem(path))
    if not path.is_relative_to(canonical(root)) or (exists and not filesystem(path).is_file()):
        raise ValueError("Dependency evidence path is missing or outside this owner")
    return path


def read(root: Path, path: Path):
    return json.loads(filesystem(contained(root, path)).read_text(encoding="utf-8-sig"))


def identity(root: Path, path: Path) -> dict:
    path = contained(root, path)
    actual = filesystem(path)
    return {"path": path.relative_to(root).as_posix(), "sha256": sha256(actual.read_bytes()), "size": actual.stat().st_size}


def project_location(root: Path, name: str, path: Path) -> str:
    path = contained(root, path)
    if name.casefold().startswith("cp6.platform."):
        expected = root / "snapshots/platform/src" / name / (name + ".csproj")
        if path != canonical(expected):
            raise ValueError("Platform project is not at its exact fixed-source snapshot path")
    elif not (path.is_relative_to(root / "snapshots/main") or path == root / "probe/Probe.csproj"):
        raise ValueError("Consumer project is outside the Main/probe snapshot")
    return path.relative_to(root).as_posix()


def package_evidence(root: Path, key: str, library: dict, cache: Path | None = None) -> dict:
    name, version = key.rsplit("/", 1)
    if name.casefold().startswith("cp6.platform."):
        raise ValueError("Residual Platform package is forbidden in the source graph")
    relative = library.get("path", "")
    if not relative or Path(relative).is_absolute() or ".." in Path(relative).parts or "\\" in relative:
        raise ValueError("Unsafe package-cache relative path")
    expected = f"{name.lower()}/{version.lower()}"
    if relative != expected:
        raise ValueError("Package cache path does not match the assets package identity")
    cache = canonical(cache or root / "nuget-cache")
    if cache not in {canonical(root / "nuget-cache"), canonical(root / "n")}:
        raise ValueError("Package cache is outside the exact old/new owner cache locations")
    folder = cache / relative
    stem = f"{name.lower()}.{version.lower()}.nupkg"
    archive = contained(root, folder / stem)
    metadata_path = contained(root, folder / ".nupkg.metadata")
    content_path = contained(root, folder / (stem + ".sha512"))
    metadata = read(root, metadata_path)
    source = metadata.get("source", "")
    parsed = urlsplit(source)
    if parsed.scheme != "https" or not parsed.netloc or parsed.username or parsed.password or parsed.query or parsed.fragment:
        raise ValueError("Package metadata source is not a credential-free HTTPS source")
    content_hash = metadata.get("contentHash")
    if not content_hash or content_hash != library.get("sha512"):
        raise ValueError("NuGet content hash differs between assets and metadata")
    cache_archive_hash = filesystem(content_path).read_text(encoding="utf-8-sig").strip()
    digest256 = hashlib.sha256()
    digest512 = hashlib.sha512()
    with filesystem(archive).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest256.update(block)
            digest512.update(block)
    archive_hash = base64.b64encode(digest512.digest()).decode("ascii")
    if cache_archive_hash != archive_hash:
        raise ValueError("Raw archive SHA512 differs from the cache .nupkg.sha512 file")
    return {"identity": key, "cachePath": folder.relative_to(root).as_posix(), "source": source,
            "metadata": identity(root, metadata_path), "cacheContentHashFile": identity(root, content_path),
            "archivePath": archive.relative_to(root).as_posix(), "archiveSize": filesystem(archive).stat().st_size,
            "archiveSha256": digest256.hexdigest(), "rawArchiveSha512Base64": archive_hash,
            "cacheArchiveSha512Base64": cache_archive_hash,
            "nugetContentHash": content_hash,
            "hashSemantics": "NuGet contentHash must match metadata/assets/lock/deps; raw archive SHA512 must independently match cache .nupkg.sha512. The two groups are not required to equal each other.",
            "signatureValidation": "RESTORE_EVIDENCE_ONLY_NO_NEW_CRYPTO_VALIDATION"}


def lock_delta(before: dict, after: dict) -> list[dict]:
    changes = []
    old = before.get("dependencies", {})
    new = after.get("dependencies", {})
    for target in sorted(set(old) | set(new)):
        for name in sorted(set(old.get(target, {})) | set(new.get(target, {}))):
            left = old.get(target, {}).get(name)
            right = new.get(target, {}).get(name)
            if left != right:
                changes.append({"target": target, "name": name, "before": left, "after": right})
    return changes


def inspect_assets(root: Path, path: Path, packages: dict, cache: Path) -> dict:
    data = read(root, path)
    restore = data.get("project", {}).get("restore", {})
    project_path = canonical(Path(restore["projectPath"]))
    project_name = project_path.stem
    project = project_location(root, project_name, project_path)
    expected_cache = canonical(cache)
    cache_paths = list(data.get("packageFolders", {}))
    if not cache_paths or any(canonical(Path(folder)) != expected_cache for folder in cache_paths):
        raise ValueError("Assets packageFolders contains a cache outside the isolated owner cache")
    if canonical(Path(restore.get("packagesPath", ""))) != expected_cache:
        raise ValueError("Restore packagesPath differs from the isolated owner cache")
    allowed_sources = set(restore.get("sources", {}))
    libraries = data.get("libraries", {})
    project_refs = []
    for framework, settings in restore.get("frameworks", {}).items():
        for reference, details in settings.get("projectReferences", {}).items():
            target = canonical(Path(details.get("projectPath", reference)))
            location = project_location(root, target.stem, target)
            project_refs.append({"framework": framework, "path": location})
    normalized_libraries = {}
    for key, library in libraries.items():
        row = dict(library)
        if library["type"] == "package":
            # Validate per-graph hash agreement even when archive hashing can be reused.
            if key not in packages:
                packages[key] = package_evidence(root, key, library, cache)
            elif packages[key]["nugetContentHash"] != library.get("sha512"):
                raise ValueError("Package identity has conflicting content hashes across graph roots")
            if packages[key]["source"] not in allowed_sources:
                raise ValueError("Actual cache source is absent from this project's restore source inventory")
        elif library["type"] == "project":
            project_file = library.get("msbuildProject", library.get("path", ""))
            if not project_file:
                raise ValueError("Project library lacks its actual MSBuild project path")
            target = canonical(project_path.parent / project_file)
            row["resolvedOwnerProjectPath"] = project_location(root, key.rsplit("/", 1)[0], target)
        else:
            raise ValueError("Unexpected assets library type")
        normalized_libraries[key] = row
    targets = data.get("targets", {})
    for target in targets.values():
        for key, row in target.items():
            if key not in libraries:
                raise ValueError("Assets target dependency lacks its library identity")
            if key.split("/", 1)[0].casefold().startswith("cp6.platform.") and row.get("type") != "project":
                raise ValueError("Assets target retained a Platform package")
    return {"input": identity(root, path), "project": project, "packageFolders": [expected_cache.relative_to(root).as_posix()],
            "restoreSourceInventory": sorted(allowed_sources), "projectReferences": project_refs,
            "projectFileDependencyGroups": data.get("projectFileDependencyGroups", {}),
            "libraries": normalized_libraries, "targets": targets, "restoreLogs": data.get("logs", [])}


def inspect_owner(root: Path) -> dict:
    state_path = root / "state.json"
    state = read(root, state_path)
    if state.get("build") not in ("PASS", "FAIL"):
        raise ValueError("Graph inspection requires a completed build phase, including completed failure")
    manifest_path = root / "snapshot-manifest.json"
    manifest = read(root, manifest_path)
    if state.get("snapshotManifestSha256") != sha256(filesystem(manifest_path).read_bytes()) or state.get("owner") != root.name or manifest.get("owner") != root.name:
        raise ValueError("Owner/source manifest binding differs from recorded preparation")
    for key, sha, tree in (("main", MAIN_SHA, MAIN_TREE), ("platform", PLATFORM_SHA, PLATFORM_TREE)):
        if manifest["sources"][key]["sha"] != sha or manifest["sources"][key]["tree"] != tree:
            raise ValueError("Source snapshot identity differs from the fixed admission baseline")
    report = {"schemaVersion": 1, "owner": root.name, "evidenceKind": "ACTUAL_LOCAL_DEPENDENCY_GRAPH",
              "status": "UNKNOWN", "compatibility": "UNKNOWN", "buildStatus": state["build"],
              "buildRoots": state.get("buildRoots", {}), "formalPackageTrust": "BLOCKED", "mainConsumption": "NOT_RUN",
              "sourceIdentity": {key: {field: manifest["sources"][key][field] for field in ("sha", "tree")} for key in ("main", "platform")},
              "inputs": [identity(root, state_path), identity(root, manifest_path)],
              "assets": [], "deps": [], "locks": [], "packages": [], "errors": [], "diagnosis": {}}
    packages = {}
    cache = recorded_cache(root, state)
    report["cache"] = {"path": cache.relative_to(root).as_posix() if cache else None,
                       "authority": "completed state's executed restore --packages arguments; old nuget-cache or new n only"}
    all_files = []
    for scope in (root / "snapshots", root / "probe"):
        if filesystem(scope).is_dir():
            all_files.extend(path for path in walk_files(scope) if path.name == "project.assets.json" or path.name.endswith((".deps.json", ".wp02-02.lock.json")))
    for path in sorted(all_files):
        relative = path.relative_to(root).as_posix()
        try:
            if path.name == "project.assets.json":
                if cache is None:
                    raise ValueError("Assets exist without a recorded executed restore cache")
                report["assets"].append(inspect_assets(root, path, packages, cache))
            elif path.name.endswith(".deps.json"):
                data = read(root, path)
                for key, library in data.get("libraries", {}).items():
                    if key.split("/", 1)[0].casefold().startswith("cp6.platform.") and library.get("type") != "project":
                        raise ValueError("Deps file contains a residual Platform package")
                report["deps"].append({"input": identity(root, path), "runtimeTarget": data.get("runtimeTarget"),
                                       "libraries": data.get("libraries", {}), "targets": data.get("targets", {})})
            else:
                data = read(root, path)
                original = path.with_name("packages.lock.json")
                old = read(root, original) if filesystem(original).exists() else {"dependencies": {}}
                for dependencies in data.get("dependencies", {}).values():
                    for name, entry in dependencies.items():
                        if name.casefold().startswith("cp6.platform.") and entry.get("type", "").casefold() != "project":
                            raise ValueError("Generated lock contains a residual Platform package")
                item = {"input": identity(root, path), "original": identity(root, original) if filesystem(original).exists() else None,
                        "dependencies": data.get("dependencies", {}), "delta": lock_delta(old, data)}
                report["locks"].append(item)
        except (ValueError, KeyError, OSError, TypeError) as error:
            report["errors"].append({"path": relative, "type": type(error).__name__, "message": str(error)})

    # Original locks must still match both Git-byte fingerprints and the separate originals copy.
    originals = []
    original_focus = []
    for source in ("main", "platform"):
        for entry in manifest["sources"][source]["files"]:
            if not entry.get("exported") or not entry["path"].endswith("packages.lock.json"):
                continue
            original = root / "snapshots" / source / entry["path"]
            saved = root / "originals" / source / entry["path"]
            try:
                first, second = identity(root, original), identity(root, saved)
                if first["sha256"] != entry["sha256"] or second["sha256"] != entry["sha256"]:
                    raise ValueError("Original lock bytes differ from the pinned Git snapshot")
                originals.append({"snapshot": first, "saved": second})
                for target, entries in read(root, original).get("dependencies", {}).items():
                    for name, dependency in entries.items():
                        if name == "System.Diagnostics.DiagnosticSource" or name.startswith("OpenTelemetry"):
                            original_focus.append({"source": source, "lock": first["path"], "target": target, "name": name, "dependency": dependency})
            except (ValueError, KeyError, OSError) as error:
                report["errors"].append({"path": original.relative_to(root).as_posix(), "type": type(error).__name__, "message": str(error)})
    report["originalLocks"] = originals
    report["packages"] = [packages[key] for key in sorted(packages)]
    # Bind every package record in deps/private locks back to the observed cache graph.
    # NuGet's hash is a different concept from the separately recorded archive digest.
    package_lookup = {key.casefold(): value for key, value in packages.items()}
    for deps in report["deps"]:
        for key, library in deps["libraries"].items():
            if library.get("type") != "package":
                continue
            found = package_lookup.get(key.casefold())
            content_hash = library.get("sha512", "").removeprefix("sha512-")
            if found is None or content_hash != found["nugetContentHash"]:
                report["errors"].append({"type": "DepsPackageHashMismatch", "path": deps["input"]["path"], "package": key})
    for lock in report["locks"]:
        for target, entries in lock["dependencies"].items():
            for name, library in entries.items():
                if library.get("type", "").casefold() == "project":
                    continue
                key = name + "/" + library.get("resolved", "")
                found = package_lookup.get(key.casefold())
                if found is None or library.get("contentHash") != found["nugetContentHash"]:
                    report["errors"].append({"type": "LockPackageHashMismatch", "path": lock["input"]["path"], "target": target, "package": key})
    expected_projects = {"snapshots/main/" + value for value in manifest["mainProjectClosure"]}
    expected_projects.update("snapshots/platform/src/" + name + "/" + name + ".csproj" for name in
                             ("CP6.Platform.Abstractions", "CP6.Platform.AspNetCore", "CP6.Platform.Contracts", "CP6.Platform.EntityFramework", "CP6.Platform.Messaging"))
    expected_projects.add("probe/Probe.csproj")
    found_projects = {item["project"] for item in report["assets"]}
    missing_projects = sorted(expected_projects - found_projects)
    if missing_projects:
        report["errors"].append({"type": "MissingAssets", "projects": missing_projects})
    expected_deps = {"snapshots/main/CP6.WebApi/bin/Release/net8.0/CP6.WebApi.deps.json",
                     "snapshots/main/tools/CP6.DatabaseCompatibility.Probe/bin/Release/net8.0/CP6.DatabaseCompatibility.Probe.deps.json",
                     "probe/bin/Release/net8.0/Probe.deps.json"}
    missing_deps = sorted(expected_deps - {item["input"]["path"] for item in report["deps"]})
    report["diagnosis"] = {
        "focusLibraries": {key: {"nugetContentHash": value["nugetContentHash"], "source": value["source"], "archiveSha256": value["archiveSha256"]}
                           for key, value in packages.items() if key.startswith(("System.Diagnostics.DiagnosticSource/", "OpenTelemetry"))},
        "originalFocusLibraries": original_focus,
        "missingProjectAssets": missing_projects, "missingRootDeps": missing_deps,
        "legacyFailure": "Prior four-DLL probe omitted transitive dependencies. This report observes the complete restored graph where present; it does not downgrade Main DiagnosticSource or erase the prior MSB3277 failure.",
        "scope": "Restore/build artifacts only; runtime identity and compatibility require the separate actual runtime policy report.",
        "signatureScope": "Cache metadata and recorded restore only. No new package signature, feed round-trip, Current revocation, or formal Platform trust validation."}
    if missing_deps:
        report["errors"].append({"type": "MissingRootDeps", "paths": missing_deps})
    report["status"] = "PASS" if not report["errors"] and state["build"] == "PASS" else "UNKNOWN" if state["build"] == "FAIL" else "FAIL"
    report["graphSha256"] = sha256(json.dumps({name: report[name] for name in ("sourceIdentity", "assets", "deps", "locks", "packages")}, sort_keys=True, separators=(",", ":")).encode())
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--owner", required=True)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        root = owner_root(WORKTREE, args.owner, args.root, create=False)
        output = contained(root, args.output, exists=False)
        if filesystem(output).exists() or output.parent != root or output.suffix != ".json":
            raise ValueError("Graph output must be a fresh JSON file directly inside this owner")
        report = inspect_owner(root)
        with filesystem(output).open("x", encoding="utf-8") as stream:
            json.dump(report, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
        print(json.dumps({"status": report["status"], "compatibility": report["compatibility"], "assets": len(report["assets"]), "packages": len(report["packages"]), "errors": len(report["errors"])}))
        return 0 if report["status"] == "PASS" else 1
    except (ValueError, KeyError, OSError, TypeError) as error:
        print(json.dumps({"status": "FAIL", "type": type(error).__name__, "message": str(error)}), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
