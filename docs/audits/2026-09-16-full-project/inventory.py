"""Reproduce the read-only CP6 inventory with Python 3.8+ (standard library).

Only git-tracked paths are read. Metrics describe source text, not runtime coverage.
Usage: python inventory.py --core PATH --crm PATH --platform PATH --output PATH
"""
import argparse
import collections
import csv
import json
import pathlib
import re
import subprocess
import xml.etree.ElementTree as ET


def git(root, *args):
    return subprocess.check_output(["git", "-C", str(root), *args]).decode("utf-8")


def write_csv(output, name, fields, rows):
    with (output / name).open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)


def source_scope(path):
    if path.startswith("docs/"):
        return "reference"
    if path.split("/")[0] in {"tools", "eng", "scripts", "tests"}:
        return "engineering"
    return "application_or_library"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("core", "crm", "platform", "output"):
        parser.add_argument("--" + name, required=name != "crm", type=pathlib.Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    projects, surfaces, ui, persistence, largest = [], [], [], [], []
    summary = {"method": {
        "scope": "git ls-files at recorded HEAD; no database, HTTP or production verification",
        "lines": "splitlines including comments and blank lines; generated files included unless explicitly excluded",
        "api": "HTTP attributes in non-test *Controller.cs and MapGet/Post/Put/Delete/Patch/Methods calls in non-test C#; textual declarations, not expanded/runtime endpoint count",
        "persistence": "public DbSet<T> declarations in non-test C#; not distinct physical tables; excludes Set<T>/modelBuilder-only entities",
        "ui": "non-test .vue/.tsx/.razor/.axaml/.xaml tracked files; includes components/layouts and native resource XAML, not reachable-page count",
        "tests": "test/spec file naming heuristic; file counts never mean passed test cases or coverage"
    }, "repositories": {}}
    http = re.compile(r'\[Http(Get|Post|Put|Delete|Patch|Head|Options)\b([^\]]*)\]')
    minimal = re.compile(r'\.Map(Get|Post|Put|Delete|Patch|Methods)\s*\((?:"(?:[^"\\]|\\.)*"|[^\s,\n)]*)')
    dbset = re.compile(r'public\s+(?:virtual\s+)?DbSet<([^>]+)>\s+(\w+)')
    code_exts = {".cs", ".ts", ".tsx", ".vue", ".razor", ".xaml", ".axaml", ".js", ".mjs", ".py", ".ps1", ".sql"}
    for name in ("core", "crm", "platform"):
        root = getattr(args, name)
        if root is None:
            continue
        files = sorted(filter(None, git(root, "ls-files", "-z").split("\0")))
        ext_counts, group_counts, group_lines = collections.Counter(), collections.Counter(), collections.Counter()
        metrics = {"commit": git(root, "rev-parse", "HEAD").strip(), "tracked_files": len(files)}
        counters = collections.Counter()
        for path in files:
            suffix = pathlib.PurePosixPath(path).suffix.lower()
            ext_counts[suffix or "[none]"] += 1
            if suffix not in code_exts and suffix != ".csproj":
                continue
            content = (root / path).read_text(encoding="utf-8-sig", errors="replace")
            lines = len(content.splitlines())
            test = bool(re.search(r'(?i)(?:^|/)(?:tests?|__tests__)(?:/|$)|\.tests?/|(?:test|tests)\.cs$|\.(?:test|spec)\.(?:ts|tsx|js|mjs)$|\.Tests\.ps1$', path))
            fixture = bool(re.search(r'(?i)(?:fixture|test-fixtures)', path))
            generated = bool(re.search(r'(?i)/Migrations/|\.(?:Designer|g)\.cs$|ModelSnapshot\.cs$|/generated/', path))
            if suffix in code_exts:
                counters["source_text_files"] += 1
                counters["source_text_lines"] += lines
                group = path.split("/")[0]
                group_counts[group] += 1
                group_lines[group] += lines
                if test:
                    counters["test_source_files_heuristic"] += 1
                if suffix == ".cs":
                    counters["csharp_files"] += 1
                    if not test and not fixture and not generated:
                        counters["non_test_non_generated_csharp_files"] += 1
                        counters["non_test_non_generated_csharp_lines"] += lines
                        largest.append({"repository": name, "path": path, "lines": lines})
                if generated:
                    counters["generated_or_migration_source_files"] += 1
            if suffix == ".csproj":
                xml = ET.fromstring(content)
                val = lambda tag: ";".join((node.text or "") for node in xml.iter(tag))
                projects.append({"repository": name, "scope": source_scope(path), "path": path,
                    "sdk": xml.attrib.get("Sdk", ""), "framework": val("TargetFramework") or val("TargetFrameworks"),
                    "project_references": ";".join(node.attrib.get("Include", "") for node in xml.iter("ProjectReference")),
                    "package_references": ";".join(node.attrib.get("Include", "") + ("@" + node.attrib["Version"] if "Version" in node.attrib else "") for node in xml.iter("PackageReference"))})
            if not test and not fixture and suffix in {".vue", ".tsx", ".razor", ".xaml", ".axaml"}:
                ui.append({"repository": name, "scope": source_scope(path), "path": path, "lines": lines, "type": suffix[1:]})
            if suffix == ".cs" and not test and not fixture and not generated:
                for match in dbset.finditer(content):
                    persistence.append({"repository": name, "scope": source_scope(path), "path": path, "line": content.count("\n", 0, match.start()) + 1,
                        "entity_type": match.group(1), "property": match.group(2)})
                patterns = [minimal] + ([http] if path.endswith("Controller.cs") else [])
                for pattern in patterns:
                    for match in pattern.finditer(content):
                        surfaces.append({"repository": name, "scope": source_scope(path), "path": path, "line": content.count("\n", 0, match.start()) + 1,
                            "method": match.group(1).upper(), "declaration": match.group(0).strip()})
        metrics.update(counters)
        metrics["extensions"] = dict(sorted(ext_counts.items()))
        metrics["source_groups"] = {group: {"files": group_counts[group], "lines": group_lines[group]} for group in sorted(group_counts)}
        metrics["projects"] = sum(row["repository"] == name for row in projects)
        metrics["api_declarations"] = sum(row["repository"] == name for row in surfaces)
        metrics["ui_files"] = sum(row["repository"] == name for row in ui)
        metrics["dbset_declarations"] = sum(row["repository"] == name for row in persistence)
        metrics["declaration_scopes"] = {key: dict(collections.Counter(row["scope"] for row in rows if row["repository"] == name))
                                         for key, rows in (("projects", projects), ("api", surfaces), ("ui", ui), ("dbset", persistence))}
        summary["repositories"][name] = metrics
    largest.sort(key=lambda row: (-row["lines"], row["repository"], row["path"]))
    summary["largest_non_test_non_generated_csharp"] = largest[:30]
    (args.output / "inventory-summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    write_csv(args.output, "projects.csv", ["repository", "scope", "path", "sdk", "framework", "project_references", "package_references"], projects)
    write_csv(args.output, "api-surface.csv", ["repository", "scope", "path", "line", "method", "declaration"], surfaces)
    write_csv(args.output, "ui-files.csv", ["repository", "scope", "path", "lines", "type"], ui)
    write_csv(args.output, "persistence.csv", ["repository", "scope", "path", "line", "entity_type", "property"], persistence)
    print(json.dumps({name: {key: value for key, value in values.items() if key not in ("extensions", "source_groups")} for name, values in summary["repositories"].items()}, indent=2))


if __name__ == "__main__":
    main()
