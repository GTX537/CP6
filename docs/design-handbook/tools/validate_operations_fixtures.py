"""Independent read-only checks of materialized operations fixture products."""
import copy
import hashlib
import json
from pathlib import Path

from validate_design_completion import apply_operations, pointer, walk

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'remediation/assets/operations-fixtures'


def canon(v):
    return json.dumps(v, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


def digest(v):
    return hashlib.sha256(canon(v)).hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def main():
    checks = []
    def check(name, passed):
        assert passed, name
        checks.append({'check': name, 'result': 'PASS', 'level': 'INDEPENDENT_STATIC_DATA'})
    manifest = read(ASSETS / 'manifest.json')
    for source in manifest['sources']:
        raw = Path(source['source_path']).read_bytes()
        check('source:' + source['sha256'], hashlib.sha256(raw).hexdigest() == source['sha256'] and len(raw) == source['bytes'])
    for product in manifest['products']:
        raw = Path(product['path']).read_bytes()
        check('product:' + Path(product['path']).name, hashlib.sha256(raw).hexdigest() == product['sha256'] and len(raw) == product['bytes'])
    patches = read(ASSETS / 'bounded-patches.json')['patches']
    for patch in patches:
        raw = Path(patch['source_path']).read_bytes()
        derived = apply_operations(raw, patch['operations'])
        check('bounded:' + Path(patch['source_path']).name, hashlib.sha256(derived).hexdigest() == patch['expected_derived_sha256'])
    products = {Path(p['path']).name: p for p in manifest['products']}
    iqc = read(products['IQC-CONTINUOUS-OPERATIONS-R1.json']['path'])
    inline = read(products['IQC-INLINE-OPERATIONS-R1.json']['path'])
    documents = [v for v in iqc['evidence'].values() if 'ref' in v and 'body' in v]
    identities = {(d['ref']['owner'], d['ref']['id'], d['ref']['version']): d['ref']['digest'] for d in documents}
    check('IQC-all-evidence-bodies', all(digest(d['body']) == d['ref']['digest'] for d in documents))
    for name, value in [('bundle', iqc), ('inline', inline)]:
        bad = []
        for path, v in walk(value):
            if isinstance(v, dict) and {'owner', 'id', 'version', 'digest'} <= v.keys():
                key = (v['owner'], v['id'], v['version'])
                if key in identities and identities[key] != v['digest']:
                    bad.append(path)
        check('IQC-complete-ref-tuples:' + name, not bad)
    measurements = [d['body'] for d in documents if d['body'].get('schema') == 'IQC-MEASUREMENT-1']
    check('IQC-all-213-provenance', len(measurements) == 213 and all(m['evaluationBasisRefs'][2] in m['input']['evidenceRefs'] for m in measurements))
    check('IQC-original100-vs-new40to100', iqc['evidence']['POLICY1@1']['body']['scope']['quantityBand']['minimum'] == '100' and iqc['evidence']['POLICY1@2']['body']['scope']['quantityBand']['minimum'] == '40')
    check('IQC-inline-pinned-bundle', inline['fixture_sha256'] == products['IQC-CONTINUOUS-OPERATIONS-R1.json']['sha256'])
    for key, d in iqc['evidence'].items():
        b = d.get('body', {})
        if b.get('schema') == 'IQC-OBSERVATION-SNAPSHOT-1':
            check('IQC-slots:' + key, digest(b['slots']) == b['obligationDigest'])
        if 'draftDigest' in b:
            check('IQC-draft:' + key, digest(b['input']) == b['draftDigest'])
    ipqc = read(products['IPQC-CONTINUOUS-OPERATIONS-R1.json']['path'])
    # Independently reconstruct rows from inverse images then execute every declared write in order.
    for index in (0, 1, 6):
        fixture = ipqc['fixtures'][index]
        db = {(r['table'], r['key']): r['row'] for r in fixture['finalRows']}
        for step in reversed(fixture['steps']):
            for write in reversed(step['stateCut']['writes']):
                key = (write['table'], write['key'])
                if write['before'] is None:
                    db.pop(key, None)
                else:
                    db[key] = write['before']
        def snapshot():
            return [{'table': t, 'key': k, 'row': row, 'rowDigest': digest(row)} for (t, k), row in sorted(db.items())]
        for step in fixture['steps']:
            cut = step['stateCut']; before = digest(snapshot()); business = None
            check('IPQC-before:' + step['stepId'], before == cut['beforeDigest'])
            for write in cut['writes']:
                key = (write['table'], write['key'])
                assert db.get(key) == write['before']
                if write['table'] in ('Command', 'AuditEvent') and business is None:
                    business = digest(snapshot())
                if write['table'] == 'AuditEvent':
                    assert write['after']['BeforeDigest'] == before and write['after']['AfterDigest'] == business
                assert write['rowDigest'] == digest(write['after'])
                db[key] = write['after']
            check('IPQC-after:' + step['stepId'], digest(snapshot()) == cut['afterDigest'])
        check('IPQC-final:' + fixture['fixtureId'], snapshot() == fixture['finalRows'] and digest(snapshot()) == fixture['finalStateDigest'])
    claims = [r['row'] for r in ipqc['fixtures'][0]['finalRows'] if r['table'] == 'InitialClaim']
    check('IPQC100-initial-claims', len(claims) == len({(r['TenantId'], r['EnvironmentId'], r['Mode'], r['TaskId'], r['SlotId']) for r in claims}) == 100)
    f7 = ipqc['fixtures'][6]; result = f7['steps'][3]['response']['result']
    delivery = next(r['row'] for r in f7['finalRows'] if r['table'] == 'Delivery' and r['row']['DeliveryId'] == result['deliveryId'])
    check('IPQC-frozen-delivery', result['canonicalKey'] == delivery['CanonicalKey'] and result['sequence'] == str(delivery['Sequence']))
    h30 = read(ASSETS / 'rma-exclusive-h30-evidence.json'); docs = h30['documents']
    check('H30-all-evidence-hashes', all(digest(d['body']) == d['ref']['digest'] for d in docs))
    by_id = {d['ref']['id']: d for d in docs}; result = h30['positive']; receipt = result['terminalReceipt']
    binding = by_id[receipt['commandBindingRef']['id']]; proof = by_id[receipt['noAdditionalEffectProofRef']['id']]
    check('H30-binding-proof-scope', binding['ref'] == proof['body']['bindingRef'] and binding['body']['effectKeys'] == receipt['effectKeys'] == [result['query']['stableEffectKey']] and proof['body']['query'] == result['query'])
    check('H30-exact-input-preimage', binding['body']['bodyDigest'] == digest(h30['native_apply_request']))
    check('H30-permanent-fence', proof['body']['stateAfter']['newClaimsPermanentlyFenced'] and proof['body']['stateAfter']['lateApplyPermanentlyFenced'] and not proof['body']['stateAfter']['existingActiveClaimReleased'])
    lines = read(ASSETS / 'rma-line-identity-evidence.json')
    check('RMA-line-body-hashes', all(digest(d['body']) == d['ref']['digest'] for d in lines['documents']))
    line = lines['documents'][0]
    check('RMA-distinct-header-line', line['ref']['id'] == 'SHIP-100-L1' and line['ref']['digest'] != line['body']['shipmentRef']['digest'])
    check('RMA-four-claim-hashes', len(lines['positive_claims']) == 4 and all(digest({k: v for k, v in c.items() if k != 'claimDigest'}) == c['claimDigest'] for c in lines['positive_claims']))
    workpart = read(ROOT / 'remediation/workparts/operations-fixtures.json')
    check('16-design-findings-runtime-not-run', len(workpart['findings']) == 16 and all(a['result'] == 'NOT_RUN' for f in workpart['findings'] for a in f['acceptance']))
    report = {'revision': manifest['revision'], 'check_count': len(checks), 'checks': checks,
              'business_execution': 'NOT_RUN', 'executed_business_tests': 0, 'runtime_adoption': 'UNPROVEN'}
    (ASSETS / 'independent-validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'independent_static_checks': len(checks), 'business_execution': 'NOT_RUN'}))


if __name__ == '__main__':
    main()
