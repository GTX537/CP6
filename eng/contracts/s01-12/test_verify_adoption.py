"""Adversarial tests of the evidence comparator, not of the adopted libraries."""
import argparse
import copy
import datetime as dt
import json
from pathlib import Path
import platform
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

import verify_adoption as verifier


class ComparatorTests(unittest.TestCase):
    expected = None

    def rejected(self, mutate):
        actual = copy.deepcopy(self.expected)
        mutate(actual)
        with self.assertRaises(verifier.EvidenceError):
            verifier.verify_documents(actual, self.expected)

    def test_positive_original_evidence_join(self):
        verifier.verify_documents(copy.deepcopy(self.expected), self.expected)

    def test_missing_id(self):
        self.rejected(lambda d: d["vectors.json"]["cases"].pop())

    def test_duplicate_id(self):
        self.rejected(lambda d: d["vectors.json"]["cases"].append(copy.deepcopy(d["vectors.json"]["cases"][0])))

    def test_changed_expected(self):
        self.rejected(lambda d: d["vectors.json"]["cases"][0]["expected"].update(accepted=False))

    def test_changed_input(self):
        self.rejected(lambda d: d["vectors.json"]["cases"][0].update(input="1"))

    def test_wrong_profile(self):
        self.rejected(lambda d: d["vectors.json"]["cases"][0].update(profile_id="fin-credit-positive"))

    def test_wrong_operation(self):
        self.rejected(lambda d: d["vectors.json"]["cases"][0].update(operation="native_registry_identity"))

    def test_wrong_verification_level(self):
        self.rejected(lambda d: d["vectors.json"]["cases"][0].update(verification_level="REQUIRED_RUNTIME"))

    def test_fabricated_scalar(self):
        self.rejected(lambda d: d["vectors.json"]["cases"][0]["consumers"]["Main:C#"].update(actual_scalar="999"))

    def test_false_behavior_equivalence(self):
        self.rejected(lambda d: d["vectors.json"]["cases"][0]["comparison"].update(whole_behavior_equivalent=True))

    def test_false_qa_three_surface_equivalence(self):
        def mutate(d):
            row = next(c for c in d["vectors.json"]["cases"] if c["id"] == "NCR-NULL-001")
            row["comparison"]["three_surface_assertions_comparable"] = True
        self.rejected(mutate)

    def test_not_applicable_cannot_be_pass(self):
        self.rejected(lambda d: d["vectors.json"]["cases"][0]["consumers"]["CRM:C#"].update(status="PASS_REUSED"))

    def test_missing_original_test_locator(self):
        self.rejected(lambda d: d["vectors.json"]["cases"][0]["consumers"]["Main:C#"].pop("actual_test"))

    def test_swapped_successful_test_locator(self):
        def mutate(d):
            rows = d["vectors.json"]["cases"]
            rows[0]["consumers"]["Main:C#"]["actual_test"] = rows[1]["consumers"]["Main:C#"]["actual_test"]
        self.rejected(mutate)

    def test_evidence_hash_tampering(self):
        def mutate(d):
            key = next(iter(d["manifest.json"]["references"]))
            d["manifest.json"]["references"][key]["sha256_git_blob_bytes"] = "0" * 64
        self.rejected(mutate)

    def test_stale_s11_final_count(self):
        def mutate(d):
            run = next(r for r in d["manifest.json"]["runs"] if r["id"] == "S11-TS")
            run["counts"]["passed"] = 416
        self.rejected(mutate)

    def test_whole_revision_cannot_be_promoted(self):
        self.rejected(lambda d: d["revision-coverage.json"]["cases"][0].update(whole_case_result="PASS"))

    def test_missing_revision(self):
        self.rejected(lambda d: d["revision-coverage.json"]["cases"].pop())

    def test_json_pointer_missing_is_not_accepted(self):
        with self.assertRaises(KeyError):
            verifier.pointer({"cases": [{}]}, "/cases/0/missing")

    def test_source_hash_is_checked_independently(self):
        path = "eng/contracts/s01/vectors.json"
        key = ("Main", verifier.PINS["Main"], path)
        original = self.store.cache[key]
        try:
            self.store.cache[key] = original + b" "
            with self.assertRaises(verifier.EvidenceError):
                self.store.ref("Main", path, expected=verifier.VECTOR_HASH)
        finally:
            self.store.cache[key] = original

    def corrupt_result(self, repo, path, kind, mutation):
        key = (repo, verifier.PINS[repo], path)
        original = self.store.read(repo, path)
        try:
            self.store.cache[key] = mutation(original)
            with self.assertRaises(verifier.EvidenceError):
                verifier.read_results(self.store, repo, path, kind)
        finally:
            self.store.cache[key] = original

    def test_trx_failed_row_with_stale_success_counters(self):
        def mutate(raw):
            tree = ET.fromstring(raw)
            tree.find('.//t:UnitTestResult', verifier.NS).set('outcome', 'Failed')
            return ET.tostring(tree)
        self.corrupt_result('Main', verifier.EVIDENCE + 'S01-03/implementation/green.trx', 'trx', mutate)

    def test_vitest_failed_row_with_stale_success_counters(self):
        def mutate(raw):
            value = verifier.decode(raw)
            value['testResults'][0]['assertionResults'][0]['status'] = 'failed'
            return json.dumps(value).encode()
        self.corrupt_result('Main', verifier.S11_TS + 'vitest.json', 'vitest', mutate)

    def test_tap_failed_row_with_stale_success_footer(self):
        self.corrupt_result('CRM', verifier.CRM_EVIDENCE + 'web-final-142.txt', 'tap',
                            lambda raw: raw.replace(b'ok 12 - S01 JSON-001', b'not ok 12 - S01 JSON-001', 1))

    def test_ambiguous_duplicate_vector_result(self):
        _, rows, _ = verifier.read_results(self.store, 'Main', verifier.EVIDENCE + 'S01-03/implementation/green.trx', 'trx')
        target = verifier.match_vector(rows, 'NUM-001')
        with self.assertRaises(verifier.EvidenceError):
            verifier.match_vector(rows + [copy.deepcopy(target)], 'NUM-001')

    def test_missing_vector_result(self):
        with self.assertRaises(verifier.EvidenceError):
            verifier.match_vector([], 'NUM-001')

    def consumer_negative(self, mutate=None, missing=False):
        value = verifier.decode(self.consumer_path.read_bytes())
        with tempfile.TemporaryDirectory(prefix='cp6-s01-12-comparator-') as temp:
            path = Path(temp) / 'consumers.json'
            if not missing:
                mutate(value)
                path.write_text(json.dumps(value), encoding='utf-8')
            with self.assertRaises(verifier.EvidenceError):
                verifier.validate_consumer_document(self.store, path)

    def test_consumer_file_required(self):
        self.consumer_negative(missing=True)

    def test_consumer_sources_cannot_be_empty(self):
        self.consumer_negative(lambda d: d.update(source_pins=[]))

    def test_consumer_wrong_task_snapshot(self):
        self.consumer_negative(lambda d: d['roadmap_task_refs'][0].update(title='fabricated title'))

    def test_consumer_dangling_source_ref(self):
        self.consumer_negative(lambda d: d['current_consumers'][0].update(source_refs=['Main:missing.cs']))

    def test_final_pg_matrix_uses_56_of_57_not_210_as_new_run(self):
        cells = [r['consumers']['Main:PostgreSQL'] for r in self.expected['vectors.json']['cases']
                 if r['consumers']['Main:PostgreSQL']['status'] != 'NOT_APPLICABLE']
        self.assertEqual(len(cells), 56)
        self.assertEqual({r['run'] for r in cells}, {'S10-PG-overlap'})
        runs = {r['id']: r['counts']['executed'] for r in self.expected['manifest.json']['runs']}
        self.assertEqual((runs['S10-PG'], runs['S10-PG-overlap'], runs['S10-PG-lifecycle']), (210, 57, 1))

    def test_ncr_field_revision_uses_exact_five_field_metadata(self):
        revision = next(c for c in self.expected['revision-coverage.json']['cases']
                        if c['revision_case_id'] == 'DR-NCR-FIELD-01::NCR-FIELD-01-POS')
        self.assertEqual(revision['fixed_vector_ids'], [], 'Const-null vectors do not establish these five fields')
        fields = {c['field']: c for c in revision['subassertions'] if 'field' in c}
        self.assertEqual(set(fields), {'priorHeadRef', 'sourceKind', 'keyword', 'cursor', 'nextCursor'})
        for name, field in fields.items():
            self.assertEqual(field['metadata_assertion']['expected_flags'],
                             {'required': True, 'nullable': name != 'priorHeadRef', 'editable': False})
            metadata_test = field['metadata_assertion']['evidence']['actual_test']
            self.assertEqual(metadata_test['locator']['test_id'], '9769af35-af53-08f7-9c17-c76f48c68c70')
            self.assertEqual(metadata_test['name_prefix'], 'CP6.ExactValues.Tests.QualityContractTests.Five_field_metadata_rows_retain_exact_required_nullable_and_editable_flags')
            self.assertEqual(field['coverage'], 'PARTIAL')
            dynamic = field['dynamic_shape']
            if name in ('sourceKind', 'keyword', 'cursor'):
                self.assertEqual(dynamic['coverage'], 'PARTIAL')
                self.assertEqual(dynamic['evidence']['actual_test']['locator']['test_id'], '04dbbf18-9182-683a-8813-d9a7c858892a')
            else:
                self.assertEqual(dynamic['coverage'], 'NOT_COVERED')
                self.assertNotIn('evidence', dynamic)
        self.assertEqual(revision['whole_case_result'], 'NOT_RUN')

    def test_ncr_field_mapping_rejects_substituted_passing_result(self):
        _, rows, _ = verifier.read_results(self.store, 'Main', verifier.EVIDENCE + 'S01-07/csharp/quality-contracts-format-final.trx', 'trx')
        rows = copy.deepcopy(rows)
        metadata = next(r for r in rows if r['locator']['test_id'] == '9769af35-af53-08f7-9c17-c76f48c68c70')
        unrelated = verifier.match_vector(rows, 'NCR-NULL-001')
        metadata.update(unrelated)
        with self.assertRaises(verifier.EvidenceError):
            verifier.ncr_field_assertions(self.store, rows)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--main-repo", type=Path, default=Path(__file__).resolve().parents[3])
    parser.add_argument("--crm-repo", type=Path, required=True)
    parser.add_argument("--platform-repo", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--test", action="append", help="Run only these exact comparator unittest method names")
    args = parser.parse_args()
    store = verifier.Store({"Main": args.main_repo, "CRM": args.crm_repo, "Platform": args.platform_repo})
    ComparatorTests.expected = verifier.build(store)
    ComparatorTests.store = store
    ComparatorTests.consumer_path = args.main_repo / verifier.OUT / 'consumers.json'
    suite = (unittest.TestSuite(ComparatorTests(name) for name in args.test) if args.test else
             unittest.defaultTestLoader.loadTestsFromTestCase(ComparatorTests))
    discovered = suite.countTestCases()
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    receipt = {"schema": "CP6-S01-12-COMPARATOR-TESTS/1", "result": "PASS" if result.wasSuccessful() else "FAIL",
               "observed_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
               "selection": args.test or "ALL_COMPARATOR_TESTS",
               "counts": {"discovered": discovered, "executed": result.testsRun, "passed": result.testsRun - len(result.failures) - len(result.errors) - len(result.skipped),
                          "failed": len(result.failures), "errors": len(result.errors), "skipped": len(result.skipped)},
               "failures": [(str(t), detail) for t, detail in result.failures + result.errors],
               "environment": {"python": sys.version, "platform": platform.platform()},
               "source_sha256": {p.name: verifier.sha(p.read_bytes()) for p in (Path(__file__), Path(verifier.__file__))},
               "repository_pins": verifier.PINS,
               "input_package_sha256": {"profiles": verifier.PROFILE_HASH, "vectors": verifier.VECTOR_HASH},
               "scope": "Comparator adversarial checks only; no new product tests, database, HTTP, Owner or build executions"}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    raise SystemExit(main())
