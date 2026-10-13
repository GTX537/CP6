"""Portable audit of selected current design identities, never a runtime Owner/Store test."""
from pathlib import Path
import copy, hashlib, json, sys, unittest

BASE = Path(__file__).resolve().parent
def sha(raw): return hashlib.sha256(raw).hexdigest()
def pairs(items):
    result = {}
    for key,value in items:
        if key in result: raise ValueError('DUPLICATE_PROPERTY_NAME')
        result[key]=value
    return result
def strict(raw): return json.loads(raw.decode('utf-8'), object_pairs_hook=pairs)
def canonical(value): return json.dumps(value,sort_keys=True,separators=(',', ':'),ensure_ascii=False).encode()

CHAIN = strict((BASE/'fixtures/current-chain.json').read_bytes())
IDENTITIES = CHAIN['credit_money']['body_identities']
def validate_record(record):
    value = record['selected_value']; expected=IDENTITIES[value['bodyKey']]
    digest,size=expected['after_digest'],expected['after_bytes']
    for key in ['sha256','bodySha256','expectedDigest']:
        if key in value: assert value[key]==digest, key
    for key in ['bytes','bodyBytes','expectedBytes']:
        if key in value: assert value[key]==size, key
    for key in ['actualIssuerRef','actualNativeCodecRef','actualRegisteredPublisherRef','actualStoreReceipt','actualRuntimeStoreReceipt']:
        if key in value: assert value[key] is None, key
    if 'actualNewMetadataWrites' in value: assert value['actualNewMetadataWrites']==0
    if 'isExecutionPermit' in value: assert value['isExecutionPermit'] is False
    for key in ['documentRawRef8','originalReceiptMetadata7']:
        if key in value:
            ref=value[key]
            assert len(ref)==(8 if key=='documentRawRef8' else 7)
            assert ref['digest']==digest and ref['byteLength']==size
            assert ref['handle']==f'cas://erp-credit/{digest}/{size}'
            assert ref['mediaType']=='application/json'
    if 'receiptCanonicalUtf8' in value:
        encoded=value['receiptCanonicalUtf8'].encode()
        assert encoded==canonical(value['originalReceiptMetadata7'])
        assert len(encoded)==value['receiptCanonicalBytes']
        assert sha(encoded)==value['receiptCanonicalSha256']

class DesignFixtureAudit(unittest.TestCase):
    def test_exact_raw_assets_and_body_identities(self):
        manifest=strict((BASE/'provenance.json').read_bytes())
        repo=BASE.parents[2]
        for item in manifest['adopted_assets']:
            raw=(repo/item['destination']).read_bytes()
            self.assertEqual(item['sha256'],sha(raw));self.assertEqual(item['byte_length'],len(raw))
        for key,expected in IDENTITIES.items():
            raw=(BASE/'fixtures/current-bodies'/f'{key}.json').read_bytes()
            self.assertEqual(expected['after_digest'],sha(raw));self.assertEqual(expected['after_bytes'],len(raw))
            self.assertEqual(raw,canonical(strict(raw)))

    def test_all_343_dependency_projections_and_Store7_remain_together(self):
        self.assertEqual({'composed-04':1,'composed-05':10,'composed-06':5,'composed-07':2,'composed-08':324,'composed-09':1},CHAIN['source_counts'])
        self.assertEqual(343,len(CHAIN['records']))
        for record in CHAIN['records']: validate_record(record)
        for key in IDENTITIES:
            records=[r['selected_value'] for r in CHAIN['records'] if r['source_id']=='composed-05' and r['selected_value']['bodyKey']==key]
            directory=next(r for r in records if 'documentRawRef8' in r)['documentRawRef8']
            store=next(r for r in records if 'originalReceiptMetadata7' in r)['originalReceiptMetadata7']
            self.assertEqual(directory['scope'],store['scope']);self.assertEqual(directory['putReceiptId'],store['receiptId'])
            self.assertEqual(directory['createdAt'],store['createdAt'])

    def test_stale_digest_length_handle_receipt_and_self_asserted_proof_rejected(self):
        checks=0
        for original in CHAIN['records']:
            if original['source_id']!='composed-05':continue
            value=original['selected_value'];old=IDENTITIES[value['bodyKey']]
            mutations=[]
            if 'documentRawRef8' in value:
                mutations=[(['sha256'],old['before_digest']),(['bytes'],old['after_bytes']+1),(['documentRawRef8','digest'],old['before_digest']),(['documentRawRef8','byteLength'],old['after_bytes']+1),(['documentRawRef8','handle'],f"cas://erp-credit/{old['before_digest']}/{old['before_bytes']}"),(['actualStoreReceipt'],{})]
            if 'originalReceiptMetadata7' in value:
                mutations=[(['originalReceiptMetadata7','digest'],old['before_digest']),(['receiptCanonicalBytes'],value['receiptCanonicalBytes']+1),(['receiptCanonicalSha256'],'0'*64),(['receiptCanonicalUtf8'],value['receiptCanonicalUtf8']+'\n'),(['actualRuntimeStoreReceipt'],{}),(['actualNewMetadataWrites'],1)]
            for path,replacement in mutations:
                mutated=copy.deepcopy(original);target=mutated['selected_value']
                for part in path[:-1]:target=target[part]
                target[path[-1]]=replacement
                with self.assertRaises(AssertionError):validate_record(mutated)
                checks+=1
        self.assertEqual(60,checks)

    def test_18_exact_native_money_locations(self):
        count=0
        for key,pointers in CHAIN['credit_money']['money_pointers'].items():
            body=strict((BASE/'fixtures/current-bodies'/f'{key}.json').read_bytes())
            for pointer in pointers:
                value=body
                for part in pointer.strip('/').split('/'):value=value[int(part)] if isinstance(value,list) else value[part]
                self.assertEqual({'value','currency'},set(value));self.assertIsInstance(value['value'],str);count+=1
        self.assertEqual(18,count)

    def test_OpenAPI_single_root_29_routes_10_extensions_C01_C02_identity(self):
        raw=(BASE/'assets/current/ERP-CREDIT-01_OPENAPI.json').read_bytes();api=strict(raw)
        methods={'get','post','put','delete','patch','head','options','trace'}
        operations=[operation for path in api['paths'].values() for method,operation in path.items() if method in methods]
        self.assertEqual(29,len(operations))
        for extension in CHAIN['credit_openapi']['preserved_extension_blocks']:self.assertIn(extension,api)
        for key in ['EC-C01','EC-C02']:
            operation=next(op for op in operations if op['operationId']==key)
            ref=lambda status:operation['responses'][status]['content']['application/json']['schema']['$ref']
            self.assertEqual(ref('200'),ref('201'));self.assertIn('_COMMITTED_CommercialDraftView',ref('200'))
        with self.assertRaises(ValueError):strict(raw+b'\n{}')
        with self.assertRaises(ValueError):strict(b'{"x-stage93":{},"x-stage93":{}}')

if __name__=='__main__':
    result=unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(DesignFixtureAudit))
    report={'verification_level':'SELECTED_DESIGN_FIXTURE_SUBASSERTIONS','tests':result.testsRun,'passed':result.testsRun-len(result.errors)-len(result.failures),'failed':len(result.failures)+len(result.errors),'skipped':len(result.skipped),'projected_records':343,'tamper_rejections':60,'native_money_locations':18,'runtime_Owner_Store_acceptance':'NOT_RUN','actual_adoption':'UNPROVEN_FENCED','whole_C06_decode':'NOT_EXPOSED'}
    if len(sys.argv)>1:Path(sys.argv[1]).write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report));sys.exit(not result.wasSuccessful())
