"""Build bounded W5 design-data revisions; never execute or overwrite archives.

All full derived artifacts remain in memory. Public output contains exact bounded
operations, dependency identities and local design checks, never runtime receipts.
"""
import copy
import hashlib
import json
import re
from collections import Counter
from datetime import datetime, timedelta
from decimal import Decimal
from pathlib import Path

from validate_design_completion import apply_operations, pointer, walk

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'remediation/assets/commercial-fixtures'
ORIGINAL = Path('D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals')
CACHE = Path('D:/CP6-archives/consolidation-20261010/commercial-cache')
CREDIT = CACHE / 'credit-stage100/members/candidate'
REVISION = 'CP6-DESIGN-REVISION-20261011/R1'
PATCHES, DERIVED, MANIFEST, CHECKS = [], {}, {}, []


def sha(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode('utf-8')


def original(prefix):
    paths = list((ORIGINAL / prefix[:2]).glob(prefix + '*'))
    assert len(paths) == 1, paths
    return paths[0]


def check(name, detail):
    CHECKS.append({'check': name, 'result': 'PASS', 'verification_level': 'LOCAL_DESIGN_DATA', 'detail': detail})


def textop(before, after, count=1):
    assert before != after
    return {'op': 'replace_text', 'before': before, 'after': after, 'expected_count': count}


def jsonop(path, before, after):
    assert before != after
    return {'op': 'replace_json', 'path': path, 'before': before, 'after': after}


def patch(path, findings, decision, operations):
    raw = path.read_bytes()
    assert path.as_posix() not in DERIVED, path
    derived = apply_operations(raw, operations)
    assert raw != derived
    PATCHES.append({'source_path': path.as_posix(), 'source_sha256': sha(raw), 'source_finding_ids': findings,
                    'decision': decision, 'operations': operations, 'derived_sha256': sha(derived),
                    'expected_derived_sha256': sha(derived), 'expected_derived_bytes': len(derived),
                    'serialization': 'UTF8_SORTED_COMPACT_JSON_NO_LF' if path.parent.name == 'BODIES' else 'UTF8_EXACT_APPLY_OPERATIONS_RESULT'})
    DERIVED[path.as_posix()] = derived
    return derived


def fences(text):
    result = []
    for m in re.finditer(r'```json\s*\n([\s\S]*?)\n```', text):
        headings = re.findall(r'^#{1,6} (.+)$', text[:m.start()], re.M)
        heading = headings[-1] if headings else ''
        key = heading.split()[0] if heading else ''
        if heading.startswith('附录 C'):
            key = 'AppendixC'
        result.append({'key': key, 'heading': heading, 'value': json.loads(m.group(1)), 'raw': m.group(1),
                       'start': m.start(1), 'end': m.end(1), 'line': text.count('\n', 0, m.start(1)) + 1})
    return result


def return_nodes(text):
    blocks = {b['key']: b for b in fences(text)}
    nodes, locations = {}, {}

    def add(digest, body, location):
        assert sha(canonical(body)) == digest, location
        assert digest not in nodes or nodes[digest] == body, location
        nodes[digest] = copy.deepcopy(body)
        locations.setdefault(digest, []).append(location)

    for name in ('12.E', '12.EP'):
        for index, value in enumerate(blocks[name]['value']):
            digest = value.get('reference', value.get('proof'))['digest']
            if 'payload' in value:
                body = value['payload']
            else:
                location, *minus = value['fullBodyLocation'].split(' minus ')
                block, path = location.split('#', 1)
                body = copy.deepcopy(pointer(blocks[block]['value'], path))
                if minus:
                    del body[minus[0]]
            add(digest, body, name + '/' + str(index))
    for auth, plan in [('12.D04', '12.D14'), ('12.D06', '12.D16'), ('12.G6R09', '12.G6R12'), ('12.L2R09', '12.L2R12')]:
        a = blocks[auth]['value']
        line = a['postingTemplate']['data']['lines'][0]
        assert line['claimSpends'] == []
        add(a['executionManifest']['parts'][0]['lineBusinessDigest'], line, auth + '/lineBusinessDigest')
        preimage = {'schema': 'PUR-RETURN-DISPATCH-2'}
        for key in ('returnRef', 'returnLineId', 'executionPartKey', 'approvalRef', 'supplierAgreementRef', 'authorizedDestination', 'purposeRef', 'origin', 'scopeProtectionRef', 'mode'):
            preimage[key] = a[key]
        preimage['originMappingSemantic'] = {k: v for k, v in a['originMapping'].items() if k not in ('ref', 'observedAt')}
        preimage['manifestSemantic'] = a['executionManifest']
        preimage['postingBusiness'] = {k: a['postingTemplate'][k] for k in ('source', 'executionManifestRef', 'occurredAt', 'use', 'data', 'reason')}
        add(a['permitBusinessDigest'], preimage, auth + '/permitBusinessDigest')
        p = blocks[plan]['value']
        p = p.get('preparation', p)['stockPlan']
        assert p['capacityBindings'] == []
        preimage = {'schema': 'WMS-STOCK-EFFECT-2', 'sourceManifestSemantic': p['executionManifest'], 'capacityRelationSemantic': []}
        preimage.update({k: p[k] for k in ('source', 'occurredAt', 'use', 'data')})
        add(p['digest'], preimage, plan + '/stockPlan/digest')
    return blocks, nodes, locations


def build_return():
    path = original('1f904a10280b2cbb')
    text = path.read_text(encoding='utf-8-sig')
    blocks, nodes, locations = return_nodes(text)
    assert len(nodes) == 298
    seeds = [('12.G6R07', None, '6.00000000'), ('12.L2R07', None, '2.00000000'),
             ('12.EP', 11, '4.00000000'), ('12.EP', 25, '6.00000000')]
    seed_values, operations = {}, []
    for block, index, quantity in seeds:
        value = blocks[block]['value'] if index is None else blocks[block]['value'][index]['payload']
        assert value['quantity'] == '10.00000000'
        digest = sha(canonical(value))
        assert digest in nodes
        seed_values[digest] = quantity
        # Source.lineId + following sibling fields are unique, bounded context.
        line_id = value['source']['lineId']
        pattern = r'"lineId": "' + re.escape(line_id) + r'"\s*},\s*"version": "1",\s*"quantity": "10\.00000000"'
        matches = list(re.finditer(pattern, blocks[block]['raw']))
        assert len(matches) == 1, (block, index, line_id)
        before = matches[0].group()
        assert text.count(before) == 1
        operations.append(textop(before, before.replace('"quantity": "10.00000000"', '"quantity": "' + quantity + '"')))
    memo, active, edges = {}, set(), {}

    def calc(digest):
        if digest in memo:
            return memo[digest]
        assert digest not in active, 'digest dependency cycle'
        active.add(digest)
        body = copy.deepcopy(nodes[digest])
        if digest in seed_values:
            body['quantity'] = seed_values[digest]
        children = set()

        def substitute(value):
            if isinstance(value, str) and value in nodes:
                children.add(value)
                return calc(value)
            if isinstance(value, dict):
                return {k: substitute(v) for k, v in value.items()}
            if isinstance(value, list):
                return [substitute(v) for v in value]
            return value
        result = sha(canonical(substitute(body)))
        active.remove(digest)
        edges[digest] = sorted(children)
        memo[digest] = result
        return result
    for digest in nodes:
        calc(digest)
    changed = {k: v for k, v in memo.items() if k != v}
    assert len(changed) == 127
    historical_start = text.find('\n## 附录 A')
    if historical_start < 0:
        historical_start = len(text)
    historical_hits = {d: text[historical_start:].count(d) for d in changed if d in text[historical_start:]}
    assert not historical_hits, historical_hits
    for old, new in changed.items():
        operations.append(textop(old, new, text.count(old)))
    after = patch(path, ['COM-RETURN-CHILD-ALLOWANCE-QUANTITY'],
                  'Versioned DEMO child allowances 4/6/6/2; all 298 registered preimages checked; recompute 127 affected hashes in dependency order.', operations)
    _, repaired, _ = return_nodes(after.decode())
    assert len(repaired) == 298 and all(d not in after.decode() for d in changed)
    MANIFEST['return'] = {'canonicalization': 'UTF-8; sorted object keys; compact JSON; no LF; SHA256; no implicit exclusions',
                          'seed_quantities': seed_values, 'nodes': [{'before': d, 'after': memo[d], 'locations': locations[d], 'dependencies': edges[d]} for d in sorted(nodes)],
                          'unchanged_nodes': 171, 'changed_nodes': 127, 'embedded_historical_hash_hits': historical_hits}
    check('RETURN-298-preimages-and-transitive-127-hashes', {'nodes': len(repaired), 'changed': 127, 'leaf_edits': 4, 'historical_blocks_unchanged': True})


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')


def build_est():
    path = original('f7d7141b4ceffa15')
    text = path.read_text(encoding='utf-8-sig')
    blocks = fences(text)
    records = next(b['value']['records'] for b in blocks if b['key'] == 'E71_FullPreparedContextEvidence')
    operations, witnesses = [], []
    revoked = next(b for b in blocks if b['key'] == 'R1Q10_RevokedIsNotHistoricalReady')
    old_policy = '"policyRevision": "DEMO-QTN-POL-1",\n    "headWatermark": "DEMO-QTN-HEAD-4"'
    assert revoked['raw'].count(old_policy) == 1
    operations.append(textop(old_policy, old_policy.replace('POL-1', 'POL-2')))
    for i, record in enumerate(records):
        domain = record['domain']
        if domain not in ('EST-LOOKUP-CONTEXT-1', 'EST-SEAL-PLAN-1'):
            continue
        ref = record['reference']
        cid, oldhash = ref['contextId'], ref['digest']
        body = json.loads(record['payloadJson'])
        assert canonical(body).decode() == record['payloadJson']
        assert sha(domain.encode() + b'\n' + canonical(body)) == oldhash
        if domain == 'EST-LOOKUP-CONTEXT-1':
            result = [b for b in blocks if b['heading'].endswith('LookupResult') and b['value'].get('context', {}).get('contextId') == cid]
            assert len(result) == 1
            issued = result[0]['value']['asOf']
            provenance = {'kind': 'EXISTING_LOOKUP_RESULT_ASOF', 'block': result[0]['key'], 'line': result[0]['line']}
        else:
            issued = {4: '2026-09-30T20:00:30.500Z', 7: '2026-09-30T20:00:56.500Z'}[i]
            provenance = {'kind': 'NEW_TASK_DEMO_ISSUANCE_WITNESS', 'decision': 'between existing Run.createdAt and sealed Version.createdAt; not an archived issuer observation'}
        expiry = (datetime.fromisoformat(issued.replace('Z', '+00:00')) + timedelta(minutes=10)).isoformat(timespec='milliseconds').replace('+00:00', 'Z')
        oldexpiry = body['ExpiresAt']
        assert oldexpiry == ref['expiresAt'] == '2026-09-30T20:30:00.000Z'
        body['ExpiresAt'] = expiry
        newhash = sha(domain.encode() + b'\n' + canonical(body))
        escaped = json.dumps(record['payloadJson'], ensure_ascii=False)[1:-1]
        marker = '\\"ExpiresAt\\":\\"' + oldexpiry + '\\"'
        at = escaped.index(marker)
        # Keep only a short, unique fragment of the large embedded canonical body.
        before = escaped[max(0, at - 100):at + len(marker) + 50]
        assert text.count(before) == 1
        operations.append(textop(before, before.replace(oldexpiry, expiry)))
        pattern = r'"(?:contextId|planId)": "' + re.escape(cid) + r'",\s*"digest": "' + oldhash + r'",\s*"expiresAt": "' + oldexpiry + '"'
        occurrences = Counter(m.group() for m in re.finditer(pattern, text))
        assert sum(occurrences.values()) == 3, (i, occurrences)
        for before, count in occurrences.items():
            operations.append(textop(before, before.replace(oldexpiry, expiry), count))
        operations.append(textop(oldhash, newhash, text.count(oldhash)))
        witnesses.append({'record_pointer': '/records/' + str(i), 'context_id': cid, 'domain': domain,
                          'issued_at': issued, 'expires_at': expiry, 'ttl_seconds': 600, 'issued_at_provenance': provenance,
                          'before_digest': oldhash, 'after_digest': newhash, 'dependent_reference_count': 3})
    assert len(witnesses) == 10
    derived = patch(path, ['COM-EST-FIXTURE-POLICY-AND-TTL'],
                    'Current revoked observation uses policy2; lastProven retains policy1. Eight lookup and two seal contexts expire exactly 600 seconds after explicit issuance witness.', operations)
    repaired = fences(derived.decode())
    records_after = next(b['value']['records'] for b in repaired if b['key'] == 'E71_FullPreparedContextEvidence')
    for witness in witnesses:
        record = pointer({'records': records_after}, witness['record_pointer'])
        body = json.loads(record['payloadJson'])
        assert body['ExpiresAt'] == record['reference']['expiresAt'] == witness['expires_at']
        assert sha(record['domain'].encode() + b'\n' + canonical(body)) == record['reference']['digest'] == witness['after_digest']
        assert witness['before_digest'] not in derived.decode()
    current = next(b['value'] for b in repaired if b['key'] == 'R1Q10_RevokedIsNotHistoricalReady')['quoteReadiness']
    assert current['policyRevision'] == 'DEMO-QTN-POL-2' and current['lastProven']['policyRevision'] == 'DEMO-QTN-POL-1'
    assert records_after[5] == records[5] and records_after[11] == records[11]
    MANIFEST['est'] = {'canonicalization': 'SHA256(domain + LF + sorted compact UTF8 payloadJson); payload has no LF', 'ttl_witnesses': witnesses,
                       'wire_fields_added': [], 'unrelated_reference_and_legacy_contexts_unchanged': True}
    check('EST-policy-and-ten-domain-hashes', {'lookup_contexts': 8, 'seal_plans': 2, 'ttl_seconds': 600, 'historical_policy_preserved': True})


def leaf_diff(before, after, path=''):
    if before == after:
        return []
    if isinstance(before, dict) and isinstance(after, dict) and before.keys() == after.keys():
        return [op for k in before for op in leaf_diff(before[k], after[k], path + '/' + k.replace('~', '~0').replace('/', '~1'))]
    if isinstance(before, list) and isinstance(after, list) and len(before) == len(after):
        return [op for i in range(len(before)) for op in leaf_diff(before[i], after[i], path + '/' + str(i))]
    return [jsonop(path, before, after)]


def build_money():
    finding = ['COM-CREDIT-CURRENT-BODY-MONEY-FIELD']
    keys = [('multi_mixed_complete_public_q14', 9), ('wn11_c06_exact_saved_200', 3),
            ('wn11_complete_current_q14', 3), ('wn11_existing_target_c06_saved_200', 3)]
    identities = {}
    money_paths = {}
    for key, count in keys:
        path = CREDIT / ('BODIES/' + key + '.json')
        raw = path.read_bytes()
        value = json.loads(raw)
        assert raw == canonical(value), key
        hits = [(p, v) for p, v in walk(value) if isinstance(v, dict) and set(v) == {'amount', 'currency'}]
        assert len(hits) == count
        replacements = Counter(canonical(v).decode() for _, v in hits)
        operations = []
        for before, occurrences in replacements.items():
            money = json.loads(before)
            after = canonical({'value': money['amount'], 'currency': money['currency']}).decode()
            assert raw.decode().count(before) == occurrences
            operations.append(textop(before, after, occurrences))
        derived = patch(path, finding, 'Public WMS Money only: {amount,currency} -> closed {value,currency}; decimal strings and native Finance/COGS/Inventory domains preserved.', operations)
        repaired = json.loads(derived)
        assert derived == canonical(repaired)
        for p, old in hits:
            assert pointer(repaired, p) == {'value': old['amount'], 'currency': old['currency']}
        for _, obj in walk(repaired):
            if isinstance(obj, dict) and all(k in obj for k in ('net', 'tax', 'gross')) and isinstance(obj['net'], dict) and 'value' in obj['net']:
                assert Decimal(obj['net']['value']) + Decimal(obj['tax']['value']) == Decimal(obj['gross']['value'])
                assert obj['net']['currency'] == obj['tax']['currency'] == obj['gross']['currency']
        identities[key] = {'before_digest': sha(raw), 'before_bytes': len(raw), 'after_digest': sha(derived), 'after_bytes': len(derived)}
        money_paths[key] = [p for p, _ in hits]

    # The lookup-binding body embeds the changed MULTI Q14 content identity.
    key = 'multi_public_native_lookup_binding'
    path = CREDIT / ('BODIES/' + key + '.json')
    raw = path.read_bytes()
    before = json.loads(raw)
    assert raw == canonical(before)
    after = copy.deepcopy(before)
    change = identities['multi_mixed_complete_public_q14']
    assert after['q14Body']['expectedDigest'] == change['before_digest'] and after['q14Body']['expectedBytes'] == change['before_bytes']
    after['q14Body']['expectedDigest'], after['q14Body']['expectedBytes'] = change['after_digest'], change['after_bytes']
    operations = [textop(canonical(before['q14Body']).decode(), canonical(after['q14Body']).decode())]
    derived = patch(path, finding, 'Propagate revised canonical Q14 identity into exact lookup-binding model; keep native issuer facts unproven.', operations)
    assert derived == canonical(after)
    identities[key] = {'before_digest': sha(raw), 'before_bytes': len(raw), 'after_digest': sha(derived), 'after_bytes': len(derived)}
    dependency_counts = {}
    paths = ['02_UNIQUE_NEW_BODY_DIRECTORY_AND_STORE_BINDINGS', '04_REQUIRED_TYPED_SOURCE_AND_DOMAIN_CONTRACTS',
             '05_API_FE_PERMISSION_DTO_AND_MODULE_BINDINGS', '12_WN11_EXISTING_TARGET_COMPLETE_BRANCH_TYPED_PRODUCERS',
             '13_FOUR_ROOT_BOUNDED_REVISION_AND_SOURCE_ROUTES']
    for name in paths:
        path = CREDIT / ('CURRENT/' + name + '.json')
        before = json.loads(path.read_bytes())
        after = copy.deepcopy(before)
        if name.startswith('02'):
            for key, change in identities.items():
                bodies = [v for v in after['bodies'] if v['bodyKey'] == key]
                receipts = [v for v in after['storeReceipts'] if v['bodyKey'] == key]
                assert len(bodies) == len(receipts) == 1
                body, receipt = bodies[0], receipts[0]
                assert body['sha256'] == receipt['bodySha256'] == change['before_digest']
                assert body['bytes'] == receipt['bodyBytes'] == change['before_bytes']
                assert body['actualStoreReceipt'] is None and receipt['actualRuntimeStoreReceipt'] is None and receipt['actualNewMetadataWrites'] == 0
                body['sha256'] = receipt['bodySha256'] = change['after_digest']
                body['bytes'] = receipt['bodyBytes'] = change['after_bytes']
                for ref in (body['documentRawRef8'], receipt['originalReceiptMetadata7']):
                    assert ref['digest'] == change['before_digest'] and ref['byteLength'] == change['before_bytes']
                    ref['digest'], ref['byteLength'] = change['after_digest'], change['after_bytes']
                    ref['handle'] = f"cas://erp-credit/{change['after_digest']}/{change['after_bytes']}"
                old_receipt = canonical(next(v for v in before['storeReceipts'] if v['bodyKey'] == key)['originalReceiptMetadata7'])
                assert receipt['receiptCanonicalSha256'] == sha(old_receipt) and receipt['receiptCanonicalUtf8'] == old_receipt.decode() and receipt['receiptCanonicalBytes'] == len(old_receipt)
                new_receipt = canonical(receipt['originalReceiptMetadata7'])
                receipt['receiptCanonicalUtf8'], receipt['receiptCanonicalSha256'], receipt['receiptCanonicalBytes'] = new_receipt.decode(), sha(new_receipt), len(new_receipt)
        elif name.startswith('04'):
            for key, change in identities.items():
                obj = after['allNewDocumentBodyTypes'][key]['wholeBody']
                assert obj['sha256'] == change['before_digest'] and obj['bytes'] == change['before_bytes']
                obj['sha256'], obj['bytes'] = change['after_digest'], change['after_bytes']
        elif name.startswith('05'):
            for key in ('q14', 'terminal'):
                obj = after['publicBodies']['WN11']['bodyPins'][key]
                change = identities[obj['bodyKey']]
                assert obj['sha256'] == change['before_digest'] and obj['bytes'] == change['before_bytes']
                obj['sha256'], obj['bytes'] = change['after_digest'], change['after_bytes']
        else:
            count = 0
            for ptr, obj in walk(after):
                if isinstance(obj, dict) and obj.get('bodyKey') == 'wn11_existing_target_c06_saved_200' and 'expectedDigest' in obj:
                    change = identities[obj['bodyKey']]
                    assert obj['expectedDigest'] == change['before_digest'] and obj['expectedBytes'] == change['before_bytes']
                    obj['expectedDigest'], obj['expectedBytes'] = change['after_digest'], change['after_bytes']
                    count += 1
            assert count == (324 if name.startswith('12') else 1), (name, count)
        operations = leaf_diff(before, after)
        derived = patch(path, finding, 'Exact bounded bodyKey dependencies: new design fixture identities and derived Store7 canonical metadata; runtime receipt/write/issuer facts remain null or zero.', operations)
        assert json.loads(derived) == after
        dependency_counts[name] = len(operations)
        # Null/zero actual adoption cells must survive byte identity repairs.
        for ptr, old in walk(before):
            if ptr.rsplit('/', 1)[-1].lower().startswith('actual') and old in (None, 0, False):
                assert pointer(after, ptr) == old
    MANIFEST['credit_money'] = {'canonicalization': 'sorted compact UTF8 JSON; no trailing LF', 'money_pointers': money_paths,
                                'body_identities': identities, 'dependency_operations': dependency_counts,
                                'dag': ['4 public bodies -> MULTI lookup-binding body', 'all 5 bodies -> CURRENT02 body directory + rawRef8 + Store7 receipt canonical/hash/bytes',
                                        'all 5 bodies -> CURRENT04 wholeBody', 'WN11 q14 + terminal -> CURRENT05 pins',
                                        'WN11 existing-target terminal -> CURRENT12 324 typed sources + CURRENT13 root R98-03'],
                                'historical_index_and_evidence_mutations': 0, 'actual_runtime_receipts_created': 0}
    check('CREDIT-public-Money-and-full-identity-cascade', {'money_fields': 18, 'canonical_bodies': 5, 'typed_existing_target_sources': 324, 'actual_writes': 0})


def root_dependency(path):
    raw = path.read_bytes()
    dependencies = []
    for relative in ('remediation/assets/core-types/bounded-patches.json', 'remediation/assets/fixtures/bounded-patches.json'):
        bundle_path = ROOT / relative
        bundle_raw = bundle_path.read_bytes()
        for spec in json.loads(bundle_raw)['patches']:
            if Path(spec['source_path']).resolve() == path.resolve():
                assert spec['source_sha256'] == sha(path.read_bytes())
                raw = apply_operations(raw, spec['operations'])
                dependencies.append({'path': relative, 'sha256': sha(bundle_raw), 'source_sha256': spec['source_sha256'], 'operations': len(spec['operations'])})
    return raw, dependencies


def build_openapi_codec():
    type_path = original('c69762861155ed95')
    type_raw, dependencies = root_dependency(type_path)
    type_sha = sha(type_raw)
    types = json.loads(type_raw)
    path = original('c5a95450610038cf')
    text = path.read_text(encoding='utf-8-sig')
    first, end = json.JSONDecoder().raw_decode(text)
    extensions, current = {}, None
    duplicated = 0
    for line in text[end:].splitlines():
        if not line.strip() or line.startswith('#'):
            continue
        if re.fullmatch(r'x-[\w-]+:', line):
            key = line[:-1]
            if key in extensions:
                assert key == 'x-stage93-finite-media-ec26-c06-required-composed-source'
                key = 'x-stage94-nullable-authority-revision-c06-required-composed-source'
                duplicated += 1
            current = extensions.setdefault(key, {})
        else:
            match = re.fullmatch(r'  ([\w]+): (.+)', line)
            assert match and current is not None, line
            key, value = match.groups()
            assert key not in current
            if value in ('true', 'false', 'null', '[]') or re.fullmatch(r'-?\d+', value):
                value = json.loads(value)
            current[key] = value
    assert len(extensions) == 10 and duplicated == 1
    assert extensions['x-stage94-nullable-authority-revision-c06-required-composed-source']['currentBindingKey'].startswith('stage94')
    assert end > 0 and text[end - 1] == '}'
    extension_text = json.dumps(extensions, ensure_ascii=False, indent=2)
    operations = [textop(text[end - 1:], ',\n' + extension_text[2:] + '\n')]
    for code, route in [('C01', '/api/erp/credit/v1/works/{workId}/drafts'), ('C02', '/api/erp/credit/v1/works/{workId}/drafts/{draftId}/revisions')]:
        prefix = '/paths/' + route.replace('~', '~0').replace('/', '~1') + '/post/responses/200'
        schema_ptr = prefix + '/content/application~1json/schema/$ref'
        before = pointer(first, schema_ptr)
        assert before == './ERP-CREDIT-01_TYPE-REGISTRY.json#/$defs/EC_' + code + '_REPLAY_CommercialDraftView'
        after = before.replace('_REPLAY_', '_COMMITTED_')
        assert after.split('#/$defs/')[1] in types['$defs']
        operations.append(jsonop(schema_ptr, before, after))
        operations.append(jsonop(prefix + '/description', pointer(first, prefix + '/description'),
                                 'Exact saved COMMITTED body from first HTTP201; replay transport HTTP200; no body relabel, redaction or rebuild.'))
    derived = patch(path, ['COM-CREDIT-OPENAPI-FROZEN-REPLAY-MAPPING'],
                    'One JSON OpenAPI root (also valid YAML), 29 routes unchanged; preserve all 10 stage metadata blocks and disambiguate stage94; replay C01/C02 exact saved COMMITTED body.', operations)

    def no_duplicates(pairs):
        result = {}
        for k, v in pairs:
            assert k not in result, k
            result[k] = v
        return result
    parsed = json.loads(derived, object_pairs_hook=no_duplicates)
    assert len(parsed['paths']) == 29 and parsed['paths'].keys() == first['paths'].keys()
    refs = 0
    for _, value in walk(parsed):
        if not isinstance(value, dict) or '$ref' not in value:
            continue
        ref = value['$ref']
        if ref.startswith('#/'):
            pointer(parsed, ref[1:])
            refs += 1
        elif ref.startswith('./ERP-CREDIT-01_TYPE-REGISTRY.json#'):
            pointer(types, ref.split('#', 1)[1])
            refs += 1
    MANIFEST['credit_openapi'] = {'parseable_format': 'single JSON OpenAPI document', 'routes': 29, 'preserved_extension_blocks': list(extensions),
                                 'stage94_duplicate_key_repaired': True, 'checked_local_and_type_refs': refs,
                                 'replay': {'C01': 'HTTP201 COMMITTED saved body -> HTTP200 same exact body', 'C02': 'HTTP201 COMMITTED saved body -> HTTP200 same exact body'}}
    check('CREDIT-single-OpenAPI-and-replay', {'paths': 29, 'stage_metadata_blocks': 10, 'refs_resolved': refs, 'replay_routes': 2})

    old_pin = '62effa1e0542784a5fa936d11961465db4da522ed4d28fc9c9bb5351cf40b6f1'
    codec_entries = []
    for prefix, language in [('319e5002c3312b27', 'C#'), ('533967724512459f', 'TypeScript')]:
        path = original(prefix)
        text = path.read_text(encoding='utf-8-sig')
        operations = [textop(old_pin, type_sha)]
        if language == 'C#':
            operations.extend([textop('namespace Cp6.ErpCredit.Generated;', 'namespace Cp6.ErpCredit.CurrentV1.Generated;'),
                               textop('if(!applied&&has)throw new WireException("OWNER_RESULT_FORBIDDEN","$");',
                                      'if(v.GetProperty("outcome").GetString()=="REJECTED"&&has)throw new WireException("OWNER_RESULT_FORBIDDEN","$");')])
        else:
            operations.extend([textop('ErpCreditR8', 'ErpCreditCurrentV1', text.count('ErpCreditR8')),
                               textop('FinArAppliedCommandResult', 'FinArProvenResultCommandResult', text.count('FinArAppliedCommandResult')),
                               textop('FinArCompletedCommandResult & {readonly outcome:"APPLIED";readonly resultRef:FinArR2.Ref}',
                                      'FinArCompletedCommandResult & {readonly outcome:"APPLIED"|"NO_CHANGE";readonly resultRef:FinArR2.Ref}'),
                               textop('exactCommandResult:FinArOutcomeUnknownCommandResult|FinArNotObservedCommandResult;',
                                      'exactCommandResult:FinArOutcomeUnknownCommandResult|FinArNotObservedCommandResult|FinArCompletedCommandResult;'),
                               textop('if((v.outcome==="REJECTED"||v.outcome==="NO_CHANGE")&&v.resultRef!==null)',
                                      'if(v.outcome==="REJECTED"&&v.resultRef!==null)')])
        # Isolated compilation found syntax/type defects; preserve strict decoder semantics.
        compile_fixes = {'C#': [{'op': 'replace_text', 'before': 'return JsonDocument.Parse(raw);', 'after': 'return JsonDocument.Parse(raw.ToArray());', 'expected_count': 1}, {'op': 'replace_text', 'before': 's.TryGetProperty("contains",out var co)&&!a.Any(x=>Matches(x,co,resolver,current,path))', 'after': 's.TryGetProperty("contains",out var containsSchema)&&!a.Any(x=>Matches(x,containsSchema,resolver,current,path))', 'expected_count': 1}, {'op': 'replace_text', 'before': 'var from=Fixed.Parse(r.GetProperty("from").GetString()!),to=Fixed.Parse(r.GetProperty("to").GetString()!);', 'after': 'Fixed from=Fixed.Parse(r.GetProperty("from").GetString()!),to=Fixed.Parse(r.GetProperty("to").GetString()!);', 'expected_count': 2}, {'op': 'replace_text', 'before': 'var n=a.GetProperty("net"),t=a.GetProperty("tax"),g=a.GetProperty("gross");', 'after': 'JsonElement n=a.GetProperty("net"),t=a.GetProperty("tax"),g=a.GetProperty("gross");', 'expected_count': 1}], 'TypeScript': [{'op': 'replace_text', 'before': 'const z=add(a,b);const neg=z.n<0n,n=neg?-z.n:z.n,p=10n**BigInt(z.s);return z.s?`${neg?"-":""}${n/p}.${String(n%p).padStart(z.s,"0")}`:`${z.n}`', 'after': 'const z=add(a,b);const neg=z.x<0n,n=neg?-z.x:z.x,p=10n**BigInt(z.s);return z.s?`${neg?"-":""}${n/p}.${String(n%p).padStart(z.s,"0")}`:`${z.x}`', 'expected_count': 1}, {'op': 'replace_text', 'before': 'crypto.subtle.digest("SHA-256",raw)', 'after': 'crypto.subtle.digest("SHA-256",new Uint8Array(raw))', 'expected_count': 1}, {'op': 'replace_text', 'before': 'if(result.kind!=="PENDING")throw new WireError("A26_PENDING_RESULT_REQUIRED","$");return{result,commandId:result.commandId', 'after': 'if(result.kind!=="PENDING")throw new WireError("A26_PENDING_RESULT_REQUIRED","$");return{result:{...result,kind:"PENDING"},commandId:result.commandId', 'expected_count': 1}]}
        operations.extend(compile_fixes[language])
        derived = patch(path, ['COM-CREDIT-FROZEN-R8-CODEC-CURRENT-ADAPTATION'],
                        'Derive isolated current-v1 codec; retain frozen R8 bytes/namespace; pin current TYPE after root Problem aliases, preserve native exact schemas; NO_CHANGE ref allowed, unproven completed remains UNKNOWN.', operations)
        derived_text = derived.decode()
        assert old_pin not in derived_text and type_sha in derived_text
        assert 'b324a9b62d888f268a514de4266ecff70783e80d30c39eb5b82e7fffaf62af3a' in derived_text
        assert '8a7864b91e2bd19f9102abcb9eea756d7f6df33ad84e7b852f24a9bd669c56ca' in derived_text
        codec_entries.append({'language': language, 'source_path': path.as_posix(), 'derived_sha256': sha(derived),
                              'isolated_destination': 'current-v1/ERP-CREDIT-01_WIRE-CODECS.current-v1.' + ('cs' if language == 'C#' else 'ts')})
    obs = types['$defs']['FinArObservation']['oneOf']
    assert obs[0]['properties']['exactCommandResult']['allOf'][1]['properties']['outcome']['enum'] == ['APPLIED', 'NO_CHANGE']
    assert obs[3]['properties']['exactCommandResult']['oneOf'][2]['allOf'][1]['properties']['kind']['const'] == 'COMPLETED'
    MANIFEST['credit_current_codec'] = {'profile': 'current-v1', 'type_source_path': type_path.as_posix(), 'type_original_sha256': sha(type_path.read_bytes()),
                                      'type_current_sha256': type_sha, 'type_current_bytes': len(type_raw), 'root_patch_dependencies': dependencies,
                                      'codecs': codec_entries, 'native_schemas_unchanged': True, 'generator_performs_compilation_or_runtime': False,
                                      'separate_compilation_evidence': 'codec-compile-validation.json (reuse only when derived byte SHA matches)',
                                      'decision_matrix': [{'outcome': outcome, 'result_ref_present': has, 'wire_valid': not ((outcome == 'APPLIED' and not has) or (outcome == 'REJECTED' and has))}
                                                          for outcome in ('APPLIED', 'NO_CHANGE', 'REJECTED') for has in (False, True)]}
    check('CREDIT-current-v1-codec-pin-and-qualified-unions', {'type_sha256': type_sha, 'codecs': 2, 'native_schema_pins_unchanged': True, 'verification': 'bounded source + schema union checks; not compilation/runtime'})


DECISIONS = {
    'COM-PR-N01-AUTHORITY-VERSION': ('ClaimPrepareRequest 使用冻结 bundle 的 PR approval revision 2；两处 authorityScope/sourceBusinessVersion 从3改2，独立 acceptedWireExamples revision3不变。', '授权scope或businessVersion与冻结Grant不同，拒绝Prepare且零写。'),
    'COM-PR-N08-PREVIEW-TIMELINE': ('首次成功与同命令重放的 recordedAt 均固定11:28，落在11:20创建/11:30到期之间；三处12:08修为11:28，读取时钟不得覆盖首次事务记录。', '未消费preview到期后首次提交返回410且零写；相同已消费command只返回原成功，异command不得复用。'),
    'COM-RETURN-CHILD-ALLOWANCE-QUANTITY': ('G6R07 quantity6、L2R07 quantity2、EP[11] quantity4、EP[25] quantity6；校验298摘要原像，有向依赖闭包重算127摘要，保留171未变化节点及历史附录。', '任一child再次授父总量10，或任何transitive digest仍旧值，当前版夹具不可采用；不得以父范围防护豁免子数量。'),
    'COM-EST-FIXTURE-POLICY-AND-TTL': ('R1Q10当前撤销policy改POL2，lastProven仍POL1；8 Lookup和2 Seal依本版本显式DEMO issuedAt见证到期600秒，E71 payload/reference/消费者摘要同步。', 'now>=expiresAt首次Apply拒绝且零写；旧POL1不可替代当前POL2；已提交同command重放保原结果，不延长窗口。'),
    'COM-CREDIT-PROBLEM-CODE-ALIASES': ('新current-v1九个Problem的issues[].code与顶层code一致，type使用该code的小写短横URN；原HTTP status/业务fence/UI恢复策略不变，旧三元组只留冻结回放。', '新current-v1不接受HEAD_CHANGED内层别名；未知code保留原错误并fail closed，不按消息文本猜业务错误。'),
    'COM-CREDIT-FROZEN-R8-CODEC-CURRENT-ADAPTATION': ('派生独立current-v1 C#/TS codec，锁根Problem修订后的TYPE字节SHA；NO_CHANGE可带Ref，FOUND仍需完整真实native/current来源，未证明COMPLETED保持UNKNOWN。冻结R8保原字节。', '错TYPE或native schema pin拒加载；APPLIED无Ref/REJECTED带Ref拒wire；NO_CHANGE或COMPLETED传输成功不能单独建立FOUND或释放占用。'),
    'COM-CREDIT-CURRENT-BODY-MONEY-FIELD': ('四个public body共18 Money改为closed value/currency；保数字字符串与货币并更新MULTI lookup binding、5正文身份、Store7 canonical摘要和全部列明current消费者。', 'amount别名、额外Money字段、任一旧digest/byteLength/handle/receipt摘要组合均不可进入current夹具；缺真实issuer/Store收据仍fenced。'),
    'COM-CREDIT-OPENAPI-FROZEN-REPLAY-MAPPING': ('闭合JSON+YAML尾块归一为单一OpenAPI JSON根；十块metadata保留，重复第二stage93准确命名stage94；29路由不变，C01/C02 HTTP200指向COMMITTED并返回首次201原字节。', '重复扩展键、多根载体不可用于生成；重放重命名COMMITTED_REPLAY、重新脱敏/重建body或再次业务写均拒绝。'),
    'COM-CREDIT-NORMAL-PHASE-AND-OLD-PARENT-MAPPING': ('CURRENT03 ops72/73/74及CURRENT10对应producer统一NEW_READY；78/79旧root父键取WN14.OLD_END已保护旧plan，同期future43 OUTPUT不得充旧父。', '错阶段current guard或用未来新plan OUTPUT充旧FK，在任何DML前拒绝；保持OLD_END先于NEW_ADMIT，opaque RV取真实OUTPUT。'),
    'COM-CREDIT-WN11-04-PROJECTION-PRODUCER-METADATA': ('WN11-04两个ledger操作31 target/producer准确绑定partition_projection，并供操作23消费其实际Store receipt；操作28 C06保存响应保持独立。', 'projection receipt不能冒充C06 saved reply receipt；31未有真实收据不得执行23，保持零新Finance派发。'),
    'COM-CREDIT-MULTI-02-BEFORE-AFTER-VERSION-MAPPING': ('两个ledger ops68/70/71业务版本分别按ProtectedBefore+1；68/71 updated_at取同Uow事件时钟，opaque row_version仍provider-only，四CAS原子性不变。', '直接写ProtectedBefore版本、算术溢出、错旧PK/RV或任一CAS冲突均整体回滚；未知原提交仅查询同原intent，不重派Finance。'),
}


def adoption_text():
    pin = MANIFEST['credit_current_codec']['type_current_sha256']
    return f'''# W5 商业夹具 current-v1 设计修订

版本：{REVISION}；profile：`erp-credit/current-v1-design-20261011-r1`。
这是本次任务新增设计补充及有界派生配方。历史接受、原归档字节和原执行资格不变；actual adoption = UNPROVEN，implementation = NOT_IMPLEMENTED，runtime = NOT_RUN。

## 采用与版本边界

消费者必须同时固定原文件 SHA、有界 patch 文件 SHA、派生文件 SHA/bytes、当前 TYPE SHA 与 profile。生成器只在内存派生完整文件，公开仓库仅保存有界补丁。原件 hash 或 expected_count/JSON preimage 不同立即停止，不能模糊匹配或跳过操作。禁止用派生夹具覆盖归档、历史 EVIDENCE、FINAL_LOCAL_RESTORE_INDEX 或已签发事实。

采用方显式选择 frozen-R8 或 current-v1 独立包/namespace。不得按payload外观猜版本，也不得自动降级到另一版本。current-v1 的 TYPE 精确字节 SHA 为 `{pin}`；具体根core/fixtures依赖及其SHA见 derivation-manifest.json。任何依赖变化须重算、重新审查本profile，旧pin不能沿用。TYPE文件名在隔离包内保留，以解析原相对引用；C# namespace为 `Cp6.ErpCredit.CurrentV1.Generated`，TS为 `ErpCreditCurrentV1`。准确WMS-RMA/FIN-AR原schema仍锁原SHA。现有method/path/header/权限字段保持原契约，不新增版本HTTP路由。

旧保存响应按原profile和原字节回放。迁移适配器只能在首次新命令/新查询的生产边界生成current-v1；不得改写旧command记录、Owner原始raw、receipt、digest或过去的接受结论。Client先部署识别profile及当前Problem三元组，Server再按显式部署配置启用新producer；未具备某Owner完整qualification的分支继续原fence，不能由文档fixture代替。发布、DB、真实Owner及Store采用属于后续独立实施/验证。

## RETURN：四个叶节点及完整依赖闭包

`12.G6R07#/quantity` 10→6，`12.L2R07#/quantity` 10→2，`12.EP#/11/payload/quantity` 10→4，`12.EP#/25/payload/quantity` 10→6；数值均保留8位小数字符串。父Part、原range、Manifest input/baseQuantity不改。

读取12.E的189条及12.EP的100条，精确解析fullBodyLocation JSON pointer，只移除显式minus字段。再加入D04/D06/G6R09/L2R09的lineBusinessDigest、permitBusinessDigest和D14/D16/G6R12/L2R12的stockPlan摘要。按digest去重后298节点。每个原像必须先与原摘要一致；若重复digest对应不同body或有依赖环立即拒绝。H为sorted-key compact UTF8 JSON的SHA256，无尾LF。

每条完整string值若等于已注册digest即构成依赖边。按子节点优先重算：四叶quantity→Allowance→Manifest/line→authorization/permit/stockPlan→Owner原像及使用者引用。明确业务hash原像见生成器return_nodes；line只有空claimSpends，本次不推导未出现的claim格式。127个摘要变化，171个不变；每次全串替换都带原出现次数，且历史附录没有受影响摘要。manifest保留298节点的旧/新摘要、来源位置和全部边。不得只改四个量而遗留父摘要。

## EST：当前政策与600秒准备窗口

R1Q10.quoteReadiness.policyRevision改为DEMO-QTN-POL-2；同proof/head的当前撤销观察由R1Q09承接，lastProven仍是DEMO-QTN-POL-1。8个Lookup的本版本DEMO发出时刻选各LookupResult.asOf；2个Seal缺原发出时刻，本次显式制定20:00:30.500Z和20:00:56.500Z，落于已列Run创建和Version创建之间。这10项都是修订夹具时钟见证，不能宣称原Owner实际发出证据。逐项contextId/record pointer/issuedAt/expiresAt见manifest。

expiresAt = issuedAt + 600秒，严格now < expiresAt才能首次Apply。Server实际实施须保存可信context创建时刻及全scope/actor/draft绑定；不在原闭合wire增加issuedAt。E71的payloadJson仅改ExpiresAt，以SHA256(domain + LF + sorted compact payloadJson)重算，再同步Candidates/Bind或SealPreview/SealApply/reference。Reference与Legacy上下文保持不变。过期首次写为原过期错误和零业务写；已知同command终态恢复原结果而不续期。时钟见证不是生产验收。

## CREDIT：Money正文与Store依赖

只修改四个公开正文中18个schema明确引用WMS Money的位置；native Finance、COGS、Inventory数值域不做泛化amount替换。Money必须精确{{value,currency}}，保原decimal字符串和货币，net+tax=gross。四正文sorted compact UTF8/no-LF，接着更新MULTI lookup-binding正文自身hash；五个正文的准确旧/新bytes/hash见manifest。

依赖顺序为public body→lookup-binding body→CURRENT02 body/rawRef8和Store7→CURRENT04/05→CURRENT12/13。rawRef8及Store7的digest/byteLength/handle一起更新；Store7七字段canonical序列化后再算receiptCanonicalUtf8/Bytes/Sha256。CURRENT12独立existing-target body的324处typed source同时修订。修改只落在列明bodyKey和JSON pointer，历史索引/证据均不动。

这些receiptId只是当前DEMO配方中的符号，必须与本版本命名空间一起解释；不得用新hash冒充旧Owner签发receipt或更新不可变Store。actualStoreReceipt/actualRuntimeStoreReceipt保持null，actualNewMetadataWrites保持0。实际采用时以真实Store返回七字段与原scope生成身份、验完整raw、SHA、长度和同scope handle；未获得准确issuer/permission/codec/source/Store证明仍REQUIRED_FENCED。禁止把fixture canonical验算升级为实际Store成功。

## CREDIT：OpenAPI、回放与codec

原closed JSON根后10个YAML扩展块有界并入同一根；第二个x-stage93按其stage94 currentBindingKey命名x-stage94，所有值不改。结果可用JSON parser直接作为OpenAPI生成输入；29条method/path和192个本地/TYPE引用均解析。C01/C02首次HTTP201保存COMMITTED完整body；同command相同请求HTTP200逐字节返回该body，绝不换成COMMITTED_REPLAY，亦不重新渲染/脱敏。不同请求碰同key仍遵从原幂等冲突。单一输入只消除载体歧义，不创建任何Cash或Finance新写路由。

Current-v1 native COMPLETED wire判定：APPLIED必须非空resultRef；REJECTED必须null；NO_CHANGE允许null或准确Ref。FOUND接受APPLIED或有Ref的NO_CHANGE，ownerResultRef须结构相等，并且producer另行证明原native BusinessResult及完整current AR/GL/member/finite来源。codec parse成功只证明wire，不授予FOUND。来源未全，原COMPLETED保持UNKNOWN，保原raw及command/query身份、已有PREPARED/POST_READY和所有占用。

FAILED_NO_EFFECT只允许REJECTED/NO_CHANGE且null Ref，并验证exact base64解码、无重复JSON键、SHA及结构等价；此外producer仍须完整可证明的无效果/无未来效果来源。HTTP成功、超时、NOT_OBSERVED或缺源不构成此证明。UNKNOWN恢复只查询同原command/intent，不能release、重复dispatch或替换经济分支。FROZEN-R8旧pin与判定保留原包，current-v1不覆盖它。

## 根补丁复用与11项验收

PR两项、九Problem别名、NEW_READY/旧父键、WN11 projection和MULTI before/after由`../fixtures/bounded-patches.json`提供本次同版本补丁。本目录workpart直接引用它们，并在构建时验证原SHA和可应用preimage；不复制第二份补丁。新的9个Problem三元组顶层code=issues[].code，type为小写短横code URN，原HTTP/status/fence及FE恢复语义不变。冻结旧body继续原样回放。

NEW_READY三CAS绑定同阶段完整保护集；旧78/79关闭只引用OLD_END旧plan，不使用后续43 OUTPUT。WN11-04操作31生产partition_projection并先于23提供其实际receipt，28仍生产C06响应。MULTI-02两个ledger的业务revision/head_version/projection_revision取保护值+1，更新时刻取同Uow事件；opaque row_version仅取provider OUTPUT，四CAS原子提交，未知结果查询原intent。

acceptance.json逐项列正向与负向场景。validation.json只报告本次本地数据派生、摘要及源/结构检查；C#/TS独立编译证据另见codec-compile-validation.json，仅在最终派生SHA相同时复用，首轮失败保留。HTTP/业务/DB/Owner集成均NOT_RUN。设计决定完整，不意味着历史已接受或实现获准。
'''


def build_workpart():
    root_patch_path = ROOT / 'remediation/assets/fixtures/bounded-patches.json'
    root_bundle = json.loads(root_patch_path.read_bytes())
    root_specs = [p for p in root_bundle['patches'] if set(p['source_finding_ids']) & DECISIONS.keys()]
    root_results = []
    for spec in root_specs:
        raw = Path(spec['source_path']).read_bytes()
        assert sha(raw) == spec['source_sha256']
        after = apply_operations(raw, spec['operations'])
        json.loads(after)
        root_results.append({'source_path': spec['source_path'], 'derived_sha256': sha(after), 'source_finding_ids': spec['source_finding_ids'], 'operations': len(spec['operations'])})
    assert len(root_results) == 6
    MANIFEST['root_reused_patches'] = {'path': root_patch_path.relative_to(ROOT).as_posix(), 'sha256': sha(root_patch_path.read_bytes()), 'results': root_results}
    check('six-root-commercial-patches-preimage-and-JSON', {'patches': 6, 'operations': sum(p['operations'] for p in root_results), 'reexecuted': 'exact in-memory data application only'})
    negatives = 0
    for spec in PATCHES + root_specs:
        raw = Path(spec['source_path']).read_bytes()
        op = copy.deepcopy(spec['operations'][0])
        if op['op'] == 'replace_text':
            op['expected_count'] += 1
        else:
            op['before'] = {'wrong_version_preimage': True}
        try:
            apply_operations(raw, [op])
        except (ValueError, AssertionError):
            negatives += 1
        else:
            raise AssertionError('changed precondition was incorrectly accepted')
    check('bounded-patch-negative-preimages', {'rejected': negatives})
    findings_src = json.loads((ROOT / 'evidence/commercial-integration-findings.json').read_bytes())['findings']
    findings, acceptance, excerpts = [], [], []
    for i, (fid, (decision, negative)) in enumerate(DECISIONS.items(), 1):
        source = next(f for f in findings_src if f['finding_id'] == fid)
        evidence = []
        for item in source['evidence']:
            path = Path(item['source_path'])
            raw = path.read_bytes()
            lines = raw.decode('utf-8-sig').splitlines()
            line = item.get('line', 1)
            end = min(line + 20, len(lines))
            selected = '\n'.join(lines[line - 1:end])
            ev = {'source_path': path.as_posix(), 'sha256': sha(raw), 'line': line, 'end_line': end,
                  'reading': 'Read exact original selected range; see selected_range_sha256 in derivation manifest.'}
            if 'json_pointer' in item:
                pointer(json.loads(raw), item['json_pointer'])
                ev['json_pointer'] = item['json_pointer']
            evidence.append(ev)
            excerpts.append({**ev, 'selected_range_sha256': sha(selected.encode('utf-8'))})
        own = [p for p in PATCHES if fid in p['source_finding_ids']]
        reused = [p for p in root_specs if fid in p['source_finding_ids']]
        assert own or reused
        for spec in own:
            evidence.append({'source_path': spec['source_path'], 'sha256': spec['source_sha256'], 'line': 1,
                             'reading': 'Full bytes read as data; exact bounded text contexts/JSON pointers and source identities in patch; original programs never executed.'})
        outputs = ['remediation/assets/commercial-fixtures/current-v1-adoption.md', 'remediation/assets/commercial-fixtures/derivation-manifest.json',
                   'remediation/assets/commercial-fixtures/acceptance.json', 'remediation/assets/commercial-fixtures/validation.json']
        if own:
            outputs.append('remediation/assets/commercial-fixtures/bounded-patches.json')
        if reused:
            outputs.append('remediation/assets/fixtures/bounded-patches.json')
        tests = [{'id': f'AC-W5-COM-{i:02}-P', 'scenario': '精确原版本应用本次有界补丁并验证派生数据/来源preimage',
                  'expected': decision, 'verification_level': 'LOCAL_DESIGN_DATA', 'result': 'PASS'},
                 {'id': f'AC-W5-COM-{i:02}-N', 'scenario': '错误版本或错误preimage的有界补丁输入', 'expected': '必须拒绝，不能模糊套用本版本补丁。',
                  'verification_level': 'LOCAL_DESIGN_DATA', 'result': 'PASS'},
                 {'id': f'AC-W5-COM-{i:02}-R', 'scenario': '真实消费者负向契约/恢复门', 'expected': negative,
                  'verification_level': 'REQUIRED_RUNTIME_ADOPTION', 'result': 'NOT_RUN'}]
        acceptance.extend(tests)
        findings.append({'issue_id': fid, 'source_finding_ids': [fid], 'batch': 'W5',
                         'classification': 'INTENTIONAL_DIFFERENCE' if 'FROZEN-R8' in fid else 'ARTIFACT_CONFLICT',
                         'targets': source['targets'], 'source_version': REVISION, 'decision': decision,
                         'preserved_constraints': ['原归档字节/历史接受不变', '仅新版本设计数据；不是runtime证明', '真实Owner/权限/Store缺源维持fence'],
                         'consumer_adoption': {'required': negative, 'runtime_status': 'UNPROVEN', 'design_acceptance_is_runtime_adoption': False},
                         'evidence': evidence, 'outputs': outputs, 'acceptance': tests,
                         'design_status': 'DESIGN_RESOLVED', 'actual_adoption_status': 'UNPROVEN', 'implementation_status': 'NOT_IMPLEMENTED',
                         'verification_status': 'LOCAL_DESIGN_DATA_PASS_RUNTIME_NOT_RUN'})
    MANIFEST['selected_original_read_ranges'] = excerpts
    return findings, acceptance


def main():
    build_return()
    build_est()
    build_money()
    build_openapi_codec()
    findings, acceptance = build_workpart()
    # Revalidate exact derived bytes and verify that original raw identities stayed immutable.
    for spec in PATCHES:
        raw = Path(spec['source_path']).read_bytes()
        assert sha(raw) == spec['source_sha256']
        result = apply_operations(raw, spec['operations'])
        assert sha(result) == spec['expected_derived_sha256'] and len(result) == spec['expected_derived_bytes']
    write_json(OUT / 'bounded-patches.json', {'revision': REVISION, 'role': 'bounded_design_data_revision_not_original', 'patches': PATCHES})
    write_json(OUT / 'derivation-manifest.json', {'revision': REVISION, 'design_status': 'CURRENT_TASK_DESIGN_SUPPLEMENT', 'actual_adoption_status': 'UNPROVEN', **MANIFEST})
    write_json(OUT / 'acceptance.json', {'revision': REVISION, 'acceptance': acceptance})
    (OUT / 'current-v1-adoption.md').write_text(adoption_text(), encoding='utf-8', newline='\n')
    write_json(OUT / 'validation.json', {'revision': REVISION, 'result': 'PASS', 'checks': CHECKS, 'patch_count': len(PATCHES),
                                        'operations': sum(len(p['operations']) for p in PATCHES),
                                        'source_files_rechecked': len(PATCHES), 'original_programs_executed': False,
                                        'derived_code_compilation': 'SEPARATE_HASH_BOUND_REPORT_codec-compile-validation.json', 'runtime_acceptance': 'NOT_RUN', 'actual_adoption_status': 'UNPROVEN'})
    assets = [{'path': p.relative_to(ROOT).as_posix(), 'sha256': sha(p.read_bytes()), 'bytes': p.stat().st_size,
               'role': 'CURRENT_TASK_DESIGN_DATA_NOT_RUNTIME_EVIDENCE'} for p in sorted(OUT.iterdir()) if p.is_file()]
    write_json(ROOT / 'remediation/workparts/commercial-fixtures.json', {'revision': REVISION, 'findings': findings, 'assets': assets})
    print(json.dumps({'result': 'PASS', 'patches': len(PATCHES), 'operations': sum(len(p['operations']) for p in PATCHES), 'findings': len(findings), 'checks': len(CHECKS)}, ensure_ascii=True))


if __name__ == '__main__':
    main()
