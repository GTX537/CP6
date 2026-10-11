"""Build bounded design-data patches; never execute or overwrite archive programs."""
import argparse
import copy
import hashlib
import json
from pathlib import Path


def get(doc, path):
    for token in path.lstrip('/').split('/'):
        key = token.replace('~1', '/').replace('~0', '~')
        doc = doc[int(key)] if isinstance(doc, list) else doc[key]
    return doc


def walk(value, path=''):
    yield path, value
    pairs = value.items() if isinstance(value, dict) else enumerate(value) if isinstance(value, list) else []
    for k, v in pairs:
        yield from walk(v, path + '/' + str(k).replace('~', '~0').replace('/', '~1'))


def build(handbook, archive, cache):
    patches = []
    originals = archive / '完整成果/docs/originals'

    def original(prefix):
        found = list(originals.glob(prefix + '*'))
        assert len(found) == 1, (prefix, len(found))
        return found[0]

    def load(path):
        return json.loads(path.read_text(encoding='utf-8-sig'))

    def replace(doc, ptr, value):
        return {'op': 'replace_json', 'path': ptr, 'before': get(doc, ptr), 'after': value}

    def save(path, ids, ops, decision):
        assert ops, (path, ids)
        patches.append({'source_path': path.as_posix(), 'source_sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'source_finding_ids': ids, 'decision': decision, 'operations': ops})

    # Frozen PR authority and preview timestamps; unrelated revision3 examples stay.
    p = cache / 'commercial-cache/pr/R3/NORMATIVE-EXAMPLES.json'
    d = load(p)
    root = '/scenarios/0/objects/PO.ClaimPrepareRequest/grants/0'
    old = get(d, root + '/responsibility/authorizationScopeId')
    assert old.endswith(':3')
    ops = [replace(d, root + '/responsibility/authorizationScopeId', old[:-1] + '2'), replace(d, root + '/sourceBusinessVersion', '2')]
    for name, infix in [('RestructureResultMerge', ''), ('RestructureResultMergeReplay', ''), ('RestructureCommandQueryResultMerge', '/result')]:
        ptr = '/scenarios/7/objects/' + name + infix + '/command/recordedAt'
        assert get(d, ptr) == '2026-10-01T12:08:00.000Z'
        ops.append(replace(d, ptr, '2026-10-01T11:28:00.000Z'))
    save(p, ['COM-PR-N01-AUTHORITY-VERSION', 'COM-PR-N08-PREVIEW-TIMELINE'], ops, 'N01 exact bound approval2; N08 first Tx before expiry, replay retains original recordedAt. Bundle/payload hashes unaffected.')

    # New ERP-CREDIT problem version, with exact old wire retained in archive.
    p = original('c6/c69762861155ed95'); d = load(p); ops = []
    codes = ['BLOCKED_DEPENDENCY', 'CORRECTION_INCREASE_INSUFFICIENT', 'EFFECTIVE_PROJECTION_CHANGED', 'FIN_RECOVERY_SOURCE_UNPROVEN', 'LEGACY_DATABASE_SCOPE_UNPROVEN', 'LEGACY_WRITER_INVENTORY_INCOMPLETE', 'ORIGINAL_FULFILMENT_MISMATCH', 'REFUND_AUTHORIZATION_EXHAUSTED', 'UOM_NOT_EXACT']
    for code in codes:
        root = '/$defs/Problem_' + code + '/properties/'
        assert get(d, root + 'issues/items/properties/code/const') == 'HEAD_CHANGED'
        ops += [replace(d, root + 'issues/items/properties/code/const', code), replace(d, root + 'type/const', 'urn:cp6:erp-credit:problem:' + code.lower().replace('_', '-'))]
    save(p, ['COM-CREDIT-PROBLEM-CODE-ALIASES'], ops, 'Current-v1 problem triplets align specific code/type/issues; original statuses and retryAdvice preserved. New codec must pin derived registry SHA.')

    credit = cache / 'commercial-cache/credit-stage100/members/candidate'
    p = credit / 'CURRENT/03_COMPLETE_NORMAL_ROWS_OUTPUT_AND_CURRENT_BINDINGS.json'; d = load(p); ops = []
    for index in [71, 72, 73]:
        prefix = f'/normalOperations/{index}'
        assert get(d, prefix + '/sourcePhase') == 'COMMERCIAL_TERMINAL'
        ops += [replace(d, prefix + '/sourcePhase', 'NEW_READY'), replace(d, prefix + '/completeSourceGuard/phase', 'NEW_READY')]
        for ptr, val in walk(get(d, prefix), prefix):
            if isinstance(val, str) and val.startswith('WN14.COMMERCIAL_TERMINAL.ProtectedFullParentSet.'):
                ops.append(replace(d, ptr, val.replace('WN14.COMMERCIAL_TERMINAL.', 'WN14.NEW_READY.', 1)))
    for index in [77, 78]:
        prefix = f'/normalOperations/{index}/exactPkAndFkBindings/foreignKeys/0/compositeColumnMap'
        for i, row in enumerate(get(d, prefix)):
            old = row['parentValue']; assert old['ordinal'] == 43
            ops.append(replace(d, prefix + f'/{i}/parentValue', {'cellKind': 'SOURCE_CELL', 'column': old['column'], 'resolveOnlyUnderActualQualifiedSameSource': True, 'source': 'WN14.OLD_END.ProtectedFullParentSet.ec39_general_correction_plan'}))
    save(p, ['COM-CREDIT-NORMAL-PHASE-AND-OLD-PARENT-MAPPING'], ops, 'NEW_READY roots use own phase protected set. OLD_END roots bind exact old-plan six-column parent, never subsequent new43 output.')
    p = credit / 'CURRENT/10_COMPLETE_TYPED_PRODUCER_FIELD_AND_PHASE_INPUT_BINDINGS.json'; d = load(p)
    ops = [replace(d, f'/operations/{i}/phase', 'NEW_READY') for i in [72, 73, 74]]
    save(p, ['COM-CREDIT-NORMAL-PHASE-AND-OLD-PARENT-MAPPING'], ops, 'Typed phase agrees with complete WN14_NEW_READY execution order.')

    p = credit / 'RECOVERY/WN11-04_COMPLETE_SOURCE_LEDGER_AND_CUT.json'; d = load(p); ops = []
    for ledger in ['attemptWriteLedger', 'newWriteLedger']:
        base = f'/{ledger}/30/'
        ops.append(replace(d, base + 'mutationTarget', 'StoreActualPutIfAbsent.wn11_existing_target_partition_projection'))
        ops.append(replace(d, base + 'branchSavedReplyProducer', 'Original closed FinancialPartitionProjection producer for wn11_existing_target_partition_projection; desired=retained net12/tax1.2/gross13.2; independently qualified original target00ef plus old retained11; dispatch=0; Store receipt is consumed by WN11-04:23 and never substitutes for the C06 saved response receipt.'))
    save(p, ['COM-CREDIT-WN11-04-PROJECTION-PRODUCER-METADATA'], ops, 'Ordinal31 is partition projection producer; ordinal28 original C06 saved200 unchanged; no new Finance effect.')

    p = credit / 'RECOVERY/MULTI-02_COMPLETE_SOURCE_LEDGER_AND_CUT.json'; d = load(p); ops = []
    for ledger in ['attemptWriteLedger', 'newWriteLedger']:
        for index, col in [(67, 'revision'), (69, 'head_version'), (70, 'projection_revision')]:
            ptr = f'/{ledger}/{index}/fullInput/{col}'; old = get(d, ptr)
            value = {'cellKind': 'DERIVED_TYPED_CELL', 'inputs': [old, {'cellKind': 'LITERAL', 'value': 1}], 'mustResolveFromCompleteQualifiedProducer': True, 'operator': 'INTEGER_ADD_EXACT', 'physicalType': 'bigint', 'rule': 'opaque row_version remains provider-only; integer business revision increases once'}
            ops.append(replace(d, ptr, value))
        for index in [67, 70]:
            ptr = f'/{ledger}/{index}/fullInput/updated_at'
            value = {'cellKind': 'DERIVED_TYPED_CELL', 'inputs': [{'cellKind': 'SOURCE_CELL', 'column': 'updated_at', 'resolveOnlyUnderActualQualifiedSameSource': True, 'source': 'MULTI-02.ORIGINAL_INTENT_RECOVERY.eventClock'}], 'mustResolveFromCompleteQualifiedProducer': True, 'operator': 'EXACT_PHASE_EVENT_CLOCK', 'physicalType': 'datetime2(3)', 'rule': 'one same-Uow observer timestamp after qualified capture and before known commit; preserve old recorded_at'}
            ops.append(replace(d, ptr, value))
    save(p, ['COM-CREDIT-MULTI-02-BEFORE-AFTER-VERSION-MAPPING'], ops, 'Business revisions advance once, both updated_at use one new server eventClock; before rows, provider row_version and original owner receipt unchanged. Four CAS atomic.')

    # INQ full fixture: correct the intended test preconditions without faking runs.
    p = original('89/8907803dd2e284d0'); d = load(p); ops = []
    ops.append(replace(d, '/families/24/steps/5/transport/proofLocation', 'X-Download-Proof header; Authorization bearer authenticates the CRM session; never URL/log'))
    ops.append(replace(d, '/families/25/steps/1/uiExpectation/configuration/etag', '"AAAAAAAAAAI="'))
    for index, field, key in [(2, 'contactName', 'fx27-long-name-0001'), (3, 'need', 'fx27-long-need-0001')]:
        prefix = f'/families/26/steps/{index}'
        ops += [replace(d, prefix + '/request/headers/Idempotency-Key', key), replace(d, prefix + '/expectedHttp/status', 422), replace(d, prefix + '/expectedHttp/body/status', 422), replace(d, prefix + '/expectedHttp/body/code', 'crm.validation_failed'), replace(d, prefix + '/expectedHttp/body/fieldErrors', [{'field': field, 'code': 'max_length'}])]
        case = get(d, prefix)
        for name, val in [('derivedBy', 'current-task design fixture correction; NOT_RUN'), ('mutation', {'kind': 'design-corrected', 'validatedBy': 'NOT_RUN'})]:
            if name in case:
                ops.append(replace(d, prefix + '/' + name, val))
    save(p, ['COM-INQ-GENERATED-CONTRACT-ADOPTION'], ops, 'max_length is new field error subtype; valid idempotency key exposes intended 201/4001 codepoint rejection; database/write expectations unchanged, not an executed-handler claim.')

    # Preserve signed legacy values in the exact stored contract.
    p = original('43/43376e6abeb9fa95'); d = load(p)
    ops = [replace(d, '/$defs/StoredLegacyAnchorRecord/properties/' + field, {'$ref': '#/$defs/SignedDecimal'}) for field in ['ReportedRequiredQty', 'ReportedAvailableQty']]
    save(p, ['PLAN-IF-006'], ops, 'Only legacy original fields preserve signed values. Formal quantity domain remains unchanged.')
    p = original('cd/cdc7a7be21c83238'); d = load(p)
    ptr = '/ports/3/contractBody/profileSchema/properties/branches/maxItems'
    assert get(d, ptr) == 3
    canonical = lambda v: json.dumps(v, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode('utf-8')
    port = d['ports'][3]
    assert hashlib.sha256(canonical(port['contractBody'])).hexdigest() == port['contractDigest']
    assert hashlib.sha256(canonical(port['closedSchemaSet'])).hexdigest() == port['bodySchemaDigest']
    body = copy.deepcopy(port['contractBody']); del body['profileSchema']['properties']['branches']['maxItems']
    contract_digest = hashlib.sha256(canonical(body)).hexdigest()
    new_version = '1.2-design-revision-20261011'
    ops = [{'op': 'remove_json', 'path': ptr, 'before': 3}, replace(d, '/ports/3/contractVersion', new_version), replace(d, '/ports/3/contractDigest', contract_digest)]
    save(p, ['PLAN-IF-009'], ops, 'New producer contract treats branches as per-portion entries, not three branch kinds; exact closed schema unchanged, contractBody SHA recomputed, version explicit. Historical pins preserved; all new consumer registration must choose this complete new tuple.')
    pins = {'revision':'CP6-DESIGN-REVISION-20261011/R1', 'source_path':p.as_posix(), 'source_sha256':hashlib.sha256(p.read_bytes()).hexdigest(), 'producer':'PLAN-POL', 'consumers':['PLAN-PO'], 'old_contract':{k:port[k] for k in ['contractId','contractVersion','contractDigest','bodySchemaDigest']}, 'new_contract':{'contractId':port['contractId'],'contractVersion':new_version,'contractDigest':contract_digest,'bodySchemaDigest':port['bodySchemaDigest']}, 'proof':'old/new contractDigest = SHA256(canonical complete contractBody); closedSchemaSet remains exact unchanged SHA', 'registration_status':'UNPROVEN_REQUIRED_FENCED', 'historical_evidence':'old grant/provider and accepted packet stay bound to old contract, never relabelled as new authority', 'positive':'four distinct portion entries are representable; full allocation tuple set must match', 'negative':'missing/duplicate/extra allocation tuple or old provider pin rejected; no truncation to three'}
    pinpath = handbook / 'remediation/assets/fixtures/owner-profile-successor-pin.json'
    pinpath.parent.mkdir(parents=True, exist_ok=True)
    pinpath.write_text(json.dumps(pins, ensure_ascii=False, indent=2)+'\n',encoding='utf-8')
    po = cache / 'root-planning-cache/plan-po-cp22/members'
    p = po / 'PLAN_PO_R3_CP18_OPENAPI.json'; d = load(p); api = load(po / 'PLAN_PO_R3_CP18_API_REGISTRY.json')
    base = '/paths/~1conversions~1{conversionLegId}~1owner-steps/post/responses/503'
    code_nodes = [(ptr, val) for ptr, val in walk(get(d, base), base) if ptr.endswith('/code/enum')]
    assert len(code_nodes) == 1, len(code_nodes)
    ptr, old = code_nodes[0]
    missing = ['CREATION_CLOSURE_REQUIRED_FENCED', 'CREATION_CLOSURE_TRANSPORT_INVALID', 'OWNER_RESPONSE_STATUS_INVALID']
    expected = json.dumps(api['apis'][13]['problems']['503'])
    assert all(code in expected and code not in old for code in missing)
    save(p, ['PLAN-IF-012'], [replace(d, ptr, old + missing)], 'Project exact API_REGISTRY PO15 503 outcomes; Owner closure behavior unchanged.')
    finding = next(f for f in load(handbook / 'evidence/root-planning-integration-findings.json')['findings'] if f.get('finding_id', f.get('id')) == 'PLAN-IF-013')
    p = Path(finding['source_path']); d = load(p)
    ops = [replace(d, x['wrapper_pointer'], get(d, x['row_pointer'])) for x in finding['occurrences']]
    assert len(ops) == 18
    save(p, ['PLAN-IF-013'], ops, 'Align wrapper keys with actual complete primary key; all real modeled rows unchanged.')

    output = handbook / 'remediation/assets/fixtures/bounded-patches.json'
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({'revision': 'CP6-DESIGN-REVISION-20261011/R1', 'role': 'bounded_design_data_revision_not_original', 'patches': patches}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    return {'source_files': len(patches), 'operations': sum(len(p['operations']) for p in patches), 'path': str(output)}


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--handbook', type=Path, required=True)
    p.add_argument('--archive', type=Path, required=True)
    p.add_argument('--cache', type=Path, required=True)
    a = p.parse_args()
    print(json.dumps(build(a.handbook, a.archive, a.cache), ensure_ascii=False))
