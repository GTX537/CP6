"""Synthetic proposal tests. Even positives MUST NOT grant actual completion."""
import copy
import unittest

from comparator import compare


def fixture():
    # Every value below is invented. synthetic=False models an asserted input
    # flag only; it is NOT a claim that this fixture is real business evidence.
    contract = {
        "version": "CP6-WP05-01-ACCEPTANCE-BASIS/1.1.0-proposed",
        "pins": {"combined_manifest": "a" * 64, "matrix": "b" * 64},
        "historical_gaps": [{"id": "GAP-ITER-LEGACY-30", "historical_inventory_state": "NOT_INVENTORIED", "recovery_state": "UNRECOVERABLE", "equivalence": "UNPROVABLE", "legacy_case_ids": None, "missing_original_sha256": None}],
        "targets": {"PLM-ITER-01": {"obligations": ["BODY-A", "CASE-A", "SUPPLEMENT-A"], "r1": ["R1-A"], "tasks": ["WP05-18"], "repositories": ["Main", "Platform"], "cross_repository_scope_state": "RESOLVED_BY_EXACT_SOURCE", "decisions": ["DP-ITER-01"]}},
    }
    def evidence(key, level="BUSINESS_RUNTIME"):
        return {"id": key, "target": "PLM-ITER-01", "level": level, "state": "PASS", "synthetic": False, "source_sha": "c" * 40, "evidence_sha256": "d" * 64, "owner": "original-owner", "run_id": "EXAMPLE-NOT-A-REAL-RUN", "environment": "isolated-example", "observed_at": "EXAMPLE-NOT-REAL-TIME", "command": "EXAMPLE", "inputs": "EXAMPLE", "observed_result": "EXAMPLE", "persistent_before_after": "EXAMPLE", "authorization_context": "EXAMPLE", "path": "example/evidence.json"}
    candidate = {
        "target": "PLM-ITER-01", "version": contract["version"], "pins": copy.deepcopy(contract["pins"]),
        "adoption": {"status": "SIGNED", "actor": "SYNTHETIC-EXAMPLE", "time": "SYNTHETIC-EXAMPLE", "authority": "SYNTHETIC-EXAMPLE", "effective_at": "SYNTHETIC-EXAMPLE", "decision_text": "以当前可知义务建立新标准，无法证明旧30/48逐项等价", "targets": ["PLM-ITER-01"], "version": contract["version"], "pins": copy.deepcopy(contract["pins"])},
        "historical_gaps": copy.deepcopy(contract["historical_gaps"]),
        "obligations": [evidence(x) for x in contract["targets"]["PLM-ITER-01"]["obligations"]],
        "r1": [evidence("R1-A")], "tasks": [evidence("WP05-18", "TASK_DELIVERY")],
        "repositories": [{"id": r, "remote_main_sha": "e" * 40, "task_sha": "f" * 40, "contains_task": True, "package_contract_version": "EXAMPLE", "adoption_evidence": "EXAMPLE"} for r in ["Main", "Platform"]],
        "decisions": [{"id": "DP-ITER-01", "status": "RESOLVED_BY_EXACT_SUCCESSOR", "authority": "EXAMPLE", "successor_sha256": "f" * 64}],
    }
    return candidate, contract


class ProposalTests(unittest.TestCase):
    def setUp(self):
        self.candidate, self.contract = fixture()

    def rejected(self, reason):
        result = compare(self.candidate, self.contract)
        self.assertEqual("PREPARATION", result["scope"])
        self.assertFalse(result["actual_completion"])
        self.assertFalse(result["actual_adoption"])
        self.assertIn(reason, result["reasons"])
        self.assertFalse(result["proposal_conditions_satisfied"])

    def test_synthetic_positive_never_authorizes_actual_completion(self):
        result = compare(self.candidate, self.contract)
        self.assertEqual("PREPARATION", result["scope"])
        self.assertTrue(result["proposal_conditions_satisfied"])
        self.assertIs(False, result["actual_completion"])
        self.assertIs(False, result["actual_adoption"])

    def test_no_adoption(self):
        self.candidate["adoption"] = None
        self.rejected("ADOPTION_REQUIRED")

    def test_unsigned_actor_cannot_be_implied_from_assignment(self):
        self.candidate["adoption"]["actor"] = None
        self.rejected("ADOPTION_REQUIRED")

    def test_wrong_hash(self):
        self.candidate["pins"]["combined_manifest"] = "0" * 64
        self.rejected("PIN_MISMATCH")

    def test_wrong_adoption_hash(self):
        self.candidate["adoption"]["pins"]["matrix"] = "0" * 64
        self.rejected("ADOPTION_BINDING_MISMATCH")

    def test_wrong_target(self):
        self.candidate["adoption"]["targets"] = ["PLM-REL-01"]
        self.rejected("ADOPTION_BINDING_MISMATCH")

    def test_missing_obligation(self):
        self.candidate["obligations"].pop()
        self.rejected("OBLIGATION_SET_MISMATCH")

    def test_duplicate_obligation_does_not_fill_gap(self):
        self.candidate["obligations"][1] = copy.deepcopy(self.candidate["obligations"][0])
        self.rejected("OBLIGATION_SET_MISMATCH")

    def test_static_pass_not_business(self):
        self.candidate["obligations"][0]["level"] = "HISTORICAL_STATIC"
        self.rejected("OBLIGATION_RUNTIME_EVIDENCE_REQUIRED")

    def test_synthetic_runtime_not_business(self):
        self.candidate["obligations"][0]["synthetic"] = True
        self.rejected("OBLIGATION_RUNTIME_EVIDENCE_REQUIRED")

    def test_missing_original_label_preserved(self):
        self.candidate["historical_gaps"][0]["historical_inventory_state"] = "COMPLETE"
        self.rejected("HISTORICAL_GAP_CHANGED")

    def test_other_target_bypass(self):
        self.candidate["target"] = "PLM-REQ-01"
        self.contract["targets"]["PLM-REQ-01"] = self.contract["targets"]["PLM-ITER-01"]
        self.rejected("TARGET_NOT_WHITELISTED")

    def test_missing_required_task(self):
        self.candidate["tasks"] = []
        self.rejected("TASK_SET_MISMATCH")

    def test_missing_r1(self):
        self.candidate["r1"] = []
        self.rejected("R1_SET_MISMATCH")

    def test_missing_cross_repo(self):
        self.candidate["repositories"].pop()
        self.rejected("REPOSITORY_SET_MISMATCH")

    def test_unresolved_real_repository_scope_is_not_inferred_from_baseline(self):
        self.contract["targets"]["PLM-ITER-01"]["cross_repository_scope_state"] = "UNRESOLVED"
        self.rejected("CROSS_REPOSITORY_SCOPE_UNRESOLVED")

    def test_unresolved_dp(self):
        self.candidate["decisions"][0]["status"] = "UNRESOLVED"
        self.rejected("EXACT_SUCCESSOR_DECISION_REQUIRED")

    def test_incomplete_runtime_record(self):
        self.candidate["obligations"][0]["persistent_before_after"] = ""
        self.rejected("OBLIGATION_RUNTIME_EVIDENCE_REQUIRED")

    def test_wrong_evidence_owner_target(self):
        self.candidate["obligations"][0]["target"] = "PLM-REL-01"
        self.rejected("OBLIGATION_RUNTIME_EVIDENCE_REQUIRED")

    def test_nonpass_runtime(self):
        for state in ("FAIL", "BLOCKED", "NOT_RUN", "REFERENCE_ONLY"):
            with self.subTest(state=state):
                self.candidate["obligations"][0]["state"] = state
                self.rejected("OBLIGATION_RUNTIME_EVIDENCE_REQUIRED")

    def test_no_mutation(self):
        before = copy.deepcopy((self.candidate, self.contract))
        compare(self.candidate, self.contract)
        self.assertEqual(before, (self.candidate, self.contract))

    def test_malformed_input_returns_preparation_rejection(self):
        for candidate in (None, [], {**self.candidate, "adoption": {"targets": None}},
                          {**self.candidate, "obligations": [{"id": []}]},
                          {**self.candidate, "target": []}):
            with self.subTest(candidate=candidate):
                result = compare(candidate, self.contract)
                self.assertEqual("PREPARATION", result["scope"])
                self.assertFalse(result["actual_completion"])
                self.assertFalse(result["proposal_conditions_satisfied"])
                self.assertIn("INPUT_SHAPE_INVALID", result["reasons"])

    def test_string_adoption_targets_not_treated_as_membership(self):
        self.candidate["adoption"]["targets"] = "PLM-ITER-01"
        self.rejected("INPUT_SHAPE_INVALID")


if __name__ == "__main__":
    unittest.main(verbosity=2)
