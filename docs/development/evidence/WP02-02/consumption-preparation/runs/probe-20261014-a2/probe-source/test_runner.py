"""Synthetic parser fixtures only; these ZIPs are not real package evidence."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("run_probe", Path(__file__).resolve().parents[1] / "run_probe.py")
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class ApprovalTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.out = self.directory / "out"
        self.out.mkdir()
        self.approval = self.directory / "approval.json"
        self.document = {"repository": "https://github.com/GTX537/CP6.Platform", "sourceGitSha": "a" * 40, "packages": []}
        for name in sorted(runner.PACKAGE_NAMES):
            package = self.directory / (name + ".0.0.0-synthetic.nupkg")
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr(name + ".nuspec", f'<package><metadata><id>{name}</id><version>0.0.0-synthetic</version>'
                    f'<repository url="https://github.com/GTX537/CP6.Platform" commit="{"a" * 40}" /></metadata></package>')
                archive.writestr("lib/net8.0/" + name + ".dll", b"synthetic bytes, never executable")
            self.document["packages"].append({"packageId": name, "version": "0.0.0-synthetic", "sourceGitSha": "a" * 40,
                                               "sha256": runner.digest(package.read_bytes())})

    def run_extract(self, approved_hash=None):
        self.approval.write_text(json.dumps(self.document), encoding="utf-8")
        return runner.extract_approved(self.approval, approved_hash or runner.digest(self.approval.read_bytes()), self.directory, self.out)

    def test_synthetic_shape_extracts_only_four_dlls(self):
        self.assertEqual(len(self.run_extract()), 7)
        self.assertEqual({p.stem for p in self.out.iterdir()}, runner.PROBE_NAMES)

    def test_independent_document_hash_mismatch_rejected(self):
        with self.assertRaisesRegex(ValueError, "approval document hash mismatch"):
            self.run_extract("0" * 64)

    def test_archive_hash_mismatch_rejected(self):
        self.document["packages"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "archive hash mismatch"):
            self.run_extract()

    def test_missing_seventh_archive_approval_rejected(self):
        self.document["packages"].pop()
        with self.assertRaisesRegex(ValueError, "seven-package"):
            self.run_extract()

    def test_mixed_source_rejected(self):
        self.document["packages"][0]["sourceGitSha"] = "b" * 40
        with self.assertRaisesRegex(ValueError, "mixed approved source"):
            self.run_extract()

    def test_mixed_version_rejected(self):
        self.document["packages"][0]["version"] = "0.0.1-synthetic"
        with self.assertRaisesRegex(ValueError, "mixed approved versions"):
            self.run_extract()

    def test_repository_mismatch_rejected(self):
        self.document["repository"] = "https://synthetic.invalid/wrong"
        with self.assertRaisesRegex(ValueError, "repository mismatch"):
            self.run_extract()

    def test_nuspec_source_mismatch_rejected_even_with_consistent_approval(self):
        self.document["sourceGitSha"] = "b" * 40
        for row in self.document["packages"]:
            row["sourceGitSha"] = "b" * 40
        with self.assertRaisesRegex(ValueError, "nuspec repository/source mismatch"):
            self.run_extract()


if __name__ == "__main__":
    unittest.main()
