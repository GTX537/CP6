"""Import hash-pinned S01-07 inputs; --check works entirely from this repository."""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
BASE = "e6ccc89b3e62c7ed2c3d4739757b6ae9f4490c47"
ADMISSION = ROOT / "docs/development/evidence/wave4-dispatch-20261013/S01-07-admission.json"


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("--archive-root", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    manifest_path = HERE / "sources.json"
    if args.check:
        manifest = json.loads(manifest_path.read_bytes())
        for item in manifest["assets"]:
            actual = sha((ROOT / item["path"]).read_bytes())
            if actual != item["sha256"]:
                raise ValueError(f"Hash mismatch: {item['path']}")
        print(f"PASS: {len(manifest['assets'])} pinned repository inputs")
        return
    if args.archive_root is None:
        parser.error("--archive-root is required for first import")
    admission = json.loads(ADMISSION.read_bytes())
    files = subprocess.check_output(["rg", "--files", str(args.archive_root)], encoding="utf-8").splitlines()
    assets = []

    def add(data, path, expected, **metadata):
        if sha(data) != expected:
            raise ValueError(f"Source hash mismatch: {path}")
        destination = HERE / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(data)
        assets.append(dict(path=destination.relative_to(ROOT).as_posix(), sha256=expected, **metadata))

    def original(expected):
        matches = [Path(p) for p in files if Path(p).name.startswith(expected[:16] + "__")]
        if len(matches) != 1:
            raise ValueError(f"Expected one original for {expected}, found {len(matches)}")
        data = matches[0].read_bytes()
        if sha(data) != expected:
            raise ValueError(f"Original hash mismatch: {expected}")
        return data

    for item in admission["sources"]:
        suffix = ".cs.txt" if item["source_id"] in ("composed-15", "composed-16") else ".json" if item["source_id"] == "composed-17" else ".ts.txt"
        name = item["source_id"] + suffix
        add(Path(item["local_composed_path"]).read_bytes(), "inputs/composed/" + name, item["composed_sha256"], source_id=item["source_id"], variant="composed", original_sha256=item["original_sha256"], patch_bundles=item["patch_bundles"])
        add(original(item["original_sha256"]), "inputs/original/" + name, item["original_sha256"], source_id=item["source_id"], variant="original")
    for item in admission["native_schema_file_closure"]["assets"]:
        add(original(item["source_sha256"]), "inputs/native/" + item["member"], item["source_sha256"], member=item["member"], schema_id=item["schema_id"], container_sha256=item["container_sha256"], container_member_ordinal=item["container_member_ordinal"])
    container_id = admission["native_schema_file_closure"]["assets"][0]["container_sha256"]
    index = json.loads(subprocess.check_output(["git", "-c", "core.longpaths=true", "show", BASE + ":docs/design-handbook/indexes/container-members.json"], cwd=ROOT))
    container = next(c for c in index["containers"] if c["sha256"] == container_id)
    extra_hashes = {
        "9e9ee118ded0250fa98ba6530d5c86f281a95ac0364ae15f1ee9e8481df08690",
        "23c597a0e6230a922823f6805c91a3c9b2d641fafbddff313ec2bd86a1cb98d9",
        "27edb910bb416a4ffe2d6f489e70f17923e64803975e5354cdac6c0d6a7f2020",
        "ec15532bb8820016f44af4dcf91823b2ec2af7684ec767cc1f25a4bbe61ec74b",
        "7faae8212b09d21e656e386023e7380ce9c9eefff35c37c6a4832616f567ff18",
        "c82ef3d5833b823cc562606af1a8bd8a68653416790bfc09c895df3a9a8c511b",
    }
    for item in container["members"]:
        if item["sha256"] in extra_hashes:
            name = "QA-IPQC-01-SPEC.md" if item["sha256"].startswith("c82ef3d5") else Path(item["member"]).name
            add(original(item["sha256"]), "inputs/protocol/" + name, item["sha256"], member=item["member"], container_sha256=container_id, container_member_ordinal=item["ordinal"])
            extra_hashes.remove(item["sha256"])
    if extra_hashes:
        raise ValueError(f"Missing indexed protocol sources: {extra_hashes}")
    pins = admission["input_pins"] + admission["sources"][0]["patch_bundles"]
    for item in pins:
        data = subprocess.check_output(["git", "-c", "core.longpaths=true", "show", BASE + ":" + item["path"]], cwd=ROOT)
        add(data, "inputs/pins/" + Path(item["path"]).name, item["sha256"], upstream_path=item["path"], git_blob=item["git_blob"], source_commit=BASE)
    manifest = dict(version="CP6-S01-07-SOURCES/1", input_package="CP6-S01-CONTRACT-INPUTS/1.0.0", source_commit=BASE, assets=assets)
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Imported {len(assets)} verified inputs")


if __name__ == "__main__":
    main()
