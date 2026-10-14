"""Derive preparation expectations from independently pinned committed producer records.

This is an evidence authoring step for human review, never a formal approval.
The checker remains separate and cannot manufacture its own expectations.
"""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import subprocess
import zipfile

PLATFORM = "e5c63a28c94bae4cc66ec50fbe6079553c03d0d0"
MAIN = "1ab2d031e3c501662ecb89a33839fac1cba55d33"
PUBLICATION_HASH = "17fd39f5724ba118771eb95aeb18d78045ea2e576431957ab7c24b8d13719181"
LOCK_HASH = "1fabaecefb24050d801a9fd894dcd908b8428f7bd486fe4d0a634ad97ffe0b01"
FEED = "https://nuget.pkg.github.com/GTX537/index.json"


def sha(data):
    return hashlib.sha256(data).hexdigest()


def blob(repo, source, path):
    return subprocess.check_output(["git", "-c", "core.longpaths=true", "-C", str(repo), "show", source + ":" + path])


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--platform", type=Path, required=True)
    p.add_argument("--cache", type=Path, required=True)
    p.add_argument("--local-packages", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    publication = blob(args.platform, PLATFORM, "docs/evidence/c01/0.10.2/formal-package-publication.v1.json")
    lock = blob(args.platform, PLATFORM, "docs/development/evidence/WP02-27/runs/coord-schema-local-pack/inspection/consumer-lock.json")
    if sha(publication) != PUBLICATION_HASH or sha(lock) != LOCK_HASH:
        raise ValueError("Independent committed publication/lock hash mismatch")
    pub = json.loads(publication)
    local = json.loads(lock)
    cases = [("old-formal", pub, "publishedPackageSha256"), ("old-local", local, "sha256")]
    origins = []
    for label, authority, hash_field in cases:
        expected = authority["packages"]
        version, source = expected[0]["version"], authority["sourceGitSha"]
        rows = []
        for row in expected:
            name = row["packageId"]
            archive = (args.cache / name.lower() / version / f"{name.lower()}.{version}.nupkg") if label == "old-formal" else (args.local_packages / f"{name}.{version}.nupkg")
            data = archive.read_bytes()
            if sha(data) != row[hash_field]:
                raise ValueError("Archive differs from independently pinned record: " + name)
            schemas = []
            with zipfile.ZipFile(archive) as package:
                for entry in sorted(package.namelist()):
                    if entry.endswith(".schema.json"):
                        source_bytes = blob(args.platform, source, entry)
                        packaged_bytes = package.read(entry)
                        if source_bytes != packaged_bytes:
                            raise ValueError("Packaged schema differs from pinned source bytes: " + entry)
                        schemas.append({"entry": entry, "sha256": sha(source_bytes)})
                        origins.append({"case": label, "packageId": name, "entry": entry, "sourceSha": source,
                                        "source_sha256": sha(source_bytes), "packaged_sha256": sha(packaged_bytes)})
            rows.append({"id": name, "path": str(archive.resolve()), "sha256": row[hash_field],
                         "sha512Archive": base64.b64encode(hashlib.sha512(data).digest()).decode(), "schemas": schemas})
        # Feed observation is a declared provenance reference, not a fresh readback.
        observed = FEED if label == "old-formal" else args.local_packages.resolve().as_uri()
        manifest = {"schemaVersion": 1, "mode": "PREPARATION_ONLY", "approval": {
            "reference": f"Preparation identity only; independent Platform {PLATFORM} record hash " + (PUBLICATION_HASH if label == "old-formal" else LOCK_HASH),
            "mainRepository": "https://github.com/GTX537/CP6", "mainSourceSha": MAIN,
            "platformRepository": "https://github.com/GTX537/CP6.Platform", "platformSourceSha": source,
            "packageVersion": version, "feedUrl": FEED, "compatibility": "UNKNOWN", "requiredCapabilities": ["ctx-binding-v1"],
            "runtimePackageIds": ["CP6.Platform." + x for x in ("Abstractions", "AspNetCore", "Contracts", "EntityFramework", "Messaging")]},
            "observation": {"feedUrl": observed, "publication": "UNVERIFIED" if label == "old-formal" else "LOCAL_ONLY", "packages": rows}}
        (args.output / (label + ".json")).write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
    (args.output / "schema-source-parity.json").write_text(json.dumps({"checks": origins,
        "approval_scope": "Preparation byte inspection only, no independent Main qualification or current trust",
        "sha512_scope": "Raw archive SHA512 after independently pinned SHA256 matched; never NuGet contentHash",
        "feed_scope": "Old feed URL from pinned publication only; local candidate observed in local directory; no fresh feed readback"}, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({"cases": 2, "source_packaged_schema_pairs": len(origins), "formal_admission": "BLOCKED"}))


if __name__ == "__main__":
    main()
