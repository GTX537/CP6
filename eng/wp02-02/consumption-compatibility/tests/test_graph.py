"""Graph reader negatives use isolated synthetic metadata, never real restore output."""
import base64
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest
import uuid

sys.dont_write_bytecode = True
TASK = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TASK))


class GraphTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root = TASK.parents[2] / "artifacts/wp02-02/consumption-compatibility" / uuid.uuid4().hex
        cls.root.mkdir(parents=True)

    def setUp(self):
        import inspect_graph
        self.g = inspect_graph
        self.case = self.root / uuid.uuid4().hex
        self.case.mkdir()

    def package(self):
        folder = self.case / "nuget-cache/example/1.0.0"
        folder.mkdir(parents=True)
        archive = b"synthetic archive bytes, not a signed NuGet package"
        (folder / "example.1.0.0.nupkg").write_bytes(archive)
        content = "synthetic-nuget-content-hash"
        (folder / "example.1.0.0.nupkg.sha512").write_text(base64.b64encode(hashlib.sha512(archive).digest()).decode())
        (folder / ".nupkg.metadata").write_text(json.dumps({"contentHash": content, "source": "https://api.nuget.org/v3/index.json"}))
        return folder, archive, {"type": "package", "path": "example/1.0.0", "sha512": content}

    def test_package_hashes_keep_nuget_content_distinct_from_raw_archive(self):
        _, archive, library = self.package()
        evidence = self.g.package_evidence(self.case, "Example/1.0.0", library)
        self.assertEqual(evidence["archiveSha256"], hashlib.sha256(archive).hexdigest())
        self.assertEqual(evidence["rawArchiveSha512Base64"], base64.b64encode(hashlib.sha512(archive).digest()).decode())
        self.assertNotEqual(evidence["rawArchiveSha512Base64"], evidence["nugetContentHash"])
        self.assertEqual(evidence["signatureValidation"], "RESTORE_EVIDENCE_ONLY_NO_NEW_CRYPTO_VALIDATION")

    def test_missing_package_archive_is_rejected(self):
        folder, _, library = self.package()
        (folder / "example.1.0.0.nupkg").unlink()
        with self.assertRaises(ValueError):
            self.g.package_evidence(self.case, "Example/1.0.0", library)

    def test_distinct_content_and_raw_archive_hashes_both_are_verified(self):
        folder, archive, library = self.package()
        valid = self.g.package_evidence(self.case, "Example/1.0.0", library)
        self.assertNotEqual(valid["cacheArchiveSha512Base64"], valid["nugetContentHash"])
        self.assertEqual(valid["cacheArchiveSha512Base64"], valid["rawArchiveSha512Base64"])
        (folder / "example.1.0.0.nupkg").write_bytes(b"tampered archive")
        with self.assertRaisesRegex(ValueError, "archive SHA512"):
            self.g.package_evidence(self.case, "Example/1.0.0", library)
        (folder / "example.1.0.0.nupkg").write_bytes(archive)
        (folder / "example.1.0.0.nupkg.sha512").write_text("wrong archive hash")
        with self.assertRaisesRegex(ValueError, "archive SHA512"):
            self.g.package_evidence(self.case, "Example/1.0.0", library)

    def test_hash_mismatch_and_credential_source_are_rejected(self):
        folder, _, library = self.package()
        for metadata in ({"contentHash": "wrong", "source": "https://api.nuget.org/v3/index.json"},
                         {"contentHash": library["sha512"], "source": "https://user:secret@example.invalid/index.json"}):
            (folder / ".nupkg.metadata").write_text(json.dumps(metadata))
            with self.assertRaises(ValueError):
                self.g.package_evidence(self.case, "Example/1.0.0", library)

    def test_residual_platform_package_and_outside_cache_are_rejected(self):
        _, _, library = self.package()
        with self.assertRaises(ValueError):
            self.g.package_evidence(self.case, "CP6.Platform.Contracts/0.10.2", library)
        for path in ("../../outside", str(self.root / "outside")):
            with self.assertRaises(ValueError):
                self.g.package_evidence(self.case, "Example/1.0.0", {**library, "path": path})

    def test_platform_project_must_be_exact_fixed_snapshot_path(self):
        right = self.case / "snapshots/platform/src/CP6.Platform.Contracts/CP6.Platform.Contracts.csproj"
        right.parent.mkdir(parents=True)
        right.write_text("<Project />")
        self.assertEqual(self.g.project_location(self.case, "CP6.Platform.Contracts", right), right.relative_to(self.case).as_posix())
        wrong = self.case / "snapshots/main/CP6.Platform.Contracts.csproj"
        wrong.parent.mkdir(parents=True)
        wrong.write_text("<Project />")
        with self.assertRaises(ValueError):
            self.g.project_location(self.case, "CP6.Platform.Contracts", wrong)

    def test_delta_retains_both_dependency_records(self):
        before = {"dependencies": {"net8.0": {"Example": {"resolved": "1", "contentHash": "one"}, "Removed": {"resolved": "1"}}}}
        after = {"dependencies": {"net8.0": {"Example": {"resolved": "2", "contentHash": "two"}, "Added": {"type": "Project"}}}}
        changes = self.g.lock_delta(before, after)
        by_name = {row["name"]: row for row in changes}
        self.assertEqual(by_name["Example"]["before"]["contentHash"], "one")
        self.assertEqual(by_name["Example"]["after"]["contentHash"], "two")
        self.assertIsNone(by_name["Removed"]["after"])
        self.assertIsNone(by_name["Added"]["before"])

    def test_running_build_cannot_be_inspected(self):
        (self.case / "state.json").write_text(json.dumps({"build": "RUNNING"}))
        with self.assertRaisesRegex(ValueError, "completed build"):
            self.g.inspect_owner(self.case)

    def test_owner_internal_alias_junction_is_rejected_before_resolve(self):
        target = self.case / "nuget-cache"
        target.mkdir()
        (target / "payload.json").write_text("{}")
        alias = self.case / "n"
        if os.name == "nt":
            created = subprocess.run(["cmd", "/c", "mklink", "/J", str(alias), str(target)], capture_output=True)
            self.assertEqual(created.returncode, 0, created.stderr)
        else:
            alias.symlink_to(target, target_is_directory=True)
        # The destination is inside this owner: containment alone cannot reject it.
        for spelling in (alias, self.g.native(alias)):
            with self.subTest(entry="contained", path=str(spelling)):
                with self.assertRaisesRegex(ValueError, "Link/reparse"):
                    self.g.contained(self.case, spelling / "payload.json")
            with self.subTest(entry="restore-cache", path=str(spelling)):
                with self.assertRaisesRegex(ValueError, "Link/reparse"):
                    self.g.recorded_cache(self.case, {"commands": [{"argv": ["dotnet", "restore", "--packages", str(spelling)]}]})

    def test_recorded_cache_accepts_only_consistent_owned_old_or_new_paths(self):
        for name in ("nuget-cache", "n"):
            cache = self.case / name
            state = {"commands": [{"argv": ["dotnet", "restore", "synthetic.csproj", "--packages", str(self.g.filesystem(cache))]}]}
            self.assertEqual(self.g.recorded_cache(self.case, state), cache)
        for value in (str(self.root / "n"), str(self.case / "arbitrary")):
            with self.assertRaises(ValueError):
                self.g.recorded_cache(self.case, {"commands": [{"argv": ["dotnet", "restore", "--packages", value]}]})
        with self.assertRaises(ValueError):
            self.g.recorded_cache(self.case, {"commands": [{"argv": ["dotnet", "restore", "--packages", str(self.case / cache)]} for cache in ("n", "nuget-cache")]})

    def test_long_package_archive_under_new_cache_is_read_with_extended_paths(self):
        name = "synthetic." + "longdependencyname" * 5
        folder = self.case / "n" / name / "1.0.0"
        self.g.filesystem(folder).mkdir(parents=True)
        stem = name + ".1.0.0.nupkg"
        archive = folder / stem
        self.assertGreater(len(str(archive)), 260)
        self.g.filesystem(archive).write_bytes(b"synthetic-long-package")
        self.g.filesystem(folder / (stem + ".sha512")).write_text(base64.b64encode(hashlib.sha512(b"synthetic-long-package").digest()).decode())
        self.g.filesystem(folder / ".nupkg.metadata").write_text(json.dumps({"contentHash": "synthetic-hash", "source": "https://api.nuget.org/v3/index.json"}))
        result = self.g.package_evidence(self.case, name + "/1.0.0", {"path": name + "/1.0.0", "sha512": "synthetic-hash"}, self.case / "n")
        self.assertEqual(result["archiveSha256"], hashlib.sha256(b"synthetic-long-package").hexdigest())
        self.assertTrue(result["cachePath"].startswith("n/"))

    def test_failed_build_missing_graph_stays_unknown_with_partial_evidence(self):
        import snapshot
        manifest = {"owner": self.case.name, "mainProjectClosure": [], "sources": {
            "main": {"sha": snapshot.MAIN_SHA, "tree": snapshot.MAIN_TREE, "files": []},
            "platform": {"sha": snapshot.PLATFORM_SHA, "tree": snapshot.PLATFORM_TREE, "files": []}}}
        path = self.case / "snapshot-manifest.json"
        path.write_text(json.dumps(manifest))
        (self.case / "state.json").write_text(json.dumps({"owner": self.case.name, "build": "FAIL", "snapshotManifestSha256": snapshot.sha256(path.read_bytes())}))
        report = self.g.inspect_owner(self.case)
        self.assertEqual(report["status"], "UNKNOWN")
        self.assertEqual(report["compatibility"], "UNKNOWN")
        self.assertTrue(report["errors"])
        self.assertEqual(report["packages"], [])
        self.assertEqual(report["formalPackageTrust"], "BLOCKED")


if __name__ == "__main__":
    unittest.main()
