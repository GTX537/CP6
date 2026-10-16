"""Algorithm-only synthetic fixtures. No fixture is an actual business adoption."""
import copy
from contextlib import contextmanager
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
        self.anchor = mock.patch.multiple(gate, INHERITED_MATRIX_HASH=matrix['sha256'], DELTA_HASH=delta['sha256'], PLANNING_GRAPH_HASH=graph['sha256'], create=True)
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
        package = self.put('package.bin', b'exact fixture package bytes')
        profile = {'profile': 'fixture-profile', 'version': '1', 'sha256': package['sha256'], 'artifact': package}
        configuration = self.put('configuration.json', {'state': 'CONFIGURED', 'scope': 'isolated algorithm fixture only'})
        scope = {'owner': 'fixture Owner', 'consumer': 'fixture consumer'}
        scenario = {'id': 'fixture-scenario', 'target': target, 'execution_scope': scope, 'configuration': configuration, 'obligation_ids': [r['id'] for r in rows], 'required_evidence_types': ['BUSINESS_RUNTIME'], 'edges': [{'id': self.edge['id'], 'contract': profile, 'reason': 'fixture actual route', 'provider_scope': scope, 'consumer_scope': scope}]}
        self.verifier.scenarios = [copy.deepcopy(scenario)]
        evidence = {'kind': 'BUSINESS_RUNTIME', 'state': 'PASS', 'synthetic': False, 'run_id': 'fixture-run', 'command': 'fixture only', 'environment': 'isolated fixture', 'observed_at': '2026-10-15T01:00:00Z', 'repository': 'Main', 'source_sha': 'a' * 40, 'owner': 'fixture Owner', 'consumer': 'fixture consumer', 'logs': logs,
                    'basis_version': VERSION, 'manifest_sha256': self.selection['manifest']['sha256'], 'target': target, 'scenario_id': scenario['id'], 'obligation_ids': scenario['obligation_ids'], 'contract_bindings': scenario['edges'], 'configuration': configuration}
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

    def test_non_object_configuration_returns_structured_runtime_rejection(self):
        for configuration in ([], ["CONFIGURED"], None, False, 1, 1.5, "CONFIGURED"):
            with self.subTest(configuration=configuration):
                self.runtime()
                self.verifier.scenarios[0]['configuration'] = self.put('invalid_configuration.json', configuration)
                result = self.evaluate()
                self.assertTrue(result['basis_adoption_valid'], result)
                self.assertTrue(result['inventory_coverage_valid'], result)
                self.assertFalse(any(result['target_runtime_valid'].values()), result)
                self.assertIn(TARGETS[0] + ': CONFIGURATION_OBJECT_REQUIRED', result['errors'])
                json.dumps(result, allow_nan=False)

    def test_no_independent_verifier_rejects_self_authority(self):
        self.selection['authority'] = {'signed': True, 'approved_by': 'user'}
        with mock.patch.dict(os.environ, {'CP6_ACCEPTANCE_TRUST_POLICY': ''}):
            result = self.evaluate(False)
        self.assertFalse(result['basis_adoption_valid'])
        self.assertTrue(result['inventory_coverage_valid'])
        self.assertEqual(result['errors'], ['INDEPENDENT_VERIFIER_UNCONFIGURED'])

    def test_malformed_objects_fail_closed(self):
        self.basis['source_documents'] = [None]
        self.freeze()
        result = self.evaluate()
        self.assertFalse(result['basis_adoption_valid'])
        self.assertFalse(result['inventory_coverage_valid'])

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

    def test_planning_graph_cannot_be_privately_rewritten(self):
        self.basis['planning_graph'] = self.put('graph.json', {'edges': [], 'roots': {}})
        self.freeze()
        self.assertFalse(self.evaluate()['inventory_coverage_valid'])

    def test_attested_runtime_for_another_scenario_or_profile_cannot_be_reused(self):
        for field, value in [('scenario_id', 'different'), ('manifest_sha256', '0'*64), ('obligation_ids', []), ('contract_bindings', []), ('owner', 'wrong Owner'), ('consumer', 'unrelated consumer')]:
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

    def test_package_bytes_and_real_configuration_required(self):
        for path in ['package.bin', 'configuration.json']:
            with self.subTest(path=path):
                self.runtime()
                (self.root/path).write_bytes(b'changed-or-unconfigured')
                self.assertFalse(self.evaluate()['target_runtime_valid'][TARGETS[0]])

    def test_production_adapter_checks_adoption_delivery_and_immutable_identity(self):
        # Trusted registry is isolated; checks below exercise actual production
        # adoption path independently of the fake-verifier positive tests.
        record = {'delivery': {'repository': 'Main', 'commit': 'a'*40, 'path': 'manifest.json'},
                  'identity_source': {'commit': 'b'*40, 'path': 'identities/fixture.json', 'sha256': 'c'*64}}
        verifier = gate.TrustedRegistryVerifier({'repositories': {'Main': 'fixture/product'}, 'authority': {'repository': 'fixture/authority'}})
        with mock.patch.object(verifier, 'registry', return_value={}), mock.patch.object(gate, 'validate_adoption', return_value=record), mock.patch.object(verifier, 'remote_main') as remote, mock.patch.object(verifier, 'api', return_value=b'incorrect bytes'):
            with self.assertRaises(ValueError): verifier.adoption(self.selection['manifest'], self.basis)
            self.assertTrue(remote.called)

    def test_transport_failure_never_means_verified(self):
        verifier = gate.TrustedRegistryVerifier({'repositories': {'Main': 'fixture/product'}})
        for error in [ValueError('HTTP403'), ValueError('HTTP404'), subprocess.TimeoutExpired('gh', 30)]:
            with mock.patch.object(verifier, 'api', side_effect=error):
                with self.assertRaises((ValueError, subprocess.TimeoutExpired)):
                    verifier.remote_main('Main', 'a'*40)

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
        policy = {'authority': {'repository': 'fixture/independent-authority', 'path': 'registry.json', 'principal': 'fixture-authority', 'allowed_signers': str(signers), 'identity_root': 'identities'}}
        verifier = gate.TrustedRegistryVerifier(policy)
        with mock.patch.object(verifier, 'api', side_effect=[{'sha': 'a'*40}, payload, signature]):
            self.assertEqual(verifier.registry('authority')['schema'], 'CP6-ACCEPTANCE-AUTHORITY-REGISTRY/1')
        verifier = gate.TrustedRegistryVerifier(policy)
        with mock.patch.object(verifier, 'api', side_effect=[{'sha': 'a'*40}, payload+b' ', signature]):
            with self.assertRaises(ValueError): verifier.registry('authority')

    @contextmanager
    def configured_registry(self):
        """Real policy loader/signatures/algorithms; transport and business are fixtures."""
        _, report = self.runtime()
        now = dt.datetime.now(dt.timezone.utc)
        validity = {'effective_at': (now-dt.timedelta(minutes=1)).isoformat(), 'expires_at': (now+dt.timedelta(minutes=10)).isoformat(), 'revoked': False}
        window = {'issued_at': validity['effective_at'], 'expires_at': validity['expires_at']}
        identity = {'identity': self.basis['identity'], 'version': VERSION, 'manifest_sha256': self.selection['manifest']['sha256'], 'targets': TARGETS}
        identity_raw = encoded(identity)
        identity_path = 'identities/' + hashlib.sha256(self.basis['identity'].encode()).hexdigest() + '.json'
        adoption = dict(validity, id='fixture-adoption', **identity, action='ADOPT_PROSPECTIVE_BASIS', state='ADOPTED', actual_adoption=True,
                        approved_by='isolated fixture authority', authorization_source={'uri': 'https://fixture.invalid/authorization', 'sha256': 'c'*64},
                        delivery={'repository': 'Main', 'commit': 'a'*40, 'path': self.selection['manifest']['path']},
                        identity_source={'commit': 'b'*40, 'path': identity_path, 'sha256': hashlib.sha256(identity_raw).hexdigest()})
        scenario = dict(self.verifier.scenarios[0], **validity, state='APPROVED', manifest_sha256=self.selection['manifest']['sha256'], basis_version=VERSION)
        report_ref = self.selection['runtime'][TARGETS[0]]
        execution_ref = json.loads((self.root/report_ref['path']).read_text())['scenarios'][0]['executions'][0]
        origin_keys = ('run_id', 'command', 'environment', 'observed_at', 'repository', 'source_sha', 'owner', 'consumer', 'kind', 'state', 'synthetic')
        attestation = dict(validity, id='fixture-execution', state='VERIFIED_BUSINESS_RUNTIME', report_sha256=execution_ref['sha256'], report_bytes=execution_ref['bytes'], verified_raw_logs=report['logs'], origin={k: report[k] for k in origin_keys})
        registries = {'authority': dict(window, schema='CP6-ACCEPTANCE-AUTHORITY-REGISTRY/1', adoptions=[adoption], identities=[identity], scenarios=[scenario]),
                      'execution': dict(window, schema='CP6-ACCEPTANCE-EXECUTION-REGISTRY/1', executions=[attestation])}
        with tempfile.TemporaryDirectory(prefix='cp6-synthetic-trust-') as external:
            external = Path(external)
            key = external/'ephemeral'
            subprocess.run(['ssh-keygen', '-q', '-t', 'ed25519', '-N', '', '-f', str(key)], check=True, capture_output=True, timeout=30)
            signers = external/'allowed_signers'
            signers.write_text('fixture-principal ' + Path(str(key)+'.pub').read_text())
            policy = {'schema': 'CP6-ACCEPTANCE-TRUST/1', 'repositories': {'Main': 'fixture/product'}}
            payloads = {}
            for role, registry in registries.items():
                cfg = {'repository': 'fixture/'+role, 'path': 'registry.json', 'principal': 'fixture-principal', 'allowed_signers': str(signers)}
                policy[role] = cfg
                if role == 'authority': cfg['identity_root'] = 'identities'
                path = external/(role+'.json'); path.write_bytes(encoded(registry))
                subprocess.run(['ssh-keygen', '-Y', 'sign', '-f', str(key), '-n', 'cp6-acceptance-'+role+'-v1', str(path)], check=True, capture_output=True, timeout=30)
                prefix = 'repos/'+cfg['repository']+'/contents/registry.json'
                payloads[prefix] = path.read_bytes()
                payloads[prefix+'.sig'] = Path(str(path)+'.sig').read_bytes()
            payloads['repos/fixture/product/contents/manifest.json'] = (self.root/'manifest.json').read_bytes()
            payloads['repos/fixture/authority/contents/' + identity_path] = identity_raw
            policy_path = external/'policy.json'; policy_path.write_bytes(encoded(policy))

            requests = []

            def api(_self, endpoint, *, raw=False):
                requests.append(endpoint)
                if endpoint.endswith('/commits/main'): return {'sha': 'f'*40}
                if '/commits?path=' in endpoint: return [{'sha': 'b'*40}]
                if endpoint.endswith('/commits/' + 'b'*40): return {'parents': [{'sha': 'c'*40}], 'files': [{'filename': identity_path, 'status': 'added'}]}
                if '/compare/' in endpoint:
                    commit = endpoint.split('/compare/')[1].split('...')[0]
                    return {'status': 'ahead', 'merge_base_commit': {'sha': commit}, 'base_commit': {'sha': commit}}
                return payloads[endpoint.split('?')[0]]

            with mock.patch.dict(os.environ, {'CP6_ACCEPTANCE_TRUST_POLICY': str(policy_path)}), mock.patch.object(gate.TrustedRegistryVerifier, 'api', api):
                yield policy, policy_path, payloads, requests

    def test_configured_production_adapter_complete_synthetic_path(self):
        with self.configured_registry():
            result = gate.evaluate(self.root, self.selection)
            self.assertTrue(result['basis_adoption_valid'], result)
            self.assertTrue(result['inventory_coverage_valid'], result)
            self.assertTrue(result['target_runtime_valid'][TARGETS[0]], result)

    def test_authority_only_adoption_does_not_load_execution_configuration(self):
        with self.configured_registry() as (policy, path, _, requests):
            self.selection['runtime'] = {}
            del policy['execution']
            for execution in ('absent', None, {}, {'repository': 42}):
                with self.subTest(execution=execution):
                    if execution != 'absent':
                        policy['execution'] = execution
                    path.write_bytes(encoded(policy))
                    result = gate.evaluate(self.root, self.selection)
                    self.assertTrue(result['basis_adoption_valid'], result)
                    self.assertTrue(result['inventory_coverage_valid'], result)
                    self.assertFalse(any(result['target_runtime_valid'].values()), result)
                    self.assertEqual(result['errors'], [t + ': RUNTIME_NOT_SUBMITTED' for t in TARGETS])
            self.assertFalse(any('fixture/execution' in request for request in requests))

    def test_execution_configuration_failures_do_not_revoke_adoption(self):
        with self.configured_registry() as (policy, path, _, requests):
            valid = copy.deepcopy(policy['execution'])
            cases = [('absent', 'EXECUTION_TRUST_UNCONFIGURED'),
                     (None, 'EXECUTION_TRUST_INVALID'),
                     ({}, 'EXECUTION_TRUST_INVALID'),
                     (dict(valid, repository=42), 'EXECUTION_TRUST_INVALID'),
                     (dict(valid, path='../registry.json'), 'EXECUTION_TRUST_INVALID'),
                     (dict(valid, principal=''), 'EXECUTION_TRUST_INVALID'),
                     (dict(valid, allowed_signers=str(ROOT / 'keys')), 'EXECUTION_TRUST_INVALID')]
            del policy['execution']
            for config, error in cases:
                with self.subTest(config=config):
                    if config != 'absent':
                        policy['execution'] = config
                    path.write_bytes(encoded(policy))
                    result = gate.evaluate(self.root, self.selection)
                    self.assertTrue(result['basis_adoption_valid'], result)
                    self.assertTrue(result['inventory_coverage_valid'], result)
                    self.assertFalse(any(result['target_runtime_valid'].values()), result)
                    self.assertTrue(any(e.startswith(TARGETS[0] + ': ' + error) for e in result['errors']), result)
            self.assertFalse(any('fixture/execution' in request for request in requests))

    def test_role_signature_failure_is_confined_to_its_stage(self):
        with self.configured_registry() as (_, _, payloads, _):
            for role in ('execution', 'authority'):
                with self.subTest(role=role):
                    key = 'repos/fixture/' + role + '/contents/registry.json'
                    original = payloads[key]
                    payloads[key] += b' '
                    result = gate.evaluate(self.root, self.selection)
                    payloads[key] = original
                    self.assertEqual(result['basis_adoption_valid'], role == 'execution', result)
                    self.assertTrue(result['inventory_coverage_valid'], result)
                    self.assertFalse(any(result['target_runtime_valid'].values()), result)
                    self.assertTrue(any('INDEPENDENT_COMMAND_FAILED' in e for e in result['errors']), result)

    def test_execution_cannot_replace_missing_or_invalid_authority(self):
        with self.configured_registry() as (policy, path, _, requests):
            valid = copy.deepcopy(policy['authority'])
            del policy['authority']
            for config, error in [('absent', 'AUTHORITY_TRUST_UNCONFIGURED'),
                                  (None, 'AUTHORITY_TRUST_INVALID'),
                                  ({}, 'AUTHORITY_TRUST_INVALID'),
                                  (dict(valid, repository=42), 'AUTHORITY_TRUST_INVALID'),
                                  (dict(valid, identity_root='../identities'), 'AUTHORITY_TRUST_INVALID')]:
                with self.subTest(config=config):
                    if config != 'absent':
                        policy['authority'] = config
                    path.write_bytes(encoded(policy))
                    result = gate.evaluate(self.root, self.selection)
                    self.assertFalse(result['basis_adoption_valid'], result)
                    self.assertTrue(result['inventory_coverage_valid'], result)
                    self.assertFalse(any(result['target_runtime_valid'].values()), result)
                    self.assertTrue(any(e.startswith(error) for e in result['errors']), result)
            self.assertFalse(any('fixture/execution' in request for request in requests))

    def test_identity_has_one_creation_in_fixed_operator_namespace(self):
        verifier = gate.TrustedRegistryVerifier({'authority': {'repository': 'fixture/authority', 'identity_root': 'identities'}})
        self.assertTrue(hasattr(verifier, 'verify_identity'), 'independent append-only history check is required')
        verifier.registry_heads = {'authority': 'f'*40}
        path = 'identities/' + hashlib.sha256(self.basis['identity'].encode()).hexdigest() + '.json'
        raw = encoded({'identity': self.basis['identity'], 'version': VERSION, 'manifest_sha256': self.selection['manifest']['sha256'], 'targets': TARGETS})
        record = {'identity_source': {'path': path, 'commit': 'b'*40, 'sha256': hashlib.sha256(raw).hexdigest()}}
        for mutation in ['none', 'edited', 'alternate_path', 'modified_creation', 'unknown_history']:
            with self.subTest(mutation=mutation):
                current = copy.deepcopy(record)
                if mutation == 'alternate_path': current['identity_source']['path'] = 'identities/other.json'
                history = [{'sha': 'b'*40}]
                if mutation == 'edited': history.insert(0, {'sha': 'd'*40})
                if mutation == 'unknown_history': history = []
                def api(endpoint, *, raw=False):
                    if '/commits?path=' in endpoint:
                        self.assertIn('sha=' + 'f'*40, endpoint)
                        self.assertIn('per_page=2', endpoint)
                        return history
                    if endpoint.endswith('/commits/' + 'b'*40):
                        return {'parents': [{'sha': 'c'*40}], 'files': [{'filename': path, 'status': 'modified' if mutation == 'modified_creation' else 'added'}]}
                    return raw_bytes
                raw_bytes = raw
                with mock.patch.object(verifier, 'api', side_effect=api), mock.patch.object(verifier, '_main_ancestor'):
                    if mutation == 'none': verifier.verify_identity(current, self.basis, self.selection['manifest'])
                    else:
                        with self.assertRaises(ValueError): verifier.verify_identity(current, self.basis, self.selection['manifest'])


if __name__ == '__main__':
    unittest.main()
