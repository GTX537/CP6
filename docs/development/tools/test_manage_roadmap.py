"""Regressions against false completion and double counting; no business tests."""
import copy
import contextlib
import io
import json
import tempfile
from pathlib import Path
import unittest
from unittest import mock

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
        for task in self.data.get("implementation_tasks", []):
            task.update(state="PLANNED", blockers=[], validation_evidence=[], delivery_evidence=None,
                        entry_gate="NOT_CHECKED", assignment=None)

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

    def test_overview_summarizes_long_dependencies_without_truncating_task_cards(self):
        task = self.data["implementation_tasks"][0]
        task["dependencies"] = [f"FIX-{index:02}" for index in range(13)]
        view = roadmap.render(self.data)["07-detailed-tasks.md"]
        self.assertIn("共13项，详见任务卡", view)
        self.assertNotIn(", ".join(task["dependencies"]), view)

    def test_activity_view_updates_with_package_status(self):
        self.data["work_packages"][0]["state"] = "DONE"
        self.data["work_packages"][2]["state"] = "IN_PROGRESS"
        view = roadmap.render(self.data)["03-progress.md"]
        self.assertIn("当前活动工作包：WP03 CRM客户经营（开发中）", view)

    def test_tasks_need_unique_ids_and_valid_targets(self):
        self.data["implementation_tasks"].append(copy.deepcopy(self.data["implementation_tasks"][0]))
        self.reject("tasks: duplicate id")
        self.data["implementation_tasks"].pop()
        self.data["implementation_tasks"][0]["target_ids"] = ["UNKNOWN"]
        self.reject("task target")

    def test_empty_task_inventory_rejected(self):
        self.data["implementation_tasks"] = []
        self.reject("task inventory")

    def test_unknown_task_dependency_and_cycle_rejected(self):
        first = next(t for t in self.data["implementation_tasks"] if t["id"] == "S01-01")
        first["dependencies"] = ["UNKNOWN"]
        self.reject("task dependency")
        first["dependencies"] = ["S01-02"]
        self.reject("task dependency cycle")

    def test_task_cannot_start_before_dependencies_done(self):
        next(t for t in self.data["implementation_tasks"] if t["id"] == "S01-02")["state"] = "IN_PROGRESS"
        self.reject("unfinished task dependencies")

    def test_wp_tasks_cannot_disable_definition_and_dispatch_checks(self):
        task = copy.deepcopy(self.data["implementation_tasks"][0])
        task["id"] = "WP02-999"
        self.data["implementation_tasks"].append(task)
        self.data.pop("dispatch_policy", None)
        self.reject("WP tasks require dispatch policy and definitions")

    def test_task_done_requires_validation_and_remote_receipt(self):
        self.data["implementation_tasks"][0]["state"] = "DONE"
        self.reject("task DONE lacks validation")
        self.reject("task DONE lacks audited remote-main")

    def test_task_blocked_and_missing_plan_rejected(self):
        self.data["implementation_tasks"][0].update(state="BLOCKED", plan_path="missing.md")
        self.reject("task BLOCKED needs a reason")
        self.reject("task plan missing")

    def test_task_plan_cannot_escape_repository(self):
        self.data["implementation_tasks"][0]["plan_path"] = str(ROOT.parent / "outside.md")
        self.reject("task plan missing")

    def test_evidenced_task_delivery_does_not_close_targets_or_cases(self):
        # Synthetic metadata only exercises registrar logic; it is never persisted.
        ev = {"path": "docs/development/README.md", "source_sha": self.data["main_baseline_sha"], "scope": "unit fixture only", "command": "unit fixture", "environment": "registrar test", "observed_at": "2026-10-11T00:00:00Z", "result": "PASS"}
        receipt = {**ev, "remote_main_sha": self.data["main_baseline_sha"], "pr_url": "https://github.com/example/example/pull/1"}
        next(t for t in self.data["implementation_tasks"] if t["id"] == "S01-01").update(state="DONE", validation_evidence=[ev], delivery_evidence=receipt, entry_gate="CONFIRMED", assignment={"chat_label": "unit fixture", "branch": "codex/unit-fixture", "worktree": "unit-fixture-only", "claimed_at": "2026-10-11T00:00:00Z"})
        self.assertEqual(roadmap.validate(self.data, ROOT, check_pins=False), [])
        self.assertEqual(roadmap.metrics(self.data)["targets_delivered"], 0)
        self.assertEqual(roadmap.metrics(self.data)["runtime_states"], {"NOT_RUN": 247})
        view = roadmap.render(self.data)["07-detailed-tasks.md"]
        self.assertIn("S01 已交付：1/12", view)
        self.assertIn("S02-00", view)


class DispatchIntegrityTests(unittest.TestCase):
    """Small in-memory dispatch fixtures; never persisted as claims or business evidence."""
    def setUp(self):
        self.targets = {"T-A": {"package_id": "WP02", "delivery_state": "PLANNED"}, "T-B": {"package_id": "WP03", "delivery_state": "PLANNED"}}
        self.data = {"work_packages": [{"id": "WP01"}, {"id": "WP02"}, {"id": "WP03"}], "implementation_tasks": []}
        for tid, package, target in [("A", "WP02", "T-A"), ("B", "WP03", "T-B")]:
            self.data["implementation_tasks"].append({"id": tid, "package_id": package, "repository": "Main", "kind": "CODE", "coverage_role": "IMPLEMENTATION", "target_ids": [target], "state": "PLANNED", "entry_gate": "NOT_CHECKED", "assignment": None, "exclusive_resources": ["Main:"+tid], "entry_conditions": ["verify current inputs"], "dependencies": []})
        self.data["target_task_coverage"] = [{"target_id": "T-A", "implementation_task_ids": ["A"]}, {"target_id": "T-B", "implementation_task_ids": ["B"]}]

    def errors(self):
        return roadmap.validate_dispatch(self.data, self.targets)

    def activate(self, task, label):
        task.update(state="IN_PROGRESS", entry_gate="CONFIRMED", assignment={"chat_label": label, "branch": "codex/"+label, "worktree": "fixture/"+label, "claimed_at": "2026-10-11T00:00:00Z"})

    def test_complete_planned_dispatch_is_valid(self):
        self.assertEqual(self.errors(), [])

    def test_design_or_support_task_cannot_replace_target_implementation(self):
        for kind, role in [("DESIGN", "IMPLEMENTATION"), ("CODE", "FOUNDATION")]:
            with self.subTest(kind=kind, role=role):
                self.data["implementation_tasks"][0].update(kind=kind, coverage_role=role)
                self.assertTrue(any("no owning implementation task" in e for e in self.errors()))

    def test_wrong_package_cannot_claim_target_implementation(self):
        self.data["implementation_tasks"][0]["package_id"] = "WP03"
        self.assertTrue(any("no owning implementation task" in e for e in self.errors()))

    def test_target_coverage_cannot_drop_a_task(self):
        self.data["target_task_coverage"][0]["implementation_task_ids"] = []
        self.assertTrue(any("target task coverage differs" in e for e in self.errors()))

    def test_active_task_requires_claim_and_entry_check(self):
        self.data["implementation_tasks"][0]["state"] = "IN_PROGRESS"
        errors=self.errors()
        self.assertTrue(any("entry gate not confirmed" in e for e in errors))
        self.assertTrue(any("assignment missing" in e for e in errors))

    def test_active_tasks_cannot_share_resources(self):
        for task in self.data["implementation_tasks"]:
            self.activate(task, task["id"])
            task["exclusive_resources"] = ["Main:shared-writer"]
        self.assertTrue(any("active resource conflict" in e for e in self.errors()))

    def test_planned_tasks_can_share_future_resources(self):
        for task in self.data["implementation_tasks"]: task["exclusive_resources"] = ["Main:shared-writer"]
        self.assertEqual(self.errors(), [])

    def test_same_chat_or_worktree_cannot_hold_two_active_tasks(self):
        for task in self.data["implementation_tasks"]: self.activate(task, "same")
        self.assertTrue(any("active assignment conflict" in e for e in self.errors()))

    def test_worktree_aliases_cannot_hide_an_active_assignment_conflict(self):
        first, second = self.data["implementation_tasks"]
        self.activate(first, "first")
        self.activate(second, "second")
        first["assignment"]["worktree"] = str(ROOT / "claim-a")
        second["assignment"]["worktree"] = str(ROOT / "claim-b" / ".." / "claim-a")
        self.assertTrue(any("active assignment conflict" in e and "worktree" in e for e in self.errors()))

    def test_target_done_requires_owning_tasks_done(self):
        self.targets["T-A"]["delivery_state"] = "DONE"
        self.assertTrue(any("target DONE with unfinished implementation tasks" in e for e in self.errors()))

    def test_validation_task_does_not_replace_implementation(self):
        self.data["implementation_tasks"][0].update(kind="VALIDATION", coverage_role="VALIDATION")
        self.assertTrue(any("no owning implementation task" in e for e in self.errors()))

    def test_coordinator_can_have_no_target(self):
        self.data["implementation_tasks"].append({"id": "C", "package_id": "WP01", "repository": "Main", "kind": "DESIGN", "coverage_role": "COORDINATION", "target_ids": [], "state": "PLANNED", "entry_gate": "NOT_CHECKED", "assignment": None, "exclusive_resources": ["coordination:dispatch"], "entry_conditions": ["source audit"], "dependencies": []})
        self.assertEqual(self.errors(), [])


class TaskDefinitionTests(unittest.TestCase):
    """Real files in an isolated directory; no workflow or business state changes."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.targets = {"T-A": {"package_id": "WP02", "delivery_state": "PLANNED"}}
        self.definition = {
            "id": "WP02-01", "package_id": "WP02", "title": "Exact owner input",
            "repository": "Main", "kind": "CODE", "target_ids": ["T-A"],
            "relative_size": "M", "dependencies": ["S01-01"],
            "scope": ["Preserve all original source bytes."],
            "outputs": ["A durable owner receipt."],
            "code_paths": ["CP6.Core/Services/A Long Original Path.cs"],
            "new_paths": ["CP6.Tests/NewOwnerTests.cs"],
            "acceptance_positive": ["An identical retry returns the original receipt."],
            "acceptance_negative": ["Changed source bytes produce no new effect."],
            "external_prerequisites": ["A real owner receipt is required for adoption."],
            "exclusive_resources": ["Main:owner-transaction"],
            "notes": ["Approval and owner application are separate facts."],
            "prerequisite_bindings": [
                {"requirement": "Use the exact-value foundation", "task_ids": ["S01-01"],
                 "mode": "TASK_DELIVERY", "condition": "Before implementation"},
                {"requirement": "Actual provider adoption", "task_ids": [],
                 "mode": "EXTERNAL_EVIDENCE", "condition": "Before enabling real effects"},
            ],
            "source_refs": ["docs/design-handbook/source with spaces.md"],
        }
        self.definitions = {"schema_version": 1, "tasks": [self.definition]}
        old = {"id": "S01-01", "title": "Existing foundation", "repository": "Main",
               "kind": "DESIGN", "target_ids": ["T-A"], "relative_size": "M",
               "package_id": "WP02", "coverage_role": "FOUNDATION", "dependencies": [],
               "plan_path": "docs/superpowers/plans/existing-plan.md", "state": "PLANNED",
               "entry_gate": "NOT_CHECKED", "entry_conditions": ["Check current inputs"],
               "exclusive_resources": ["Main:foundation"], "blockers": []}
        metadata = ("id", "package_id", "title", "repository", "kind", "target_ids",
                    "relative_size", "dependencies", "exclusive_resources")
        new = {key: copy.deepcopy(self.definition[key]) for key in metadata}
        new.update(plan_path="docs/development/task-plans/WP02.md", card_anchor="wp02-01",
                   state="PLANNED", coverage_role="IMPLEMENTATION", entry_gate="NOT_CHECKED",
                   entry_conditions=["Check current inputs"], blockers=[])
        self.data = {
            "work_packages": [{"id": f"WP{i:02}", "title": f"Package {i}"} for i in range(1, 15)],
            "implementation_tasks": [old, new],
            "target_task_coverage": [{"target_id": "T-A", "implementation_task_ids": ["WP02-01"]}],
            "dispatch_policy": {
                "definition_path": "docs/development/task-definitions.json",
                "package_plans": {f"WP{i:02}": f"docs/development/task-plans/WP{i:02}.md" for i in range(1, 15)},
            },
        }
        for path in (self.definition["source_refs"][0], old["plan_path"], new["plan_path"]):
            target = self.root / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("Fixture content\n", encoding="utf-8")
        self.save_definitions()

    def save_definitions(self):
        path = self.root / "docs/development/task-definitions.json"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(self.definitions), encoding="utf-8")

    def errors(self):
        self.save_definitions()
        return roadmap.validate_tasks(self.data, self.root, self.targets)

    def reject(self, phrase):
        errors = self.errors()
        self.assertTrue(any(phrase in error for error in errors), errors)

    def test_definition_matches_metadata_and_external_evidence_need_not_be_a_task(self):
        self.assertEqual(self.errors(), [])

    def test_definitions_must_cover_exactly_the_new_wp_tasks(self):
        self.definitions["tasks"] = []
        self.reject("definition task IDs differ")

    def test_existing_s01_task_must_not_be_redefined(self):
        self.definitions["tasks"].append({**copy.deepcopy(self.definition), "id": "S01-01"})
        self.reject("definition task IDs differ")

    def test_all_shared_metadata_must_match(self):
        for field, value in [("title", "Drifted"), ("repository", "CRM"),
                             ("dependencies", []), ("exclusive_resources", ["Main:other"])]:
            with self.subTest(field=field):
                original = copy.deepcopy(self.definition[field])
                self.definition[field] = value
                self.reject("definition metadata differs: " + field)
                self.definition[field] = original

    def test_scope_outputs_and_both_acceptance_sides_cannot_be_empty(self):
        for field in ("scope", "outputs", "acceptance_positive", "acceptance_negative"):
            with self.subTest(field=field):
                original = self.definition[field]
                self.definition[field] = ["  "]
                self.reject("definition requires nonempty " + field)
                self.definition[field] = original

    def test_source_references_must_be_existing_repository_inputs(self):
        for path in ("docs/missing.md", "../outside.md", "CP6.Core/Outside.cs"):
            with self.subTest(path=path):
                self.definition["source_refs"] = [path]
                self.reject("invalid definition source reference")

    def test_normalized_repository_fixture_generator_is_a_valid_source(self):
        path = self.root / "tools/build_fixture.py"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("# fixture source only\n", encoding="utf-8")
        self.definition["source_refs"].append("tools/build_fixture.py")
        self.assertEqual(self.errors(), [])

    def test_task_delivery_binding_must_be_a_direct_dependency(self):
        self.definition["dependencies"] = []
        self.data["implementation_tasks"][1]["dependencies"] = []
        self.reject("TASK_DELIVERY binding missing dependency")

    def test_all_binding_modes_reject_unknown_task_references(self):
        for mode in ("TASK_DELIVERY", "CONDITIONAL_CONSUMER", "EXTERNAL_EVIDENCE"):
            with self.subTest(mode=mode):
                self.definition["prerequisite_bindings"][0].update(mode=mode, task_ids=["WP99-99"])
                self.reject("invalid prerequisite task reference")

    def test_conditional_consumer_does_not_create_unconditional_dependency(self):
        self.definition["dependencies"] = []
        self.data["implementation_tasks"][1]["dependencies"] = []
        self.definition["prerequisite_bindings"][0].update(mode="CONDITIONAL_CONSUMER")
        self.assertEqual(self.errors(), [])

    def test_binding_requires_supported_mode_and_condition(self):
        binding = self.definition["prerequisite_bindings"][0]
        binding["mode"] = "WAIT_FOR_WHOLE_PACKAGE"
        self.reject("invalid prerequisite binding")
        binding.update(mode="TASK_DELIVERY", condition="")
        self.reject("invalid prerequisite binding")

    def test_task_anchor_and_plan_must_match_the_generated_card(self):
        task = self.data["implementation_tasks"][1]
        task["card_anchor"] = "another-task"
        self.reject("definition card locator differs")
        task.update(card_anchor="wp02-01", plan_path="docs/superpowers/plans/existing-plan.md")
        self.reject("definition card locator differs")

    def test_malformed_definition_file_reports_validation_error(self):
        path = self.root / "docs/development/task-definitions.json"
        path.write_text("{broken", encoding="utf-8")
        errors = roadmap.validate_tasks(self.data, self.root, self.targets)
        self.assertTrue(any("cannot read task definitions" in error for error in errors), errors)

    def test_invalid_definition_schema_is_rejected(self):
        self.definitions["schema_version"] = 2
        self.reject("unsupported task definition schema")

    def test_duplicate_definition_ids_are_rejected(self):
        self.definitions["tasks"].append(copy.deepcopy(self.definition))
        self.reject("duplicate definition ID")

    def test_generated_card_can_be_bootstrapped_without_an_existing_file(self):
        (self.root / self.data["implementation_tasks"][1]["plan_path"]).unlink()
        self.assertEqual(self.errors(), [])

    def test_absent_definition_path_skips_helper_but_cannot_bypass_wp_guard(self):
        self.data["dispatch_policy"].pop("definition_path")
        self.definitions["tasks"] = []
        self.assertEqual(roadmap.validate_definitions(self.data, self.root, self.targets), [])
        self.reject("WP tasks require dispatch policy and definitions")

    def test_render_all_package_cards_without_repeating_ledger_state(self):
        self.assertTrue(callable(getattr(roadmap, "render_cards", None)), "render_cards is required")
        cards = roadmap.render_cards(self.definitions, self.data)
        self.assertEqual(set(cards), {f"task-plans/WP{i:02}.md" for i in range(1, 15)})
        card = cards["task-plans/WP02.md"]
        self.assertIn('<a id="wp02-01"></a>', card)
        self.assertIn("../../../docs/design-handbook/source%20with%20spaces.md", card)
        self.assertIn(self.definition["code_paths"][0], card)
        self.assertIn("../../../docs/superpowers/plans/existing-plan.md", card)
        self.assertIn("EXTERNAL_EVIDENCE", card)
        self.assertIn("Before enabling real effects", card)
        self.assertNotIn("PLANNED", card)
        self.assertNotIn("NOT_CHECKED", card)
        self.data["implementation_tasks"][1].update(state="DONE", assignment={"chat_label": "private-state"})
        self.assertEqual(roadmap.render_cards(self.definitions, self.data), cards)

    def test_definition_cannot_be_a_second_state_ledger(self):
        for field in ("state", "assignment", "validation_evidence", "delivery_evidence", "entry_gate"):
            with self.subTest(field=field):
                self.definition[field] = "copied ledger state"
                self.reject("mutable ledger field in definition")
                self.definition.pop(field)

    def invoke_main(self, *, write=False):
        # Full source/target auditing is covered above. Keep this fixture focused
        # on the real definition validator, renderer and CLI filesystem behavior.
        self.data["revision_acceptances"] = []
        self.data["targets"] = [{"assessment": "PENDING", "delivery_state": "PLANNED",
                                  "original_acceptance_inventory": {"state": "NOT_INVENTORIED"}}]
        ledger = self.root / "docs/development/roadmap.json"
        ledger.write_text(json.dumps(self.data), encoding="utf-8")
        args = ["manage_roadmap.py", "--root", str(self.root)] + (["--write"] if write else [])
        output = io.StringIO()
        with mock.patch("sys.argv", args), contextlib.redirect_stdout(output), \
             mock.patch.object(roadmap, "validate", side_effect=lambda data, root: roadmap.validate_tasks(data, root, self.targets)), \
             mock.patch.object(roadmap, "render", return_value={}):
            result = roadmap.main()
        return result, json.loads(output.getvalue())

    def test_cli_write_bootstraps_cards_and_check_detects_manual_drift(self):
        (self.root / self.data["implementation_tasks"][1]["plan_path"]).unlink()
        result, report = self.invoke_main(write=True)
        self.assertFalse(result, report)
        directory = self.root / "docs/development/task-plans"
        self.assertEqual(len(list(directory.glob("WP*.md"))), 14)
        result, report = self.invoke_main()
        self.assertFalse(result, report)
        card = directory / "WP02.md"
        card.write_text(card.read_text(encoding="utf-8") + "Manual drift\n", encoding="utf-8")
        result, report = self.invoke_main()
        self.assertTrue(result)
        self.assertTrue(any("task-plans/WP02.md" in error for error in report["errors"]), report)

    def test_cli_invalid_definition_does_not_generate_cards(self):
        self.definition["scope"] = []
        self.save_definitions()
        result, report = self.invoke_main(write=True)
        self.assertTrue(result)
        self.assertTrue(any("nonempty scope" in error for error in report["errors"]), report)
        self.assertFalse((self.root / "docs/development/task-plans/WP01.md").exists())

    def test_card_content_changes_when_definition_changes(self):
        self.assertTrue(callable(getattr(roadmap, "render_cards", None)), "render_cards is required")
        old = roadmap.render_cards(self.definitions, self.data)
        self.definition["acceptance_negative"].append("Revocation prevents a late worker effect.")
        new = roadmap.render_cards(self.definitions, self.data)
        self.assertNotEqual(old["task-plans/WP02.md"], new["task-plans/WP02.md"])
        self.assertIn("Revocation prevents a late worker effect.", new["task-plans/WP02.md"])


if __name__ == "__main__":
    unittest.main()
