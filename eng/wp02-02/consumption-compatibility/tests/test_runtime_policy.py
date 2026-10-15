"""Synthetic report mutation tests only; none is a real build/runtime qualification."""
import copy
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import runtime_policy as policy


class RuntimePolicyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workspace = Path(__file__).resolve().parents[4]
        cls.owner = uuid.uuid4().hex
        cls.root = cls.workspace / 'artifacts/wp02-02/consumption-compatibility' / cls.owner
        cls.root.mkdir(parents=True, exist_ok=False)

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=self.root)
        self.addCleanup(self.temp.cleanup)
        self.attempt = Path(self.temp.name)
        rows = []
        for name in policy.REQUIRED:
            p = self.attempt / (name + '.dll')
            p.write_bytes(('SYNTHETIC:' + name).encode())
            rows.append({'name': name, 'version': '10.0.0.0', 'path': str(p), 'mvid': str(uuid.uuid4()),
                         'sha256': hashlib.sha256(p.read_bytes()).hexdigest()})
        e = self.attempt / 'build-evidence.json'
        e.write_text('{}', encoding='utf-8')
        self.expected = {'owner': self.owner, 'mainSource': policy.MAIN,
                         'platformSource': policy.PLATFORM, 'compatibility': 'SPECIFIED_PATHS_VERIFIED',
                         'builds': {x: {'restoreExitCode': 0, 'buildExitCode': 0} for x in ('WebApi', 'DatabaseCompatibility.Probe', 'Probe')},
                         'evidence': [{'path': str(e), 'sha256': hashlib.sha256(e.read_bytes()).hexdigest()}],
                         'assemblies': rows, 'requiredAssertions': ['http-and-sdk', 'context-cleared']}
        self.observed = {'owner': self.owner, 'status': 'PASS', 'mainConsumption': 'NOT_RUN',
                         'formalPackageTrust': 'BLOCKED', 'evidenceKind': 'SYNTHETIC_AUTHORITY',
                         'assemblies': [dict(r, observedTypes=['actual-type']) for r in rows],
                         'assertions': [{'name': x, 'passed': True} for x in self.expected['requiredAssertions']],
                         'failures': [], 'http': {'address': 'http://127.0.0.1:12345', 'requests': [{'passed': True, 'actualStatus': 200, 'expectedStatus': 200}]},
                         'telemetry': {'flushSucceeded': True, 'droppedCount': 0, 'propagationIntact': True,
                                       'records': [{'signal': x} for x in ('Trace', 'Metric', 'Log')]},
                         'cleanup': {'stopSucceeded': True, 'disposeSucceeded': True, 'listenerClosed': True,
                                     'propagatorsRestored': True}}

    def check(self, expected=None, observed=None):
        return policy.validate(expected or self.expected, observed or self.observed, self.root)

    def test_output_requires_exact_task_owner_and_preserves_existing_report(self):
        fake = self.attempt / ('a' * 32)
        fake.mkdir()
        with self.assertRaises(ValueError):
            policy.output_path(fake, fake / 'result.json')
        outside = self.root.parent / 'result.json'
        with self.assertRaises(ValueError):
            policy.output_path(self.root, outside)
        existing = self.attempt / 'result.json'
        existing.write_text('original evidence', encoding='utf-8')
        with self.assertRaises(ValueError):
            policy.output_path(self.root, existing)
        self.assertEqual('original evidence', existing.read_text(encoding='utf-8'))
        self.assertEqual(self.attempt / 'new.json', policy.output_path(self.root, self.attempt / 'new.json'))

    @unittest.skipUnless(sys.platform == 'win32', 'Windows junction regression')
    def test_output_rejects_junction_parent(self):
        import subprocess
        target = self.attempt / 'target'
        target.mkdir()
        junction = self.attempt / 'junction'
        result = subprocess.run(['cmd', '/d', '/c', 'mklink', '/J', str(junction), str(target)], capture_output=True)
        self.assertEqual(0, result.returncode, result.stderr.decode(errors='replace'))
        try:
            with self.assertRaises(ValueError):
                policy.output_path(self.root, junction / 'result.json')
        finally:
            junction.rmdir()

    def test_synthetic_identity_match_still_never_grants_adoption(self):
        r = self.check()
        self.assertEqual('MATCHED_SPECIFIED_PATHS', r['result'])
        self.assertEqual('BLOCKED', r['formalPackageTrust'])
        self.assertEqual('NOT_RUN', r['mainConsumption'])

    def test_required_mutations_rejected(self):
        mutations = {
            'missing_dependency': lambda e,o: o['assemblies'].pop(),
            'wrong_version': lambda e,o: o['assemblies'][0].update(version='8.0.0.0'),
            'wrong_source': lambda e,o: e.update(platformSource='0'*40),
            'duplicate_assembly': lambda e,o: o['assemblies'].append(copy.deepcopy(o['assemblies'][0])),
            'unknown_compatibility': lambda e,o: e.update(compatibility='Unknown'),
            'cross_snapshot': lambda e,o: o.update(owner='0'*32),
            'cross_owner_path': lambda e,o: o['assemblies'][0].update(path=str(self.root.parent/'other.dll')),
            'false_hash': lambda e,o: o['assemblies'][0].update(sha256='0'*64),
            'invalid_mvid': lambda e,o: o['assemblies'][0].update(mvid='invalid'),
            'wrong_mvid': lambda e,o: o['assemblies'][0].update(mvid=str(uuid.uuid4())),
            'failed_build': lambda e,o: e['builds']['WebApi'].update(buildExitCode=1),
            'empty_assertions': lambda e,o: o.update(assertions=[]),
            'missing_http': lambda e,o: o['http'].update(requests=[]),
            'missing_metric': lambda e,o: o['telemetry']['records'].pop(1),
            'cleanup_failure': lambda e,o: o['cleanup'].update(listenerClosed=False),
            'false_formal_claim': lambda e,o: o.update(formalPackageTrust='TRUSTED'),
        }
        for name, mutate in mutations.items():
            with self.subTest(name=name):
                e,o = copy.deepcopy(self.expected),copy.deepcopy(self.observed)
                mutate(e,o)
                self.assertEqual('REJECTED', self.check(e,o)['result'])

    def test_changed_pre_runtime_build_evidence_rejected(self):
        Path(self.expected['evidence'][0]['path']).write_text('{"changed":true}', encoding='utf-8')
        self.assertEqual('REJECTED', self.check()['result'])

    def test_changed_binary_after_expected_capture_rejected(self):
        Path(self.expected['assemblies'][0]['path']).write_bytes(b'changed')
        self.assertEqual('REJECTED', self.check()['result'])


if __name__ == '__main__':
    unittest.main()
