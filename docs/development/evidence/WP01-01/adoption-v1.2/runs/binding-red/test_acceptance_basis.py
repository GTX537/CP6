"""Algorithm-only synthetic fixtures. No fixture is an actual business adoption."""
import copy
import hashlib
import json
import os
import subprocess
import datetime as dt
from pathlib import Path
import tempfile
import unittest
from unittest import mock

try:
    import acceptance_basis as gate
except ImportError:
    gate = None

ROOT = Path(__file__).resolve().parents[3]
VERSION = 'CP6-WP05-01-ACCEPTANCE-BASIS/1.2.0'
TARGETS = ['PLM-ITER-01', 'PLM-REL-01']


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode()


class FixtureVerifier:
    """Explicit isolated trust injection; never selected by CLI or package fields."""
    def __init__(self):
        self.authority = True
        self.execution = True
        self.remote = True
        self.scenarios = []

    def adoption(self, manifest, basis):
        if not self.authority:
            raise ValueError('AUTHORITY_UNVERIFIED')

    def scenario(self, scenario_id, manifest, target):
        return copy.deepcopy(next(s for s in self.scenarios if s['id'] == scenario_id))

    def execution_origin(self, report_ref, report):
        if not self.execution:
            raise ValueError('EXECUTION_ORIGIN_UNVERIFIED')

    def remote_main(self, repository, commit):
        if not self.remote:
            raise ValueError('REMOTE_ANCESTRY_UNVERIFIED')


class AcceptanceBasisTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(gate, 'independent acceptance_basis adapter is not implemented')
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.verifier = FixtureVerifier()
        self.source = self.put('source.txt', b'fixture source bytes\n')
        self.rows = []
        for i in range(577):
            self.rows.append({'id': f'fixture-{i}', 'target': TARGETS[0 if i < 400 else 1],
                              'source_refs': [self.source], 'source_assertion': {'a': [1, 2], 'flag': True} if i < 18 else 'fixture assertion',
                              'owner': 'fixture Owner', 'kind': 'R1' if i < 8 else 'OBLIGATION',
                              'observations': ['exact'], 'runtime_state': 'NOT_RUN'})
        self.delta_rows = [{'id': f'DC-0{i+1}', 'target': t, 'source_refs': [self.source], 'observations': ['delta']} for i, t in enumerate(TARGETS)]
        matrix = self.put('matrix.json', {'rows': self.rows})
        delta = self.put('delta.json', {'additional_obligations': self.delta_rows})
        issue = self.put('issue.json', (ROOT / 'docs/development/evidence/WP01-01/decision-closure/manifest.json').read_bytes())
        self.edge = {'id': 'consumer->provider', 'targets': TARGETS, 'provider_repository': 'Main', 'consumer_repository': 'Main', 'provider_task': 'provider', 'consumer_task': 'consumer', 'bindings': [{'mode': 'TASK_DELIVERY'}]}
        graph = self.put('graph.json', {'edges': [self.edge], 'roots': {t: ['consumer'] for t in TARGETS}})
        self.basis = {'schema': 'CP6-ACCEPTANCE-BASIS/1', 'identity': 'fixture-basis', 'version': VERSION,
                      'targets': TARGETS, 'rows': self.rows + self.delta_rows, 'inherited_matrix': matrix,
                      'delta': delta, 'predecessors': [issue], 'source_documents': [self.source], 'planning_graph': graph,
                      'historical_gaps': [{'target': t, 'declared_legacy_count': n, 'legacy_case_ids': None, 'missing_original_sha256': None, 'historical_inventory_state': 'NOT_INVENTORIED', 'recovery_state': 'UNRECOVERABLE', 'equivalence': 'UNPROVABLE'} for t, n in zip(TARGETS, [30, 48])]}
        self.freeze()
        self.anchor = mock.patch.multiple(gate, INHERITED_MATRIX_HASH=matrix['sha256'], DELTA_HASH=delta['sha256'], create=True)
        self.anchor.start()
        self.addCleanup(self.anchor.stop)

    def put(self, path, value):
        b = value if isinstance(value, bytes) else encoded(value)
        p = self.root / path
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(b)
        return {'path': path, 'sha256': hashlib.sha256(b).hexdigest(), 'bytes': len(b), 'representation': 'RAW_FILE_BYTES'}

    def freeze(self):
        basis_ref = self.put('basis.json', self.basis)
        self.selection = {'manifest': self.put('manifest.json', {'schema': 'CP6-ACCEPTANCE-MANIFEST/1', 'identity': 'fixture-basis', 'version': VERSION, 'artifacts': [basis_ref], 'basis': basis_ref, 'predecessors': self.basis['predecessors']}), 'runtime': {}}

    def evaluate(self, trusted=True):
        return gate.evaluate(self.root, self.selection, verifier=self.verifier if trusted else None)

    def runtime(self, target=TARGETS[0]):
        rows = [r for r in self.basis['rows'] if r['target'] == target]
        logs = [self.put('runtime.log', b'independently attested fixture log\n')]
        profile = {'profile': 'fixture-profile', 'version': '1', 'sha256': 'b' * 64}
        scenario = {'id': 'fixture-scenario', 'target': target, 'obligation_ids': [r['id'] for r in rows], 'required_evidence_types': ['BUSINESS_RUNTIME'], 'edges': [{'id': self.edge['id'], 'contract': profile, 'reason': 'fixture actual route'}]}
        self.verifier.scenarios = [copy.deepcopy(scenario)]
        evidence = {'kind': 'BUSINESS_RUNTIME', 'state': 'PASS', 'synthetic': False, 'run_id': 'fixture-run', 'command': 'fixture only', 'environment': 'isolated fixture', 'observed_at': '2026-10-15T01:00:00Z', 'repository': 'Main', 'source_sha': 'a' * 40, 'owner': 'fixture Owner', 'consumer': 'fixture consumer', 'logs': logs,
                    'basis_version': VERSION, 'manifest_sha256': self.selection['manifest']['sha256'], 'target': target, 'scenario_id': scenario['id'], 'obligation_ids': scenario['obligation_ids'], 'contract_bindings': scenario['edges']}
        execution = self.put('execution.json', evidence)
        report = {'target': target, 'basis_version': VERSION, 'manifest_sha256': self.selection['manifest']['sha256'], 'obligations': rows, 'scenarios': [{'id': scenario['id'], 'executions': [execution], 'edges': [{'id': self.edge['id'], 'contract': profile, 'provider': execution, 'consumer': execution}]}]}
        self.selection['runtime'][target] = self.put('runtime.json', report)
        return report, evidence

    def test_operable_trusted_basis_inventory_without_runtime(self):
        result = self.evaluate()
        self.assertTrue(result['basis_adoption_valid'], result)
        self.assertTrue(result['inventory_coverage_valid'], result)
        self.assertFalse(any(result['target_runtime_valid'].values()))

    def test_operable_isolated_runtime_algorithm(self):
        self.runtime()
        self.assertTrue(self.evaluate()['target_runtime_valid'][TARGETS[0]])

    def test_no_independent_verifier_rejects_self_authority(self):
        self.selection['authority'] = {'signed': True, 'approved_by': 'user'}
        self.assertFalse(self.evaluate(False)['basis_adoption_valid'])

    def test_untrusted_authority_and_execution_and_unknown_remote(self):
        self.runtime()
        for flag in ['authority', 'execution', 'remote']:
            with self.subTest(flag=flag):
                setattr(self.verifier, flag, False)
                self.assertFalse(self.evaluate()['target_runtime_valid'][TARGETS[0]])
                setattr(self.verifier, flag, True)

    def test_manifest_source_bytes_and_path_escape(self):
        for change in ['bytes', 'escape', 'representation']:
            with self.subTest(change=change):
                selected = copy.deepcopy(self.selection)
                if change == 'bytes':
                    (self.root / 'source.txt').write_bytes(b'changed')
                elif change == 'escape':
                    self.selection['manifest']['path'] = '../manifest.json'
                else:
                    self.selection['manifest']['representation'] = 'CANONICAL_JSON'
                self.assertFalse(self.evaluate()['inventory_coverage_valid'])
                self.selection = selected
                self.put('source.txt', b'fixture source bytes\n')

    def test_exact_rows_missing_extra_duplicate_structure_order_and_types(self):
        pristine = copy.deepcopy(self.basis)
        for mutation in ['missing', 'extra', 'duplicate', 'field', 'order', 'type', 'string']:
            with self.subTest(mutation=mutation):
                self.basis = copy.deepcopy(pristine)
                rows = self.basis['rows']
                if mutation == 'missing': rows.pop()
                elif mutation == 'extra': rows.append(dict(rows[0], id='extra'))
                elif mutation == 'duplicate': rows[-1] = copy.deepcopy(rows[0])
                elif mutation == 'field': del rows[0]['source_assertion']['a']
                elif mutation == 'order': rows[0]['source_assertion']['a'].reverse()
                elif mutation == 'type': rows[0]['source_assertion']['flag'] = 1
                elif mutation == 'string': rows[0]['source_assertion'] = json.dumps(rows[0]['source_assertion'])
                self.freeze()
                self.assertFalse(self.evaluate()['inventory_coverage_valid'])

    def test_runtime_static_synthetic_not_run_missing_consumer_or_raw_log(self):
        for field, value in [('kind', 'STATIC'), ('synthetic', True), ('state', 'NOT_RUN'), ('consumer', ''), ('logs', [])]:
            with self.subTest(field=field):
                report, evidence = self.runtime()
                evidence[field] = value
                new_ref = self.put('execution.json', evidence)
                report['scenarios'][0]['executions'] = [new_ref]
                report['scenarios'][0]['edges'][0].update(provider=new_ref, consumer=new_ref)
                self.selection['runtime'][TARGETS[0]] = self.put('runtime.json', report)
                self.assertFalse(self.evaluate()['target_runtime_valid'][TARGETS[0]])

    def test_runtime_missing_edge_or_extra_obligation_rejected(self):
        for mutation in ['edge', 'obligation', 'scenario', 'profile']:
            with self.subTest(mutation=mutation):
                report, _ = self.runtime()
                if mutation == 'edge': report['scenarios'][0]['edges'] = []
                elif mutation == 'obligation': report['obligations'].append(report['obligations'][0])
                elif mutation == 'scenario': report['scenarios'] = []
                else: report['scenarios'][0]['edges'][0]['contract']['version'] = 'wrong'
                self.selection['runtime'][TARGETS[0]] = self.put('runtime.json', report)
                self.assertFalse(self.evaluate()['target_runtime_valid'][TARGETS[0]])

    def test_candidate_preserves_full_real_rows_and_gaps(self):
        actual = json.loads((ROOT / 'docs/development/evidence/WP01-01/adoption-v1.2/basis.json').read_text())
        self.assertEqual(len(actual['rows']), 579)
        self.assertEqual(sum(isinstance(r.get('source_assertion'), dict) for r in actual['rows']), 18)
        self.assertEqual([sum(r['target'] == t for r in actual['rows']) for t in TARGETS], [401, 178])
        self.assertFalse(actual['actual_adoption'])

    def test_production_registry_rejects_expired_revoked_wrong_scope_identity(self):
        # Validity validation is shared by live signed registries and synthetic tests.
        now = '2026-10-15T12:00:00+00:00'
        record = {'id': 'adoption-1', 'identity': 'fixture-basis', 'version': VERSION, 'manifest_sha256': self.selection['manifest']['sha256'], 'targets': TARGETS, 'action': 'ADOPT_PROSPECTIVE_BASIS', 'state': 'ADOPTED', 'actual_adoption': True, 'authorization_source': {'uri': 'https://authority.invalid/decision/1', 'sha256': 'c'*64}, 'approved_by': 'actual authority fixture', 'effective_at': '2026-10-15T11:00:00Z', 'expires_at': '2026-10-15T13:00:00Z', 'revoked': False}
        registry = {'issued_at': '2026-10-15T11:00:00Z', 'expires_at': '2026-10-15T13:00:00Z', 'adoptions': [record], 'identities': [{'identity': 'fixture-basis', 'version': VERSION, 'manifest_sha256': self.selection['manifest']['sha256']}]}
        gate.validate_adoption(registry, self.selection['manifest'], self.basis, now=now)
        for key, value in [('revoked', True), ('expires_at', '2026-10-15T10:00:00Z'), ('targets', [TARGETS[0]]), ('action', 'TECHNICAL_DEVELOPMENT'), ('version', 'wrong')]:
            with self.subTest(key=key):
                bad = copy.deepcopy(registry)
                bad['adoptions'][0][key] = value
                with self.assertRaises(ValueError): gate.validate_adoption(bad, self.selection['manifest'], self.basis, now=now)

    def test_policy_in_git_worktree_is_rejected(self):
        with mock.patch.dict(os.environ, {'CP6_ACCEPTANCE_TRUST_POLICY': str(ROOT / 'policy.json')}):
            with self.assertRaises(ValueError): gate.TrustedRegistryVerifier.from_environment(ROOT)

    def test_replacing_inherited_sources_cannot_redefine_contract(self):
        self.basis['rows'][0]['observations'] = ['weakened']
        self.basis['inherited_matrix'] = self.put('matrix.json', {'rows': self.basis['rows'][:577]})
        self.freeze()
        self.assertFalse(self.evaluate()['inventory_coverage_valid'])

    def test_attested_runtime_for_another_scenario_or_profile_cannot_be_reused(self):
        for field, value in [('scenario_id', 'different'), ('manifest_sha256', '0'*64), ('obligation_ids', []), ('contract_bindings', [])]:
            with self.subTest(field=field):
                report, evidence = self.runtime()
                evidence[field] = value
                ref = self.put('execution.json', evidence)
                report['scenarios'][0]['executions'] = [ref]
                report['scenarios'][0]['edges'][0].update(provider=ref, consumer=ref)
                self.selection['runtime'][TARGETS[0]] = self.put('runtime.json', report)
                self.assertFalse(self.evaluate()['target_runtime_valid'][TARGETS[0]])

    def test_production_remote_ancestry_queries_exact_current_main(self):
        verifier = gate.TrustedRegistryVerifier({'repositories': {'Main': 'fixture/product'}})
        responses = [{'sha': 'b'*40}, {'status': 'ahead', 'merge_base_commit': {'sha': 'a'*40}, 'base_commit': {'sha': 'a'*40}}]
        with mock.patch.object(verifier, 'api', side_effect=responses) as api:
            verifier.remote_main('Main', 'a'*40)
            self.assertEqual(api.call_args_list[1].args[0], 'repos/fixture/product/compare/' + 'a'*40 + '...' + 'b'*40)
        for status in ['behind', 'diverged']:
            verifier.remote_results.clear()
            with mock.patch.object(verifier, 'api', side_effect=[responses[0], dict(responses[1], status=status)]):
                with self.assertRaises(ValueError): verifier.remote_main('Main', 'a'*40)

    def test_actual_openssh_signature_and_tampered_registry(self):
        # Ephemeral private key is deleted with the TemporaryDirectory and never committed.
        key = self.root / 'ephemeral-key'
        generated = subprocess.run(['ssh-keygen', '-q', '-t', 'ed25519', '-N', '', '-f', str(key)], capture_output=True, timeout=30)
        self.assertEqual(generated.returncode, 0, generated.stderr)
        signers = self.root / 'allowed_signers'
        signers.write_text('fixture-authority ' + Path(str(key)+'.pub').read_text())
        now = dt.datetime.now(dt.timezone.utc)
        payload = encoded({'schema': 'CP6-ACCEPTANCE-AUTHORITY-REGISTRY/1', 'issued_at': (now-dt.timedelta(minutes=1)).isoformat(), 'expires_at': (now+dt.timedelta(minutes=10)).isoformat()})
        body = self.root / 'registry.json'; body.write_bytes(payload)
        signed = subprocess.run(['ssh-keygen', '-Y', 'sign', '-f', str(key), '-n', 'cp6-acceptance-authority-v1', str(body)], capture_output=True, timeout=30)
        self.assertEqual(signed.returncode, 0, signed.stderr)
        signature = Path(str(body)+'.sig').read_bytes()
        policy = {'authority': {'repository': 'fixture/independent-authority', 'path': 'registry.json', 'principal': 'fixture-authority', 'allowed_signers': str(signers)}}
        verifier = gate.TrustedRegistryVerifier(policy)
        with mock.patch.object(verifier, 'api', side_effect=[{'sha': 'a'*40}, payload, signature]):
            self.assertEqual(verifier.registry('authority')['schema'], 'CP6-ACCEPTANCE-AUTHORITY-REGISTRY/1')
        verifier = gate.TrustedRegistryVerifier(policy)
        with mock.patch.object(verifier, 'api', side_effect=[{'sha': 'a'*40}, payload+b' ', signature]):
            with self.assertRaises(ValueError): verifier.registry('authority')


if __name__ == '__main__':
    unittest.main()
