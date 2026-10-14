"""Read fixed Git objects and dependency declarations; never restore or edit product inputs."""
import argparse
import datetime
import hashlib
import json
from pathlib import Path, PurePosixPath
import posixpath
import subprocess
import sys
import xml.etree.ElementTree as ET


MAIN = "1ab2d031e3c501662ecb89a33839fac1cba55d33"
PLATFORM = "e5c63a28c94bae4cc66ec50fbe6079553c03d0d0"
RUNTIME = ["CP6.Platform." + x for x in ("Abstractions", "AspNetCore", "Contracts", "EntityFramework", "Messaging")]


def git(root, *args):
    return subprocess.check_output(["git", "-c", "core.longpaths=true", "-C", str(root), *args])


def sha(data):
    return hashlib.sha256(data).hexdigest()


def capture(main, platform, dotnet):
    admission_path = "docs/development/evidence/wave6-dispatch-20261013/admission-consumption.json"
    admission_bytes = git(main, "show", MAIN + ":" + admission_path)
    admission = json.loads(admission_bytes)
    records = []
    for name, paths in admission["fingerprints"].items():
        root = main if name == "Main" else platform
        for r in paths:
            data = git(root, "show", r["source_sha"] + ":" + r["path"])
            if sha(data) != r["sha256"]:
                raise ValueError("Admission fingerprint mismatch: " + r["path"])
            records.append({"repository": name, "path": r["path"], "source": r["source_sha"], "blob": r["blob"], "sha256": sha(data)})
    projects = []
    locks = []
    files = git(main, "ls-tree", "-r", "--name-only", MAIN).decode().splitlines()
    for path in files:
        if not (path.endswith(".csproj") or path.endswith("/packages.lock.json")):
            continue
        data = git(main, "show", MAIN + ":" + path)
        if path.endswith(".csproj"):
            doc = ET.fromstring(data)
            refs = []
            for e in doc.iter("ProjectReference"):
                inc = e.get("Include", "").replace("\\", "/")
                refs.append({"include": inc, "resolved": posixpath.normpath(str(PurePosixPath(path).parent / inc)), "condition": e.get("Condition")})
            packages = [{"id": e.get("Include") or e.get("Update"), "version": e.get("Version") or e.findtext("Version"),
                         "condition": e.get("Condition")} for e in doc.iter("PackageReference")]
            projects.append({"path": path, "sha256": sha(data), "target_framework": doc.findtext(".//TargetFramework"),
                             "target_frameworks": doc.findtext(".//TargetFrameworks"), "projects": refs, "packages": packages})
        else:
            doc = json.loads(data)
            deps = [{"tfm": tfm, "id": k, **v} for tfm, group in doc["dependencies"].items()
                    for k, v in group.items() if k.startswith("CP6.Platform.")]
            if deps:
                locks.append({"path": path, "sha256": sha(data), "platform_dependencies": deps,
                              "scope": "HISTORICAL_TOOL_EXCLUDED" if path.startswith("tools/p10/") else "REGENERATE_AFTER_APPROVED_ADOPTION"})
    graph = {p["path"]: [r["resolved"] for r in p["projects"]] for p in projects}
    def closure(start):
        seen = set()
        def visit(p):
            if p in seen: return
            seen.add(p)
            for child in graph.get(p, []): visit(child)
        visit(start)
        return sorted(seen)
    web = next(l for l in locks if l["path"] == "CP6.WebApi/packages.lock.json")
    actual = sorted(set(d["id"] for d in web["platform_dependencies"]))
    if actual != RUNTIME:
        raise ValueError("Unexpected Main runtime package closure: " + repr(actual))
    source_inputs = []
    for name, root, commit in (("Main", main, MAIN), ("Platform", platform, PLATFORM)):
        entries = git(root, "ls-tree", "-r", "--name-only", commit).decode().splitlines()
        for path in entries:
            if path.endswith((".csproj", "packages.lock.json")) or path in ("NuGet.config", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json"):
                data = git(root, "show", commit + ":" + path)
                work = root / path
                current = work.read_bytes() if work.is_file() else None
                source_inputs.append({"repository": name, "source": commit, "path": path, "sha256": sha(data),
                                      "working_sha256": sha(current) if current is not None else None,
                                      "working_exact": current == data, "working_text_lf_equivalent": current is not None and current.replace(b"\r\n", b"\n") == data.replace(b"\r\n", b"\n")})
    pgt_data = git(main, "show", MAIN + ":docs/development/acceptance/postgresql-tenancy.json")
    pgt = [c for c in json.loads(pgt_data)["cases"] if c["id"] in admission["required_pgt_cases"]]
    if len(pgt) != 17 or not all(c["status"] == "NOT_RUN" and c["execution_evidence"] == [] for c in pgt):
        raise ValueError("Original 17 PGT cases or their NOT_RUN boundary changed")
    return {"task": "WP02-02", "mode": "PREPARATION_ONLY", "state": "BLOCKED", "entry_gate": "NOT_CHECKED",
            "captured_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(), "python": sys.version,
            "repositories": [{"name": n, "url": u, "source": c, "tree": git(r, "rev-parse", c + "^{tree}").decode().strip(),
                              "main": git(r, "rev-parse", "main").decode().strip(), "origin_main": git(r, "rev-parse", "origin/main").decode().strip()}
                             for n, u, r, c in (("Main", "https://github.com/GTX537/CP6", main, MAIN), ("Platform", "https://github.com/GTX537/CP6.Platform", platform, PLATFORM))],
            "dotnet": {"path": str(dotnet), "sdk": subprocess.check_output([str(dotnet), "--version"], cwd=platform).decode().strip(),
                       "runtimes": subprocess.check_output([str(dotnet), "--list-runtimes"]).decode().splitlines()},
            "admission_sha256": sha(admission_bytes), "admission_fingerprints_verified": records,
            "source_dependency_inputs": source_inputs, "projects": projects, "platform_lock_inventory": locks,
            "webapi_project_closure": closure("CP6.WebApi/CP6.WebApi.csproj"), "webapi_platform_package_closure": actual,
            "closure_limit": "Static declared ProjectReference graph, not MSBuild condition evaluation; actual existing lock entries retained per TFM. No restore or host execution.",
            "proposed_formal_version": {"value": "0.10.3", "status": "PROPOSAL_ONLY", "producer_source": None, "publication": "NOT_RUN", "archive_hashes": None},
            "pgt_source_sha256": sha(pgt_data), "required_pgt_cases": pgt}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--main", type=Path, required=True)
    p.add_argument("--platform", type=Path, required=True)
    p.add_argument("--dotnet", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    if args.output.exists(): raise ValueError("Choose a new evidence file; never overwrite")
    result = capture(args.main.resolve(), args.platform.resolve(), args.dotnet.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(result, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    print(json.dumps({"projects": len(result["projects"]), "platform_locks": len(result["platform_lock_inventory"]),
                      "admission_anchors": len(result["admission_fingerprints_verified"]), "runtime_packages": len(RUNTIME), "pgt_not_run": len(result["required_pgt_cases"])}))


if __name__ == "__main__":
    main()
