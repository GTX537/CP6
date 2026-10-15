"""Real filesystem/Git regression tests; scratch lives in a fresh task owner."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest
import uuid
from types import SimpleNamespace
from unittest.mock import patch

sys.dont_write_bytecode = True

TASK = Path(__file__).resolve().parents[1]
WORKTREE = TASK.parents[2]
sys.path.insert(0, str(TASK))


class SnapshotTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.testroot = WORKTREE / "artifacts/wp02-02/consumption-compatibility" / uuid.uuid4().hex
        cls.testroot.mkdir(parents=True, exist_ok=False)
        cls.serial = 0

    def setUp(self):
        spec = importlib.util.find_spec("snapshot")
        self.assertIsNotNone(spec, "snapshot safety implementation is missing")
        import snapshot
        self.s = snapshot
        type(self).serial += 1
        self.case = self.testroot / str(self.serial)
        self.case.mkdir()

    def test_owner_rejects_outside_and_noncanonical_and_reuse(self):
        owner = uuid.uuid4().hex
        root = self.case / "worktree"
        root.mkdir()
        expected = root / "artifacts/wp02-02/consumption-compatibility" / owner
        for path in (self.case / owner, expected.parent / ".." / owner):
            with self.assertRaises(ValueError):
                self.s.owner_root(root, owner, path, create=True)
        actual = self.s.owner_root(root, owner, expected, create=True)
        self.assertEqual(actual, expected)
        with self.assertRaises(ValueError):
            self.s.owner_root(root, owner, expected, create=True)

    def test_owner_rejects_symlink_or_junction(self):
        root = self.case / "worktree"
        root.mkdir()
        outside = self.case / "outside"
        outside.mkdir()
        link = root / "artifacts"
        if os.name == "nt":
            result = subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(outside)], capture_output=True)
            self.assertEqual(result.returncode, 0, result.stderr)
        else:
            link.symlink_to(outside, target_is_directory=True)
        owner = uuid.uuid4().hex
        with self.assertRaises(ValueError):
            self.s.owner_root(root, owner, link / "wp02-02/consumption-compatibility" / owner, create=True)

    def test_overlay_exactly_four_references_and_no_other_byte_changes(self):
        source = b'\xef\xbb\xbf<Project>\r\n<PackageReference Include="CP6.Platform.Messaging" Version="[0.10.2]" />\r\n<PackageReference Include="Other" Version="1.2.3" />\r\n</Project>\r\n'
        changed = self.s.replace_reference(source, "CP6.Platform.Messaging", "../platform/Messaging.csproj")
        self.assertEqual(changed.replace(b'<ProjectReference Include="../platform/Messaging.csproj" />', b'<PackageReference Include="CP6.Platform.Messaging" Version="[0.10.2]" />'), source)
        for invalid in (source.replace(b"0.10.2", b"0.10.3"), source + source, b"<Project />"):
            with self.assertRaises(ValueError):
                self.s.replace_reference(invalid, "CP6.Platform.Messaging", "../platform/Messaging.csproj")

    def test_export_uses_committed_bytes_and_reports_omissions(self):
        repo = self.case / "repo"
        repo.mkdir()
        def git(*args):
            return subprocess.check_output(["git", "-C", str(repo), *args]).decode().strip()
        git("init", "--quiet")
        (repo / "source.cs").write_bytes(b"committed\r\n")
        (repo / "ignored.bin").write_bytes(b"not in build closure")
        git("-c", "core.autocrlf=false", "add", "source.cs", "ignored.bin")
        git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "-qm", "fixture")
        sha = git("rev-parse", "HEAD")
        (repo / "source.cs").write_bytes(b"dirty")
        exported = self.case / "export"
        manifest = self.s.export_git(repo, sha, exported, lambda path: path == "source.cs")
        self.assertEqual((exported / "source.cs").read_bytes(), b"committed\r\n")
        self.assertFalse((exported / "ignored.bin").exists())
        self.assertEqual(len(manifest["files"]), 2)
        self.assertEqual(sum(row["exported"] for row in manifest["files"]), 1)
        self.s.verify_files(exported, manifest["files"])
        (exported / "source.cs").write_bytes(b"tampered")
        with self.assertRaises(ValueError):
            self.s.verify_files(exported, manifest["files"])

    def test_manifest_rejects_changed_source_or_overlay_or_config(self):
        data = json.loads((TASK / "input-manifest.json").read_text(encoding="utf-8"))
        self.s.validate_plan(data)
        for mutate in (lambda d: d["main"].update(sha="0" * 40), lambda d: d["overlay"].pop(), lambda d: d.update(sdk="10.0.100"), lambda d: d["main"].update(nugetConfigSha256="0" * 64)):
            copy = json.loads(json.dumps(data))
            mutate(copy)
            with self.assertRaises(ValueError):
                self.s.validate_plan(copy)

    def test_unsafe_git_path_rejected(self):
        for name in ("../bad", "/absolute", "a/../../b", "a\\bad", "C:/bad", "a/./b", "a//b"):
            with self.assertRaises(ValueError):
                self.s.safe_relative(name)

    def test_extra_source_and_missing_source_are_rejected(self):
        root = self.case / "source"
        root.mkdir()
        path = root / "known.cs"
        path.write_bytes(b"known")
        rows = [{"path": "known.cs", "exported": True, "sha256": self.s.sha256(b"known")}]
        self.s.verify_files(root, rows)
        extra = root / "injected.cs"
        extra.write_bytes(b"unexpected compile input")
        with self.assertRaises(ValueError):
            self.s.verify_files(root, rows)
        extra.unlink()
        path.unlink()
        with self.assertRaises(ValueError):
            self.s.verify_files(root, rows)

    def test_main_restore_failure_does_not_hide_other_root_diagnostics(self):
        import run
        state = {"owner": self.case.name, "build": "NOT_RUN", "runtime": "NOT_RUN", "commands": [], "failures": [], "formalPackageTrust": "BLOCKED", "mainConsumption": "NOT_RUN"}
        labels = []
        def fake_command(root, label, argv, env):
            labels.append(label)
            if label.endswith("-restore"):
                self.assertEqual(argv[argv.index("--packages") + 1], str(root / "n"))
            log = root / (label + ".log")
            log.write_text("8.0.424" if label == "sdk-version" else "fixture", encoding="utf-8")
            return {"label": label, "exitCode": 1 if label == "main-webapi-restore" else 0, "log": log.name}
        with patch.object(run, "verify", return_value=state), patch.object(run, "environment", return_value={}), patch.object(run, "command", side_effect=fake_command), patch.object(run, "capture_identities"):
            with self.assertRaises(ValueError):
                run.build(self.case, SimpleNamespace(owner=self.case.name, dotnet=Path("dotnet")))
        self.assertIn("database-probe-build-only-build", labels)
        self.assertIn("private-probe-build", labels)
        self.assertNotIn("main-webapi-build", labels)
        final = json.loads((self.case / "state.json").read_text())
        self.assertEqual(final["build"], "FAIL")
        self.assertEqual(final["buildRoots"]["private-probe"]["build"], "PASS")

    def test_probe_can_report_partial_scope_after_independent_main_failure(self):
        import run
        state = {"owner": self.case.name, "build": "FAIL", "runtime": "NOT_RUN", "commands": [], "failures": ["Main failed"], "buildRoots": {"private-probe": {"restore": "PASS", "build": "PASS", "restoreExitCode": 0, "buildExitCode": 0}}}
        def fake_command(root, label, argv, env):
            (root / "runtime/runtime.json").write_text(json.dumps({"status": "PASS", "owner": self.case.name}))
            return {"label": label, "exitCode": 0, "log": "fixture.log"}
        with patch.object(run, "verify", return_value=state), patch.object(run, "environment", return_value={}), patch.object(run, "command", side_effect=fake_command):
            try:
                result = run.probe(self.case, SimpleNamespace(owner=self.case.name, dotnet=Path("dotnet")))
            except ValueError as error:
                self.fail(f"Independent successful private probe was incorrectly blocked: {error}")
        self.assertEqual(result["build"], "FAIL")
        self.assertEqual(result["runtimeScope"], "PRIVATE_PROBE_ONLY_MAIN_BUILD_FAILED")
        self.assertEqual(result["compatibility"], "UNKNOWN")

    @unittest.skipUnless(os.name == "nt", "Windows long-path regression")
    def test_long_paths_are_verified_and_hashed_without_missing_files(self):
        import run
        root = self.case / "snapshots/main"
        path = root / ("a" * 90) / ("b" * 90) / "dependency.dll"
        extended = Path("\\\\?\\" + str(path))
        extended.parent.mkdir(parents=True)
        extended.write_bytes(b"real long path bytes")
        self.assertGreater(len(str(path)), 260)
        self.assertFalse(path.exists(), "Fixture must reproduce this Python process's MAX_PATH failure")
        rows = [{"path": path.relative_to(root).as_posix(), "exported": True, "sha256": self.s.sha256(b"real long path bytes")}]
        try:
            self.s.verify_files(root, rows)
            run.capture_identities(self.case)
        except (OSError, ValueError) as error:
            self.fail(f"Existing long path was incorrectly rejected: {error}")
        result = json.loads((self.case / "build-identities.json").read_text())
        self.assertEqual(len(result["files"]), 1)
        self.assertEqual(result["files"][0]["sha256"], rows[0]["sha256"])

    @unittest.skipUnless(os.name == "nt", "Windows long-path regression")
    def test_nuget_cache_uses_short_normal_path_for_msbuild_imports(self):
        import run
        env = run.environment(self.case, Path("C:/dotnet/dotnet.exe"))
        self.assertEqual(env["NUGET_PACKAGES"], str(self.case / "n"))
        self.assertEqual(env["NUGET_HTTP_CACHE_PATH"], "\\\\?\\" + str(self.case / "h"))


if __name__ == "__main__":
    unittest.main()
