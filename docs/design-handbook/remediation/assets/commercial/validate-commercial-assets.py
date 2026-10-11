"""Static document/schema/provenance checks only; does not execute any business fixture."""
from pathlib import Path
import hashlib, json, re

HERE=Path(__file__).resolve().parent
BOOK=HERE.parents[2]
def load(name): return json.loads((HERE/name).read_text(encoding='utf-8'))
def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()

api=load('commercial-wire-1.0.0.openapi.json')
schemas=api['components']['schemas']
refs=[]
def walk(node,path='$'):
    if isinstance(node,dict):
        if '$ref' in node:
            value=node['$ref']; assert value.startswith('#/components/schemas/'),(path,value)
            assert value.rsplit('/',1)[-1] in schemas,(path,value)
            refs.append(value)
        if node.get('type')=='object':
            assert node.get('additionalProperties') is False,path
            assert set(node.get('required',[])) <= set(node.get('properties',{})),path
        for key,value in node.items(): walk(value,path+'/'+key)
    elif isinstance(node,list):
        for i,value in enumerate(node): walk(value,path+'/'+str(i))
walk(api)
operations=[(path,method,op) for path,methods in api['paths'].items() for method,op in methods.items()]
ids=[op['operationId'] for _,_,op in operations]
assert len(ids)==len(set(ids))
assert all(f'A{n:02d}' in ids for n in range(1,14))
for path,method,op in operations:
    assert set(re.findall(r'{([^}]+)}',path))=={p['name'] for p in op['parameters'] if p['in']=='path'}
    assert op['x-capability'] and op['x-design-status']=='CURRENT_TASK_SUPPLEMENT_NOT_DEPLOYED'
    assert {'400','403','409','412','413','422','428','503'} <= set(op['responses'])
    if method in ('put','patch'): assert any(p['name']=='If-Match' for p in op['parameters'])

fields=load('ord-dem-fields-1.0.0.json')['fields']
assert len(fields)==140
assert {f['field_id'] for f in fields}=={f'OD-R02-FLD-{n:03d}' for n in range(1,141)}
for f in fields:
    assert f['physical_disposition']!='OWNER_RECORD',f['field_id']
    if '#/' in f['target']:
        node=api
        for part in f['target'].split('#/',1)[1].split('/'): node=node[part]
    assert f['runtime_status']=='NOT_IMPLEMENTED'
sources=load('evidence-reading.json')['sources']
for source in sources:
    path=Path(source['source_path']); assert digest(path)==source['sha256'],str(path)
    lines=path.read_text(encoding='utf-8').splitlines()
    assert all(1<=r['line']<=r['end_line']<=len(lines) for r in source['selected_ranges'])
ord_src=next(s for s in sources if s['id']=='ORD-R2')
lines=Path(ord_src['source_path']).read_text(encoding='utf-8').splitlines()
for f in fields: assert f['field_id'] in lines[f['source_line']-1]
bindings=load('commercial-owner-bindings-1.0.0.json')['bindings']
for b in bindings:
    assert digest(Path(b['source_path']))==b['sha256']
    assert b['exact_types_or_contracts'] and b['consumer_requirement']
    assert b['adoption_status']=='UNPROVEN'
assert len({b['binding_id'] for b in bindings})==len(bindings)

json_schema_check='NOT_RUN_LIBRARY_UNAVAILABLE'
try:
    from jsonschema import Draft202012Validator
except ImportError:
    pass
else:
    for name,schema in schemas.items():
        Draft202012Validator.check_schema(schema)
    def check(name,body):
        root={'$ref':'#/components/schemas/'+name,'components':api['components']}
        return list(Draft202012Validator(root).iter_errors(body))
    assert not check('AccountCreate',{'displayName':'Design fixture','departmentId':'dept-001'})
    assert check('AccountCreate',{'displayName':'Design fixture','departmentId':'dept-001','lifecycle':'ACTIVE'})
    assert check('AccountCreate',{'displayName':'Design fixture','departmentId':'dept-001','accountType':'PROSPECT'})
    assert not check('ReceiptQuery',{'kind':'COMMAND','locator':{'operation':'A03','commandKey':'doc-test-key'}})
    assert check('ReceiptQuery',{'kind':'MAIN_REQUEST','requestId':'missing-scope'})
    assert check('SourceOperation',{'schemaVersion':'ORD-DEM-SOURCE/1','kind':'CONTROL_SOURCE'})
    json_schema_check='PASS_META_SCHEMA_AND_6_DOCUMENT_CASES'

acceptance=load('acceptance-1.0.0.json')
for case in acceptance['cases']:
    if case['id']=='W1-STATIC-01': case['result']='PASS_STATIC_ONLY'
    else: assert case['result']=='NOT_RUN'
(HERE/'acceptance-1.0.0.json').write_text(json.dumps(acceptance,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
report={'scope':'Local static document checks only. No HTTP/DB/runtime/Owner/CI tests executed.','result':'PASS_STATIC_ONLY','operation_count':len(operations),'schema_count':len(schemas),'local_reference_count':len(refs),'field_count':len(fields),'source_hash_count':len(sources),'owner_binding_count':len(bindings),'json_schema_validation':json_schema_check,'required_runtime':'NOT_RUN','source_mutations':'NONE'}
(HERE/'validation-1.0.0.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
part_path=BOOK/'remediation/workparts/commercial.json'
part=json.loads(part_path.read_text(encoding='utf-8'))
for finding in part['findings']:
    finding['verification_status']='STATIC_ASSETS_PASS; REQUIRED_RUNTIME_NOT_RUN'
    finding['acceptance']=[x for x in finding['acceptance'] if x['id']!='W1-STATIC-01']
    finding['acceptance'].append({'id':'W1-STATIC-01','scenario':'Local schema references, 140 original field IDs, exact source hashes and boundary checks','expected':'All structural checks pass without running archived code or business tests','verification_level':'STATIC_ASSET','result':'PASS_STATIC_ONLY'})
part['assets']=[{'path':p.relative_to(BOOK).as_posix(),'sha256':digest(p),'version':'CP6-COMMERCIAL-PHYSICAL/1.0.0'} for p in sorted(HERE.iterdir()) if p.suffix in ('.json','.py')]
part_path.write_text(json.dumps(part,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report,ensure_ascii=False))
