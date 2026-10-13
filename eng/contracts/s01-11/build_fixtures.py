"""Rebuild constructed contract unit fixtures from repository-owned pinned schemas.

These fixtures prove codec behavior only. They are not Owner commands, receipts,
issuer/Store attestations, or business acceptance evidence.
"""
from pathlib import Path
import base64, copy, hashlib, json

BASE = Path(__file__).resolve().parent
WMS = 'ERP-CREDIT-01_WMS-RMA-R3-EXACT.schema.json'
AR = 'ERP-CREDIT-01_FIN-AR-R2-EXACT.schema.json'
REGISTRY = 'ERP-CREDIT-01_TYPE-REGISTRY.json'
ROOTS = [(WMS, r) for r in ['CreditOwnerRequest', 'CreditOwnerQueryRequest', 'CreditOwnerQueryResult', 'CommercialOutcomeRef']] + [(AR, r) for r in ['CreditAuthorityReadRequest', 'CreditAuthorityReadResult', 'CreditSelectionsQuery', 'CreditSelectionPage', 'CreditPrepare', 'PostInput', 'SafeOperationDetail', 'CommandResult']] + [(REGISTRY, r) for r in ['TerminalCommandReceipt', 'FinArObservation']]
ID = '00000000-0000-4000-8000-000000000001'
HASH = 'a' * 64
NOW = '2026-10-13T00:00:00.000Z'
PRIMITIVES = dict(Guid=ID, Id=ID, Key='fixture', OpaqueId='fixture', Seq='1', RV='AQ==', RowVersion='AQ==', Hash=HASH, Digest=HASH, D8='1.0', NonNegativeD8='1.0', DecimalQty='1', SignedDecimal='1.0', Currency='USD', DateOnly='2026-10-13', Utc=NOW, Instant=NOW)

docs = {WMS: json.loads((BASE/'assets/native'/WMS).read_text()), AR: json.loads((BASE/'assets/native'/AR).read_text()), REGISTRY: json.loads((BASE/'assets/current'/REGISTRY).read_text())}
def construct(schema, current):
    if '$ref' in schema:
        filename, pointer = schema['$ref'].split('#')
        filename = filename.removeprefix('./') or current
        name = pointer.split('/')[-1]
        if name in PRIMITIVES: return PRIMITIVES[name]
        s = docs[filename]
        for part in pointer.strip('/').split('/'): s = s[part]
        value = construct(s, filename)
        if name == 'Range': value.update({'from': '0', 'to': '1'})
        if name == 'Slice': value.update({'from': '0', 'toExclusive': '1'})
        if name == 'InvoiceAllocation': value.update(net={'value':'1.0','currency':'USD'}, tax={'value':'-0','currency':'USD'}, gross={'value':'1.0','currency':'USD'})
        if name == 'CreditAuthorityBody': value.update(maxQty='1', maxNet='1.0', maxTax='0', maxGross='1.0')
        return value
    if 'const' in schema: return schema['const']
    if 'enum' in schema: return schema['enum'][0]
    if 'allOf' in schema and 'properties' not in schema:
        value = {}
        for child in schema['allOf']: value.update(construct(child, current))
        return value
    if 'oneOf' in schema: return construct(schema['oneOf'][0], current)
    kind = schema.get('type', 'object' if 'properties' in schema else None)
    if kind == 'object': return {k: construct(schema['properties'][k], current) for k in schema.get('required', [])}
    if kind == 'array': return [construct(schema['items'], current) for _ in range(schema.get('minItems', 0))]
    if kind == 'null': return None
    if kind == 'boolean': return False
    if kind == 'integer': return schema.get('minimum', 0)
    if kind == 'string':
        if schema.get('format') == 'uuid': return ID
        if schema.get('format') == 'date': return '2026-10-13'
        if schema.get('format') == 'date-time': return NOW
        return 'fixture'
    raise ValueError(schema)

def compact(value): return json.dumps(value, ensure_ascii=False, separators=(',', ':'))
objects = {root: construct({'$ref':'#/$defs/'+root}, file) for file, root in ROOTS}
objects['SafeOperationDetail']['expectedMemberCount'] = 0
objects['CreditOwnerRequest']['expectedOwnerHeadRef'] = None
objects['CreditOwnerQueryRequest']['queryIdentity']['expectedHeadRef'] = None
objects['CreditOwnerQueryRequest']['queryIdentity']['expectedResultDigest'] = None
objects['CommercialOutcomeRef']['previousOutcomeRef'] = None
objects['CreditOwnerQueryResult']['result'] = copy.deepcopy(objects['CommercialOutcomeRef'])
objects['CommandResult']['error'] = None
objects['FinArObservation']['exactCommandResult'] = copy.deepcopy(objects['CommandResult'])
objects['FinArObservation']['ownerResultRef'] = copy.deepcopy(objects['CommandResult']['resultRef'])
objects['TerminalCommandReceipt']['problemDigest'] = None
objects['TerminalCommandReceipt']['localNoEffectEvidenceId'] = None
objects['TerminalCommandReceipt']['ownerCommandResultDigest'] = None

cases = []
def case(name, root, value, accepted, profiles=('current','frozen'), origin='CONSTRUCTED_CONTRACT_UNIT_INPUT', source_pointer=None):
    for profile in profiles: cases.append(dict(id=profile+'-'+name, profile=profile, root=root, raw=compact(value), accepted=accepted, origin=origin, source_pointer=source_pointer))
for root, value in objects.items():
    case('full-'+root, root, value, True)
    missing = copy.deepcopy(value); missing.pop(next(iter(value)))
    case('required-'+root, root, missing, False)
    extra = copy.deepcopy(value); extra['extra'] = 1
    case('closed-'+root, root, extra, False)
for value in ['-0', '1.0', '-123456789012345678901234567890.12345678', '999999999999999999999999999999.99999999']:
    case('wms-'+value, 'Money', {'value':value,'currency':'USD'}, True)
for value in ['+1','01','1e0','1.123456789','1000000000000000000000000000000','NaN','1\n']:
    case('wms-invalid-'+repr(value), 'Money', {'value':value,'currency':'USD'}, False)
case('money-alias', 'Money', {'amount':'1','currency':'USD'}, False)
case('money-extra', 'Money', {'value':'1','currency':'USD','amount':'1'}, False)
case('money-number', 'Money', {'value':1,'currency':'USD'}, False)
for value in ['-0','1.0','-12345678901234567890.12345678']:
    case('ar-'+value,'D8', value, True)
for value in ['123456789012345678901','1.123456789','+1','1e0','01','1\n']:
    case('ar-invalid-'+repr(value),'D8', value, False)
for outcome, has_ref, current_ok, frozen_ok in [('APPLIED',False,False,False),('REJECTED',True,False,False),('NO_CHANGE',True,True,False),('NO_CHANGE',False,True,True)]:
    value=copy.deepcopy(objects['CommandResult']);value['outcome']=outcome
    if not has_ref:value['resultRef']=None
    case(f'outcome-{outcome}-{has_ref}','CommandResult',value,current_ok,('current',))
    case(f'outcome-{outcome}-{has_ref}','CommandResult',value,frozen_ok,('frozen',))
found=copy.deepcopy(objects['FinArObservation']);found['exactCommandResult']['outcome']='NO_CHANGE'
case('found-no-change','FinArObservation',found,True,('current',));case('found-no-change','FinArObservation',found,False,('frozen',))
unknown=dict(state='UNKNOWN',queryIdentityDigest=HASH,exactCommandResult=objects['CommandResult'],queryRef='fixture',observedAt=NOW)
case('unproven-completed','FinArObservation',unknown,True,('current',));case('unproven-completed','FinArObservation',unknown,False,('frozen',))
no_effect=copy.deepcopy(objects['CommandResult']);no_effect.update(outcome='NO_CHANGE',resultRef=None)
encoded=compact(dict(reversed(list(no_effect.items())))).encode()
observation=dict(state='FAILED_NO_EFFECT',queryIdentityDigest=HASH,exactCommandResult=no_effect,exactCommandResultCanonicalBase64=base64.b64encode(encoded).decode(),localEvidenceId=ID,exactCommandResultDigest=hashlib.sha256(encoded).hexdigest(),observedAt=NOW)
case('no-effect-reordered','FinArObservation',observation,True)
bad=copy.deepcopy(observation);bad['exactCommandResultDigest']='0'*64;case('no-effect-hash','FinArObservation',bad,False)
bad=copy.deepcopy(observation);bad['exactCommandResult']['queryRef']='changed';case('no-effect-projection','FinArObservation',bad,False)
duplicate=encoded[:-1]+b',"kind":"COMPLETED"}'
bad=copy.deepcopy(observation);bad['exactCommandResultCanonicalBase64']=base64.b64encode(duplicate).decode();bad['exactCommandResultDigest']=hashlib.sha256(duplicate).hexdigest();case('no-effect-duplicate','FinArObservation',bad,False)
bad=copy.deepcopy(objects['FinArObservation']);bad['ownerResultRef']['version']='other';case('found-projection','FinArObservation',bad,False)
bad=copy.deepcopy(objects['CreditOwnerRequest']);bad['requestedComponents']=['CREDIT','CREDIT'];case('unique','CreditOwnerRequest',bad,False)
bad=copy.deepcopy(objects['CreditOwnerRequest']);bad['eligibleQuantity']['value']='2';case('range-measure','CreditOwnerRequest',bad,False)
bad=copy.deepcopy(objects['CommercialOutcomeRef']);bad['components'][0]['invoiceAllocations'][0]['gross']['value']='2';case('money-sum','CommercialOutcomeRef',bad,False)
bad=copy.deepcopy(objects['CommercialOutcomeRef']);bad['components'][0]['invoiceAllocations'][0]['gross']['currency']='EUR';case('money-currency','CommercialOutcomeRef',bad,False)
bad=copy.deepcopy(objects['CommercialOutcomeRef']);bad['components'][0]['invoiceAllocations'][0]['coveredRanges'][0]['to']='0';case('range-empty','CommercialOutcomeRef',bad,False)
bad=copy.deepcopy(objects['CommercialOutcomeRef']);bad['components']*=2;case('range-overlap','CommercialOutcomeRef',bad,False)
bad=copy.deepcopy(objects['SafeOperationDetail']);bad['expectedMemberCount']=1;case('members','SafeOperationDetail',bad,False)
bad=copy.deepcopy(objects['CreditAuthorityReadResult']);bad['value']['body']['maxGross']='2';case('ar-sum','CreditAuthorityReadResult',bad,False)
for raw in ['{"value":"1","value":"1","currency":"USD"}', '{"value":"1","\\u0076alue":"1","currency":"USD"}', '{"value":"1","currency":"USD","__proto__":{}}', '{"value":"1","currency":"USD","constructor":{}}', '{"value":"1","currency":"USD"} {}', '{"value":"1","currency":"USD",}', '{"value":"1","currency":"USD"/*x*/}']:
    for profile in ['current','frozen']:cases.append(dict(id=profile+'-strict-'+str(len(cases)),profile=profile,root='Money',raw=raw,accepted=False))
for token,ok in [('1.0',True),('1e0',True),('1.000000000000000000001',False),('9007199254740993',False),('1e999999999999999999999',False)]:
    raw=compact(objects['CreditSelectionsQuery']).replace('"limit":1','"limit":'+token)
    for profile in ['current','frozen']:cases.append(dict(id=profile+'-integer-'+token,profile=profile,root='CreditSelectionsQuery',raw=raw,accepted=ok))

# Additional complete-object invariants and nested wire behavior.
for field in ['problemDigest','localNoEffectEvidenceId','ownerCommandResultDigest']:
    bad=copy.deepcopy(objects['TerminalCommandReceipt']);bad[field]='invented';case('receipt-null-only-'+field,'TerminalCommandReceipt',bad,False)
    bad.pop(field);case('receipt-null-required-'+field,'TerminalCommandReceipt',bad,False)
bad=copy.deepcopy(objects['CommercialOutcomeRef']);money=bad['components'][0]['invoiceAllocations'][0]['net'];money['amount']=money.pop('value');case('native-object-money-alias','CommercialOutcomeRef',bad,False)
large=copy.deepcopy(objects['CommercialOutcomeRef']);allocation=large['components'][0]['invoiceAllocations'][0]
allocation['net']['value']=allocation['gross']['value']='999999999999999999999999999999.99999999';case('native-object-30-digits','CommercialOutcomeRef',large,True)
bad=copy.deepcopy(objects['CreditOwnerQueryResult']);bad['result']['components'][0]['invoiceAllocations'][0]['gross']['value']='2';case('nested-money-invariant','CreditOwnerQueryResult',bad,False)
bad=copy.deepcopy(unknown);bad['exactCommandResult']['resultRef']=None;case('nested-current-command-semantics','FinArObservation',bad,False,('current',))
for state in ['APPLIED','NO_CHANGE','REJECTED']:
    bad=copy.deepcopy(objects['CommandResult']);bad.update(outcome=state,resultRef=None)
    v=dict(state='UNKNOWN',queryIdentityDigest=HASH,exactCommandResult=bad,queryRef='fixture',observedAt=NOW)
    case('unknown-'+state,'FinArObservation',v,state!='APPLIED',('current',))
bad=copy.deepcopy(objects['CreditOwnerRequest']);bad['requestedComponents']=['REFUND_AUTHORIZATION'];case('requested-native-array','CreditOwnerRequest',bad,True)
single=copy.deepcopy(objects['CommercialOutcomeRef']);single['components']=[dict(kind='RETURN_ONLY',componentRef=single['approvalRef'],coveredClaimRefs=[single['approvalRef']],coveredRanges=[{'from':'0','to':'1'}],quantity=copy.deepcopy(objects['CreditOwnerRequest']['eligibleQuantity']),reasonCode='fixture')]
case('singleton-native','CommercialOutcomeRef',single,True)
bad=copy.deepcopy(single);bad['components']*=2;case('conditional-maxItems-without-type','CommercialOutcomeRef',bad,False)
refund=copy.deepcopy(objects['CommercialOutcomeRef']);credit=refund['components'][0]
refund['components'].append(dict(kind='REFUND_AUTHORIZATION',componentRef={**credit['componentRef'],'id':'refund'},creditComponentRef=copy.deepcopy(credit['componentRef']),refundAuthorizationRef=copy.deepcopy(credit['approvalRef']),authorizedAmount={'value':'1.0','currency':'USD'},currency='USD',stableEffectKey='refund'))
case('refund-native','CommercialOutcomeRef',refund,True)
bad=copy.deepcopy(refund);bad['components'][1]['authorizedAmount']['value']='2';case('refund-cap','CommercialOutcomeRef',bad,False)
bad=copy.deepcopy(refund);bad['components']=bad['components'][1:];case('conditional-contains-credit','CommercialOutcomeRef',bad,False)
bad=copy.deepcopy(refund);bad['components'][1]['creditComponentRef']['id']='unknown';case('refund-link','CommercialOutcomeRef',bad,False)
bad=copy.deepcopy(objects['CreditAuthorityReadRequest']);bad['slice']['toExclusive']='0';case('ar-native-slice','CreditAuthorityReadRequest',bad,False)
for date in ['2026-02-30','0000-01-01']:
    bad=copy.deepcopy(objects['CreditSelectionsQuery']);bad['effectiveDate']=date;case('calendar-'+date,'CreditSelectionsQuery',bad,False)
for stamp,accepted in [('2024-02-29T23:59:59.123456789Z',True),('2026-02-30T00:00:00.000Z',False),('2026-10-13T24:00:00.000Z',False)]:
    bad=copy.deepcopy(objects['CreditOwnerRequest']);bad['occurredAt']=stamp;case('timestamp-'+stamp,'CreditOwnerRequest',bad,accepted)
for key in ['toString','hasOwnProperty','__proto__']:
    bad=copy.deepcopy(objects['PostInput']);bad[key]={};case('prototype-'+key,'PostInput',bad,False)
bad=copy.deepcopy(objects['PostInput']);bad['expectedRowVersion']='AQ==\n';case('base64-whitespace','PostInput',bad,False)
pair=copy.deepcopy(objects['CommercialOutcomeRef']);first=pair['components'][0];first['componentRef'].update(owner='A\0B',id='C')
second=copy.deepcopy(first);second['componentRef'].update(owner='A',id='B\0C');second['invoiceAllocations'][0]['coveredRanges']=[{'from':'1','to':'2'}];pair['components'].append(second)
case('structural-ref-key-no-NUL-collision','CommercialOutcomeRef',pair,True)

# Every native union alternative has a complete positive object and a required/null-only negative.
for index in range(1,6):
    value=construct(docs[WMS]['$defs']['CreditOwnerQueryResult']['oneOf'][index],WMS)
    case('query-branch-'+value['status'],'CreditOwnerQueryResult',value,True)
    bad=copy.deepcopy(value);bad['result']={};case('query-null-'+value['status'],'CreditOwnerQueryResult',bad,False)
for index in [1,2]:
    value=construct(docs[AR]['$defs']['CreditAuthorityReadResult']['oneOf'][index],AR)
    case('authority-branch-'+value['kind'],'CreditAuthorityReadResult',value,True)
    bad=copy.deepcopy(value);bad.pop('readKey');case('authority-required-'+value['kind'],'CreditAuthorityReadResult',bad,False)
for kind in ['UNPROVEN','UNKNOWN']:
    value=construct(docs[AR]['$defs']['CreditAuthorityReadResult']['oneOf'][2],AR);value['kind']=kind;case('authority-branch-'+kind,'CreditAuthorityReadResult',value,True)
for kind in ['PENDING','OUTCOME_UNKNOWN']:
    value=construct(docs[AR]['$defs']['CommandResult']['oneOf'][1],AR);value['kind']=kind;value['operationId']=None
    case('command-'+kind,'CommandResult',value,True)
    bad=copy.deepcopy(value);bad.pop('operationId');case('command-required-'+kind,'CommandResult',bad,False)
    if kind=='PENDING':
        observation=dict(state='PENDING',queryIdentityDigest=HASH,exactCommandResult=value,operationId=None,queryRef=value['queryRef'],observedAt=NOW)
        case('pending-projection','FinArObservation',observation,True)
        bad=copy.deepcopy(observation);bad['queryRef']='different';case('pending-projection-mismatch','FinArObservation',bad,False)
value=construct(docs[AR]['$defs']['CommandResult']['oneOf'][2],AR);case('not-observed','CommandResult',value,True)
bad=copy.deepcopy(value);bad['provesPermanentNoEffect']=True;case('not-observed-no-proof','CommandResult',bad,False)
for index in [1,2]:
    value=construct(docs[REGISTRY]['$defs']['TerminalCommandReceipt']['oneOf'][index],REGISTRY)
    case('terminal-'+value['outcome'],'TerminalCommandReceipt',value,True)
    bad=copy.deepcopy(value);bad['resultRef']=objects['TerminalCommandReceipt']['resultRef'];case('terminal-forbidden-'+value['outcome'],'TerminalCommandReceipt',bad,False)
for kind,index in [('REPLACEMENT',2),('SUPPLEMENTARY_SHIPMENT',2),('REJECTED',4)]:
    value=copy.deepcopy(objects['CommercialOutcomeRef']);component=construct(docs[WMS]['$defs']['CommercialOutcomeComponent']['oneOf'][index],WMS);component['kind']=kind;value['components']=[component]
    case('component-'+kind,'CommercialOutcomeRef',value,True)
    bad=copy.deepcopy(value);bad['components'][0]['quantity']['value']='2';case('component-quantity-'+kind,'CommercialOutcomeRef',bad,False)
value=copy.deepcopy(objects['CreditSelectionPage']);value['items']=[construct({'$ref':'#/$defs/CreditCandidateBundle'},AR)];case('nonempty-native-selection-page','CreditSelectionPage',value,True)
bad=copy.deepcopy(value);bad['items'][0]['isExecutionPermit']=True;case('selection-no-execution-permit','CreditSelectionPage',bad,False)
value=copy.deepcopy(objects['SafeOperationDetail']);value['members']=[construct({'$ref':'#/$defs/SafeOperationMember'},AR)];value['expectedMemberCount']=1;case('nonempty-native-operation','SafeOperationDetail',value,True)
bad=copy.deepcopy(value);bad['members'][0]['eventDisplay']={'state':'EMPTY','value':'fabricated'};case('cell-closed-union','SafeOperationDetail',bad,False)

# Selected CURRENT bodies are exact retained design bytes, not whole-C06 schema validation.
chain=json.loads((BASE/'fixtures/current-chain.json').read_text('utf-8'))
for body_key,pointers in chain['credit_money']['money_pointers'].items():
    body=json.loads((BASE/'fixtures/current-bodies'/f'{body_key}.json').read_text('utf-8'))
    for n,pointer in enumerate(pointers):
        value=body
        for part in pointer.strip('/').split('/'):value=value[int(part)] if isinstance(value,list) else value[part]
        case(f'selected-Money-{body_key}-{n}','Money',value,True,origin='SELECTED_CURRENT_DESIGN_PROJECTION',source_pointer=body_key+'.json#'+pointer)
        bad=copy.deepcopy(value);bad['amount']=bad.pop('value');case(f'selected-Money-alias-{body_key}-{n}','Money',bad,False)
body=json.loads((BASE/'fixtures/current-bodies/multi_mixed_complete_public_q14.json').read_text('utf-8'))
case('selected-native-outcome-projection','CommercialOutcomeRef',body['data']['outcome']['value'],True,origin='SELECTED_CURRENT_DESIGN_PROJECTION',source_pointer='multi_mixed_complete_public_q14.json#/data/outcome/value')
for root in ['EC_C01_COMMITTED_CommercialDraftView','EC_C02_COMMITTED_CommercialDraftView']:
    objects[root]=construct({'$ref':'#/$defs/'+root},REGISTRY)

(BASE/'fixtures').mkdir(exist_ok=True)
(BASE/'fixtures/native-objects.json').write_bytes((json.dumps({'origin':'CONSTRUCTED_CONTRACT_UNIT_INPUT','objects':objects},ensure_ascii=False,indent=2)+'\n').encode())
(BASE/'fixtures/cases.json').write_bytes((json.dumps({'origin':'CONTRACT_UNIT_AND_SELECTED_DESIGN_INPUTS_NOT_OWNER_PROOF','cases':cases},ensure_ascii=False,indent=2)+'\n').encode())
print(f'Constructed {len(cases)} codec cases; {len(objects)} complete objects.')
