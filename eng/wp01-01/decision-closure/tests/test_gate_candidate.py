"""Synthetic algorithm fixtures: never authority, adoption or business evidence."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

MODULE = Path(__file__).resolve().parents[1] / 'gate_candidate.py'
if MODULE.exists():
    spec = importlib.util.spec_from_file_location('gate_candidate', MODULE)
    gate = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(gate)
else:
    gate = None


class GateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.original_source=self.put('original.txt','Original obligation source\nexact second line')
        self.matrix = {'rows': [{'id': 'O1', 'target': 'PLM-ITER-01',
                                'source_refs':[dict(self.original_source,line_start=1,line_end=2)],
                                'observations': ['retain original bytes', 'same Owner transaction']}]}
        self.history = [{'declared_legacy_count': 30, 'legacy_case_ids': None,
                         'missing_original_sha256': None, 'historical_inventory_state': 'NOT_INVENTORIED',
                         'recovery_state': 'UNRECOVERABLE', 'equivalence': 'UNPROVABLE'},
                        {'declared_legacy_count': 48, 'legacy_case_ids': None,
                         'missing_original_sha256': None, 'historical_inventory_state': 'NOT_INVENTORIED',
                         'recovery_state': 'UNRECOVERABLE', 'equivalence': 'UNPROVABLE'}]
        self.edge = {'id': 'consumer->provider', 'consumer_task': 'consumer', 'provider_task': 'provider',
                     'consumer_repository': 'Main', 'provider_repository': 'Platform', 'condition': 'declared dependency',
                     'contract': {'profile': 'fixture-profile', 'version': 'fixture/1', 'file': self.put('profile.json', {'profile':'fixture-profile','version':'fixture/1'})}}
        self.contract = {'version': 'CP6-WP05-01-ACCEPTANCE-BASIS/1.2.0-proposed',
                         'production_enabled': False, 'matrix': self.put('matrix.json', self.matrix),
                         'sources': [self.put('source.txt', 'exact source')],
                         'provider_graph': self.put('graph.json', {'edges': [self.edge]}),
                         'historical_gaps': self.history, 'additional_obligations': [],
                         'target_edges': {'PLM-ITER-01': [self.edge['id']], 'PLM-REL-01': []}}
        self.contract['decision_package']=self.put('decisions.json',{'production_enabled':False,'actual_adoption':False,'actual_completion':False})
        self.contract['basis_delta']=self.put('delta.json',{'additional_obligations':[],'removed_obligations':[]})
        self.sub = {'target': 'PLM-ITER-01', 'basis_version': self.contract['version'],
                    'synthetic': True, 'historical_gaps': copy.deepcopy(self.history),
                    'adoption': self.put('authority.json', {'kind': 'SYNTHETIC_AUTHORITY', 'synthetic': True,
                        'basis_version': self.contract['version'], 'target': 'PLM-ITER-01', 'actor': 'fixture',
                        'scope': ['O1'], 'authority_source': self.put('authority-source.txt', 'synthetic scope')}),
                    'obligations': [{'id': 'O1', 'subassertions': self.matrix['rows'][0]['observations'],
                        'evidence': self.put('evidence.json', {'status': 'PASS', 'kind': 'SYNTHETIC_RUNTIME',
                            'synthetic': True, 'target': 'PLM-ITER-01', 'obligation': 'O1',
                            'subassertions': self.matrix['rows'][0]['observations'],
                            'source_sha256': self.original_source['sha256'],
                            'source_bindings':[{'path':self.original_source['path'],'sha256':self.original_source['sha256']}]})}],
                    'edges': [{'id': self.edge['id'], 'state': 'BOUND', 'receipt': self.put('receipt.json', {
                        'synthetic': True, 'status': 'PASS', 'edge': self.edge['id'], 'provider_task': 'provider',
                        'consumer_task': 'consumer', 'provider_repository': 'Platform', 'consumer_repository': 'Main',
                        'contract_version': 'fixture/1', 'package': self.put('package.bin', 'synthetic package'),
                        'contract_profile':'fixture-profile', 'contract_sha256':self.edge['contract']['file']['sha256'],
                        'provider_evidence': self.put('provider.txt', {'synthetic':True,'status':'PASS','task':'provider','repository':'Platform','edge':self.edge['id'],'contract_sha256':self.edge['contract']['file']['sha256']}),
                        'consumer_evidence': self.put('consumer.txt', {'synthetic':True,'status':'PASS','task':'consumer','repository':'Main','edge':self.edge['id'],'contract_sha256':self.edge['contract']['file']['sha256']}),
                        'remote': self.put('remote.json', {'synthetic': True, 'repository': 'Platform',
                            'commit': 'a'*40, 'package_sha256': hashlib.sha256(b'synthetic package').hexdigest()}),
                        'consumer_remote':self.put('consumer-remote.json',{'synthetic':True,'repository':'Main','commit':'b'*40,
                            'package_sha256':hashlib.sha256(b'synthetic package').hexdigest()})})}]}

    def put(self, path, value):
        data = value.encode() if isinstance(value, str) else json.dumps(value, ensure_ascii=False).encode()
        (self.root / path).write_bytes(data)
        return {'path': path, 'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data)}

    def evaluate(self, mode='synthetic'):
        self.assertIsNotNone(gate, 'candidate adapter missing: negative protections not implemented')
        before = {p.relative_to(self.root).as_posix(): p.read_bytes() for p in self.root.rglob('*') if p.is_file()}
        ref = self.put('contract.json', self.contract)
        before['contract.json'] = (self.root/'contract.json').read_bytes()
        result = gate.evaluate_candidate(self.root, ref, self.sub, mode=mode)
        self.assertEqual(before, {p.relative_to(self.root).as_posix(): p.read_bytes() for p in self.root.rglob('*') if p.is_file()})
        for key in ['production_enabled', 'actual_adoption', 'actual_completion']:
            self.assertIs(result[key], False)
        return result

    def reject(self):
        self.assertFalse(self.evaluate()['candidate_valid'])

    def test_missing_adoption(self):
        self.sub.pop('adoption'); self.reject()

    def test_synthetic_positive_only(self):
        self.assertTrue(self.evaluate()['candidate_valid'])

    def test_actual_authority_and_remote_never_verified(self):
        result = self.evaluate('actual')
        self.assertFalse(result['candidate_valid'])
        self.assertIn('AUTHORITY_AUTHENTICITY_UNVERIFIED', result['errors'])

    def test_forged_authority(self):
        self.sub['adoption'] = self.put('forged.json', {'actor': 'approver', 'signature': 'signed'})
        self.reject()

    def test_malformed_authority(self):
        self.sub['adoption'] = self.put('bad.json', '{'); self.reject()

    def test_authority_scope_mismatch(self):
        d=json.loads((self.root/'authority.json').read_bytes()); d['scope']=[]
        self.sub['adoption']=self.put('authority.json',d); self.reject()

    def test_source_bytes_drift(self):
        (self.root/'source.txt').write_text('drift'); self.reject()

    def test_matrix_hash_drift(self):
        (self.root/'matrix.json').write_text('{}'); self.reject()

    def test_path_escape(self):
        self.sub['adoption']['path']='../authority.json'; self.reject()

    def test_absolute_path(self):
        self.sub['adoption']['path']=str(self.root/'authority.json'); self.reject()

    def test_wrong_target(self):
        self.sub['target']='PLM-ENG-01'; self.reject()

    def test_other_123_not_accepted(self):
        roadmap=Path(__file__).resolve().parents[4]/'docs/development/roadmap.json'
        others=[t['id'] for t in json.loads(roadmap.read_bytes())['targets'] if t['id'] not in {'PLM-ITER-01','PLM-REL-01'}]
        self.assertEqual(123,len(others))
        for target in others:
            with self.subTest(target=target):
                self.sub['target']=target; self.reject()

    def test_missing_obligation(self):
        self.sub['obligations']=[]; self.reject()

    def test_extra_obligation(self):
        self.sub['obligations'].append(dict(self.sub['obligations'][0],id='O2')); self.reject()

    def test_missing_subassertion(self):
        self.sub['obligations'][0]['subassertions']=['retain original bytes']; self.reject()

    def test_duplicate_obligation(self):
        self.sub['obligations']*=2; self.reject()

    def test_nonruntime_statuses(self):
        for status in ['STATIC','REFERENCE_ONLY','NOT_RUN','FAIL']:
            with self.subTest(status=status):
                d=json.loads((self.root/'evidence.json').read_bytes());d['status']=status
                self.sub['obligations'][0]['evidence']=self.put('evidence.json',d);self.reject()

    def test_unmarked_synthetic(self):
        self.sub['synthetic']=False;self.reject()

    def test_synthetic_cannot_be_real(self):
        self.assertFalse(self.evaluate('actual')['candidate_valid'])

    def test_evidence_file_mismatch(self):
        (self.root/'evidence.json').write_text('{}');self.reject()

    def test_evidence_wrong_source(self):
        d=json.loads((self.root/'evidence.json').read_bytes());d['source_sha256']='b'*64
        self.sub['obligations'][0]['evidence']=self.put('evidence.json',d);self.reject()

    def test_missing_provider_edge(self):
        self.sub['edges']=[];self.reject()

    def test_conditional_edge_requires_scope_authority(self):
        self.sub['edges']=[{'id':self.edge['id'],'state':'EXCLUDED','authority':'approved'}];self.reject()

    def test_receipt_remote_binding(self):
        d=json.loads((self.root/'remote.json').read_bytes());d['package_sha256']='c'*64
        r=json.loads((self.root/'receipt.json').read_bytes());r['remote']=self.put('remote.json',d)
        self.sub['edges'][0]['receipt']=self.put('receipt.json',r);self.reject()

    def test_missing_provider_artifact(self):
        (self.root/'provider.txt').unlink();self.reject()

    def test_history_cannot_be_relabelled(self):
        self.sub['historical_gaps'][0]['historical_inventory_state']='RECOVERED';self.reject()

    def test_unknown_original_hash_stays_null(self):
        self.sub['historical_gaps'][1]['missing_original_sha256']='a'*64;self.reject()

    def test_contract_cannot_drop_declared_edges(self):
        self.contract['target_edges']['PLM-ITER-01']=[];self.reject()

    def test_contract_cannot_enable_production(self):
        self.contract['production_enabled']=True;self.reject()

    def test_duplicate_json_members(self):
        self.sub['adoption']=self.put('duplicate.json','{"kind":"FORGED","kind":"SYNTHETIC_AUTHORITY"}')
        self.assertIn('JSON_DUPLICATE_KEY', ' '.join(self.evaluate()['errors']))

    def test_nonfinite_json(self):
        self.sub['adoption']=self.put('nonfinite.json','{"actor":NaN}')
        self.assertIn('JSON_NONFINITE', ' '.join(self.evaluate()['errors']))

    def test_actual_mode_reads_source_before_rejecting_trust(self):
        self.sub['synthetic']=False
        (self.root/'source.txt').write_text('drift')
        self.assertIn('FILE_BYTES_MISMATCH', self.evaluate('actual')['errors'])

    def test_wrong_edge_contract_version(self):
        d=json.loads((self.root/'receipt.json').read_bytes());d['contract_version']='fixture/2'
        self.sub['edges'][0]['receipt']=self.put('receipt.json',d);self.reject()

    def test_wrong_edge_contract_hash(self):
        d=json.loads((self.root/'receipt.json').read_bytes());d['contract_sha256']='c'*64
        self.sub['edges'][0]['receipt']=self.put('receipt.json',d);self.reject()

    def test_wrong_edge_contract_profile(self):
        d=json.loads((self.root/'receipt.json').read_bytes());d['contract_profile']='another'
        self.sub['edges'][0]['receipt']=self.put('receipt.json',d);self.reject()

    def test_wrong_provider_repository(self):
        d=json.loads((self.root/'receipt.json').read_bytes());d['provider_repository']='CRM'
        self.sub['edges'][0]['receipt']=self.put('receipt.json',d);self.reject()

    def test_wrong_consumer_task(self):
        d=json.loads((self.root/'receipt.json').read_bytes());d['consumer_task']='other'
        self.sub['edges'][0]['receipt']=self.put('receipt.json',d);self.reject()

    def test_contract_profile_file_drift(self):
        (self.root/'profile.json').write_text('{}');self.reject()

    def test_source_assertion_not_dropped(self):
        self.matrix['rows'][0]['source_assertion']='Exact underlying assertion beyond navigation summary'
        self.contract['matrix']=self.put('matrix.json',self.matrix)
        self.reject()

    def test_provider_status_cannot_be_static(self):
        d=json.loads((self.root/'provider.txt').read_bytes());d['status']='STATIC'
        r=json.loads((self.root/'receipt.json').read_bytes());r['provider_evidence']=self.put('provider.txt',d)
        self.sub['edges'][0]['receipt']=self.put('receipt.json',r);self.reject()

    def test_consumer_evidence_wrong_contract(self):
        d=json.loads((self.root/'consumer.txt').read_bytes());d['contract_sha256']='f'*64
        r=json.loads((self.root/'receipt.json').read_bytes());r['consumer_evidence']=self.put('consumer.txt',d)
        self.sub['edges'][0]['receipt']=self.put('receipt.json',r);self.reject()

    def test_decision_file_drift(self):
        (self.root/'decisions.json').write_text('{}');self.reject()

    def test_delta_cannot_drop_obligations(self):
        self.contract['additional_obligations']=[{'id':'EXTRA','target':'PLM-REL-01','observations':['one']}]
        self.reject()

    def test_synthetic_conditional_exclusion_requires_exact_scope(self):
        self.edge['exclusion_allowed']=True
        self.contract['provider_graph']=self.put('graph.json',{'edges':[self.edge]})
        self.sub['edges']=[{'id':self.edge['id'],'state':'EXCLUDED','authority':self.put('exclude.json',{
            'kind':'SYNTHETIC_CONDITIONAL_AUTHORITY','synthetic':True,'edge':self.edge['id'],'target':self.sub['target'],
            'condition':self.edge['condition'],'basis_version':self.contract['version'],
            'authority_source':self.put('scope.txt','SYNTHETIC: exact edge exclusion scope')})}]
        self.assertTrue(self.evaluate()['candidate_valid'])
        d=json.loads((self.root/'exclude.json').read_bytes());d['target']='PLM-REL-01'
        self.sub['edges'][0]['authority']=self.put('exclude.json',d);self.reject()

    def test_consumer_remote_package_binding(self):
        d=json.loads((self.root/'consumer-remote.json').read_bytes());d['package_sha256']='e'*64
        r=json.loads((self.root/'receipt.json').read_bytes());r['consumer_remote']=self.put('consumer-remote.json',d)
        self.sub['edges'][0]['receipt']=self.put('receipt.json',r);self.reject()

    def test_unrelated_valid_top_level_source_cannot_replace_obligation_source(self):
        d=json.loads((self.root/'evidence.json').read_bytes())
        d['source_sha256']=self.contract['sources'][0]['sha256']
        d['source_bindings']=[{'path':self.contract['sources'][0]['path'],'sha256':self.contract['sources'][0]['sha256']}]
        self.sub['obligations'][0]['evidence']=self.put('evidence.json',d)
        self.reject()

    def test_obligation_original_file_drift_rejected(self):
        # Keep the formerly accepted unrelated top-level hash to expose the missing original read.
        d=json.loads((self.root/'evidence.json').read_bytes());d['source_sha256']=self.contract['sources'][0]['sha256']
        self.sub['obligations'][0]['evidence']=self.put('evidence.json',d)
        (self.root/'original.txt').write_text('drift')
        result=self.evaluate()
        self.assertFalse(result['candidate_valid'])
        self.assertIn('FILE_BYTES_MISMATCH',result['errors'])

    def test_original_matrix_shape_without_bytes_and_owner_authority(self):
        self.matrix['rows'][0]['source_refs']=[{'path':'original.txt','sha256':self.original_source['sha256'],'line_start':2}]
        self.matrix['rows'][0]['owner_authority']={'source_path':'original.txt','source_sha256':self.original_source['sha256'],
            'source_line':1,'observation':'Original obligation source'}
        self.contract['matrix']=self.put('matrix.json',self.matrix)
        self.assertTrue(self.evaluate()['candidate_valid'])

    def test_duplicate_navigation_preserves_source_union(self):
        second=self.put('second-original.txt','second source')
        self.matrix['rows'].append(dict(self.matrix['rows'][0],source_refs=[second]))
        self.contract['matrix']=self.put('matrix.json',self.matrix)
        d=json.loads((self.root/'evidence.json').read_bytes())
        d['source_bindings'].append({'path':second['path'],'sha256':second['sha256']})
        self.sub['obligations'][0]['evidence']=self.put('evidence.json',d)
        self.assertTrue(self.evaluate()['candidate_valid'])
        d['source_bindings'].pop()
        self.sub['obligations'][0]['evidence']=self.put('evidence.json',d);self.reject()

if __name__ == '__main__':
    unittest.main()
