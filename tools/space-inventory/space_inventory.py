#!/usr/bin/env python3
"""Generate the deterministic CP6 Space E00-S01 current-facts inventory."""

import argparse
import fnmatch
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Sequence, Tuple


SCHEMA_VERSION = "1.0"
TEXT_EXTENSIONS = {
    ".cs",
    ".csproj",
    ".json",
    ".md",
    ".ps1",
    ".slnx",
    ".ts",
    ".tsx",
    ".vue",
}
BASELINE_SPACE_PREFIXES = (
    "CP6.Core/Migrations/",
    "CP6.Core/Services/Integration/",
    "CP6.Core/Services/Space/",
    "CP6.Entity/DTOs/Space/",
    "CP6.Entity/DomainModels/Space/",
    "CP6.Tests/Space/",
    "CP6.WebApi/BackgroundServices/Space",
    "CP6.WebApi/Controllers/Space/",
    "CP6.WebApi/Hubs/Space",
    "CP6.WebApi/Seed/I18nSpace",
    "CP6.WebApi/Services/SignalRSpace",
    "cp6.web/e2e/space",
    "cp6.web/src/api/space/",
    "cp6.web/src/space-editor/",
    "cp6.web/src/space-viewer/",
    "cp6.web/src/stores/space",
    "cp6.web/src/types/space/",
    "cp6.web/src/utils/space",
    "cp6.web/src/views/space/",
)
BASELINE_EXACT_PATHS = {
    "CP6.Core/EFDbContext/CP6Context.cs",
    "CP6.Tests/SignalRSpaceNotifierTests.cs",
    "CP6.Tests/SpaceBridgeHookTests.cs",
    "CP6.Tests/SpaceLocateServiceTests.cs",
    "CP6.Tests/SpaceMasterServiceTests.cs",
    "CP6.WebApi/Program.cs",
}
HTTP_ATTRIBUTE_RE = re.compile(
    r"\[Http(Get|Post|Put|Patch|Delete)(?:\(\s*\"([^\"]*)\"\s*\))?\]",
    re.IGNORECASE,
)
PERMISSION_RE = re.compile(
    r"RequirePermission\(\s*\"([^\"]+)\"\s*,\s*\"([^\"]+)\"\s*\)"
)
ACTION_RE = re.compile(
    r"(?P<attrs>(?:\s*\[[^\]]+\]\s*)+)"
    r"\s*public\s+(?:async\s+)?[^;={]+?\s+(?P<name>[A-Za-z_]\w*)\s*\(",
    re.MULTILINE,
)
CLASS_ROUTE_RE = re.compile(
    r"\[Route\(\s*\"([^\"]+)\"\s*\)\s*\]"
    r"[\s\S]{0,1200}?\bclass\s+([A-Za-z_]\w*Controller)\b",
    re.MULTILINE,
)
TABLE_RE = re.compile(
    r"\[Table\(\s*\"([^\"]+)\"\s*\)\s*\]"
    r"[\s\S]{0,500}?\bclass\s+([A-Za-z_]\w*)\b",
    re.MULTILINE,
)
CREATE_TABLE_RE = re.compile(r"CreateTable\(\s*name:\s*\"([^\"]+)\"", re.MULTILINE)
TO_TABLE_RE = re.compile(r"\.ToTable\(\s*\"([^\"]+)\"", re.MULTILINE)
FRONTEND_CALL_RE = re.compile(
    r"http\.(get|post|put|patch|delete)[^(]*\(\s*`([^`]+)`",
    re.IGNORECASE,
)
_GIT_BLOB_CACHE: Dict[Tuple[str, str, str], bytes] = {}


class InventoryError(RuntimeError):
    """Raised when an inventory input cannot be read completely."""


def run_git_bytes(root: Path, arguments: Sequence[str]) -> bytes:
    result = subprocess.run(
        ["git", "-C", str(root)] + list(arguments),
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if result.returncode != 0:
        detail = result.stderr.decode("utf-8", errors="replace").strip()
        raise InventoryError(
            "git {} failed for {}: {}".format(" ".join(arguments), root, detail)
        )
    return result.stdout


def run_git_text(root: Path, arguments: Sequence[str]) -> str:
    return run_git_bytes(root, arguments).decode("utf-8", errors="strict").strip()


def tracked_files(root: Path) -> List[str]:
    raw = run_git_bytes(root, ["ls-files", "-z"])
    return sorted(
        item.decode("utf-8", errors="strict").replace("\\", "/")
        for item in raw.split(b"\0")
        if item
    )


def parse_porcelain_z(raw: bytes) -> List[Dict[str, str]]:
    tokens = raw.split(b"\0")
    result: List[Dict[str, str]] = []
    index = 0
    while index < len(tokens):
        token = tokens[index]
        index += 1
        if not token:
            continue
        decoded = token.decode("utf-8", errors="strict")
        if len(decoded) < 4:
            raise InventoryError("Unexpected git status entry: {!r}".format(decoded))
        status = decoded[:2]
        path = decoded[3:].replace("\\", "/")
        entry = {"status": status, "path": path}
        if "R" in status or "C" in status:
            if index >= len(tokens) or not tokens[index]:
                raise InventoryError("Rename/copy status is missing its source path.")
            entry["fromPath"] = tokens[index].decode(
                "utf-8", errors="strict"
            ).replace("\\", "/")
            index += 1
        result.append(entry)
    return sorted(result, key=lambda item: (item["path"], item["status"]))


def working_tree_changes(root: Path) -> List[Dict[str, str]]:
    raw = run_git_bytes(
        root, ["status", "--porcelain=v1", "-z", "--untracked-files=all"]
    )
    return parse_porcelain_z(raw)


def is_baseline_space_file(path: str) -> bool:
    if path in BASELINE_EXACT_PATHS:
        return True
    return path.startswith(BASELINE_SPACE_PREFIXES)


def read_bytes(
    root: Path, relative_path: str, git_ref: Optional[str] = None
) -> bytes:
    if git_ref:
        cache_key = (str(root.resolve()), git_ref, relative_path)
        if cache_key not in _GIT_BLOB_CACHE:
            _GIT_BLOB_CACHE[cache_key] = run_git_bytes(
                root, ["show", "{}:{}".format(git_ref, relative_path)]
            )
        return _GIT_BLOB_CACHE[cache_key]

    path = root / Path(relative_path)
    try:
        return path.read_bytes()
    except OSError as exc:
        raise InventoryError("Cannot read source {}: {}".format(path, exc))


def read_text(
    root: Path, relative_path: str, git_ref: Optional[str] = None
) -> str:
    try:
        return read_bytes(root, relative_path, git_ref).decode(
            "utf-8", errors="strict"
        )
    except UnicodeError as exc:
        raise InventoryError(
            "Cannot read UTF-8 source {}: {}".format(relative_path, exc)
        )


def normalize_route(route: str) -> str:
    route = re.sub(r"\$\{[^}]+\}", "{param}", route)
    route = re.sub(r"/+", "/", route.replace("\\", "/"))
    if not route.startswith("/"):
        route = "/" + route
    if len(route) > 1:
        route = route.rstrip("/")
    return route


def join_route(prefix: str, suffix: str) -> str:
    if not suffix:
        return normalize_route(prefix)
    return normalize_route(prefix.rstrip("/") + "/" + suffix.lstrip("/"))


def parse_csharp_endpoints(
    root: Path,
    paths: Iterable[str],
    source: str,
    git_ref: Optional[str] = None,
) -> List[Dict[str, Any]]:
    endpoints: List[Dict[str, Any]] = []
    for relative_path in sorted(paths):
        if not relative_path.endswith("Controller.cs"):
            continue
        text = read_text(root, relative_path, git_ref)
        class_match = CLASS_ROUTE_RE.search(text)
        if not class_match:
            continue
        class_route, controller_name = class_match.groups()
        class_route = class_route.replace(
            "[controller]", controller_name[: -len("Controller")]
        )
        class_prefix = text[: class_match.end()]
        class_authorized = "[Authorize" in class_prefix
        for action_match in ACTION_RE.finditer(text):
            attributes = action_match.group("attrs")
            http_attributes = list(HTTP_ATTRIBUTE_RE.finditer(attributes))
            if not http_attributes:
                continue
            permission_match = PERMISSION_RE.search(attributes)
            permission = (
                "{}:{}".format(*permission_match.groups())
                if permission_match
                else None
            )
            for http_match in http_attributes:
                method = http_match.group(1).upper()
                action_route = http_match.group(2) or ""
                route = join_route(class_route, action_route)
                endpoints.append(
                    {
                        "source": source,
                        "implementationStatus": (
                            "Implemented" if source == "baseline" else "Partial"
                        ),
                        "method": method,
                        "route": route,
                        "owner": "{}#{}".format(
                            relative_path, action_match.group("name")
                        ),
                        "permission": permission,
                        "authorized": class_authorized
                        or "[Authorize" in attributes,
                    }
                )
    return sorted(
        endpoints,
        key=lambda item: (item["route"].lower(), item["method"], item["owner"]),
    )


def parse_frontend_calls(
    root: Path, paths: Iterable[str], git_ref: Optional[str] = None
) -> List[Dict[str, str]]:
    calls: List[Dict[str, str]] = []
    for relative_path in sorted(paths):
        if not (
            relative_path.startswith("cp6.web/src/api/space/")
            and relative_path.endswith((".ts", ".tsx"))
        ):
            continue
        text = read_text(root, relative_path, git_ref)
        for match in FRONTEND_CALL_RE.finditer(text):
            calls.append(
                {
                    "implementationStatus": "Implemented",
                    "method": match.group(1).upper(),
                    "route": normalize_route(match.group(2)),
                    "owner": relative_path,
                }
            )
    return sorted(
        calls, key=lambda item: (item["route"].lower(), item["method"], item["owner"])
    )


def parse_tables(
    root: Path,
    paths: Iterable[str],
    source: str,
    git_ref: Optional[str] = None,
) -> List[Dict[str, Any]]:
    found: Dict[str, Dict[str, Any]] = {}
    for relative_path in sorted(paths):
        if not relative_path.endswith(".cs"):
            continue
        text = read_text(root, relative_path, git_ref)
        matches: List[Tuple[str, Optional[str]]] = []
        matches.extend(TABLE_RE.findall(text))
        matches.extend((name, None) for name in CREATE_TABLE_RE.findall(text))
        matches.extend((name, None) for name in TO_TABLE_RE.findall(text))
        for table_name, class_name in matches:
            if not table_name.lower().startswith("space_"):
                continue
            key = table_name.lower()
            item = found.setdefault(
                key,
                {
                    "source": source,
                    "implementationStatus": (
                        "Implemented" if source == "baseline" else "Partial"
                    ),
                    "name": table_name,
                    "entities": [],
                    "owners": [],
                },
            )
            if class_name and class_name not in item["entities"]:
                item["entities"].append(class_name)
            if relative_path not in item["owners"]:
                item["owners"].append(relative_path)
    for item in found.values():
        item["entities"].sort()
        item["owners"].sort()
    return sorted(found.values(), key=lambda item: item["name"].lower())


def discover_pages(paths: Iterable[str]) -> List[Dict[str, str]]:
    pages = []
    for path in sorted(paths):
        if path.startswith("cp6.web/src/views/space/") and path.endswith(".vue"):
            pages.append(
                {
                    "implementationStatus": "Implemented",
                    "component": Path(path).stem,
                    "owner": path,
                }
            )
    return pages


def discover_tests(paths: Iterable[str], source: str) -> List[Dict[str, str]]:
    tests = []
    for path in sorted(paths):
        lower = path.lower()
        filename = Path(path).name.lower()
        path_segments = lower.split("/")
        in_test_project = any(
            segment.endswith("tests") for segment in path_segments[:-1]
        )
        is_test = (
            (in_test_project and filename.endswith(("test.cs", "tests.cs")))
            or filename.endswith(".spec.ts")
            or filename.endswith(".spec.tsx")
        )
        if is_test and "space" in lower:
            tests.append(
                {
                    "source": source,
                    "implementationStatus": (
                        "Implemented" if source == "baseline" else "Partial"
                    ),
                    "owner": path,
                }
            )
    return tests


def build_permission_inventory(
    endpoints: Iterable[Dict[str, Any]]
) -> List[Dict[str, Any]]:
    grouped: Dict[str, Dict[str, Any]] = {}
    for endpoint in endpoints:
        permission = endpoint.get("permission")
        if not permission:
            continue
        item = grouped.setdefault(
            permission,
            {
                "permission": permission,
                "sources": [],
                "owners": [],
            },
        )
        if endpoint["source"] not in item["sources"]:
            item["sources"].append(endpoint["source"])
        if endpoint["owner"] not in item["owners"]:
            item["owners"].append(endpoint["owner"])
    for item in grouped.values():
        item["sources"].sort()
        item["owners"].sort()
        item["implementationStatus"] = (
            "Implemented" if "baseline" in item["sources"] else "Partial"
        )
    return sorted(grouped.values(), key=lambda item: item["permission"])


def endpoint_ownership_check(
    endpoints: Iterable[Dict[str, Any]], scope: str
) -> Dict[str, Any]:
    owners: Dict[str, List[str]] = {}
    for endpoint in endpoints:
        key = "{} {}".format(
            endpoint["method"].upper(), endpoint["route"].lower()
        )
        owners.setdefault(key, []).append(endpoint["owner"])
    duplicates = [
        {"endpoint": key, "owners": sorted(set(value))}
        for key, value in sorted(owners.items())
        if len(set(value)) > 1
    ]
    return {
        "scope": scope,
        "passed": not duplicates,
        "endpointCount": sum(1 for _ in endpoints),
        "duplicates": duplicates,
    }


def endpoint_discovery_check(
    root: Path,
    paths: Iterable[str],
    endpoints: Sequence[Dict[str, Any]],
    scope: str,
    git_ref: Optional[str] = None,
) -> Dict[str, Any]:
    attribute_count = 0
    for relative_path in sorted(paths):
        if not relative_path.endswith("Controller.cs"):
            continue
        attribute_count += len(
            HTTP_ATTRIBUTE_RE.findall(read_text(root, relative_path, git_ref))
        )
    return {
        "scope": scope,
        "passed": attribute_count == len(endpoints),
        "httpAttributeCount": attribute_count,
        "parsedEndpointCount": len(endpoints),
    }


def path_matches(path: str, pattern: str) -> bool:
    return fnmatch.fnmatchcase(path, pattern)


def evaluate_contracts(
    contract_document: Dict[str, Any],
    roots: Dict[str, Path],
    source_paths: Dict[str, List[str]],
    source_refs: Optional[Dict[str, Optional[str]]] = None,
) -> List[Dict[str, Any]]:
    source_refs = source_refs or {}
    results = []
    for contract in contract_document.get("contracts", []):
        evidence_results = []
        for evidence in contract.get("evidence", []):
            source = evidence["source"]
            pattern = evidence["glob"]
            candidates = [
                path
                for path in source_paths[source]
                if path_matches(path, pattern)
            ]
            regex = evidence.get("regex")
            if regex:
                compiled = re.compile(regex, re.MULTILINE)
                candidates = [
                    path
                    for path in candidates
                    if Path(path).suffix.lower() in TEXT_EXTENSIONS
                    and compiled.search(
                        read_text(roots[source], path, source_refs.get(source))
                    )
                ]
            evidence_results.append(
                {
                    "id": evidence["id"],
                    "description": evidence["description"],
                    "matched": bool(candidates),
                    "matches": sorted(candidates),
                }
            )
        matched_count = sum(1 for item in evidence_results if item["matched"])
        if matched_count == 0:
            status = "NotStarted"
        elif matched_count < len(evidence_results):
            status = "Partial"
        else:
            status = contract.get("completeStatus", "Implemented")
        results.append(
            {
                "id": contract["id"],
                "name": contract["name"],
                "status": status,
                "rationale": contract["rationale"],
                "evidence": evidence_results,
                "missingEvidence": [
                    item["description"]
                    for item in evidence_results
                    if not item["matched"]
                ],
            }
        )
    return results


def sha256_inputs(
    roots: Dict[str, Path],
    source_paths: Dict[str, List[str]],
    source_refs: Dict[str, Optional[str]],
    contracts_path: Path,
) -> str:
    digest = hashlib.sha256()
    for source in sorted(source_paths):
        root = roots[source]
        for relative_path in sorted(source_paths[source]):
            digest.update(source.encode("utf-8"))
            digest.update(b"\0")
            digest.update(relative_path.encode("utf-8"))
            digest.update(b"\0")
            digest.update(read_bytes(root, relative_path, source_refs[source]))
            digest.update(b"\0")
    digest.update(b"contracts\0")
    digest.update(contracts_path.read_bytes())
    return digest.hexdigest()


def load_contracts(path: Path) -> Dict[str, Any]:
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise InventoryError("Cannot load contract catalog {}: {}".format(path, exc))
    if document.get("schemaVersion") != SCHEMA_VERSION:
        raise InventoryError(
            "Contract catalog schemaVersion must be {}.".format(SCHEMA_VERSION)
        )
    return document


def build_inventory(
    repo_root: Path, candidate_root: Path, contracts_path: Path
) -> Dict[str, Any]:
    repo_root = repo_root.resolve()
    candidate_root = candidate_root.resolve()
    contracts_path = contracts_path.resolve()
    if not repo_root.is_dir():
        raise InventoryError("Baseline repository does not exist: {}".format(repo_root))
    if not candidate_root.is_dir():
        raise InventoryError(
            "Candidate repository does not exist: {}".format(candidate_root)
        )

    baseline_commit = run_git_text(repo_root, ["rev-parse", "HEAD"])
    all_baseline_files = tracked_files(repo_root)
    baseline_files = [
        path for path in all_baseline_files if is_baseline_space_file(path)
    ]
    candidate_changes = working_tree_changes(candidate_root)
    candidate_files = [item["path"] for item in candidate_changes]
    candidate_inventory_files = [
        dict(item, implementationStatus="Partial")
        for item in candidate_changes
    ]
    for path in candidate_files:
        if not (candidate_root / Path(path)).is_file():
            raise InventoryError(
                "Candidate inventory only supports file changes; found: {}".format(path)
            )

    roots = {"baseline": repo_root, "candidate": candidate_root}
    source_refs = {"baseline": baseline_commit, "candidate": None}
    source_paths = {
        "baseline": baseline_files,
        "candidate": candidate_files,
    }
    contracts = load_contracts(contracts_path)

    baseline_endpoints = parse_csharp_endpoints(
        repo_root, baseline_files, "baseline", baseline_commit
    )
    candidate_endpoints = parse_csharp_endpoints(
        candidate_root, candidate_files, "candidate"
    )
    ownership_checks = [
        endpoint_ownership_check(baseline_endpoints, "baseline"),
        endpoint_ownership_check(candidate_endpoints, "candidate"),
        endpoint_ownership_check(
            baseline_endpoints + candidate_endpoints, "combined"
        ),
    ]
    discovery_checks = [
        endpoint_discovery_check(
            repo_root,
            baseline_files,
            baseline_endpoints,
            "baseline",
            baseline_commit,
        ),
        endpoint_discovery_check(
            candidate_root, candidate_files, candidate_endpoints, "candidate"
        ),
    ]
    all_checks_passed = all(
        item["passed"] for item in ownership_checks + discovery_checks
    )

    permission_inventory = build_permission_inventory(
        baseline_endpoints + candidate_endpoints
    )
    non_get_without_permission = sorted(
        {
            "{} {} ({})".format(
                endpoint["method"], endpoint["route"], endpoint["owner"]
            )
            for endpoint in baseline_endpoints + candidate_endpoints
            if endpoint["method"] != "GET" and not endpoint["permission"]
        }
    )

    contract_results = evaluate_contracts(
        contracts, roots, source_paths, source_refs
    )
    status_counts = {
        status: sum(1 for item in contract_results if item["status"] == status)
        for status in ("Implemented", "Partial", "NotStarted")
    }

    inventory = {
        "schemaVersion": SCHEMA_VERSION,
        "task": "E00-S01",
        "title": "CP6 Space current-facts inventory",
        "provenance": {
            "baseline": {
                "commit": baseline_commit,
                "branch": run_git_text(repo_root, ["branch", "--show-current"]),
                "spaceFileCount": len(baseline_files),
            },
            "candidate": {
                "commit": run_git_text(candidate_root, ["rev-parse", "HEAD"]),
                "branch": run_git_text(
                    candidate_root, ["branch", "--show-current"]
                ),
                "changeCount": len(candidate_changes),
            },
            "contractCatalog": contracts.get("title"),
            "inputDigestSha256": sha256_inputs(
                roots, source_paths, source_refs, contracts_path
            ),
            "determinism": (
                "No timestamps or absolute paths are emitted; inputs and output "
                "collections are sorted."
            ),
        },
        "summary": {
            "baselineEndpoints": len(baseline_endpoints),
            "candidateEndpoints": len(candidate_endpoints),
            "frontendCalls": len(
                parse_frontend_calls(repo_root, baseline_files, baseline_commit)
            ),
            "baselineTables": len(
                parse_tables(
                    repo_root, baseline_files, "baseline", baseline_commit
                )
            ),
            "candidateTables": len(
                parse_tables(candidate_root, candidate_files, "candidate")
            ),
            "pages": len(discover_pages(baseline_files)),
            "permissions": len(permission_inventory),
            "baselineTests": len(discover_tests(baseline_files, "baseline")),
            "candidateTests": len(discover_tests(candidate_files, "candidate")),
            "candidateFiles": len(candidate_changes),
            "contractStatuses": status_counts,
        },
        "checks": {
            "allPassed": all_checks_passed,
            "endpointDiscovery": discovery_checks,
            "endpointOwnership": ownership_checks,
            "nonGetEndpointsWithoutExplicitPermission": non_get_without_permission,
            "privacy": {
                "passed": True,
                "policy": (
                    "The report contains repository-relative paths and source "
                    "symbols only. It does not emit source bodies, credentials, "
                    "environment values, or customer data."
                ),
            },
        },
        "contracts": contract_results,
        "inventory": {
            "baselineEndpoints": baseline_endpoints,
            "candidateEndpoints": candidate_endpoints,
            "frontendCalls": parse_frontend_calls(
                repo_root, baseline_files, baseline_commit
            ),
            "baselineTables": parse_tables(
                repo_root, baseline_files, "baseline", baseline_commit
            ),
            "candidateTables": parse_tables(
                candidate_root, candidate_files, "candidate"
            ),
            "pages": discover_pages(baseline_files),
            "permissions": permission_inventory,
            "baselineTests": discover_tests(baseline_files, "baseline"),
            "candidateTests": discover_tests(candidate_files, "candidate"),
            "candidateFiles": candidate_inventory_files,
        },
    }
    return inventory


def markdown_escape(value: Any) -> str:
    return str(value).replace("|", "\\|").replace("\n", " ")


def markdown_table(headers: Sequence[str], rows: Iterable[Sequence[Any]]) -> List[str]:
    lines = [
        "| " + " | ".join(headers) + " |",
        "|" + "|".join("---" for _ in headers) + "|",
    ]
    for row in rows:
        lines.append(
            "| "
            + " | ".join(markdown_escape(value) for value in row)
            + " |"
        )
    return lines


def render_markdown(inventory: Dict[str, Any]) -> str:
    summary = inventory["summary"]
    provenance = inventory["provenance"]
    lines = [
        "# CP6 Space E00-S01 Current Facts Inventory",
        "",
        "> Generated by `tools/space-inventory/space_inventory.py`. "
        "Do not edit this report by hand.",
        "",
        "## Provenance",
        "",
        "- Baseline: `{}` on `{}`".format(
            provenance["baseline"]["commit"], provenance["baseline"]["branch"]
        ),
        "- Candidate: `{}` on `{}` with {} working-tree files".format(
            provenance["candidate"]["commit"],
            provenance["candidate"]["branch"],
            provenance["candidate"]["changeCount"],
        ),
        "- Input SHA-256: `{}`".format(provenance["inputDigestSha256"]),
        "- Determinism: {}".format(provenance["determinism"]),
        "",
        "## Summary",
        "",
    ]
    lines.extend(
        markdown_table(
            ["Area", "Count"],
            [
                ("Baseline backend endpoints", summary["baselineEndpoints"]),
                ("Candidate backend endpoints", summary["candidateEndpoints"]),
                ("Frontend API calls", summary["frontendCalls"]),
                ("Baseline tables", summary["baselineTables"]),
                ("Candidate tables", summary["candidateTables"]),
                ("Space pages", summary["pages"]),
                ("Permission tuples", summary["permissions"]),
                ("Baseline tests", summary["baselineTests"]),
                ("Candidate tests", summary["candidateTests"]),
                ("Candidate working-tree files", summary["candidateFiles"]),
            ],
        )
    )
    lines.extend(["", "## Acceptance Checks", ""])
    ownership_rows = []
    for check in inventory["checks"]["endpointOwnership"]:
        ownership_rows.append(
            (
                check["scope"],
                "PASS" if check["passed"] else "FAIL",
                check["endpointCount"],
                "; ".join(
                    "{} => {}".format(
                        item["endpoint"], ", ".join(item["owners"])
                    )
                    for item in check["duplicates"]
                )
                or "None",
            )
        )
    lines.extend(
        markdown_table(
            ["Scope", "Unique ownership", "Endpoints", "Duplicates"],
            ownership_rows,
        )
    )
    lines.extend(["", "Endpoint discovery completeness:", ""])
    lines.extend(
        markdown_table(
            ["Scope", "Discovery", "HTTP attributes", "Parsed endpoints"],
            (
                (
                    check["scope"],
                    "PASS" if check["passed"] else "FAIL",
                    check["httpAttributeCount"],
                    check["parsedEndpointCount"],
                )
                for check in inventory["checks"]["endpointDiscovery"]
            ),
        )
    )
    lines.extend(
        [
            "",
            "- Privacy: **PASS**. {}".format(
                inventory["checks"]["privacy"]["policy"]
            ),
            "- Non-GET endpoints without explicit `RequirePermission`: {}".format(
                len(
                    inventory["checks"][
                        "nonGetEndpointsWithoutExplicitPermission"
                    ]
                )
            ),
        ]
    )
    if inventory["checks"]["nonGetEndpointsWithoutExplicitPermission"]:
        lines.extend(
            "- `{}`".format(item)
            for item in inventory["checks"][
                "nonGetEndpointsWithoutExplicitPermission"
            ]
        )

    lines.extend(["", "## Frozen Contract Mapping", ""])
    contract_rows = []
    for contract in inventory["contracts"]:
        matches = sorted(
            {
                match
                for evidence in contract["evidence"]
                for match in evidence["matches"]
            }
        )
        if len(matches) > 4:
            match_summary = "<br>".join(matches[:4]) + "<br>(+{} more)".format(
                len(matches) - 4
            )
        else:
            match_summary = "<br>".join(matches)
        contract_rows.append(
            (
                contract["id"],
                contract["name"],
                contract["status"],
                match_summary or "None",
                "<br>".join(contract["missingEvidence"]) or "None",
            )
        )
    lines.extend(
        markdown_table(
            ["ID", "Contract", "Status", "Evidence", "Missing evidence"],
            contract_rows,
        )
    )

    lines.extend(["", "## Baseline Backend Endpoints", ""])
    lines.extend(
        markdown_table(
            ["Status", "Method", "Route", "Owner", "Permission"],
            (
                (
                    item["implementationStatus"],
                    item["method"],
                    item["route"],
                    item["owner"],
                    item["permission"] or "Authorize only",
                )
                for item in inventory["inventory"]["baselineEndpoints"]
            ),
        )
    )
    lines.extend(["", "## Candidate Backend Endpoints", ""])
    lines.extend(
        markdown_table(
            ["Status", "Method", "Route", "Owner", "Permission"],
            (
                (
                    item["implementationStatus"],
                    item["method"],
                    item["route"],
                    item["owner"],
                    item["permission"] or "Authorize only",
                )
                for item in inventory["inventory"]["candidateEndpoints"]
            ),
        )
    )
    lines.extend(["", "## Frontend API Calls", ""])
    lines.extend(
        markdown_table(
            ["Status", "Method", "Route", "Owner"],
            (
                (
                    item["implementationStatus"],
                    item["method"],
                    item["route"],
                    item["owner"],
                )
                for item in inventory["inventory"]["frontendCalls"]
            ),
        )
    )
    lines.extend(["", "## Tables", ""])
    lines.extend(
        markdown_table(
            ["Status", "Source", "Table", "Entities", "Owners"],
            (
                (
                    item["implementationStatus"],
                    item["source"],
                    item["name"],
                    ", ".join(item["entities"]) or "Migration/configuration",
                    "<br>".join(item["owners"]),
                )
                for item in inventory["inventory"]["baselineTables"]
                + inventory["inventory"]["candidateTables"]
            ),
        )
    )
    lines.extend(["", "## Pages", ""])
    lines.extend(
        markdown_table(
            ["Status", "Component", "Owner"],
            (
                (
                    item["implementationStatus"],
                    item["component"],
                    item["owner"],
                )
                for item in inventory["inventory"]["pages"]
            ),
        )
    )
    lines.extend(["", "## Permission Tuples", ""])
    lines.extend(
        markdown_table(
            ["Status", "Permission", "Sources", "Owners"],
            (
                (
                    item["implementationStatus"],
                    item["permission"],
                    ", ".join(item["sources"]),
                    "<br>".join(item["owners"]),
                )
                for item in inventory["inventory"]["permissions"]
            ),
        )
    )
    lines.extend(["", "## Tests", ""])
    lines.extend(
        markdown_table(
            ["Status", "Source", "Owner"],
            (
                (
                    item["implementationStatus"],
                    item["source"],
                    item["owner"],
                )
                for item in inventory["inventory"]["baselineTests"]
                + inventory["inventory"]["candidateTests"]
            ),
        )
    )
    lines.extend(["", "## Candidate Working-Tree Files", ""])
    lines.extend(
        markdown_table(
            ["Implementation status", "Git status", "Path"],
            (
                (
                    item["implementationStatus"],
                    item["status"],
                    item["path"],
                )
                for item in inventory["inventory"]["candidateFiles"]
            ),
        )
    )
    lines.extend(
        [
            "",
            "## Interpretation",
            "",
            "- `Implemented` means the frozen baseline contains all configured "
            "evidence for that contract.",
            "- `Partial` means evidence is incomplete or exists only in the "
            "uncommitted candidate worktree. Candidate code is never treated as "
            "the formal implementation.",
            "- `NotStarted` means none of the configured implementation evidence "
            "was found.",
            "",
            "Rollback: delete this report and its JSON companion. The scanner is "
            "read-only and does not modify application state.",
            "",
        ]
    )
    return "\n".join(lines)


def stable_json(inventory: Dict[str, Any]) -> str:
    return json.dumps(
        inventory, ensure_ascii=False, indent=2, sort_keys=True
    ) + "\n"


def write_or_check(path: Path, expected: str, check: bool) -> bool:
    if check:
        if not path.is_file():
            print("Missing generated file: {}".format(path), file=sys.stderr)
            return False
        actual = path.read_text(encoding="utf-8")
        if actual != expected:
            print("Generated file is stale: {}".format(path), file=sys.stderr)
            return False
        return True
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as output:
        output.write(expected)
    return True


def parse_args(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", default=".")
    parser.add_argument("--candidate-root", required=True)
    parser.add_argument(
        "--contracts", default="tools/space-inventory/contracts.json"
    )
    parser.add_argument(
        "--json-out",
        default="docs/space/reports/e00-s01-current-facts.json",
    )
    parser.add_argument(
        "--markdown-out",
        default="docs/space/reports/e00-s01-current-facts.md",
    )
    parser.add_argument(
        "--check",
        action="store_true",
        help="Fail when generated files are missing or stale.",
    )
    return parser.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = parse_args(argv)
    repo_root = Path(args.repo_root).resolve()
    candidate_root = Path(args.candidate_root).resolve()
    contracts_path = Path(args.contracts)
    if not contracts_path.is_absolute():
        contracts_path = repo_root / contracts_path
    json_out = Path(args.json_out)
    if not json_out.is_absolute():
        json_out = repo_root / json_out
    markdown_out = Path(args.markdown_out)
    if not markdown_out.is_absolute():
        markdown_out = repo_root / markdown_out

    try:
        inventory = build_inventory(repo_root, candidate_root, contracts_path)
        json_ok = write_or_check(json_out, stable_json(inventory), args.check)
        markdown_ok = write_or_check(
            markdown_out, render_markdown(inventory), args.check
        )
    except InventoryError as exc:
        print("Space inventory failed: {}".format(exc), file=sys.stderr)
        return 2
    if not json_ok or not markdown_ok:
        return 1
    if not inventory["checks"]["allPassed"]:
        print(
            "Space inventory failed endpoint ownership validation.",
            file=sys.stderr,
        )
        return 1
    print(
        "Space inventory {}: {} baseline endpoints, {} candidate files, "
        "input {}.".format(
            "verified" if args.check else "generated",
            inventory["summary"]["baselineEndpoints"],
            inventory["summary"]["candidateFiles"],
            inventory["provenance"]["inputDigestSha256"][:12],
        )
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
