"""Bounded integrity/regression checks for preparation artifacts; no business run."""
import copy
import json
from pathlib import Path
import unittest

from comparator import compare
from prepare import BASELINE, BUNDLE, OUT, git_bytes, parse, sha, source_references

ROOT = Path(__file__).resolve().parents[3]
EVIDENCE = ROOT / OUT


class BundleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.matrix = parse((EVIDENCE / "obligation-matrix.json").read_bytes())
        cls.contract = parse((EVIDENCE / "gate-contract.proposed.json").read_bytes())
        cls.decision = parse((EVIDENCE / "decision.UNSIGNED.json").read_bytes())
        cls.sources = {name: parse(git_bytes(ROOT, BASELINE, BUNDLE + name)) for name in ("cases.json", "normative-coverage.json", "supplements/review-r1/wp05-supplement-proposed.json")}
        cls.roadmap = parse(git_bytes(ROOT, BASELINE, "docs/development/roadmap.json"))

    def test_manifest_uses_git_bytes_not_crlf_worktree(self):
        raw = git_bytes(ROOT, BASELINE, BUNDLE + "basis-manifest.json")
        self.assertEqual("979cb7f4fc779f6200e16b2919a89afc244681d122683036378e068931fcd3ee", sha(raw))
        self.assertNotEqual(sha(raw), sha(raw.replace(b"\n", b"\r\n")))

    def test_matrix_preserves_every_source_assertion_and_exact_id(self):
        expected = {}
        for row in self.sources["cases.json"]["cases"]:
            expected[row["id"]] = row["source_assertion"]
        for row in self.sources["normative-coverage.json"]["obligations"]:
            expected[row["id"]] = row["source_obligation"]
        for row in self.sources["supplements/review-r1/wp05-supplement-proposed.json"]["rows"]:
            expected[row["id"]] = row["required_observation"]
        self.assertEqual(577, len(self.matrix["rows"]))
        self.assertEqual(expected, {r["id"]: r["source_assertion"] for r in self.matrix["rows"]})
        self.assertTrue(all(r["object"] and r["operation"] and r["owner"] and r["observations"] and r["source_refs"] and r["runtime_state"] == "NOT_RUN" for r in self.matrix["rows"]))

    def test_each_pg01_field_observed_not_just_page_count(self):
        for row in self.matrix["rows"]:
            if row["kind"] == "CURRENT_PAGE_FIELDS":
                fields = [s.strip() for s in row["source_assertion"].splitlines() if s.strip().startswith("| `PG01-FLD-")]
                self.assertGreater(len(fields), 1)
                self.assertEqual(fields, row["observations"])

    def test_pins_and_unsigned_fields(self):
        self.assertEqual(self.contract["pins"]["matrix"], sha((EVIDENCE / "obligation-matrix.json").read_bytes()))
        self.assertEqual(self.contract["pins"], self.decision["pins"])
        self.assertEqual("UNSIGNED", self.decision["status"])
        for key in ("actor", "time", "authority", "effective_at", "decision_text", "adoption_id"):
            self.assertIsNone(self.decision[key])
        self.assertFalse(self.decision["actual_adoption"])

    def test_all_123_other_targets_reject_even_if_contract_is_extended(self):
        others = [t["id"] for t in self.roadmap["targets"] if t["id"] not in self.contract["targets"]]
        self.assertEqual(123, len(others))
        for target in others:
            with self.subTest(target=target):
                contract = copy.deepcopy(self.contract)
                contract["targets"][target] = contract["targets"]["PLM-ITER-01"]
                result = compare({"target": target}, contract)
                self.assertIn("TARGET_NOT_WHITELISTED", result["reasons"])
                self.assertFalse(result["actual_completion"])

    def test_actual_unsigned_template_cannot_satisfy_proposal(self):
        for target in self.contract["targets"]:
            result = compare({"target": target, "adoption": self.decision}, self.contract)
            self.assertFalse(result["proposal_conditions_satisfied"])
            self.assertIn("ADOPTION_REQUIRED", result["reasons"])
            self.assertIn("CROSS_REPOSITORY_SCOPE_UNRESOLVED", result["reasons"])

    def test_exact_main_task_and_r1_membership(self):
        for target, contract in self.contract["targets"].items():
            self.assertEqual([t["id"] for t in self.roadmap["implementation_tasks"] if target in t["target_ids"] and t["coverage_role"] == "IMPLEMENTATION"], contract["tasks"])
            self.assertEqual(next(t["runtime_case_ids"] for t in self.roadmap["targets"] if t["id"] == target), contract["r1"])
            self.assertEqual(["Main"], contract["repositories"])
            self.assertEqual("UNRESOLVED", contract["cross_repository_scope_state"])

    def test_dp_and_historical_gaps_remain_open(self):
        dp = [r for r in self.matrix["rows"] if r["kind"] == "OPEN_DECISION"]
        self.assertEqual({f"WP05-01/1/BODY/DP-ITER-{i:02}" for i in range(1, 9)}, {r["id"] for r in dp})
        self.assertTrue(all(r["coverage_state"] == "UNRESOLVED" for r in dp))
        for gap in self.decision["historical_gaps"]:
            self.assertEqual(("NOT_INVENTORIED", "UNRECOVERABLE", "UNPROVABLE"), (gap["historical_inventory_state"], gap["recovery_state"], gap["equivalence"]))
            self.assertIsNone(gap["legacy_case_ids"])
            self.assertIsNone(gap["missing_original_sha256"])

    def test_all_structured_source_reference_occurrences_accounted(self):
        count = sum(len(list(source_references(value))) for value in self.sources.values())
        report = parse((EVIDENCE / "source-verification.json").read_bytes())
        self.assertEqual(892, count)
        self.assertEqual(count, report["reference_occurrences_checked"])
        self.assertFalse(report["errors"])
        self.assertTrue(all(r["result"] == "PASS" for r in report["references"]))

    def test_duplicate_keys_and_nan_reject(self):
        for raw in (b'{"a":1,"a":2}', b'{"value":NaN}'):
            with self.assertRaises(ValueError):
                parse(raw)


if __name__ == "__main__":
    unittest.main(verbosity=2)
