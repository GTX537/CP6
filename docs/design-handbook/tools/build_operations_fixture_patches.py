"""Rebuild W5 operations design fixtures from SHA-pinned, read-only sources.

This is a data generator/static verifier, not an application or archived script.
Large independent DEMO fixture worlds are materialized outside the repository.
"""
import argparse
import copy
import hashlib
import json
import re
import sys
from importlib.metadata import version as package_version
from collections import Counter
from pathlib import Path

from validate_design_completion import apply_operations, pointer, walk

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'remediation/assets/operations-fixtures'
ORIGINAL = Path('D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals')
OBJECTS = Path('D:/CP6-archives/consolidation-20261010/objects')
REVISION = 'CP6-DESIGN-REVISION-20261011/R1'
PATCHES, SOURCES, CHECKS, PRODUCTS, RECIPES = [], {}, [], [], []
DERIVED = {}


def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode('utf-8')


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def digest(value):
    return sha(canonical(value))


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def original(prefix):
    found = list((ORIGINAL / prefix[:2]).glob(prefix + '*'))
    assert len(found) == 1, (prefix, found)
    return found[0]


def read(path):
    raw = path.read_bytes()
    expected = path.name.split('__')[0] if '__' in path.name else path.stem
    if re.fullmatch('[0-9a-f]{16}|[0-9a-f]{64}', expected):
        assert sha(raw).startswith(expected), ('source identity mismatch', path)
    SOURCES[path.as_posix()] = {'source_path': path.as_posix(), 'sha256': sha(raw), 'bytes': len(raw)}
    return raw


def load(path):
    return json.loads(read(path).decode('utf-8-sig'))


def check(name, condition, detail=None):
    assert condition, (name, detail)
    CHECKS.append({'check': name, 'result': 'PASS', 'verification_level': 'LOCAL_DESIGN_DATA', 'detail': detail})


def setp(value, path, item):
    parent, _, key = path.rpartition('/')
    target = pointer(value, parent)
    key = key.replace('~1', '/').replace('~0', '~')
    target[int(key) if isinstance(target, list) else key] = copy.deepcopy(item)


def diff(a, b, path=''):
    if a == b:
        return []
    if isinstance(a, dict) and isinstance(b, dict):
        result = []
        for key in sorted(a.keys() | b.keys()):
            p = path + '/' + key.replace('~', '~0').replace('/', '~1')
            if key not in a:
                result.append({'op': 'add_json', 'path': p, 'after': b[key]})
            elif key not in b:
                result.append({'op': 'remove_json', 'path': p, 'before': a[key]})
            else:
                result += diff(a[key], b[key], p)
        return result
    if isinstance(a, list) and isinstance(b, list) and len(a) == len(b):
        return [op for i in range(len(a)) for op in diff(a[i], b[i], path + '/' + str(i))]
    return [{'op': 'replace_json', 'path': path, 'before': a, 'after': b}]


def patch(path, findings, decision, operations):
    raw = read(path)
    assert path.as_posix() not in DERIVED
    after = apply_operations(raw, operations)
    check('bounded-preimage:' + path.name, raw != after)
    # Deliberately corrupt one expected preimage; a bounded patch must fail closed.
    bad = copy.deepcopy(operations)
    bad[0]['before'] = '__INCORRECT_PREIMAGE__'
    try:
        apply_operations(raw, bad)
    except (AssertionError, ValueError, KeyError, TypeError):
        check('bounded-preimage-negative:' + path.name, True)
    else:
        raise AssertionError('wrong preimage accepted')
    PATCHES.append({'source_path': path.as_posix(), 'source_sha256': sha(raw),
                    'source_finding_ids': findings, 'decision': decision, 'operations': operations,
                    'expected_derived_sha256': sha(after), 'expected_derived_bytes': len(after)})
    DERIVED[path.as_posix()] = after
    return after


def jsonpatch(path, findings, decision, before, after):
    return patch(path, findings, decision, diff(before, after))


def replace_tokens(value, mapping):
    if isinstance(value, dict):
        return {k: replace_tokens(v, mapping) for k, v in value.items()}
    if isinstance(value, list):
        return [replace_tokens(v, mapping) for v in value]
    if isinstance(value, str):
        return mapping.get(value, value)
    return value


def write_product(path, value, findings, role):
    raw = canonical(value) + b'\n'
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(raw)
    PRODUCTS.append({'path': path.as_posix(), 'sha256': sha(raw), 'bytes': len(raw),
                     'source_finding_ids': findings, 'role': role,
                     'business_execution': 'NOT_RUN', 'runtime_adoption': 'UNPROVEN'})
    return raw


def finance_and_small():
    # OPS038 is a versioned page-state wire repair, not a Money-to-Work object cast.
    p = original('3a61c4b68b8f5c01'); old = load(p); new = copy.deepcopy(old)
    new['$id'] = 'urn:cp6:fin-inv:r10:wire:operations-revision-1'
    for name, field in [('OutboundValueRow', 'valueExitState'), ('ReverseExceptionRow', 'inventoryValueState')]:
        new['$defs'][name]['properties'][field] = {'anyOf': [{'type': 'string', 'minLength': 1, 'maxLength': 40}, {'type': 'null'}],
            'description': 'Exact native ValueEffect.State or Work.state from this row source; independent of Money and all other axes. Null requires a missing/unknown/not-applicable source reason.'}
    jsonpatch(p, ['OPS-IF-038'], 'Use native state string|null in a new pinned schema identity; preserve separate Money fields.', old, new)
    import jsonschema
    validator = jsonschema.Draft202012Validator(new)
    for name, field in [('OutboundValueRow', 'valueExitState'), ('ReverseExceptionRow', 'inventoryValueState')]:
        node = new['$defs'][name]['properties'][field]
        check('OPS038-state-positive:' + field, jsonschema.Draft202012Validator(node).is_valid('APPLIED'))
        check('OPS038-money-negative:' + field, not jsonschema.Draft202012Validator(node).is_valid({'amount': '0.00', 'currency': 'CNY', 'scale': 2}))
    jsonschema.Draft202012Validator.check_schema(new)

    p = original('72ba69312a04f2bb'); old = load(p); new = copy.deepcopy(old)
    sc = new['Scenarios']['SC12']; c01, c03, c09 = sc['Steps'][0], sc['Steps'][2], sc['Steps'][3]
    binding1 = digest(c01['Request']['binding']); binding3 = digest(c03['Request']['binding'])
    check('OPS039-original-binding-preimages', binding1 == c01['Response']['receipt']['bindingDigest'] and binding3 == c03['Response']['receipt']['bindingDigest'])
    c01['PersistedRows'][9]['Row']['BindingDigest'] = binding1
    c09['Response']['receipt'] = copy.deepcopy(c03['Response']['receipt'])
    c09['Response']['bindingDigest'] = binding3
    c09['CanonicalResponseUtf8'] = canonical(c09['Response']).decode()
    c09['CanonicalResponseSha256'] = digest(c09['Response'])
    check('OPS039-C09-original-receipt-exact', c09['Response']['receipt'] == c03['Response']['receipt'])
    check('OPS039-wrong-binding-negative', old['Scenarios']['SC12']['Steps'][3]['Response']['receipt'] != c03['Response']['receipt'])
    sc20 = new['Scenarios']['SC20']; effect = sc20['CorrectionEffect']
    # Original SC20 digest excludes adoptionMode (the original C03 result profile differs).
    effect_core = lambda e: {k: v for k, v in e.items() if k not in ('resultDigest', 'adoptionMode')}
    check('OPS040-original-digest-profile', digest(effect_core(effect)) == effect['resultDigest'])
    check('OPS040-no-invented-original-consumption', not any(r['Table'] == 'LayerConsumption' for r in sc20['InitialPersistedRows']))
    before_digest = effect['resultDigest']
    effect['layerConsumptions'][0]['effectId'] = effect['effectId']
    sc20['FinalRows']['ReverseLayerConsumption'] = copy.deepcopy(effect['layerConsumptions'][0])
    effect['resultDigest'] = digest(effect_core(effect))
    sc20 = replace_tokens(sc20, {before_digest: effect['resultDigest'], 'urn:sha256:' + before_digest: 'urn:sha256:' + effect['resultDigest']})
    new['Scenarios']['SC20'] = sc20
    row = sc20['Steps'][1]['PersistedRows'][2]
    row['PrimaryKey']['ObjectId'] = row['Row']['ObjectId']
    check('OPS040-exact-consumption-identity', sc20['CorrectionEffect']['layerConsumptions'][0]['effectId'] == sc20['Steps'][0]['PersistedRows'][6]['Row']['EffectId'] and sc20['FinalRows']['ReverseLayerConsumption']['reverseOfConsumptionId'] is None)
    check('OPS041-wrapper-equals-row-PK', all(row['Row'][k] == v for k, v in row['PrimaryKey'].items()))
    # Published FixtureDigest predates additional R10 rows. Do not pretend it hashes the complete current object.
    seals = {}
    for name in ('SC12', 'SC20'):
        scenario = new['Scenarios'][name]
        seals[name] = {'original_fixture_digest': old['Scenarios'][name]['FixtureDigest'],
                       'profile': 'OPS-SCENARIO-FULL/1: SHA256(canonical scenario excluding FixtureDigest)',
                       'original_legacy_digest_preserved': True,
                       'derived_full_digest': digest({k: v for k, v in scenario.items() if k != 'FixtureDigest'})}
    save(OUT / 'fin-inv-digest-domains.json', {'SC20_result_excludes': ['resultDigest', 'adoptionMode'],
         'SC12_binding': 'entire Request.binding', 'SC12_result': 'Response.result excluding resultDigest', 'scenario_seals': seals})
    jsonpatch(p, ['OPS-IF-039', 'OPS-IF-040', 'OPS-IF-041'], 'Exact command receipt replay; SC20 first withdrawal from original receipt layer retains null reverse-consumption, exact correction lineage and original digest domain; SQL wrapper PK equals Row.', old, new)

    p = original('dfc3c4002d5c9381'); old = load(p); new = copy.deepcopy(old)
    assert new['vectors'][1]['why'] == 'unknown root member must be 422 INVALID_FIELD with JSON Pointer'
    new['vectors'][1]['why'] = 'unknown root member must be 400 INVALID_FIELD with JSON Pointer'
    jsonpatch(p, ['OPS-IF-042'], 'R4 section11 HTTP400 is normative; schema rejection is not an executed HTTP result.', old, new)

    p = original('57f3a7ec38d3c587'); old = load(p); new = copy.deepcopy(old)
    assert new['cases'][27]['code'] == 'MC-RCAP02'
    new['cases'][27]['title'] = '同一次SHIP后第一地点容量头40到36不减未收'
    before = new['resourceSemantics']['predicateResources']
    new['resourceSemantics']['predicateResources'] = before.replace('the concrete member Package b3000000-0000-4000-8000-000000001300 row is explicitly enlisted', 'both concrete member Package rows b3100000-0000-4000-8000-000000001300 and b3100000-0000-4000-8000-000000002300 are explicitly enlisted')
    check('OPS043-exact-two-packages', [x['packageId'] for x in new['packagingIdentity']['packages']] == ['b3100000-0000-4000-8000-000000001300', 'b3100000-0000-4000-8000-000000002300'])
    jsonpatch(p, ['OPS-IF-043'], 'Prose follows first location40→36 and exact two Package rows; body digests and Stock quantities unchanged.', old, new)

    p = original('94de90613be08775'); before = '"operationId": "a1c16cf6-9a0d-576e-9653-f74581f5f917"'
    after = '"operationId": "72305281-7790-5c74-a80c-dc3da2e6f0d5"'
    patch(p, ['OPS-IF-044'], 'Original locator query returns the operationId of the single committed Stock receipt/movement.', [{'op': 'replace_text', 'before': before, 'after': after, 'expected_count': 1}])


def ipqc_replay(fixture, mutate=False):
    """Reconstruct seed by inverse writes, then replay complete state and audit cuts."""
    hrow = lambda table, key, row: {'table': table, 'key': key, 'row': copy.deepcopy(row), 'rowDigest': digest(row)}
    db = {(r['table'], r['key']): copy.deepcopy(r['row']) for r in fixture['finalRows']}
    for step in reversed(fixture['steps']):
        for w in reversed(step['stateCut']['writes']):
            key = (w['table'], w['key'])
            if w['before'] is None:
                db.pop(key, None)
            else:
                db[key] = copy.deepcopy(w['before'])
    snapshot = lambda: [hrow(t, k, r) for (t, k), r in sorted(db.items())]
    assert digest(snapshot()) == fixture['steps'][0]['stateCut']['beforeDigest']
    for step in fixture['steps']:
        cut = step['stateCut']; before_digest = digest(snapshot())
        touched = sorted({(w['table'], w['key']) for w in cut['writes']})
        before_rows = [hrow(t, k, db[(t, k)]) for t, k in touched if (t, k) in db]
        assert mutate or before_digest == cut['beforeDigest']
        business_after = None
        for w in cut['writes']:
            key = (w['table'], w['key'])
            assert w['before'] == db.get(key), ('before row mismatch', step['stepId'], key)
            if w['table'] in ('Command', 'AuditEvent') and business_after is None:
                business_after = digest(snapshot())
            if mutate and w['table'] == 'AuditEvent':
                w['after']['BeforeDigest'] = before_digest
                w['after']['AfterDigest'] = business_after
            w['rowDigest'] = digest(w['after'])
            db[key] = copy.deepcopy(w['after'])
        after_digest = digest(snapshot())
        if mutate:
            cut.update(beforeDigest=before_digest, beforeRows=before_rows,
                       afterRows=[hrow(t, k, db[(t, k)]) for t, k in touched], afterDigest=after_digest)
            step['transactionContract']['businessWrites'] = sum(w['table'] not in ('Command', 'AuditEvent') for w in cut['writes'])
            step['transactionContract']['terminalLedgerWrites'] = sum(w['table'] in ('Command', 'AuditEvent') for w in cut['writes'])
        else:
            assert after_digest == cut['afterDigest'], step['stepId']
    if mutate:
        fixture['finalRows'] = snapshot(); fixture['finalStateDigest'] = digest(snapshot())
    else:
        assert snapshot() == fixture['finalRows'] and digest(snapshot()) == fixture['finalStateDigest']


def ipqc_refresh_command(step):
    request, response = step['request'], step['response']
    if 'inputDigest' in request:
        request['inputDigest'] = digest({k: v for k, v in request.items() if k != 'inputDigest'})
        step['computedInputDigest'] = request['inputDigest']
        if 'inputDigest' in response:
            response['inputDigest'] = request['inputDigest']
    step['requestJcs'] = canonical(request).decode()
    if 'terminalDigest' in response:
        response['terminalDigest'] = digest(response['result'] if response.get('result') is not None else response['problem'])
    step['responseJcs'] = canonical(response).decode()
    step['computedResponseDigest'] = digest(response)
    for w in step['stateCut']['writes']:
        if w['table'] == 'Command':
            w['after'].update(InputDigest=step['computedInputDigest'], RequestCanonicalUtf8=step['requestJcs'],
                              TerminalBody=step['responseJcs'], TerminalBodyDigest=step['computedResponseDigest'])


def ipqc(output):
    p = OBJECTS / '76161b6b2ef5944c2d7a445edbe33546c056364f804f1fd10b851ad30ef6a79f.json'
    old = load(p); new = copy.deepcopy(old)
    for i in (0, 1, 6):
        ipqc_replay(copy.deepcopy(old['fixtures'][i]))
    check('IPQC-original-full-state-replay', True, 'F02/F03/F07 all original step/full-state digests reproduce')
    step = new['fixtures'][0]['steps'][2]
    claims = []
    for w in step['stateCut']['writes']:
        if w['table'] != 'Observation' or w['after']['Kind'] != 'INITIAL':
            continue
        r = {k: w['after'][k] for k in ('TenantId', 'EnvironmentId', 'Mode', 'TaskId', 'SlotId', 'ObservationId')}
        r['ClaimedAt'] = step['response']['recordedAt']
        key = '|'.join(r[k] for k in ('TenantId', 'EnvironmentId', 'Mode', 'TaskId', 'SlotId'))
        claims.append({'table': 'InitialClaim', 'key': key, 'operation': 'INSERT', 'before': None, 'after': r,
                       'rowDigest': digest(r), 'valueSource': 'Exact matching INITIAL Observation and original I09 terminal timestamp'})
    check('IPQC01-one-per-initial-slot', len(claims) == 100 and len({c['key'] for c in claims}) == 100)
    step['stateCut']['writes'][-2:-2] = claims
    # F03 fixed Owner body is OUT-QA-F03, stage/purpose continue to be TRIAL.
    f3 = new['fixtures'][1]
    assert f3['fixedOwnerReads'][0]['body']['sourceKey'] == 'OUT-QA-F03'
    for i in (1, 8, 10):
        s = f3['steps'][i]
        s['request'] = replace_tokens(s['request'], {'OUT-TRIAL-F03': 'OUT-QA-F03'})
        if i == 1:
            s['response']['result']['sourceRef']['id'] = 'OUT-QA-F03'
        if i == 8:
            payload = s['request']['handoff']; payload_jcs = canonical(payload).decode(); payload_hash = digest(payload)
            s['response']['result'].update(payloadBodyJcs=payload_jcs, payloadDigest=payload_hash)
            for w in s['stateCut']['writes']:
                if w['table'] == 'PlmEvidenceHandoff':
                    w['after'].update(BodyJson=payload_jcs, BodyDigest=payload_hash)
                elif w['table'] == 'DeliveryPayload':
                    w['after'].update(PayloadBody=payload_jcs, PayloadDigest=payload_hash)
                elif w['table'] == 'Delivery':
                    w['after']['PayloadDigest'] = payload_hash
        ipqc_refresh_command(s)
    s7 = new['fixtures'][6]['steps'][3]
    s7['response']['result'].update(canonicalKey='F07:OWNER-SEQUENCE', sequence='3')
    ipqc_refresh_command(s7)
    check('IPQC03-result-preimage', s7['response']['terminalDigest'] == '416b392ac2fa74673049a4a0c261215b385b352ddde7e66a087911ed644a48eb')
    check('IPQC03-complete-response-preimage', s7['computedResponseDigest'] == '582be7783096fa70a3bb97ddc220c487e7e50cef2632fecaab55de6ce8a4309e')
    for i in (0, 1, 6):
        ipqc_replay(new['fixtures'][i], mutate=True)
        ipqc_replay(copy.deepcopy(new['fixtures'][i]))
    check('IPQC01-final-claims', sum(r['table'] == 'InitialClaim' for r in new['fixtures'][0]['finalRows']) == 100 and len(new['fixtures'][0]['finalRows']) == 1067)
    check('IPQC02-complete-alias-removal', 'OUT-TRIAL-F03' not in canonical(new['fixtures'][1]).decode())
    f7 = new['fixtures'][6]
    delivery = next(r['row'] for r in f7['finalRows'] if r['table'] == 'Delivery' and r['row']['DeliveryId'] == s7['response']['result']['deliveryId'])
    check('IPQC03-immutable-delivery', delivery == next(r['row'] for r in old['fixtures'][6]['finalRows'] if r['table'] == 'Delivery' and r['row']['DeliveryId'] == delivery['DeliveryId']))
    check('IPQC03-old-response-rejected', old['fixtures'][6]['steps'][3]['response']['result']['canonicalKey'] != delivery['CanonicalKey'])
    write_product(output / 'IPQC-CONTINUOUS-OPERATIONS-R1.json', new, ['IPQC-CONT-01', 'IPQC-CONT-02', 'IPQC-CONT-03'], 'REVISED_INDEPENDENT_DEMO_FIXTURE_WORLD')
    RECIPES.append({'id': 'IPQC-CONTINUOUS-OPERATIONS-R1', 'source_path': p.as_posix(), 'source_sha256': sha(read(p)),
        'entrypoint': 'ipqc', 'findings': ['IPQC-CONT-01', 'IPQC-CONT-02', 'IPQC-CONT-03'],
        'state_digest': 'H(sorted(table,key) full [{table,key,row,rowDigest=H(row)}]); touched cut arrays are NOT the full preimage',
        'audit_digest': 'BeforeDigest=whole DB before step; AfterDigest=whole DB after business writes, before Command/Audit',
        'response_digest': 'terminalDigest=H(result) or H(problem); computedResponseDigest=H(complete response)',
        'claims_added': 100, 'final_F02_rows': 1067, 'changed_fixtures': ['F02', 'F03', 'F07']})
    return old, new


def evidence(owner, identity, body, version='1'):
    return {'ref': {'owner': owner, 'id': identity, 'version': version, 'digest': digest(body)}, 'body': body}


def replace_refs(value, pairs):
    if isinstance(value, dict):
        for before, after in pairs:
            if value == before:
                return copy.deepcopy(after)
        return {k: replace_refs(v, pairs) for k, v in value.items()}
    if isinstance(value, list):
        return [replace_refs(v, pairs) for v in value]
    return value


def stock_semantic(wire):
    source = wire['subjects'][0]['subject']['receiptSource']
    return {'schema': 'WMS-STOCK-RECEIPT-1', 'owner': wire['owner'], 'kind': 'QUALITY_DECISION',
        'wireBusinessTarget': {'namespace': 'QA-IQC', 'documentId': source['documentId'], 'lineId': source['lineId'], 'scopeKey': wire['decisionFamilyRef']['id']},
        'stream': wire['stream'], 'stateForm': wire['stateForm'], 'snapshotProofRef': wire['snapshotProofRef'], 'epochTransitionRef': None,
        'occurredAt': wire['occurredAt'], 'businessRef': wire['decisionRef'],
        'data': {k: wire[k] for k in ('decisionFamilyRef', 'replacesDecisionRef', 'subjects')}}


def iqc(output):
    p = OBJECTS / '263c8313c8b6c6f39c20f8b4e42784f17d548044227ffef41143bbcfb930d7ba.json'
    old = load(p); new = copy.deepcopy(old); e = new['evidence']
    md = original('709e1399a9e1b6c0'); md_text = read(md).decode('utf-8-sig')
    inline = []
    for number, match in enumerate(re.finditer(r'^```json\s*\n(.*?)^```\s*$', md_text, re.S | re.M)):
        try:
            body = json.loads(match[1])
        except ValueError:
            continue
        inline.append({'fence': number, 'source_line': md_text[:match.start()].count('\n') + 1, 'value': body})
    original_combined = {'fixture': old, 'inline': copy.deepcopy(inline)}
    digest_fields = {path: value for path, value in walk(original_combined) if isinstance(value, str) and
                     'digest' in path.rsplit('/', 1)[-1].lower() and path.rsplit('/', 1)[-1] not in ('digest', 'originalByteDigest')}
    wanted = set(digest_fields.values()); preimages = {}
    for path, value in walk(original_combined):
        if isinstance(value, (dict, list)):
            value_hash = digest(value)
            if value_hash in wanted:
                preimages.setdefault(value_hash, []).append(path)
    scalar_links = {path: preimages[value] for path, value in digest_fields.items() if value in preimages}
    stock_paths = []
    for path, value in walk(original_combined):
        if isinstance(value, dict) and {'semanticDigest', 'decisionRef', 'decisionFamilyRef', 'subjects', 'snapshotProofRef', 'messageId'} <= value.keys():
            check('IQC-original-stock-semantic:' + path, digest(stock_semantic(value)) == value['semanticDigest'])
            stock_paths.append(path)
    docs = {k: v for k, v in e.items() if 'ref' in v and 'body' in v}
    check('IQC-original-body-hashes', all(digest(v['body']) == v['ref']['digest'] for v in docs.values()))
    check('IQC-original-slot-order-hashes', all(digest(v['body']['slots']) == v['body']['obligationDigest'] for v in docs.values() if v['body'].get('schema') == 'IQC-OBSERVATION-SNAPSHOT-1'))
    measurements = [v for v in docs.values() if v['body'].get('schema') == 'IQC-MEASUREMENT-1']
    check('OPS046-total-measurement-versions', len(measurements) == 213)
    repaired = []
    for k, v in docs.items():
        b = v['body']
        if b.get('schema') == 'IQC-MEASUREMENT-1' and b['evaluationBasisRefs'][2] not in b['input']['evidenceRefs']:
            repaired.append(k); b['evaluationBasisRefs'][2] = copy.deepcopy(b['input']['evidenceRefs'][0])
    check('OPS046-exact-seeds', len(repaired) == 102 and set(repaired) == {'R1-RETEST-M1@1', 'R1-RETEST-M2@1'} | {f'LEAF-C-M{i}@1' for i in range(1, 41)} | {f'LEAF-D-M{i}@1' for i in range(41, 101)})

    # NEW approved DEMO version. The original POLICY1@1, PLAN1@1 and binding remain byte-for-byte facts.
    binding = copy.deepcopy(e['TECH-BIND1@1']['body'])
    binding.update(id='OPS-TECH-BIND2', revision='1', createdAt='2026-10-01T08:55:00Z')
    binding['scope']['quantityBand'].update(minimum='40', maximum='100')
    e['OPS-TECH-BIND2@1'] = evidence('MAIN_QUALITY', 'OPS-TECH-BIND2', binding)
    plan = copy.deepcopy(e['PLAN1@1']['body'])
    plan.update(version='2', previousRef=e['PLAN1@1']['ref'], technicalBindingRef=e['OPS-TECH-BIND2@1']['ref'], reason='NEW DEMO revision: FULL_COUNT over exact 40..100 inspection population; no production approval implied')
    e['PLAN1@2'] = evidence('MAIN_QUALITY', 'PLAN1', plan, '2')
    policy = copy.deepcopy(e['POLICY1@1']['body'])
    policy.update(version='2', previousRef=e['POLICY1@1']['ref'], technicalBindingRef=e['OPS-TECH-BIND2@1']['ref'], planRef=e['PLAN1@2']['ref'], reason='NEW DEMO approved revision; original 100-only version remains immutable')
    policy['scope']['quantityBand'].update(minimum='40', maximum='100')
    e['POLICY1@2'] = evidence('MAIN_QUALITY', 'POLICY1', policy, '2')
    pairs = [(e['POLICY1@1']['ref'], e['POLICY1@2']['ref']), (e['PLAN1@1']['ref'], e['PLAN1@2']['ref']), (e['TECH-BIND1@1']['ref'], e['OPS-TECH-BIND2@1']['ref'])]
    selected = []
    for k, v in list(e.items()):
        if k.startswith(('LEAF-C-', 'LEAF-D-', 'E05-', 'R2-D-', 'R2-LEAF-D2-')) and 'body' in v:
            # Only explicit policy/plan/binding axes are migrated. Original InspectionSource stays frozen.
            if v['body'].get('schema') == 'IQC-INSPECTION-SOURCE-1':
                continue
            b = v['body']; changed = False
            for field in ('policyVersionRef', 'policyRef', 'canonicalPolicyVersionRef', 'planRef', 'technicalBindingRef'):
                if field in b:
                    replaced = replace_refs(b[field], pairs)
                    changed |= replaced != b[field]; b[field] = replaced
            if changed:
                selected.append(k)
                if b.get('schema') == 'QAP-RESOLUTION-1':
                    b.update(catalogRevision='2', policyControlRevision='2', planControlRevision='2')
                    old_family = b['familyContextRef']
                    family = next(vv['body'] for vv in e.values() if vv.get('ref') == old_family)
                    family = copy.deepcopy(family)
                    family.update(id='OPS-R1-' + old_family['id'], canonicalPolicyVersionRef=e['POLICY1@2']['ref'],
                                  priorPolicyContextRef=e['POLICY1@1']['ref'], priorTaskFamilyRefs=[old_family], relationship='REASSESS_REQUIRED')
                    ek = family['id'] + '@1'; e[ek] = evidence('MAIN_QUALITY', family['id'], family)
                    b['familyContextRef'] = e[ek]['ref']
    for k, v in e.items():
        if k.startswith(('LEAF-C-', 'LEAF-D-', 'E05-', 'R2-D-', 'R2-LEAF-D2-', 'R4-', 'R5-')):
            for _, node in walk(v.get('body', {})):
                if isinstance(node, dict) and 'catalog' in node and 'writerEpoch' in node:
                    node['catalog'] = '2'

    author = {'owner': 'DEMO_IDENTITY', 'id': 'OPS-POLICY-AUTHOR', 'version': '1'}
    approver = {'owner': 'DEMO_IDENTITY', 'id': 'OPS-POLICY-APPROVER', 'version': '1'}
    validity = policy['scope']['validity']
    role = evidence('DEMO_IDENTITY', 'OPS-QUALITY-ROLE', {'schema': 'QAP-REQ-REFERENCE-1', 'kind': 'ROLE', 'subject': approver,
        'label': 'DEMO policy reviewer, plan approver and activator', 'siteIds': ['SITE-A'], 'validity': validity,
        'current': True, 'mode': 'DEMO', 'originalRecordRefs': [], 'authorityRef': approver,
        'meaning': 'Synthetic fixture identity only; never sufficient as REAL role or approval'})
    approval_docs = [role]
    for action, subject in [('PLAN_APPROVE', e['PLAN1@2']), ('POLICY_REVIEW', e['POLICY1@2']), ('POLICY_ACTIVATE', e['POLICY1@2'])]:
        auth = evidence('MAIN_QUALITY', 'OPS-AUTH-' + action, {'schema': 'QAP-AUTHORITY-1', 'actorRef': approver,
            'action': action, 'siteIds': ['SITE-A'], 'stages': ['IQC'], 'roleRef': role['ref'], 'delegationRef': None,
            'separation': 'DIFFERENT_FROM_AUTHOR', 'validity': validity, 'controlRevision': '1', 'current': True, 'mode': 'DEMO'})
        approval_docs.append(auth)
        if action != 'POLICY_ACTIVATE':
            approval_docs.append(evidence('MAIN_QUALITY', 'OPS-APPROVAL-' + action, {'schema': 'QAP-APPROVAL-1',
                'subjectRef': subject['ref'], 'subjectDigest': subject['ref']['digest'], 'authorityRef': auth['ref'],
                'approverRef': approver, 'authorRef': author, 'decision': 'APPROVE',
                'reason': 'DEMO only: exact 40..100 population FULL_COUNT revision', 'recordedAt': '2026-10-01T08:57:00Z'}))
    activation = evidence('MAIN_QUALITY', 'OPS-DEMO-ACTIVATION', {'schema': 'OPS-QAP-DEMO-ACTIVATION-1', 'mode': 'DEMO',
        'policyRef': e['POLICY1@2']['ref'], 'planRef': e['PLAN1@2']['ref'], 'technicalBindingRef': e['OPS-TECH-BIND2@1']['ref'],
        'approvalRefs': [d['ref'] for d in approval_docs if d['body']['schema'] == 'QAP-APPROVAL-1'],
        'activationAuthorityRef': approval_docs[-1]['ref'], 'expected': {'catalog': '1', 'policyControl': '1', 'planControl': '1'},
        'result': {'catalog': '2', 'policyControl': '2', 'planControl': '2', 'policy': 'ACTIVE', 'plan': 'FROZEN'},
        'recordedAt': '2026-10-01T08:58:00Z', 'sameUowRequired': True, 'actualExecution': 'NOT_RUN'})
    for d in approval_docs + [activation]:
        e[d['ref']['id'] + '@' + d['ref']['version']] = d

    e['MEASUREMENT-BATCH-REQUEST']['input'] = copy.deepcopy(new['complete_measurement_batch'])
    submit = e['RESULT-SUBMIT-REQUEST']
    submit['input']['observationSnapshotRef'] = submit.pop('observationSnapshotRef')
    del submit['input']['measurementRefs']
    submit['expectedResponse'] = {'ref': e['SUBMISSION1@1']['ref'], **copy.deepcopy(e['SUBMISSION1@1']['body'])}

    replacements = [(old['evidence'][key]['input'], e[key]['input']) for key in ('MEASUREMENT-BATCH-REQUEST', 'RESULT-SUBMIT-REQUEST')]
    replacements += [(old['evidence']['RESULT-SUBMIT-REQUEST']['expectedResponse'], submit['expectedResponse'])]
    combined = replace_refs({'fixture': new, 'inline': inline}, replacements)
    new = combined['fixture']

    def refresh_inline(value, current):
        if isinstance(value, dict):
            ref = value.get('ref')
            if 'body' in value and isinstance(ref, dict) and {'owner', 'id', 'version'} <= ref.keys():
                key = (ref['owner'], ref['id'], ref['version'])
                if key in current:
                    return {**value, **copy.deepcopy(current[key])}
            return {k: refresh_inline(v, current) for k, v in value.items()}
        if isinstance(value, list):
            return [refresh_inline(v, current) for v in value]
        return value

    # Fixed-point propagation changes full ref digests AND digest-only bindings. A cycle is an error.
    # Snapshot slots retain their source natural-key order (proved above); obligation digest has its own domain.
    history = {}
    for iteration in range(100):
        mapping = {}
        current_docs = {(v['ref']['owner'], v['ref']['id'], v['ref']['version']): v for v in new['evidence'].values() if 'ref' in v and 'body' in v}
        combined['inline'] = refresh_inline(combined['inline'], current_docs)
        for inline_item in combined['inline'][:12]:
            number = inline_item['fence']
            for axis in ('request', 'response'):
                inline_item['value'][axis] = copy.deepcopy(new['examples'][number][axis])
        for path, source_paths in scalar_links.items():
            try:
                before = pointer(combined, path)
                candidates = [digest(pointer(combined, sp)) for sp in source_paths]
            except (KeyError, IndexError, TypeError):
                continue
            # Equal old preimages are canonical mirrors; divergence is never solved by choosing a hash silently.
            if len(set(candidates)) != 1:
                # Canonical EvidenceDocument body is the authority when a source inline mirror is one propagation round behind.
                preferred = next((sp for sp in source_paths if sp.startswith('/fixture/evidence/') and sp.endswith('/body')), source_paths[0])
                after = digest(pointer(combined, preferred))
            else:
                after = candidates[0]
            if before != after:
                mapping[before] = after; setp(combined, path, after)
        for path in stock_paths:
            wire = pointer(combined, path); after = digest(stock_semantic(wire))
            if wire['semanticDigest'] != after:
                mapping[wire['semanticDigest']] = after; wire['semanticDigest'] = after
        for k, v in new['evidence'].items():
            if 'ref' not in v or 'body' not in v:
                continue
            b = v['body']
            if 'draftDigest' in b and 'input' in b:
                after = digest(b['input'])
                if b['draftDigest'] != after:
                    mapping[b['draftDigest']] = after; b['draftDigest'] = after
                validation_ref = b.get('validationRef')
                if validation_ref:
                    validation = new['evidence'][validation_ref['id'] + '@' + validation_ref['version']]['body']
                    if validation['inputDigest'] != after:
                        mapping[validation['inputDigest']] = after; validation['inputDigest'] = after
            if b.get('schema') == 'IQC-OBSERVATION-SNAPSHOT-1':
                current = digest(b['slots'])
                if current != b['obligationDigest']:
                    mapping[b['obligationDigest']] = current; b['obligationDigest'] = current
            current = digest(b)
            if current != v['ref']['digest']:
                mapping[v['ref']['digest']] = current; v['ref']['digest'] = current
        if not mapping:
            break
        history.update(mapping); combined = replace_tokens(combined, mapping); new = combined['fixture']
    else:
        raise AssertionError('IQC digest dependency cycle or non-convergence')
    e = new['evidence']
    check('IQC-all-packet-input-digests', all(v['body']['draftDigest'] == digest(v['body']['input']) for v in e.values() if 'draftDigest' in v.get('body', {})))
    check('IQC-all-inline-stock-semantic', all(pointer(combined, path)['semanticDigest'] == digest(stock_semantic(pointer(combined, path))) for path in stock_paths))
    for key in ('LEAF-C-RESOLUTION@1', 'LEAF-D-RESOLUTION@1', 'E05-RESOLUTION@1', 'R2-D-RESOLUTION@1'):
        resolution = e[key]['body']; family = e[resolution['familyContextRef']['id'] + '@1']['body']
        check('OPS045-current-context:' + key, family['canonicalPolicyVersionRef'] == resolution['policyVersionRef'] and
              family['relationship'] == 'REASSESS_REQUIRED' and len(family['priorTaskFamilyRefs']) == 1 and
              not family['createsTask'] and not family['replacesDecisionFamily'])
    check('IQC-revised-all-body-hashes', all(digest(v['body']) == v['ref']['digest'] for v in e.values() if 'body' in v and 'ref' in v))
    check('IQC-revised-all-slot-digests', all(digest(v['body']['slots']) == v['body']['obligationDigest'] for v in e.values() if v.get('body', {}).get('schema') == 'IQC-OBSERVATION-SNAPSHOT-1'))
    check('OPS045-original-policy-preserved', all(e[k] == old['evidence'][k] for k in ('POLICY1@1', 'PLAN1@1', 'TECH-BIND1@1')))
    check('OPS045-band-positives', all(40 <= int(e[k]['body']['quantity']) <= 100 and e[k]['body']['policyVersionRef'] == e['POLICY1@2']['ref'] for k in ('LEAF-C-RESOLUTION@1', 'LEAF-D-RESOLUTION@1', 'E05-RESOLUTION@1', 'R2-D-RESOLUTION@1')))
    check('OPS045-old-policy-leaf-negative', all(int(old['evidence'][k]['body']['quantity']) != 100 for k in ('LEAF-C-RESOLUTION@1', 'LEAF-D-RESOLUTION@1', 'E05-RESOLUTION@1', 'R2-D-RESOLUTION@1')))
    check('OPS045-approval-sod', author != approver and len(activation['body']['approvalRefs']) == 2)
    check('OPS046-provenance-positive', all(v['body']['evaluationBasisRefs'][2] in v['body']['input']['evidenceRefs'] for v in e.values() if v.get('body', {}).get('schema') == 'IQC-MEASUREMENT-1'))
    check('OPS046-old-provenance-negative', old['evidence']['LEAF-D-M91@1']['body']['evaluationBasisRefs'][2] not in old['evidence']['LEAF-D-M91@1']['body']['input']['evidenceRefs'])
    check('OPS047-batch-positive', all('slotRef' in item and 'expectedSlotHeadRevision' in item for item in e['MEASUREMENT-BATCH-REQUEST']['input']['items']))
    check('OPS047-batch-negative', any('slotRef' not in item for item in old['evidence']['MEASUREMENT-BATCH-REQUEST']['input']['items']))
    check('OPS047-submit-closed-shape', set(e['RESULT-SUBMIT-REQUEST']['input']) == {'expected', 'observationSnapshotRef', 'declaredRanges', 'reason'})
    check('OPS047-response-complete', all(k in e['RESULT-SUBMIT-REQUEST']['expectedResponse'] for k in ('observationSnapshotRef', 'obligationDigest', 'slotEntries')))
    changed = [k for k in old['evidence'] if old['evidence'][k] != e[k]]
    changed_refs = [{'key': k, 'before': old['evidence'][k]['ref'], 'after': e[k]['ref']} for k in changed if 'ref' in e[k]]
    identities = {(v['ref']['owner'], v['ref']['id'], v['ref']['version']): v['ref']['digest'] for v in e.values() if 'ref' in v and 'body' in v}
    stale = []
    for path, v in walk(new):
        if isinstance(v, dict) and {'owner', 'id', 'version', 'digest'} <= v.keys():
            identity = (v['owner'], v['id'], v['version'])
            if identity in identities and v['digest'] != identities[identity]:
                stale.append(path)
    check('IQC-all-known-reference-tuples', not stale, stale[:20])
    stale_inline = []
    for path, v in walk(combined['inline']):
        if isinstance(v, dict) and {'owner', 'id', 'version', 'digest'} <= v.keys():
            identity = (v['owner'], v['id'], v['version'])
            if identity in identities and v['digest'] != identities[identity]:
                stale_inline.append(path)
    check('IQC-inline-known-reference-tuples', not stale_inline, stale_inline[:20])
    new['artifact_scope'] = 'STATIC_EXPECTED_ONLY_INDEPENDENT_OPERATIONS_R1_BRANCHES_NO_BUSINESS_EXECUTION'
    bundle_raw = write_product(output / 'IQC-CONTINUOUS-OPERATIONS-R1.json', new, ['OPS-IF-045', 'OPS-IF-046', 'OPS-IF-047'], 'REVISED_INDEPENDENT_DEMO_FIXTURE_WORLD')
    inline_product = {'schema': 'OPS-IQC-NORMATIVE-INLINE-REVISION-1', 'source_path': md.as_posix(), 'source_sha256': sha(read(md)),
        'fixture_sha256': sha(bundle_raw), 'business_execution': 'NOT_RUN', 'inline_examples': combined['inline']}
    write_product(output / 'IQC-INLINE-OPERATIONS-R1.json', inline_product, ['OPS-IF-045', 'OPS-IF-046', 'OPS-IF-047'], 'REVISED_DEMO_INLINE_EXAMPLES_WITH_FROZEN_GR_STOCK_PAYLOADS')
    save(OUT / 'iqc-dependency-cascade.json', {'revision': REVISION, 'seed_measurements': repaired, 'policy_migration_direct_documents': selected,
        'changed_evidence_documents': len(changed_refs), 'fixed_point_iterations': iteration, 'changed_refs': changed_refs,
        'new_evidence': [v for k, v in e.items() if k not in old['evidence']], 'preserved_original_policy': old['evidence']['POLICY1@1'],
        'activation_profile': 'OPS-QAP-DEMO-ACTIVATION-1 is an explicit DEMO fixture sidecar, not a new native QAP wire or actual activation receipt',
        'branch_scope': 'POLICY1@2 activation/control is the NEW DEMO cut for the four selected branches; original POLICY1@1 examples remain the prior independent baseline, never asserted to be simultaneously current after activation',
        'scalar_digest_preimages': scalar_links, 'stock_semantic_paths': stock_paths,
        'packet_input_rule': 'Native complete DraftInput is the preimage of packet.draftDigest and validation.inputDigest; old stale digests are recorded in source and are not preserved as valid checksums',
        'runtime_status': 'NOT_RUN', 'all_exact_reference_tuples_checked': True})
    RECIPES.append({'id': 'IQC-CONTINUOUS-OPERATIONS-R1', 'source_path': p.as_posix(), 'source_sha256': sha(read(p)), 'entrypoint': 'iqc',
        'additional_source_path': md.as_posix(), 'additional_source_sha256': sha(read(md)),
        'products': ['IQC-CONTINUOUS-OPERATIONS-R1.json', 'IQC-INLINE-OPERATIONS-R1.json'],
        'findings': ['OPS-IF-045', 'OPS-IF-046', 'OPS-IF-047'], 'body_hash': 'SHA256(UTF8 compact sorted-key JSON(body)); ref excluded',
        'obligation_hash': 'H(full slots array in original natural-key order); propagate to every dependent submission/review/decision',
        'cascade': 'Repeat body, full-slot, exact scalar-preimage and native Stock semantic hashes over bundle plus21 parsed normative JSON fences until fixed point; reject cycles/stale known ref tuples; packet/validation hash complete DraftInput',
        'measurements_total_versions': 213, 'measurement_provenance_repairs': 102, 'changed_ref_count': len(changed_refs),
        'scope': 'Independent revised DEMO world; no Owner ledger replay or production adoption; immutable source retained'})
    return old, new


def rma():
    import jsonschema
    cp = original('fbac66e3f15e7da1'); contract = load(cp)
    p = original('2159cd0850d66cf1'); old = load(p); new = copy.deepcopy(old)
    valid = lambda name, body: jsonschema.Draft202012Validator({'$ref': '#/$defs/' + name, '$defs': contract['$defs']}, format_checker=jsonschema.FormatChecker()).is_valid(body)
    source = new['examples'][10]['closedQueries']['h30Sealed']; query = copy.deepcopy(source['query'])
    environment = {'tenantId': 'CP6-DEMO', 'environmentId': 'RMA-OPERATIONS-R1', 'mode': 'DEMO'}
    original_command = {'endpoint': 'H30/RETURN_VALUE_SOURCE', 'requestKey': 'b1000000-0000-4000-8000-000000000049', 'writerEpoch': 1}
    scope = {**environment, 'consumer': query['consumer'], 'stableEffectKey': query['stableEffectKey'],
             'physicalReturnReceiptRef': query['physicalReturnReceiptRef'], 'purpose': 'RETURN_VALUE_SOURCE', 'ranges': [{'from': '0', 'to': '5'}], 'baseUom': {'code': 'EA', 'version': '1'}}
    authority = evidence('DEMO_IDENTITY', 'OPS-H30-SEAL-AUTHORITY', {'schema': 'OPS-H30-SEAL-AUTHORITY-1', 'mode': 'DEMO',
        'actorId': 'RMA-SEAL-OPERATOR', 'action': 'H30_EXCLUSIVE_SEAL', 'scope': scope,
        'validFrom': '2026-10-02T03:00:00Z', 'validTo': '2026-10-02T04:00:00Z', 'securityStamp': '1', 'grantVersion': '1'})
    scope_document = evidence('WMS-RMA-01', 'OPS-H30-SCOPE-49', {'schema': 'OPS-H30-EXCLUSIVE-SCOPE-1', 'scope': scope, 'mode': 'DEMO'})
    guard = evidence('WMS-RMA-01', 'OPS-H30-GUARD-49', {'schema': 'OPS-H30-DEMO-RANGE-GUARD-1', 'scope': scope,
        'physicalReceiptRef': query['physicalReturnReceiptRef'], 'coverage': 'COMPLETE', 'mode': 'DEMO'})
    producer_head = evidence('WMS-RMA-01', 'OPS-H30-PRODUCER-HEAD-49', {'schema': 'OPS-H30-DEMO-PRODUCER-HEAD-1',
        'physicalReceiptRef': query['physicalReturnReceiptRef'], 'revision': '1', 'mode': 'DEMO'})
    apply_request = {'commandId': original_command['requestKey'], 'consumer': query['consumer'], 'purpose': scope['purpose'],
        'physicalReturnReceiptRef': query['physicalReturnReceiptRef'], 'physicalReturnResultDigest': query['physicalReturnReceiptRef']['digest'],
        'rangeGuardRef': guard['ref'], 'ranges': scope['ranges'], 'quantity': {'value': '5', 'uom': scope['baseUom']},
        'qualityOwnerResultRef': None, 'commercialOwnerResultRef': None, 'stableEffectKey': query['stableEffectKey'],
        'expectedProducerHeadRef': producer_head['ref'], 'expectedConsumerClaimHeadRef': None}
    binding = evidence('WMS-RMA-01', 'OPS-H30-BIND-49', {'commandId': original_command['requestKey'],
        'scope': {'environmentId': environment['environmentId'], 'siteId': 'b1000000-0000-4000-8000-000000000001',
            'warehouseId': 'b1000000-0000-4000-8000-000000000002', 'mode': 'DEMO', 'scopeRef': scope_document['ref']},
        'principalRef': authority['ref'], 'method': 'POST', 'routeTemplate': '/internal/wms/rma/v1/h30/consumer-claims',
        'actorCapability': 'wms.rma.h30.consume', 'businessOperation': 'RMA-H30C1', 'businessTargetRef': query['physicalReturnReceiptRef'],
        'sourceVersions': [query['physicalReturnReceiptRef'], guard['ref']], 'bodyDigest': digest(apply_request),
        'effectKeys': [query['stableEffectKey']], 'expectedHeadRefs': [producer_head['ref']], 'createdAt': '2026-10-02T03:00:00Z'})
    check('OPS049-native-binding', valid('CommandBinding', binding['body']))
    check('OPS049-native-original-apply-request', valid('H30ConsumerApplyRequest', apply_request))
    proof = evidence('WMS-RMA-01', 'OPS-H30-PERMANENT-SEAL-49', {'schema': 'OPS-H30-EXCLUSIVE-SEAL-1',
        'bindingRef': binding['ref'], 'query': query, 'scope': scope, 'authorityRef': authority['ref'], 'originalApplyRequest': apply_request,
        'bindingBodyDigest': binding['body']['bodyDigest'], 'originalCommand': original_command,
        'stateBefore': {'claim': None, 'ack': None, 'consumerEffect': None, 'completeCoverage': True},
        'stateAfter': {'newClaimsPermanentlyFenced': True, 'lateApplyPermanentlyFenced': True, 'existingActiveClaimReleased': False},
        'uniqueFenceKey': digest(scope), 'sameUow': {'lock': 'exact environment+consumer+stableEffectKey+physicalReturnReceipt identity',
        'writes': ['durable unique fence', 'exclusive seal proof', 'original binding terminal receipt'], 'allOrNothing': True},
        'recordedAt': '2026-10-02T03:02:00Z', 'mode': 'DEMO'})
    receipt = copy.deepcopy(source['terminalReceipt'])
    receipt.update(receiptId='b1000000-0000-4000-8000-000000000050', commandBindingRef=binding['ref'],
                   effectKeys=[query['stableEffectKey']], noAdditionalEffectProofRef=proof['ref'])
    source['terminalReceipt'] = receipt
    new['schemaCases']['positive'][18]['payload'] = copy.deepcopy(source)
    check('OPS049-native-H30-shape', valid('H30ConsumerQueryResult', source))

    def semantic_h30(result):
        r = result['terminalReceipt']; b = binding['body']; s = proof['body']
        return (result['query'] == s['query'] and r['commandBindingRef'] == binding['ref']
                and r['noAdditionalEffectProofRef'] == proof['ref'] and s['bindingRef'] == binding['ref']
                and r['effectKeys'] == b['effectKeys'] == [result['query']['stableEffectKey']] and s['scope'] == scope_document['body']['scope']
                and b['bodyDigest'] == digest(s['originalApplyRequest']) and b['businessTargetRef'] == result['query']['physicalReturnReceiptRef']
                and s['stateBefore'] == {'claim': None, 'ack': None, 'consumerEffect': None, 'completeCoverage': True}
                and s['stateAfter']['newClaimsPermanentlyFenced'] and s['stateAfter']['lateApplyPermanentlyFenced'])
    check('OPS049-semantic-positive', semantic_h30(source))
    check('OPS049-cross-effect-negative', not semantic_h30(old['examples'][10]['closedQueries']['h30Sealed']))
    tampered = copy.deepcopy(source); tampered['query']['physicalReturnReceiptRef']['id'] = 'PHYS-OTHER'
    check('OPS049-other-receipt-negative', not semantic_h30(tampered))
    save(OUT / 'rma-exclusive-h30-evidence.json', {'revision': REVISION, 'role': 'NEW_SYNTHETIC_DEMO_ONLY',
        'native_contract_source_sha256': sha(read(cp)), 'documents': [authority, scope_document, guard, producer_head, binding, proof], 'positive': source,
        'native_apply_request': apply_request, 'native_binding_schema': 'CommandBinding',
        'seal_boundary': 'This fixture specifies an authorized durable internal cancellation of the original H30C1 binding; it does not add a public seal endpoint or claim existing runtime support. Exact quality/valuation authority is still required before any apply; absence seal creates no application.',
        'negative_wrong_effect': old['examples'][10]['closedQueries']['h30Sealed'],
        'recovery_rules': ['Exact original query replays the immutable receipt', 'Any scope/body/authority mismatch rejects before writes',
        'NOT_OBSERVED/UNAVAILABLE/UNKNOWN never imply absence', 'An existing active claim or ack/effect rejects this absence-seal transaction; it requires its own Owner resolution',
        'Serially lock and permanently fence this exact consumer key in the same Uow as proof and receipt; racing claim or late apply loses against the permanent fence',
        'Other consumers, effects and physical receipts retain their own claims; this proof never releases their ranges'], 'runtime_status': 'NOT_RUN'})

    snapshot = new['examples'][0]['wireApplicationLifecycle']['draftRead']['requestedLines'][0]['originalSnapshot']
    line_body = {'schema': 'OPS-WMS-OUT-LINE-EVIDENCE-1', 'mode': 'DEMO', 'shipmentRef': snapshot['shipmentRef'],
        'shipmentLineId': snapshot['shipmentLineId'], 'shipmentLineVersion': snapshot['shipmentLineVersion'], 'originalSnapshot': snapshot,
        'identityAuthority': 'New synthetic exact WMS-OUT line evidence for this independent fixture world; no actual Owner read claimed'}
    line = evidence('WMS-OUT-01', snapshot['shipmentLineId'], line_body, snapshot['shipmentLineVersion'])
    version = evidence('WMS-OUT-01', 'OPS-SHIP-100-L1-V1', {'schema': 'OPS-WMS-OUT-LINE-VERSION-1', 'mode': 'DEMO',
        'lineRef': line['ref'], 'sourceHeaderRef': snapshot['shipmentRef'], 'shipmentLineVersion': snapshot['shipmentLineVersion'],
        'eligibleRanges': snapshot['sourceShareRanges'], 'baseQty': snapshot['baseQty']})
    cut = evidence('WMS-OUT-01', 'OPS-SHIP-100-L1-CUT-1', {'schema': 'OPS-WMS-OUT-LINE-CUT-1', 'mode': 'DEMO',
        'lineRef': line['ref'], 'sourceVersionRef': version['ref'], 'complete': True, 'eligibleRanges': snapshot['sourceShareRanges'],
        'invoiceLineRef': snapshot['invoiceLineRef'], 'scheduleRef': snapshot['scheduleRef'], 'observedAt': '2026-10-02T00:00:00Z'})
    claims = [claim for tx in new['examples'][2]['claimTransactions'] for claim in tx['afterClaims']] + [new['schemaCases']['positive'][3]['payload']]
    for claim in claims:
        claim['coordinate'].update(shipmentLineRef=line['ref'], sourceCutRef=cut['ref'], sourceVersionRef=version['ref'])
        claim['claimDigest'] = digest({k: v for k, v in claim.items() if k != 'claimDigest'})
        check('OPS050-claim-native-schema:' + claim['claimId'], valid('ReturnRangeClaim', claim))
    check('OPS050-four-occurrences', len(claims) == 4)
    check('OPS050-distinct-line-hash', line['ref']['digest'] != snapshot['shipmentRef']['digest'])
    def coordinate_key(coordinate):
        ref_key = lambda ref: (ref['owner'], ref['id']) if ref is not None else ('NONE',)
        return (coordinate['sourceOwner'], ref_key(coordinate['shipmentLineRef']), ref_key(coordinate['invoiceLineRef']),
                ref_key(coordinate['scheduleRef']), coordinate['baseUom']['code'], coordinate['baseUom']['version'])
    c1 = claims[0]['coordinate']; c2 = copy.deepcopy(c1); c2['shipmentLineRef'] = {**line['ref'], 'id': 'SHIP-100-L2'}
    check('OPS050-same-sku-other-line-distinct', coordinate_key(c1) != coordinate_key(c2))
    c3 = copy.deepcopy(c1); c3['invoiceLineRef'] = {**c1['invoiceLineRef'], 'id': 'INV-100-L2'}
    check('OPS050-other-invoice-distinct', coordinate_key(c1) != coordinate_key(c3))
    save(OUT / 'rma-line-identity-evidence.json', {'revision': REVISION, 'role': 'NEW_SYNTHETIC_DEMO_ONLY', 'documents': [line, version, cut],
        'claim_digest_profile': 'OPS-RETURN-CLAIM-DIGEST-1: H(complete ReturnRangeClaim excluding only claimDigest); not silently substituted for an old unspecified digest domain',
        'coordinate_key_fields': ['sourceOwner', 'shipmentLineRef.owner/id', 'invoiceLineRef.owner/id or NONE', 'scheduleRef.owner/id or NONE', 'baseUom.code/version'],
        'positive_claims': claims, 'negative_header_as_line': old['schemaCases']['positive'][3]['payload'],
        'remaining_opaque_refs': 'Original invoice/schedule/authorization/etc shape-only DEMO references retain their original placeholders; this asset proves exact line identity and coordinate separation, not production source authority',
        'runtime_status': 'NOT_RUN'})

    new['examples'][8]['expected']['disposition'] = 'PENDING'
    new['examples'][8]['expected']['qualification'] = 'REQUIRED_FENCED'
    work = copy.deepcopy(new['examples'][10]['closedQueries']['effectSealed']['work'])
    work.update(workId='b1000000-0000-4000-8000-000000000048', state='BLOCKED', revision=1,
                checkpoints=[{'name': 'EFFECT_OWNER_QUALIFICATION', 'ordinal': 1, 'state': 'BLOCKED', 'owner': 'WMS-RMA-01',
                'queryLocator': 'OPS-RMA-QUALIFICATION-48', 'lastConfirmedRef': None, 'attemptCount': 1,
                'lastAttemptAt': '2026-10-02T03:00:00Z', 'reasonCodes': ['DISPOSITION_ADAPTER_REQUIRED_FENCED']}])
    qualification = evidence('WMS-RMA-01', 'OPS-RMA-QUALIFICATION-48', {'schema': 'OPS-RMA-QUALIFICATION-1', 'mode': 'DEMO',
        'effectOwner': None, 'ownerContractRef': None, 'state': 'REQUIRED_FENCED', 'stockCallMade': False,
        'canInferNoEffect': False, 'recordedAt': '2026-10-02T03:00:00Z'})
    ref = lambda name: evidence('WMS-RMA-01', name, {'schema': 'OPS-RMA-SHAPE-REFERENCE-1', 'id': name, 'mode': 'DEMO'})['ref']
    slices = []
    for i, (kind, projection) in enumerate((k, v) for k, v in new['examples'][8]['dispositionTargetProjections'].items() if isinstance(v, dict) and 'adapterId' in v):
        slices.append({'sliceId': f'b1000000-0000-4000-8000-{48 + i:012}', 'rmaId': work['rmaId'], 'proposalRef': ref('OPS-PROPOSAL-48'),
            'selectionRef': ref('OPS-SELECTION-' + kind), 'sourceReceiptRef': query['physicalReturnReceiptRef'],
            'qualityDecisionRef': ref('OPS-QUALITY-48'), 'quality': 'ACCEPTED' if kind == 'RESALE_STOCK' else 'REJECTED',
            'ranges': [{'from': str(i), 'to': str(i + 1)}], 'quantity': {'value': '1', 'uom': {'code': 'EA', 'version': '1'}},
            'disposition': kind, 'adapterId': projection['adapterId'], 'destinationRef': None, 'repairRef': None,
            'supplierReturnRef': None, 'customerReturnRef': None, 'effectKey': 'OPS-RMA-48|' + kind, 'state': 'REQUIRED_FENCED',
            'ownerWorkRef': None, 'ownerResultRef': None, 'stockReceiptRef': None, 'applicationReceiptRef': None, 'previousSliceRef': None})
    result = {'state': 'REQUIRED_FENCED', 'work': work, 'slices': slices, 'qualificationObservationRef': qualification['ref'], 'recordedAt': '2026-10-02T03:00:00Z'}
    check('OPS048-native-apply-result', valid('RmaDispositionApplyResult', result))
    check('OPS048-native-head', valid('DispositionState', 'PENDING'))
    check('OPS048-illegal-head-negative', not valid('DispositionState', 'REQUIRED_FENCED'))
    bad_work = copy.deepcopy(work); bad_work['state'] = 'REQUIRED_FENCED'
    check('OPS048-illegal-work-negative', not valid('Work', bad_work))
    save(OUT / 'rma-qualification-projection.json', {'revision': REVISION, 'schema': 'OPS-RMA-DISPOSITION-DETAIL-PROJECTION-1',
        'qualification': qualification, 'detailProjection': {'qualification': 'REQUIRED_FENCED', 'dispositionState': 'PENDING', 'work': work, 'slices': slices},
        'nativeApplyResult': result, 'partialRule': 'If exact previously confirmed slices exist, retain their APPLIED facts and aggregate PARTIAL; new fenced slices stay REQUIRED_FENCED and current Work/checkpoint BLOCKED. Never demote prior receipts.',
        'noEffectsRule': 'PENDING aggregate, BLOCKED Work and checkpoint, REQUIRED_FENCED qualification and slices; preserve occupied claims; no Owner/Stock send',
        'uiRule': 'Render qualification reason separately from DispositionState; BLOCKED is not terminal effect failure or success',
        'contractRule': 'Original closed enums unchanged; the detail projection is a new named read projection, native D05 returns the existing RmaDispositionApplyResult', 'runtime_status': 'NOT_RUN'})
    jsonpatch(p, ['OPS-IF-048', 'OPS-IF-049', 'OPS-IF-050'], 'Versioned independent DEMO examples with exact state projection, exclusive H30 seal, and independently hashed shipment line.', old, new)
    ap = original('561c763d68c1f87a'); ao = load(ap); an = copy.deepcopy(ao)
    old_text = 'validate the local qualification registration and atomically retain slice/head and Work as REQUIRED_FENCED with audit; no APPLIED transition'
    new_text = 'validate local qualification; atomically retain qualification and slices as REQUIRED_FENCED, disposition head PENDING (PARTIAL only for already confirmed slices), Work/checkpoint BLOCKED, occupied claims and audit; no new APPLIED transition or Owner/Stock call'
    operation = next(v for v in an['operations'] if v['operationId'] == 'RMA-D05'); assert operation['transactionDecision'] == old_text
    operation['transactionDecision'] = new_text
    jsonpatch(ap, ['OPS-IF-048'], 'Explicit existing-enum D05 projection; original enums are not expanded.', ao, an)
    mp = original('dc0ca1dd23d08f92'); text = read(mp).decode('utf-8-sig')
    patch(mp, ['OPS-IF-048'], 'Propagate exact D05 state mapping and matching example expectation to narrative.', [
        {'op': 'replace_text', 'before': old_text, 'after': new_text, 'expected_count': 1},
        {'op': 'replace_text', 'before': '"disposition": "REQUIRED_FENCED"', 'after': '"disposition": "PENDING", "qualification": "REQUIRED_FENCED"', 'expected_count': 1}])
    return old, new


def mirrors(ipqc_old, ipqc_new, rma_old, rma_new):
    s = ipqc_new['fixtures'][1]['steps'][8]
    for prefix, before, after, ids in [
        ('99bf7b7371715a62', ipqc_old['fixtures'][1]['steps'][8]['request'], s['request'], ['IPQC-CONT-02']),
        ('c603f220fd83336d', ipqc_old['fixtures'][1]['steps'][8]['response'], s['response'], ['IPQC-CONT-02']),
        ('420ebe12bc2aec95', ipqc_old['fixtures'][1]['steps'][8]['stateCut']['writes'], s['stateCut']['writes'], ['IPQC-CONT-02']),
        ('eba9b5010c3181a3', ipqc_old['fixtures'][6]['steps'][3], ipqc_new['fixtures'][6]['steps'][3], ['IPQC-CONT-03'])]:
        p = original(prefix); actual = load(p); check('mirror-original:' + prefix, actual == before)
        jsonpatch(p, ids, 'Exact mirror of revised continuous relational fixture; includes canonical bytes and dependent digests.', actual, after)
    p = original('9a3c4c7f7927e1bd'); old = load(p); new = copy.deepcopy(old)
    a = new['actions'][17]['actualEvidence']; a['request'] = s['request']; a['response'] = s['response']
    # Replace any embedded exact step/digest mirrors, preserving presentation metadata.
    mapping = {}
    for path, v in walk(ipqc_old['fixtures'][1]['steps'][8]):
        if isinstance(v, str):
            try:
                after = pointer(s, path)
                if isinstance(after, str) and v != after:
                    mapping[v] = after
            except (KeyError, IndexError, TypeError):
                pass
    new = replace_tokens(new, mapping)
    jsonpatch(p, ['IPQC-CONT-02'], 'Frontend actualEvidence request/response and exact digest mirrors follow F03 I29.', old, new)
    p = original('27edb910bb416a4f'); old = load(p); new = copy.deepcopy(old)
    publication = new['publications'][3]; prior = publication['payloadDigest']; payload_hash = s['response']['result']['payloadDigest']
    publication['body'] = s['request']['handoff']; publication['frozenPayload']['payloadBody'] = s['request']['handoff']
    publication = replace_tokens(publication, {prior: payload_hash, 'OUT-TRIAL-F03': 'OUT-QA-F03'})
    new['publications'][3] = publication
    jsonpatch(p, ['IPQC-CONT-02'], 'Publication body/frozen payload/sourceRefs and every payload digest follow exact F03 I29 body.', old, new)
    check('IPQC-publication-body-hash', digest(publication['body']) == publication['payloadDigest'] == publication['frozenPayload']['payloadDigest'])

    # The second RMA copy has the same current semantic paths; do not substitute whole documents across revisions.
    p = original('38a6c24482eb54fc'); old = load(p); new = copy.deepcopy(old)
    for op in diff(rma_old, rma_new):
        try:
            before = pointer(old, op['path'])
        except (KeyError, IndexError):
            continue
        if op['op'] == 'replace_json' and before == op['before']:
            setp(new, op['path'], op['after'])
    # The added qualification is an explicit new field of the example expectation, not a native contract extension.
    if new['examples'][8].get('expected', {}).get('disposition') == 'PENDING':
        new['examples'][8]['expected']['qualification'] = 'REQUIRED_FENCED'
    if new != old:
        jsonpatch(p, ['OPS-IF-048', 'OPS-IF-049', 'OPS-IF-050'], 'Same exact preimage repairs on duplicate RMA examples; unrelated differences retained.', old, new)

    # Split markdown is a byte-fragment presentation of the authoritative JSON; bound its exact scalar copies.
    fp = original('72ba69312a04f2bb'); fo = load(fp); fn = json.loads(DERIVED[fp.as_posix()])
    leaf_ops = diff(fo, fn)
    for prefix in ('2bf87130cde5b1fe', 'a044573d078160c1', '67dcc3fb406be18f', '5e0b3a55dcdf6cd2'):
        p = original(prefix); text = read(p).decode('utf-8-sig'); ops = []; seen = set()
        for op in leaf_ops:
            before, after = op.get('before'), op.get('after')
            if not isinstance(before, str) or before in seen:
                continue
            # Hashes/canonical JSON strings and the incorrect correction effect identity are globally unambiguous in these parts.
            safe = bool(re.fullmatch('[0-9a-f]{64}', before)) or before.startswith(('urn:sha256:', '{')) or before == '2738c8a7-bfa6-5931-8dcf-69f06f749d87'
            if not safe:
                continue
            a, b = json.dumps(before, ensure_ascii=False), json.dumps(after, ensure_ascii=False)
            count = text.count(a)
            if count:
                ops.append({'op': 'replace_text', 'before': a, 'after': b, 'expected_count': count}); text = text.replace(a, b); seen.add(before)
        before = '"reverseOfConsumptionId": "CONS-RCPT-20-ORIGINAL"'
        if before in text:
            ops.append({'op': 'replace_text', 'before': before, 'after': '"reverseOfConsumptionId": null', 'expected_count': text.count(before)})
        if prefix == '67dcc3fb406be18f':
            before = '"ObjectId": "OWNERFACT-AP-SC20"'
            assert text.count(before) == 1
            ops.append({'op': 'replace_text', 'before': before, 'after': '"ObjectId": "OWNERFACT-AP-SC20-RETURN"', 'expected_count': 1})
        if ops:
            patch(p, ['OPS-IF-039', 'OPS-IF-040', 'OPS-IF-041'], 'Exact split Markdown digest/identity mirrors; machine JSON remains sole executable fixture entry.', ops)


DECISIONS = {
    'OPS-IF-038': 'New FIN-INV schema identity: page state fields are native state string|null, never Money or a Work object. Consumer pins revised schema and keeps missing-state reason.',
    'OPS-IF-039': 'SC12 C01 persisted binding equals H(original binding); C09 repeats exact C03 receipt/binding/result and regenerates canonical response bytes/hash.',
    'OPS-IF-040': 'SC20 consumption carries exact correction effect id, null reverseOfConsumptionId because no original consumption exists; preserve original receipt/layer lineage and original result digest domain.',
    'OPS-IF-041': 'SC20 OwnerFactSnapshot PK wrapper equals its actual Row ObjectId OWNERFACT-AP-SC20-RETURN; no business-row identity rewrite.',
    'OPS-IF-042': 'MAT R4 section11 HTTP400 INVALID_FIELD is normative; update the single stale 422 vector description without claiming an executed HTTP result.',
    'OPS-IF-043': 'MAT MC-RCAP02 prose uses first-location40→36 and exact two b310 Package members; schema/quantity/Stock effects remain unchanged.',
    'OPS-IF-044': 'MES-MAT original-locator receipt query repeats the operationId in the committed Stock receipt and movement; never allocates another operation.',
    'OPS-IF-045': 'Create explicitly NEW DEMO POLICY1@2, PLAN1@2, technical binding and separated-author approval/activation fixture for exact40..100 population. Preserve POLICY1@1 100-only and frozen original source. Four resolutions migrate with REASSESS_REQUIRED context and digest closure. No actual policy approval/adoption claimed.',
    'OPS-IF-046': 'Repair102 input/evaluation scan mismatches among213 measurement versions; recompute entire evidence-reference closure plus slot obligation digests and downstream content approvals.',
    'OPS-IF-047': 'Use complete100-item MeasurementBatch slot/head fields; ResultSubmit takes observationSnapshotRef in input, not measurementRefs; return complete native ResultSubmission with slotEntries/obligationDigest.',
    'OPS-IF-048': 'Select original closed enums: qualification/slices REQUIRED_FENCED; aggregate PENDING (PARTIAL for existing confirmed facts); Work/checkpoint BLOCKED. New named detail projection and native D05 typed result, no enum expansion/Stock call.',
    'OPS-IF-049': 'New exclusive H30 binding/proof/receipt binds exact FIN-INV effect, physical receipt, environment, purpose and ranges; atomic permanent fence prevents new claims/late apply. Other consumer claims cannot be released.',
    'OPS-IF-050': 'Independent hashed DEMO shipment-line evidence, version and source cut establish SHIP-100-L1, separate from SHIP-100 header; four claims regenerated and same-SKU different-line/invoice keys stay distinct.',
    'IPQC-CONT-01': 'Add100 native InitialClaim rows for I09 INITIAL observations using exact tenant/environment/mode/task/slot PK and original recordedAt; replay F02 full DB snapshots/Audit/terminal ledgers through1067 final rows.',
    'IPQC-CONT-02': 'Use exact fixed Owner source OUT-QA-F03 for I05 and both I29 calls; retain TRIAL stage/purpose and negative case; regenerate payload/request/response/Command/Audit/full-state digests and every current mirror.',
    'IPQC-CONT-03': 'F07 resend response uses immutable existing Delivery canonicalKey F07:OWNER-SEQUENCE and sequence3; payload/message/delivery/lease9 unchanged; regenerate terminal/Command/Audit/full-state digests and full-step mirror.'}


ACCEPTANCE_CASES = {
    'OPS-IF-038': {
        'POS': ('Decode native state page fields',
                'Revised OutboundValueRow.valueExitState="APPLIED" from ValueEffect.State; ReverseExceptionRow.inventoryValueState is the exact native Work.state string, or null with the missing-source reason.',
                'The revised schema accepts the native string|null and the page displays the independent state axis without converting it to Money or a Work object.',
                'Read projection only; no ValueEffect, posting, inventory or command write.', 'Return the existing page response; do not create a business receipt.'),
        'NEG': ('Reject Money in a state field',
                'Supply {"amount":"0.00","currency":"CNY","scale":2} as valueExitState or inventoryValueState, as admitted by the old erroneous Money|null definition.',
                'Closed revised field validation rejects the object; neither zero money nor null may be used to manufacture an applied state.',
                'No business write or effect; invalid data cannot be persisted through the page adapter.', 'No success receipt or successful state projection.')},
    'OPS-IF-039': {
        'POS': ('SC12 original command receipt replay',
                'Replay SC12 C01 and C03, then issue C09 for the original C03 binding. C01 binding digest is 7b87f069191c40e447eb327b3924e830c6a7998bd4a8b0e29dee5a3a9b261be2; C03 binding digest is 395846b388973070162965e2656c7e746a95ac091e9e582ef81da0e715f7d570.',
                'C01 persisted BindingDigest equals H(Request.binding). C09 returns exactly C03 receipt/result/binding, with regenerated canonical response bytes and hash.',
                'Only the original command effect may exist; C09/retry adds no ValueEffect, LayerConsumption or posting.', 'C09 receipt is structurally identical to C03 receipt, including its original resultDigest.'),
        'NEG': ('Reject the stale SC12 binding copies',
                'Use C01 persisted 43ab6ea8b88ba0e7554fc1abe1a9b532899a22f4b88a62fb178169bb7d3d06f7 or C09 receipt binding 66cbde781045fb3575af79f1f80ec57f53d396fe4cde700032abcce6ecd246ab against the original requests.',
                'Binding/preimage validation rejects the mismatched persisted or returned evidence; C09 cannot claim an exact replay merely because the command names match.',
                'No replacement effect, second consumption or re-posting is allowed to repair the mismatch.', 'Do not accept the stale receipt as the original C03 terminal receipt.')},
    'OPS-IF-040': {
        'POS': ('SC20 exact correction consumption lineage',
                'Use SC20 effect cb852aba-05ba-563b-afc0-32d44630941e and the original receipt layer; InitialPersistedRows contains no original LayerConsumption.',
                'Nested consumption and final ReverseLayerConsumption use that correction effectId and reverseOfConsumptionId=null. Result hash is 817fd246bf816181cd08f194fe58b14c08283ffe822a885cae119d82e3d34d03 under the original exclusion of resultDigest/adoptionMode.',
                'Persist only the correction consumption and its declared identity/outbox/audit rows; preserve original receipt, value-line and CorrectionLink lineage.', 'Original-command retries repeat the correction receipt and digest without another withdrawal.'),
        'NEG': ('Reject an invented reverse consumption',
                'Set nested/final effectId=2738c8a7-bfa6-5931-8dcf-69f06f749d87 or reverseOfConsumptionId="CONS-RCPT-20-ORIGINAL" while no such original consumption exists.',
                'Reject the lineage/digest inconsistency before applying the correction; an original receipt is not an original consumption that can be reversed.',
                'No compensating consumption, fabricated predecessor or duplicate inventory-value effect.', 'No correction success receipt for the mismatched identity; old digest cannot validate the changed result.')},
    'OPS-IF-041': {
        'POS': ('SC20 row image and wrapper address the same fact',
                'Use /Scenarios/SC20/Steps/1/PersistedRows/2 with PrimaryKey.ObjectId and Row.ObjectId both OWNERFACT-AP-SC20-RETURN, retaining the original ScopeDigest/ObjectType.',
                'The persistence adapter identifies exactly the return OwnerFactSnapshot row; wrapper and actual primary-key column values agree.',
                'Write only that declared return fact row in the original Uow; unrelated OwnerFact rows remain unchanged.', 'Preserve the original command/receipt identity; this repair does not allocate a new command.'),
        'NEG': ('Reject a wrapper targeting another OwnerFact',
                'Keep Row.ObjectId=OWNERFACT-AP-SC20-RETURN but restore PrimaryKey.ObjectId=OWNERFACT-AP-SC20.',
                'Reject the fixture/persistence image as an inconsistent row address instead of silently overwriting or aliasing the earlier OwnerFact.',
                'Neither OwnerFact row is changed by the rejected write; no correction effect is applied from this invalid image.', 'No successful persistence/command receipt may cite the mismatched row image.')},
    'OPS-IF-042': {
        'POS': ('Accept the closed MAT wire shape',
                'Decode the valid R4 vector using its declared fields and native numeric/string types, with no additional root member.',
                'Closed input validation succeeds and dispatches only the original MAT operation; the error-code correction does not alter its quantity semantics.',
                'Decoding itself writes nothing; any subsequent operation uses only its original declared write set.', 'Preserve the original operation response; do not invent an HTTP/business success from a schema-only check.'),
        'NEG': ('Unknown root field is HTTP400',
                'Replay vectors[1], adding the unknown root member identified by its original JSON Pointer.',
                'The R4 endpoint returns HTTP400 INVALID_FIELD with that JSON Pointer, not the stale 422 description.',
                'Reject before business writes, resource claims or Stock calls.', 'Return the validation problem; no accepted command or effect receipt.')},
    'OPS-IF-043': {
        'POS': ('MC-RCAP02 exact first-location capacity and members',
                'After the same SHIP, first-location capacity changes 40→36; predicate reads enlist Package b3100000-0000-4000-8000-000000001300 and b3100000-0000-4000-8000-000000002300.',
                'Capacity and predicate evidence use those exact facts. The capacity observation does not reduce outstanding unreceived quantity.',
                'Enlist both concrete Package rows; retain the original Stock quantities, movement and outstanding amount.', 'The existing SHIP receipt remains authoritative; no new receipt from a capacity-only observation.'),
        'NEG': ('Reject incomplete predicate coverage',
                'Use the old 100→90 narrative and claim predicate coverage from only Package b3000000-0000-4000-8000-000000001300.',
                'Reject the claimed complete read/predicate set because neither the exact location values nor the two concrete members match the fixture.',
                'No outstanding-quantity reduction, additional SHIP or replacement Package write may be inferred.', 'Do not emit a new Stock receipt or mark the original SHIP undone.')},
    'OPS-IF-044': {
        'POS': ('MES-MAT original locator returns its committed operation',
                'Query the original MES-MAT Stock locator whose committed receipt and movement carry operationId=72305281-7790-5c74-a80c-dc3da2e6f0d5.',
                'The query returns that exact operationId and the original receipt/movement identities and semantic digest.',
                'Read-only lookup; no new Stock command, material movement or semantic-key allocation.', 'Return the original committed receipt; retry uses the same locator.'),
        'NEG': ('Reject a receipt projection with another operationId',
                'Return operationId=a1c16cf6-9a0d-576e-9653-f74581f5f917 for that same original locator and otherwise unchanged receipt/movement.',
                'Reject the contradictory projection; retain unresolved/query-required status until the original receipt identity is read consistently.',
                'Do not create a second operation or movement to make the erroneous projection appear true.', 'The mismatched projection cannot be accepted as proof of the committed Stock result.')},
    'OPS-IF-045': {
        'POS': ('Four exact populations use the new approved DEMO cut',
                'Resolve LEAF-C=40, LEAF-D=60, E05=80 and R2-D=50 using POLICY1@2, PLAN1@2, OPS-TECH-BIND2, the two exact-content DEMO approvals and the distinct author/approver activation at catalog/control2.',
                'Every population is within40..100 and uses its exact source scope. New family contexts carry REASSESS_REQUIRED and preserve prior families; POLICY1@1 remains100-only and immutable.',
                'Within the isolated DEMO branch write only the version2 content/control/resolution evidence and its declared descendants; do not rewrite the original source or create a physical Stock/GR effect merely from policy approval.', 'Return the exact new resolution/approval evidence; no REAL policy adoption or quality acceptance receipt is inferred.'),
        'NEG': ('Reject a leaf authorized only by the old100-only policy',
                'Resolve the40-unit LEAF-C population against POLICY1@1 quantityBand100..100, or present the new DEMO approval as REAL authority.',
                'Reject the population-scope/authority mismatch. The mother100 population cannot stand in for the leaf40, and DEMO authority cannot satisfy REAL approval.',
                'No policy activation, resolved quality decision, GR acceptance or Stock effect; retain the original policy/source records.', 'No successful resolution/final-guard/acceptance receipt for the rejected scope.')},
    'OPS-IF-046': {
        'POS': ('Repaired observation provenance reaches every dependent digest',
                'Load all213 measurement versions with the102 repaired R1-RETEST/LEAF-C/LEAF-D entries; each evaluationBasisRefs[2] is an exact member of its input.evidenceRefs.',
                'Measurement bodies, complete ordered SlotEntry obligation hashes, submission/review/draft/approval/decision refs, GR frozen payloads and native Stock semantic hashes all recompute from the revised preimages.',
                'Persist the declared revised evidence once in the isolated fixture world; retain original archived observations and do not duplicate a measurement or physical effect.', 'Approval/decision receipts reference the complete revised digest chain, with exact-command replay returning the same result.'),
        'NEG': ('Reject LEAF-D-M91 scan substitution',
                'Restore LEAF-D-M91@1 evaluationBasisRefs[2] to old SCAN-091 while input.evidenceRefs retains the corrected L1 scan, or keep an old downstream obligation/approval/payload digest.',
                'Reject provenance or digest closure before accepting the PASS observation/result review; a valid digest of a different scan does not establish the declared measurement basis.',
                'No new accepted submission/approval/quality decision or downstream application; existing evidence remains immutable.', 'No PASS/decision receipt may cite the mismatched scan or a mixed old/new digest chain.')},
    'OPS-IF-047': {
        'POS': ('Native batch slots and snapshot-based submission',
                'Send complete_measurement_batch with100 items, each containing exact slotRef and expectedSlotHeadRevision; submit {expected,observationSnapshotRef,declaredRanges,reason} for the resulting frozen snapshot.',
                'Validate every slot/head CAS; ResultSubmission returns observationSnapshotRef, obligationDigest and complete slotEntries as well as the native measurement/result fields.',
                'Write only the valid original batch observation/claim/head rows and the single snapshot-bound submission in their declared Uows; duplicate command replay writes no extra observation or submission.', 'Return the original native batch/submission receipts and exact frozen snapshot identity.'),
        'NEG': ('Reject missing slot fields and obsolete ResultSubmit input',
                'Omit slotRef or expectedSlotHeadRevision from an item, or send measurementRefs with observationSnapshotRef outside ResultSubmit.input as in the old request.',
                'Reject closed-shape/CAS validation before business execution; measurementRefs cannot replace the required complete snapshot binding.',
                'No Observation, InitialClaim, SlotHead or ResultSubmission write from the invalid request; no partial accepted batch.', 'Return validation/CAS rejection, never a successful batch or submission receipt.')},
    'OPS-IF-048': {
        'POS': ('Missing Owner qualification maps to legal independent states',
                'Apply the five current disposition branches with effectOwner/ownerContractRef absent; use native D05 RmaDispositionApplyResult and the new detail projection.',
                'Qualification and new slices remain REQUIRED_FENCED; aggregate is PENDING, Work/checkpoint BLOCKED. If separately confirmed historical slices exist, preserve their receipts and aggregate PARTIAL without promoting any fenced slice.',
                'Atomically record only local qualification observation, fenced slices/head projection, blocked Work/checkpoint and audit; preserve occupied claims. Owner/Stock call count is zero.', 'D05 returns the original local REQUIRED_FENCED observation and typed blocked Work, with all new Owner/Stock/application receipt refs null.'),
        'NEG': ('Reject illegal head/Work state or invented application',
                'Set DispositionState or Work.state to REQUIRED_FENCED, or mark a current fenced slice APPLIED while Owner/Stock/application receipt refs are null.',
                'Reject the closed-enum or applied-evidence invariant; do not expand the old enums or infer an Owner result from qualification absence.',
                'No APPLIED transition, claim release, Owner/Stock call, locator allocation or quantity effect.', 'No Stock/application success receipt; retain the original blocked qualification observation.')},
    'OPS-IF-049': {
        'POS': ('Exclusive H30 seal binds one consumer effect and original command',
                'Query FIN-INV-01 / FININV|RMA-1|0-5 / PHYS-1 with the new native OPS-H30-BIND-49, exact H30C1 bodyDigest, scope0..5 and OPS-H30-PERMANENT-SEAL-49; complete locked state shows no claim, ack or consumer effect.',
                'Binding/proof/receipt agree on environment, consumer, effect, physical receipt, purpose and ranges. Same-Uow permanent fence wins against a later new claim or late apply; exact replay returns the same sealed result.',
                'Atomically write only this key’s durable unique fence, exclusive proof and terminal receipt. Do not release an existing active claim or any other consumer/effect/receipt range.', 'Return SEALED_NO_EFFECT with claim=null, ack=null, effectKeys=[FININV|RMA-1|0-5] and the exact binding/proof refs.'),
        'NEG': ('Reject another effect’s seal proof and an already-active claim',
                'For that H30 query supply the old terminalReceipt.effectKeys=[RMA-1|DSP-1|EFFECT], or change physical receipt to PHYS-OTHER; also reject absence sealing if the locked state contains an active claim/ack/effect.',
                'Fail exact query/binding/proof or absence validation. NOT_OBSERVED, missing contract and a disposition seal cannot prove this FIN-INV effect absent.',
                'No no-effect fence/receipt committed from the invalid proof, no claim release, no consumer apply and no other key affected; an existing claim requires its own Owner resolution.', 'Do not return canInferNoEffect=true or accept the old disposition receipt as an H30 no-effect receipt.')},
    'OPS-IF-050': {
        'POS': ('Four return claims use independently evidenced shipment-line identity',
                'Use the independent SHIP-100-L1/version1 body, OPS-SHIP-100-L1-V1 and OPS-SHIP-100-L1-CUT-1 in all four revised claim locations; compare a same-product SHIP-100-L2 or INV-100-L2 coordinate.',
                'Line body hash differs from the SHIP-100 header digest. Each claimDigest recomputes from the complete claim excluding only claimDigest; different line/invoice identities remain distinct coordinate keys.',
                'Reserve only each exact line/invoice/schedule/UOM range with its original quantity. Do not merge the two lines’ claims or create a physical Stock movement.', 'Return the corresponding exact claim evidence; no header-only reference is accepted as line authority.'),
        'NEG': ('Reject header-as-line and header digest reused for a line',
                'Use shipmentLineRef.id=SHIP-100 with the header digest, or change only id to SHIP-100-L1 while retaining the header’s a*64 digest instead of the independent line-body digest.',
                'Reject missing exact line evidence/hash binding before allocating a ReturnRangeClaim; matching SKU or shipment header does not establish line identity.',
                'No claim insertion, occupied-range merge, authorization change or Stock effect; existing claims on both lines remain unchanged.', 'No successful claim/return-authorization receipt for the invalid coordinate.')},
    'IPQC-CONT-01': {
        'POS': ('I09 persists one permanent INITIAL claim per slot',
                'Run F02 step2 I09 with100 INITIAL observations. For each, use TenantId|EnvironmentId|Mode|TaskId|SlotId as InitialClaim key and the original terminal recordedAt as ClaimedAt.',
                'Exactly100 claims match their ObservationId and slot. The step has311 writes (309 business plus Command/Audit2), and F02 final state has1067 rows with complete replayable state/Audit digests.',
                'Commit the100 InitialClaim rows with the matching observations/heads and terminal ledgers in the original Uow; same-command replay creates no extra claims or observations.', 'Return the original I09 terminal result; permanent claims survive response loss and replay.'),
        'NEG': ('Reject missing permanent claims and a competing INITIAL observation',
                'Use the old F02 write set with100 INITIAL observations but zero InitialClaim rows, or race a different command/ObservationId for the same E+TaskId+SlotId after the first INITIAL commit.',
                'The old write set fails the native persistence invariant; the competing INITIAL must lose the permanent unique claim and cannot be treated as a second first observation.',
                'One winning claim/observation per slot only; loser writes no observation/head/claim effect, and no terminal success can be committed without the required claim row.', 'Replay returns the original winner receipt; the competing command returns a rejection rather than another INITIAL success.')},
    'IPQC-CONT-02': {
        'POS': ('I05/I29 use the exact fixed Owner source',
                'Use sourceRef MES-OUTPUT/OUT-QA-F03/version1/digest8aacdd7c63db9733b495f2ca45e50785b7ba513c80fbad7db83950cf03323fc4 in I05 and both I29 requests, while retaining TRIAL stage/purpose and the original I29 negative branch.',
                'Source identity matches fixedOwnerReads with no alias inference. Payload, request, response, Command, Audit and full-state digests reproduce; publication/frontend/request/response/writes mirrors agree.',
                'Successful original I29 writes its one PlmEvidenceHandoff, DeliveryPayload/Delivery and terminal ledgers; the original negative I29 branch performs no handoff/application effect.', 'Successful replay returns the original handoff/delivery result with the corrected payload digest; the negative branch remains a rejection.'),
        'NEG': ('Equal digest does not authorize OUT-TRIAL-F03 alias',
                'Send OUT-TRIAL-F03 with the same8aacdd... digest while fixedOwnerReads resolves OUT-QA-F03 and originalSourceAliases is empty.',
                'Reject the Owner/id/version identity mismatch; TRIAL purpose and equal body bytes cannot create an authoritative source alias.',
                'No PlmEvidenceHandoff, DeliveryPayload, Delivery or consumer application for the rejected source; preserve any already committed original handoff.', 'Return identity/dependency rejection, not a successful I05/I29 receipt for an invented alias.')},
    'IPQC-CONT-03': {
        'POS': ('I26 resend preserves the immutable Delivery identity',
                'Replay F07 step3 I26 for delivery22a15a95-a62a-557b-9970-8adaeb4e9e02, messageb692c0d4-a8f5-57e2-a612-8ea1b2087562 and the existing payload/lease9.',
                'FrozenDelivery returns canonicalKey=F07:OWNER-SEQUENCE and wire sequence="3" corresponding to SQL Sequence3. Terminal digest is416b392ac2fa74673049a4a0c261215b385b352ddde7e66a087911ed644a48eb and complete response digest is582be7783096fa70a3bb97ddc220c487e7e50cef2632fecaab55de6ce8a4309e.',
                'Only the declared recovery/Command/Audit writes occur; no Delivery identity, message, payload or sequence rewrite and no duplicate consumer effect.', 'Return the original immutable delivery identity; acknowledgement/application observations remain separate from resend acceptance.'),
        'NEG': ('Reject a response inventing another canonical delivery sequence',
                'For the same persisted delivery/message/payload and no Delivery write, return canonicalKey=F07:I26 and sequence="4" as in the old response.',
                'Reject the FrozenDelivery projection as inconsistent with immutable persisted identity; an attempt is not a newly allocated business delivery sequence.',
                'Do not insert/update Delivery, allocate sequence4, regenerate payload or repeat the business application to justify the response.', 'The contradictory resend response cannot serve as an application success or as evidence of a new delivery.')}
}


def acceptance_cases(key):
    return [{'id': key + '-' + polarity, 'polarity': polarity, 'scenario': values[0], 'input': values[1],
             'expected': values[2], 'expected_write_set_and_effects': values[3], 'expected_receipt': values[4],
             'verification_level': 'REQUIRED_RUNTIME', 'result': 'NOT_RUN'}
            for polarity, values in ACCEPTANCE_CASES[key].items()]


def documentation(workpart_only=False):
    selected = set(DECISIONS); originals = {}
    for name in ('operations-integration-findings.json', 'root-integration-findings.json'):
        source = ROOT / 'evidence' / name
        obj = json.loads(source.read_text(encoding='utf-8-sig'))
        for f in obj.get('findings', []):
            key = f.get('finding_id', f.get('id'))
            if key in selected:
                originals[key] = f
    assert originals.keys() == selected, (originals.keys(), selected)
    findings = []
    for key, decision in DECISIONS.items():
        f = originals[key]; evidence_rows = []
        for item in f['evidence']:
            path = Path(item.get('source_path', item.get('path'))); raw = read(path)
            evidence_rows.append({'source_path': path.as_posix(), 'sha256': sha(raw), 'line': item['line'],
                                  'end_line': item.get('end_line', item['line']), 'reading': item.get('claim', item.get('reading', 'Original finding evidence'))})
        outputs = ['remediation/assets/operations-fixtures/manifest.json', 'remediation/assets/operations-fixtures/bounded-patches.json',
                   'remediation/assets/operations-fixtures/recipes.json', 'remediation/assets/operations-fixtures/static-validation.json',
                   'remediation/assets/operations-fixtures/independent-validation.json', 'remediation/assets/operations-fixtures/consumer-index.json',
                   'remediation/assets/operations-fixtures/DECISIONS.md', 'tools/build_operations_fixture_patches.py']
        findings.append({'issue_id': 'DR-' + key, 'source_finding_ids': [key], 'batch': 'W5',
            'classification': 'DESIGN_GAP' if key == 'OPS-IF-048' else 'ARTIFACT_CONFLICT',
            'classification_basis': 'Original D05 assigns a value absent from aggregate/Work enums; the original contract lacks a legal cross-axis mapping, now explicitly selected without enum expansion.' if key == 'OPS-IF-048' else 'Existing normative state, identity, exact-source, quantity-scope or persistence contract contradicts the current fixture/schema/text asset; new revision repairs the artifact rather than claiming a runtime bug.',
            'targets': f.get('targets', ['QA-IPQC-01']), 'source_version': 'Exact original SHA plus CP6-DESIGN-REVISION-20261011/R1', 'decision': decision,
            'preserved_constraints': ['Original source bytes remain immutable', 'Independent revised DEMO fixtures do not prove actual Owner authority or runtime adoption',
                'Native fields/enums, complete effect identity and original digest domains retained; new domains have explicit names',
                'No business implementation, build, Actions, deployment or external send'],
            'consumer_adoption': {'required': ['Pin this manifest/source SHA/derived SHA; consume revised asset or recipe output as a unit',
                'Reject old/new mixed hashes, stale ref versions, wrong JSON preimages and unreproducible recipe output',
                'Run required Owner/business acceptance before promoting runtime status'], 'runtime_status': 'UNPROVEN', 'design_acceptance_is_runtime_adoption': False},
            'evidence': evidence_rows, 'outputs': outputs,
            'acceptance': acceptance_cases(key),
            'design_status': 'DESIGN_RESOLVED', 'actual_adoption_status': 'UNPROVEN', 'implementation_status': 'NOT_IMPLEMENTED_BY_THIS_DESIGN_TASK',
            'verification_status': 'STATIC_DESIGN_CHECKS_ONLY_RUNTIME_NOT_RUN'})
    save(ROOT / 'remediation/workparts/operations-fixtures.json', {'revision': REVISION, 'findings': findings})
    if workpart_only:
        return
    text = '# W5 operations fixture revision\n\nThese assets resolve the 16 named design findings. All source archives stay immutable. Small documents use SHA-pinned expected-value patches; large IQC/IPQC bundles use the deterministic generator and output manifests. The generated bundles form independent revised DEMO fixture worlds, never replays into an existing immutable Owner ledger.\n\n'
    text += '\n'.join('- **' + k + '** — ' + v for k, v in DECISIONS.items()) + '\n\n'
    text += 'Use Python3.13 with jsonschema4.26.0, isolated from project dependencies. Reproducible command: `uv run --no-project --python 3.13 --with jsonschema==4.26.0 python docs/design-handbook/tools/build_operations_fixture_patches.py`; then run `uv run --no-project --python 3.13 python docs/design-handbook/tools/validate_operations_fixtures.py`. The default materialization directory is `D:/CP6-archives/design-revision-20261011-checks/operations`. Source files are only read. The generator pins their SHA values, rejects changed preimages, records deterministic output SHA/bytes and performs positive/negative local data checks. No source scripts are executed.\n\n'
    text += 'Digest domains: compact Unicode UTF-8 JSON, sorted object keys and original array order; no implicit decimal parsing. IQC hashes full EvidenceDocument.body, and separately the complete ordered SlotEntry array. IPQC row hashes cover row only; whole-state hashes cover sorted table/key descriptors including rowDigest. Audit.AfterDigest is the full business state before terminal Command/Audit writes. Command terminal hashes and response-content hashes remain distinct. FIN-INV SC20 preserves its original exclusion of resultDigest and adoptionMode; historical FixtureDigest is retained as historical metadata with a separately named full-scenario digest.\n\n'
    text += 'OPS045 is a new DEMO approval fixture, not retroactive expansion of POLICY1@1 and not evidence of a real approver. OPS049 proof authority/fence documents and OPS050 line documents are explicitly new DEMO profiles; original opaque upstream references are not upgraded to real Owner evidence. For REAL use, read exact Owner records and authority, enforce same-Uow uniqueness/CAS, permanent fence and complete coverage, then independently execute runtime acceptance.\n\n'
    text += 'Consumer selection is atomic: the manifest selects derived machine JSON and the listed current mirrors. Earlier R2/R3 continuous bundles, old PASS reports, split-markdown presentation fragments and review-window excerpts are historical evidence and cannot remain an executable fixture entry alongside revised machine JSON. Do not replace historical hash domains or turn a stale PASS report into a new run. All business acceptance and runtime adoption remain NOT_RUN / UNPROVEN.\n'
    (OUT / 'DECISIONS.md').write_text(text, encoding='utf-8')


def consumer_index():
    historical = []
    for prefix, reason in [('3ad6645097bc3509', 'Exact historical review windows; revised parsed machine inline examples are IQC-INLINE-OPERATIONS-R1.json'),
                           ('48abc6', 'Prior IPQC R2 fixture revision, not a current consumer entry'),
                           ('3bc85fd485a91ddb', 'Prior IPQC R3 fixture revision, not a current consumer entry')]:
        candidates = list((ORIGINAL / prefix[:2]).glob(prefix + '*'))
        if len(candidates) == 1:
            p = candidates[0]; raw = read(p)
            historical.append({'source_path': p.as_posix(), 'sha256': sha(raw), 'classification': 'SUPERSEDED', 'consumer_status': 'HISTORICAL_READ_ONLY', 'reason': reason})
    save(OUT / 'consumer-index.json', {'revision': REVISION, 'small_current_revisions': [
        {'source_path': p['source_path'], 'source_sha256': p['source_sha256'], 'derived_sha256': p['expected_derived_sha256'],
         'entry': 'bounded-patches.json', 'consumer_status': 'CURRENT_REVISED_MACHINE' if p['source_path'].endswith('.json') else 'PRESENTATION_ONLY_MACHINE_JSON_IS_AUTHORITY'} for p in PATCHES],
        'large_current_revisions': [{'recipe': r['id'], 'source_path': r['source_path'], 'source_sha256': r['source_sha256'],
                                    'consumer_status': 'CURRENT_REVISED_DEMO_MACHINE', 'manifest': 'manifest.json'} for r in RECIPES],
        'historical': historical, 'rule': 'An old exact-review report or PASS result is not regenerated or reused as new execution. Original type/normative prose stays evidence; obsolete literal examples are selected only through this revision manifest.',
        'runtime_adoption': 'UNPROVEN'})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--materialize', type=Path, default=Path('D:/CP6-archives/design-revision-20261011-checks/operations'))
    args = parser.parse_args(); output = args.materialize.resolve()
    assert not output.is_relative_to(ORIGINAL.resolve()) and not output.is_relative_to(OBJECTS.resolve()), 'Read-only source boundary'
    OUT.mkdir(parents=True, exist_ok=True)
    finance_and_small()
    ip_old, ip_new = ipqc(output)
    iqc(output)
    rm_old, rm_new = rma()
    mirrors(ip_old, ip_new, rm_old, rm_new)
    documentation()
    consumer_index()
    for item in PATCHES:
        raw = DERIVED[item['source_path']]
        target = output / ('derived-' + Path(item['source_path']).name)
        target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(raw)
        PRODUCTS.append({'path': target.as_posix(), 'sha256': sha(raw), 'bytes': len(raw), 'source_finding_ids': item['source_finding_ids'],
                         'role': 'DERIVED_SHA_PINNED_BOUNDED_PATCH', 'business_execution': 'NOT_RUN', 'runtime_adoption': 'UNPROVEN'})
    save(OUT / 'bounded-patches.json', {'revision': REVISION, 'role': 'DATA_ONLY_EXPECTED_VALUE_PATCHES', 'patches': PATCHES})
    save(OUT / 'recipes.json', {'revision': REVISION, 'generator': 'tools/build_operations_fixture_patches.py',
        'generator_sha256': sha(Path(__file__).read_bytes()), 'canonical_profile': 'CP6-COMPACT-SORTED-UTF8-JSON/1', 'recipes': RECIPES})
    save(OUT / 'static-validation.json', {'revision': REVISION, 'checks': CHECKS, 'local_static_check_count': len(CHECKS),
        'python_version': sys.version.split()[0], 'jsonschema_version': package_version('jsonschema'),
        'business_acceptance': 'NOT_RUN', 'executed_business_tests': 0, 'runtime_adoption': 'UNPROVEN'})
    save(OUT / 'manifest.json', {'revision': REVISION, 'findings': list(DECISIONS), 'sources': list(SOURCES.values()), 'products': PRODUCTS,
        'small_patch_count': len(PATCHES), 'large_recipe_count': len(RECIPES), 'business_execution': 'NOT_RUN', 'runtime_adoption': 'UNPROVEN',
        'consumer_rule': 'Pin manifest + complete product SHA; no mixing original/derived parts. Original archive is immutable, old check reports stay historical.',
        'source_boundary': 'Only source bytes were read; no archive script or business code executed'})
    print(json.dumps({'findings': len(DECISIONS), 'bounded_patches': len(PATCHES), 'recipes': len(RECIPES), 'local_static_checks': len(CHECKS), 'products': len(PRODUCTS), 'business_execution': 'NOT_RUN'}))


if __name__ == '__main__':
    main()
