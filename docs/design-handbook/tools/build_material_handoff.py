"""Author the new MAT/MRP adapter contract, without modifying native MAT wire."""
import argparse
import copy
import json
from pathlib import Path


def build(book):
    dst = book / 'remediation/assets/material-handoff'
    dst.mkdir(parents=True, exist_ok=True)
    def ref(name): return {'$ref': '#/$defs/' + name}
    def obj(props): return {'type': 'object', 'properties': props, 'required': list(props), 'additionalProperties': False}
    def arr(name): return {'type': 'array', 'items': ref(name), 'uniqueItems': True}
    def enum(*values): return {'enum': list(values)}
    def nullable(name): return {'anyOf': [ref(name), {'type': 'null'}]}
    defs = {
        'Id': {'type': 'string'},
        'Version': {'type': 'string', 'pattern': '^[0-9]+$'},
        'Digest': {'type': 'string', 'pattern': '^[0-9a-f]{64}$'},
        'Qty': {'type': 'string', 'pattern': '^[0-9]{1,13}(\\.[0-9]{1,8})?$'},
        'Ref': obj({k: ref('Id') for k in ['owner', 'id', 'version']}),
        'Evidence': obj({'ref': ref('Ref'), 'digest': ref('Digest')}),
        'Quantity': obj({'value': ref('Qty'), 'unitRef': ref('Ref')}),
        'Range': obj({'portionId': ref('Id'), 'from': ref('Qty'), 'to': ref('Qty')}),
        'Scope': obj({'scopeId': ref('Id'), 'scopeVersion': ref('Version'), 'ranges': arr('Range')}),
        'BasisKind': enum('NET_RESIDUAL', 'GROSS_WITH_EXPLICIT_OFFSETS'),
    }
    binding = {k: ref('Id') for k in ['rootWorkOrderId','workOrderId','snapshotId','materialLineId','materialObligationId','operationId']}
    binding.update({'snapshotDigest': ref('Digest'), 'purpose': enum('ENGINEERING','TRIAL','PILOT','PRODUCTION'), 'executionScope': ref('Scope')})
    binding.update({k: ref('Ref') for k in ['mbomOccurrenceRef','itemRef','siteRef','unitRef','usageBasisRef','lossRuleRef']})
    binding.update({'substitutionRuleRef': nullable('Ref'), 'transitionRef': ref('Evidence'), 'sourceProtectionRefs': arr('Evidence')})
    defs['MaterialBinding'] = obj(binding)
    defs['ResponsibilitySlice'] = obj({'shareKey': ref('Id'), 'sourceObligationRef': ref('Ref'), 'currentRepresentativeRef': ref('Ref'), 'scope': ref('Scope'), 'grossQuantity': ref('Quantity'), 'unitBasis': ref('Evidence'), 'originEvidence': ref('Evidence')})
    defs['Offset'] = obj({'offsetId': ref('Id'), 'shareKey': ref('Id'), 'originalFact': ref('Evidence'), 'scope': ref('Scope'), 'quantity': ref('Quantity'), 'category': enum('CONFIRMED_CONSUMED_NET','LINE_SIDE','RESERVED'), 'applicableNeedNode': ref('Ref'), 'qualification': ref('Evidence'), 'coverageMembers': {'type': 'array', 'items': ref('Id'), 'uniqueItems': True}, 'supersedesOffsetIds': {'type': 'array', 'items': ref('Id'), 'uniqueItems': True}, 'activeProtection': arr('Evidence')})
    defs['OffsetCoverageMember'] = obj({'memberId': ref('Id'), 'shareKey': ref('Id'), 'quantity': ref('Quantity'), 'scope': ref('Scope'), 'ownershipEvidence': ref('Evidence')})
    defs['UnresolvedAction'] = obj({'actionId': ref('Id'), 'nativeKind': ref('Id'), 'potentialQuantity': nullable('Quantity'), 'scope': ref('Scope'), 'disjointFromRequestedScopeProof': nullable('Evidence')})
    defs['Coverage'] = obj({'state': enum('COMPLETE','PARTIAL','UNKNOWN'), 'requestedScope': ref('Scope'), 'actualScope': ref('Scope'), 'cut': arr('Evidence'), 'completenessProof': nullable('Evidence'), 'missingScopes': arr('Scope')})
    defs['ReadRequest'] = obj({'schema': {'const': 'MAT-MRP-RESIDUAL-READ/1'}, 'requestKey': ref('Id'), 'mappingVersion': {'const':'1.0.0'}, 'bindingRef': ref('Evidence'), 'expectedMaterialRevision': ref('Version'), 'requestedScope': ref('Scope'), 'asKnownCut': arr('Evidence'), 'basisKind': ref('BasisKind')})
    defs['ResidualHandoff'] = obj({'schema': {'const': 'MAT-MRP-RESIDUAL/1'}, 'mappingVersion': {'const':'1.0.0'}, 'captureRef': ref('Evidence'), 'requestDigest': ref('Digest'), 'nativeRemainingBasisRef': ref('Evidence'), 'materialRevision': ref('Version'), 'binding': ref('MaterialBinding'), 'sourceLineageRef': ref('Evidence'), 'needNodeRef': ref('Ref'), 'basisKind': ref('BasisKind'), 'inputQuantity': nullable('Quantity'), 'responsibilitySlices': arr('ResponsibilitySlice'), 'offsets': arr('Offset'), 'offsetCoverageMembers': arr('OffsetCoverageMember'), 'alreadyAppliedOffsetSet': {'type':'array','items':ref('Id'),'uniqueItems':True}, 'excludeFromFurtherNettingSet': {'type':'array','items':ref('Id'),'uniqueItems':True}, 'protectionBindings': arr('Evidence'), 'transitionRef': ref('Evidence'), 'unresolvedActions': arr('UnresolvedAction'), 'ownerVector': arr('Evidence'), 'coverage': ref('Coverage'), 'knowledge': enum('KNOWN','PARTIAL','UNKNOWN'), 'canCloseMaterialResponsibility': {'type':'boolean'}, 'eligibleForFormalSeal': {'type':'boolean'}, 'allowedUses': {'type':'array','items':enum('DIAGNOSTIC','FORMAL_PLANNING_INPUT'),'uniqueItems':True}})
    defs['Problem'] = obj({'code': enum('INVALID_WIRE','UNAUTHENTICATED','FORBIDDEN','NOT_FOUND','REQUEST_KEY_CONFLICT','BINDING_MISMATCH','MATERIAL_REVISION_CHANGED','SCOPE_MISMATCH','OWNER_CUT_UNAVAILABLE'), 'traceId': ref('Id'), 'recovery': enum('FIX_REQUEST','REFRESH_AUTHORITY','NEW_CAPTURE_REQUIRED','QUERY_ORIGINAL_KEY','RETRY_SAME_READ_KEY')})
    schema = {'$schema':'https://json-schema.org/draft/2020-12/schema', '$id':'urn:cp6:mat-mrp-residual:1.0.0', '$defs':defs, '$ref':'#/$defs/ResidualHandoff'}
    def save(name,value): (dst/name).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    save('wire-1.0.0.schema.json',schema)
    R=lambda id: {'owner':'MAT','id':id,'version':'1'}
    E=lambda id: {'ref':R(id),'digest':'1'*64}
    Q=lambda v: {'value':str(v),'unitRef':R('EA')}
    scope={'scopeId':'scope-1','scopeVersion':'1','ranges':[{'portionId':'part-1','from':'0','to':'80'}]}
    b={k:'DEMO-'+k for k in binding}
    b.update({k:R(k) for k in ['mbomOccurrenceRef','itemRef','siteRef','unitRef','usageBasisRef','lossRuleRef']})
    b.update(snapshotDigest='1'*64,purpose='PRODUCTION',executionScope=scope,substitutionRuleRef=None,transitionRef=E('transition'),sourceProtectionRefs=[E('protect')],unitRef=R('EA'))
    handoff={'schema':'MAT-MRP-RESIDUAL/1','mappingVersion':'1.0.0','captureRef':E('capture'),'requestDigest':'2'*64,'nativeRemainingBasisRef':E('remaining'),'materialRevision':'1','binding':b,'sourceLineageRef':E('lineage'),'needNodeRef':R('need'), 'basisKind':'NET_RESIDUAL','inputQuantity':Q(60),'responsibilitySlices':[{'shareKey':'share-1','sourceObligationRef':R('obligation'),'currentRepresentativeRef':R('current'),'scope':scope,'grossQuantity':Q(80),'unitBasis':E('unit'),'originEvidence':E('origin')}], 'offsets':[{'offsetId':'consumed-1','shareKey':'share-1','originalFact':E('consumed'),'scope':scope,'quantity':Q(20),'category':'CONFIRMED_CONSUMED_NET','applicableNeedNode':R('need'),'qualification':E('qual'),'coverageMembers':['cover-1'],'supersedesOffsetIds':[],'activeProtection':[E('protect')]}], 'offsetCoverageMembers':[{'memberId':'cover-1','shareKey':'share-1','quantity':Q(20),'scope':scope,'ownershipEvidence':E('ownership')}], 'alreadyAppliedOffsetSet':['consumed-1'],'excludeFromFurtherNettingSet':['cover-1'],'protectionBindings':[E('protect')],'transitionRef':E('transition'),'unresolvedActions':[],'ownerVector':[E('owner-head')],'coverage':{'state':'COMPLETE','requestedScope':scope,'actualScope':scope,'cut':[E('cut')],'completenessProof':E('full'),'missingScopes':[]},'knowledge':'KNOWN','canCloseMaterialResponsibility':False,'eligibleForFormalSeal':True,'allowedUses':['DIAGNOSTIC','FORMAL_PLANNING_INPUT']}
    request={'schema':'MAT-MRP-RESIDUAL-READ/1','requestKey':'DEMO-read-1','mappingVersion':'1.0.0','bindingRef':E('binding'),'expectedMaterialRevision':'1','requestedScope':scope,'asKnownCut':[E('cut')],'basisKind':'NET_RESIDUAL'}
    cases=[]
    def case(id,body,expected=True,kind='ResidualHandoff',reason=''):
        cases.append({'id':id,'schema':kind,'body':copy.deepcopy(body),'expected_document_valid':expected,'reason':reason,'runtime_status':'NOT_RUN'})
    case('MAT-READ-POS',request,kind='ReadRequest')
    case('MAT-NET-POS',handoff)
    gross=copy.deepcopy(handoff);gross.update(basisKind='GROSS_WITH_EXPLICIT_OFFSETS',inputQuantity=Q(80),alreadyAppliedOffsetSet=[])
    case('MAT-GROSS-POS',gross)
    unknown=copy.deepcopy(handoff); unknown.update(inputQuantity=None,knowledge='UNKNOWN',eligibleForFormalSeal=False,allowedUses=['DIAGNOSTIC']);unknown['coverage'].update(state='UNKNOWN',completenessProof=None,missingScopes=[scope])
    case('MAT-UNKNOWN-POS',unknown)
    for id,path,value,reason in [('MAT-NET-WRONG','inputQuantity',Q(40),'net must equal 80-20'),('MAT-UNKNOWN-SEAL','knowledge','UNKNOWN','unknown evidence must block formal seal'),('MAT-UNIT-WRONG','inputQuantity',{'value':'60','unitRef':R('KG')},'unit cannot be guessed')]:
        bad=copy.deepcopy(handoff);bad[path]=value;case(id,bad,False,reason=reason)
    bad=copy.deepcopy(handoff);bad['excludeFromFurtherNettingSet']=[];case('MAT-DOUBLE-NET',bad,False,reason='all eligible coverage members excluded from subsequent netting')
    bad=copy.deepcopy(handoff);bad['responsibilitySlices'].append(copy.deepcopy(bad['responsibilitySlices'][0]));case('MAT-DUP-SHARE',bad,False,reason='same share cannot be counted twice')
    bad=copy.deepcopy(request);bad['tenantId']='body-override';case('MAT-TENANT-OVERRIDE',bad,False,'ReadRequest','trusted scope cannot be overridden')
    save('examples-1.0.0.json',{'role':'NEW_DEMO_DESIGN_VECTORS_NOT_OWNER_EVIDENCE','cases':cases})
    save('ports-1.0.0.json',{'revision':'1.0.0','permission':'MAT_MRP_BASIS_READ','tenant_and_actor':'trusted context only','operations':[{'method':'POST','route':'/internal/mes/material-obligations/{materialObligationId}/mrp-basis/v1/read','request':'wire-1.0.0.schema.json#/$defs/ReadRequest','success':'wire-1.0.0.schema.json#/$defs/ResidualHandoff','idempotency':['tenant','authenticated consumer','mappingVersion','requestKey'],'transaction':'consistent Owner capture plus immutable read receipt, no material effects','errors':{'400':'INVALID_WIRE','401':'UNAUTHENTICATED','403':'FORBIDDEN','404':'NOT_FOUND','409':['REQUEST_KEY_CONFLICT','BINDING_MISMATCH','MATERIAL_REVISION_CHANGED','SCOPE_MISMATCH'],'503':'OWNER_CUT_UNAVAILABLE'}},{'method':'GET','route':'/internal/mes/material-obligations/{materialObligationId}/mrp-basis/v1/reads/{requestKey}','success':'original immutable capture after current authorization','missing':'404 NOT_FOUND','recovery':'never recreate read or assert current applicability from historical capture'}], 'runtime_status':'NOT_RUN'})
    return len(cases)


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--handbook',type=Path,required=True)
    print(build(p.parse_args().handbook))
