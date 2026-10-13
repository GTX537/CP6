"""Construct schema-derived contract examples; not QA Owner/business acceptance data.

The output is committed and read verbatim by tests. Generation reads pinned original
schemas and declaration routes, never the C# validator or its validation results.
"""
import copy
import json
import re
import generate as g

UUID = '11111111-1111-4111-8111-111111111111'

def example(route):
    route,node=g.resolved(route)
    if 'const' in node:return node['const']
    if 'enum' in node:return node['enum'][0]
    for key in ['oneOf','anyOf']:
        if key in node:return example(route+'/'+key+'/0')
    typ=node.get('type')
    if typ=='null':return None
    if typ=='object':return {k:example(route+'/properties/'+k) for k in node['required']}
    if typ=='array':return [example(route+'/items') for _ in range(max(1,node.get('minItems',0)))]
    if typ=='boolean':return True
    if typ in ['number','integer']:return node.get('minimum',1)
    if typ=='string':
        if node.get('format')=='uuid':return UUID
        if node.get('format')=='date-time':return '2026-10-13T00:00:00.000000Z'
        if node.get('format')=='date':return '2026-10-13'
        for value in ['1','0','sample',UUID,'a'*64,'USD','qa://source','rma://source','2026-10-13T00:00:00.000000Z','/api/fin/inventory/v1/commands/query']:
            if len(value)<node.get('minLength',0) or len(value)>node.get('maxLength',100000):continue
            if 'pattern' in node and re.search(node['pattern'],value) is None:continue
            return value
    raise ValueError((route,node))

fixtures=[]
for original,route in sorted(g.type_routes.items()):
    value=example(route)
    typ=g.adapted_name(original)
    fixtures.append({'id':original+'/complete','type':typ,'source':route,'accepted':True,'value':value})
    if isinstance(value,dict):
        for key in value:
            missing=copy.deepcopy(value);del missing[key]
            fixtures.append({'id':original+'/missing/'+key,'type':typ,'source':route,'accepted':False,'value':missing})
        extra=copy.deepcopy(value);extra['undeclaredNativeField']=True
        fixtures.append({'id':original+'/closed','type':typ,'source':route,'accepted':False,'value':extra})

# These independent concrete mutations exercise schema constraints beyond fields.
def extra(original,suffix,mutate,accepted=False):
    route=g.type_routes[original];value=example(route);mutate(value)
    fixtures.append({'id':original+'/'+suffix,'type':g.adapted_name(original),'source':route,'accepted':accepted,'value':value})

M='QaNcrMainR6Declarations.'
Q=M+'Main_InitialSourceOptionQuery'
for field in ['sourceKind','keyword','cursor']:
    extra(Q,field+'/null',lambda v,f=field:v.__setitem__(f,None),True)
    extra(Q,field+'/empty',lambda v,f=field:v.__setitem__(f,''))
    extra(Q,field+'/number',lambda v,f=field:v.__setitem__(f,0))
extra(Q,'pageSize/zero',lambda v:v.__setitem__('pageSize',0))
extra(Q,'pageSize/101',lambda v:v.__setitem__('pageSize',101))
extra(Q,'pageSize/fraction',lambda v:v.__setitem__('pageSize',1.5))
extra(Q,'keyword/129',lambda v:v.__setitem__('keyword','x'*129))
extra(Q,'keyword/128-unicode-scalars',lambda v:v.__setitem__('keyword','😀'*128),True)
extra(Q,'keyword/129-unicode-scalars',lambda v:v.__setitem__('keyword','😀'*129))
PAGE=M+'Main_InitialSourceOptionPage'
extra(PAGE,'nextCursor/null',lambda v:v.__setitem__('nextCursor',None),True)
extra(PAGE,'nested/baseline-unknown',lambda v:v['items'][0].__setitem__('baseline',{'state':'UNKNOWN','baselineRef':None,'reason':'unavailable'}),True)
extra(PAGE,'nested/wrong-unit',lambda v:v['items'][0]['incidentRanges'][0]['unit'].__setitem__('kind','INVENTED'))
extra(PAGE,'nested/const-mismatch',lambda v:v['items'][0].__setitem__('advisoryOnly',False))
extra(PAGE,'time/invalid',lambda v:v.__setitem__('capturedAt','2026-02-30T00:00:00Z'))
extra(PAGE,'uuid/invalid',lambda v:v.__setitem__('snapshotId','11111111111141118111111111111111'))
RESULT=M+'Main_MainResult_Variant14_Payload'
extra(RESULT,'priorHeadRef/null',lambda v:v.__setitem__('priorHeadRef',None))
extra(RESULT,'factRefs/empty',lambda v:v.__setitem__('factRefs',[]))
extra(RESULT,'schema/wrong',lambda v:v.__setitem__('schema','QA-NCR-MAIN-RESULT-5'))
extra(RESULT,'factRefs/invalid-digest',lambda v:v['factRefs'][0].__setitem__('digest','A'*64))
for original in ['SourceQA.QapRef','SourceQA.QapEvidence','SourceQA.QapUom','SourceQA.ExecRef']:
    extra(original,'version/leading-zero',lambda v:v.__setitem__('version','01'))
extra('SourceQA.QapEvidence','flattened-exec-ref',lambda v:(v.clear(),v.update({'ref':{'owner':'EXEC','id':'source','version':'1'},'digest':'a'*64})))
extra('SourceQA.ExecEvidence','flat-qap-ref',lambda v:(v.clear(),v.update({'owner':'QAP','id':'source','version':'1','digest':'a'*64})))
extra('SourceQA.QapUom','wms-code',lambda v:(v.pop('id'),v.__setitem__('code','EA')))
extra(M+'External6aa69b80_Uom','qap-id',lambda v:(v.pop('code'),v.__setitem__('id','EA')))
extra('SourceQA.ExecScope','scope/empty-ranges',lambda v:v.__setitem__('ranges',[]))
extra('OwnerWire.Scope','scope/empty-ranges',lambda v:v.__setitem__('ranges',[]))
extra(M+'Externalf70281e4_ScopeRef','scope/owner-shape',lambda v:(v.clear(),v.update({'scopeId':UUID,'scopeVersion':'1','ranges':[]})))
for original in [M+'External6d9d3eee__types_IpqcFinalGuardResult_Variant1_Payload','SourceQA.IpqcFinalGuardResult_Case2']:
    extra(original,'nullable-requiredRef/non-null',lambda v:v.__setitem__('missing',[{'code':'MISSING','path':'guard','owner':'QA','requiredRef':{'owner':'QA','id':'source','version':'1'}}]),True)
    extra(original,'nullable-requiredRef/null',lambda v:v.__setitem__('missing',[{'code':'MISSING','path':'guard','owner':'QA','requiredRef':None}]),True)

g.TASK.joinpath('csharp/full-object-fixtures.json').write_text(json.dumps({'schema':'S01-07-CSHARP-FIXTURES-1','role':'SCHEMA_DERIVED_CONTRACT_EXAMPLES_NOT_OWNER_ACCEPTANCE','cases':fixtures},indent=2,ensure_ascii=True)+'\n',encoding='utf-8',newline='\n')
print(json.dumps({'fixtures':len(fixtures),'accepted':sum(f['accepted'] for f in fixtures),'rejected':sum(not f['accepted'] for f in fixtures)}))
