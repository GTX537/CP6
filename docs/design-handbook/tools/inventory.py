"""Inventory the explicitly supplied archive; never execute or alter its files."""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path


def digest_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--archive", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    root = args.archive.resolve(strict=True)
    output = args.output.resolve()
    if output == root or root in output.parents:
        raise ValueError("Inventory outputs must be outside the source archive")
    output.mkdir(parents=True, exist_ok=True)
    started = datetime.now(timezone.utc).isoformat()
    paths = sorted(p for p in root.rglob("*") if p.is_file())
    expected = {p.relative_to(root).as_posix(): (p.stat().st_size, p.stat().st_mtime_ns) for p in paths}
    mandatory = json.loads((root / "完整成果/docs/provenance/mandatory-current-artifacts.json").read_text(encoding="utf-8"))
    roles = defaultdict(list)
    for target in mandatory:
        for artifact in target["required_artifacts"]:
            sha = artifact.get("actual_sha256") or artifact.get("expected_sha256")
            if sha:
                roles[sha].append({"target_id": target["target_id"], "component_id": artifact["component_id"], "role": artifact["role"]})

    def inspect(path: Path) -> dict:
        rel = path.relative_to(root).as_posix()
        before = path.stat()
        sha = digest_file(path)
        after = path.stat()
        stable = (before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns) == expected[rel]
        parts = Path(rel).parts
        if sha in roles:
            disposition = "current_required_material_or_exact_duplicate"
        elif parts[0] in {"Drive原始文件", "原始22文件", "校验记录"} or rel.startswith("CP6_DOCS_FULL_"):
            disposition = "transport_or_restore_evidence"
        elif parts[0] == "完整成果" and len(parts) > 2 and parts[2] in {"provenance", "checks", "targets"}:
            disposition = "source_selection_or_navigation"
        elif parts[0] in {"CP6_遗漏原包补充_20261009", "增补快照"}:
            disposition = "supplement_identity_and_semantic_classification_pending"
        elif parts[0] == "找回原件":
            disposition = "recovered_review_or_recovery_provenance"
        else:
            disposition = "historical_reference_or_other_material_pending_classification"
        return {
            "relative_path": rel, "absolute_path": path.as_posix(), "bytes": after.st_size,
            "sha256": sha, "extension": path.suffix.lower(), "mtime_ns": after.st_mtime_ns,
            "stable_during_hash": stable, "physical_category": disposition,
            "current_required_references": roles.get(sha, []),
            "semantic_read_status": "not_inferred_from_inventory",
        }

    with ThreadPoolExecutor(max_workers=4) as pool:
        records = list(pool.map(inspect, paths))
    groups = defaultdict(list)
    for record in records:
        groups[record["sha256"]].append(record["relative_path"])
    current_paths = sorted(p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_file())
    summary = {
        "source_root": root.as_posix(), "started_at_utc": started,
        "finished_at_utc": datetime.now(timezone.utc).isoformat(),
        "file_count": len(records), "bytes_including_duplicates": sum(r["bytes"] for r in records),
        "unique_sha256_count": len(groups),
        "unique_content_bytes": sum(next(r["bytes"] for r in records if r["sha256"] == sha) for sha in groups),
        "duplicate_groups": sum(len(v) > 1 for v in groups.values()),
        "file_set_stable": current_paths == sorted(expected),
        "files_unstable_during_hash": [r["relative_path"] for r in records if not r["stable_during_hash"]],
        "extension_counts": dict(Counter(r["extension"] for r in records)),
        "category_counts": dict(Counter(r["physical_category"] for r in records)),
        "meaning": "All physical files hashed, including transport duplicates. Classification is structural; it is not semantic reading, final authority selection, or proof all container members are materialized.",
    }
    (output / "all-files.json").write_text(json.dumps({"snapshot": summary, "files": records}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (output / "content-groups.json").write_text(json.dumps({"groups": [{"sha256": k, "paths": v} for k, v in sorted(groups.items())]}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    with (output / "all-files.csv").open("w", encoding="utf-8-sig", newline="") as stream:
        fields = ["relative_path", "bytes", "sha256", "extension", "physical_category", "semantic_read_status"]
        writer = csv.DictWriter(stream, fields, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(records)
    (output / "inventory-summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: summary[k] for k in ["file_count", "bytes_including_duplicates", "unique_sha256_count", "unique_content_bytes", "file_set_stable", "files_unstable_during_hash"]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
