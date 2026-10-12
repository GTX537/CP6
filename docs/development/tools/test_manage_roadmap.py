"""Regressions against false completion and double counting; no business tests."""
import copy
from pathlib import Path
import unittest

import manage_roadmap as roadmap


ROOT = Path(__file__).resolve().parents[3]


class ProgressIntegrityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.baseline = roadmap.read_json(ROOT / "docs/development/roadmap.json")

    def setUp(self):
        self.data = copy.deepcopy(self.baseline)
        # Reset a copy for isolated checker fixtures; live ledger progress may advance.
        for target in self.data["targets"]:
            target.update(assessment="PENDING", assessment_evidence=[], delivery_state="PLANNED", blockers=[], implementation_evidence=[], delivery_evidence=None)
            target["original_acceptance_inventory"].update(state="NOT_INVENTORIED", cases=[])
        for case in self.data["revision_acceptances"]:
            case["state"] = "NOT_RUN" if case["kind"] == "RUNTIME" else "REFERENCE_ONLY"
            case["execution_evidence"] = []
        for package in self.data["work_packages"]:
            package.update(state="ASSESSING" if package["id"] == "WP01" else "PLANNED", blockers=[], evidence=[])

    def test_complete_baseline_is_valid(self):
        self.assertEqual(roadmap.validate(self.data, ROOT, check_pins=False), [])

    def reject(self, phrase):
        errors = roadmap.validate(self.data, ROOT, check_pins=False)
        self.assertTrue(any(phrase in e for e in errors), errors)

    def test_missing_target_rejected(self):
        self.data["targets"].pop()
        self.reject("target coverage")

    def test_duplicate_target_rejected(self):
        self.data["targets"].append(copy.deepcopy(self.data["targets"][0]))
        self.reject("duplicate id")

    def test_duplicate_package_ownership_rejected(self):
        self.data["work_packages"][0]["target_ids"].append(self.data["targets"][0]["id"])
        self.reject("exactly one owning package")

    def test_runtime_pass_without_execution_evidence_rejected(self):
        case = next(c for c in self.data["revision_acceptances"] if c["kind"] == "RUNTIME")
        case["state"] = "PASS"
        self.reject("runtime PASS lacks execution evidence")

    def test_evidenced_progress_can_advance_in_memory(self):
        # Synthetic metadata tests the registrar, never persisted as business evidence.
        evidence = {"path": "docs/development/README.md", "source_sha": self.data["main_baseline_sha"], "scope": "unit fixture only", "command": "unit fixture", "environment": "registrar test", "observed_at": "2026-10-11T00:00:00Z", "result": "PASS"}
        case = next(c for c in self.data["revision_acceptances"] if c["kind"] == "RUNTIME")
        case.update(state="PASS", execution_evidence=[evidence])
        self.assertEqual(roadmap.validate(self.data, ROOT, check_pins=False), [])
        self.assertEqual(roadmap.metrics(self.data)["runtime_states"]["PASS"], 1)

    def test_static_pass_cannot_inflate_runtime(self):
        case = next(c for c in self.data["revision_acceptances"] if c["kind"] == "HISTORICAL_STATIC")
        case["kind"] = "RUNTIME"
        self.reject("static/runtime classification")

    def test_missing_consumer_rejected(self):
        case = self.data["revision_acceptances"][0]
        case["consumer_targets"] = []
        self.reject("immutable source mapping")

    def test_cannot_drop_required_revision_assets(self):
        target = next(t for t in self.data["targets"] if t["revision_outputs"])
        target["revision_outputs"] = []
        self.reject("required revision output mapping")

    def test_done_cannot_skip_original_module_acceptance(self):
        target = next(t for t in self.data["targets"] if not t["runtime_case_ids"])
        target["delivery_state"] = "DONE"
        self.reject("DONE requires all original acceptance cases")

    def test_blocked_needs_reason(self):
        self.data["targets"][0]["delivery_state"] = "BLOCKED"
        self.reject("BLOCKED requires a concrete blocker")

    def prepare_assessed_wp01(self):
        # In-memory metadata only; inventory completion does not mean runtime PASS.
        evidence = {"path": "docs/development/README.md", "source_sha": self.data["main_baseline_sha"], "scope": "unit fixture only"}
        package = next(p for p in self.data["work_packages"] if p["id"] == "WP01")
        package.update(state="DONE", evidence=[evidence])
        for target in self.data["targets"]:
            target.update(assessment="REUSE_CONFIRMED", assessment_evidence=[evidence])

    def test_wp01_cannot_close_without_original_inventories(self):
        self.prepare_assessed_wp01()
        self.reject("WP01: original acceptance inventory remains open")

    def test_wp01_cannot_close_with_empty_or_duplicate_inventory(self):
        self.prepare_assessed_wp01()
        for target in self.data["targets"]:
            target["original_acceptance_inventory"].update(state="COMPLETE", cases=[{"id": "fixture-ac", "state": "NOT_RUN"}])
        inventory = self.data["targets"][0]["original_acceptance_inventory"]
        for cases in ([], [{"id": "duplicate", "state": "NOT_RUN"}] * 2):
            with self.subTest(cases=cases):
                inventory["cases"] = cases
                self.reject("original acceptance inventory empty/duplicated")

    def test_wp01_inventory_completion_does_not_require_runtime_pass(self):
        self.prepare_assessed_wp01()
        for target in self.data["targets"]:
            target["original_acceptance_inventory"].update(state="COMPLETE", cases=[{"id": "fixture-ac", "state": "NOT_RUN"}])
        self.assertEqual(roadmap.validate(self.data, ROOT, check_pins=False), [])
        self.assertEqual(roadmap.metrics(self.data)["runtime_states"], {"NOT_RUN": 247})

    def test_count_each_runtime_case_once(self):
        metrics = roadmap.metrics(self.data)
        consumer_count = sum(len(t["runtime_case_ids"]) for t in self.data["targets"])
        self.assertGreater(consumer_count, metrics["revision_runtime"])
        self.assertEqual(metrics["revision_runtime"], 247)
        self.assertEqual(metrics["historical_static"], 41)

    def test_unassessed_existing_code_not_counted_as_delivered(self):
        metrics = roadmap.metrics(self.data)
        self.assertEqual(metrics["targets_assessed"], 0)
        self.assertEqual(metrics["targets_delivered"], 0)
        self.assertEqual(metrics["runtime_states"], {"NOT_RUN": 247})

    def test_render_has_distinct_business_and_static_counts(self):
        views = roadmap.render(self.data)
        self.assertIn("0/247", views["03-progress.md"])
        self.assertIn("41 条，不计业务运行进度", views["03-progress.md"])

    def test_activity_view_updates_with_package_status(self):
        self.data["work_packages"][0]["state"] = "DONE"
        self.data["work_packages"][2]["state"] = "IN_PROGRESS"
        view = roadmap.render(self.data)["03-progress.md"]
        self.assertIn("当前活动工作包：WP03 CRM客户经营（开发中）", view)


if __name__ == "__main__":
    unittest.main()
