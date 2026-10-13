"""Optional design-source acquisition; reads selected source bytes as data, never executes them.

Tests use only the resulting repository-relative files. Reacquisition needs the
original fixed admission locator manifest, and checks every source before copying.
"""
from pathlib import Path
import hashlib, json, re

BASE = Path(__file__).resolve().parent
REPO = BASE.parents[2]
admission = json.loads((REPO/'docs/development/evidence/wave4-dispatch-20261013/S01-11-admission.json').read_text('utf-8'))
manifest_path = REPO/'docs/design-handbook/remediation/assets/commercial-fixtures/derivation-manifest.json'
manifest = json.loads(manifest_path.read_text('utf-8'))
targets = set(manifest['credit_money']['body_identities'])
def sha(raw): return hashlib.sha256(raw).hexdigest()
def source(sid, variant='composed'):
    item=next(s for s in admission['sources'] if s['source_id']==sid)
    raw=Path(item['local_'+variant+'_path']).read_bytes()
    assert sha(raw)==item[variant+'_sha256'], sid
    return raw
def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes((json.dumps(data,ensure_ascii=False,indent=2)+'\n').encode())
def escape(part): return str(part).replace('~','~0').replace('/','~1')
def walk(value,pointer=''):
    if isinstance(value,dict):
        if value.get('bodyKey') in targets: yield pointer,value
        for k,v in value.items(): yield from walk(v,pointer+'/'+escape(k))
    elif isinstance(value,list):
        for k,v in enumerate(value): yield from walk(v,pointer+'/'+str(k))

for i in range(5):
    raw=source(f'composed-{i:02}')
    item=next(s for s in admission['sources'] if s['source_id']==f'composed-{i:02}')
    path=BASE/'fixtures/current-bodies'/item['filename']; path.parent.mkdir(parents=True,exist_ok=True); path.write_bytes(raw)
records=[]
for i in range(4,10):
    sid=f'composed-{i:02}'; data=json.loads(source(sid))
    for pointer,value in walk(data):
        # Omit unrelated historical SQL row recipes; retain complete byte identities and fence markers.
        kept={k:v for k,v in value.items() if k!='completeOriginalStore17RowRecipe'}
        records.append({'source_id':sid,'json_pointer':pointer,'selected_value':kept})
chain={'origin':'SELECTED_CURRENT_DESIGN_PROJECTION_NOT_OWNER_PROOF','derivation_manifest_sha256':sha(manifest_path.read_bytes()),
       'credit_money':manifest['credit_money'],'credit_openapi':manifest['credit_openapi'],'records':records,
       'source_counts':{f'composed-{i:02}':sum(r['source_id']==f'composed-{i:02}' for r in records) for i in range(4,10)}}
write(BASE/'fixtures/current-chain.json',chain)

# Exact generated source ranges are hashed for audit; no archived program is compiled or republished.
ranges=[]
for sid in ['composed-11','composed-12']:
    for variant in ['original','composed']:
        raw=source(sid,variant);lines=raw.splitlines(keepends=True)
        for first,last,label in ([(65,131,'strict parser, schema verifier and semantic rules'),(132,183,'native CLR value/record surface'),(207,228,'local receipt/observation records and published factories')] if sid=='composed-11' else [(1,83,'native TypeScript value surface'),(88,206,'strict parser, schema verifier, semantics and published factories')]):
            selected=b''.join(lines[first-1:min(last,len(lines))]);ranges.append(dict(source_id=sid,variant=variant,source_sha256=sha(raw),first_line=first,last_line=min(last,len(lines)),purpose=label,selected_range_sha256=sha(selected)))
source_text=source('composed-12').decode()
factory=source_text[source_text.index('export async function createVerifiedWireCodecs'):source_text.index('export async function verifyFailedNoEffectExactBytes')]
factory_roots=re.findall(r'\w+:codec<[^\n]+?>\("([^"]+)","([^"]+)"',factory)
assert len(factory_roots)==14
surface={'origin':'BOUNDED_MANUAL_FACADE_DERIVATION','source_ranges':ranges,'published_factories':[dict(schema_file=f,root=r) for f,r in factory_roots],
 'scalar_helpers':['Money','D8'],'replay_roots':['EC_C01_COMMITTED_CommercialDraftView','EC_C02_COMMITTED_CommercialDraftView'],
 'api_mapping':'Generated typed records map to complete NativeCreditDocument JSON plus exact schema/profile/root and immutable original UTF8. No DTO numeric conversion, discriminator order restriction, serializer replay, SQL binding or Owner dispatch.',
 'runtime_corrections':['Shared net8 StrictJson and JsonSemanticEquality replace archived scanner/DeepEquals','Conditional minItems/maxItems apply without explicit type; refs retain sibling constraints','All native nested semantic rules execute after reference validation','TypeScript null-prototype objects, own-property lookup, exact number tokens and immutable snapshots','Version-specific NO_CHANGE and FinArObservation rules remain separate; FOUND/no-effect proof remains fenced'],
 'omitted':'Historical SQL Server ports, procedure arguments and business dispatch/orchestration. No F09-dependent C06 generic root exposure.'}
closures={}
for profile in ['current','frozen']:
    docs={name:json.loads((BASE/'assets/native'/name).read_text('utf-8')) for name in ['ERP-CREDIT-01_WMS-RMA-R3-EXACT.schema.json','ERP-CREDIT-01_FIN-AR-R2-EXACT.schema.json']}
    docs['ERP-CREDIT-01_TYPE-REGISTRY.json']=json.loads((BASE/'assets'/profile/'ERP-CREDIT-01_TYPE-REGISTRY.json').read_text('utf-8'))
    visited=set();integers=[]
    def visit(node,file,pointer):
        if isinstance(node,dict):
            if '$ref' in node:
                f,p=node['$ref'].split('#');f=f.removeprefix('./') or file
                assert f in docs, (profile,f)
                if (f,p) not in visited:
                    visited.add((f,p));value=docs[f]
                    for part in p.strip('/').split('/'):value=value[part]
                    visit(value,f,p)
            if node.get('type')=='integer':
                assert 'maximum' in node and node['maximum']<=86400 and node.get('minimum',0)>=0,(file,pointer,node)
                integers.append({'file':file,'pointer':pointer,'minimum':node.get('minimum'),'maximum':node['maximum']})
            for k,v in node.items():visit(v,file,pointer+'/'+escape(k))
        elif isinstance(node,list):
            for k,v in enumerate(node):visit(v,file,pointer+'/'+str(k))
    for file,root in factory_roots+[(list(docs)[0],'Money'),(list(docs)[1],'D8')]+[('ERP-CREDIT-01_TYPE-REGISTRY.json',r) for r in surface['replay_roots']]:
        visit({'$ref':file+'#/$defs/'+root},file,'entry')
    closures[profile]={'referenced_definitions':sorted(f+'#'+p for f,p in visited),'integer_domains':integers,'additional_schema_files':[]}
surface['closed_schema_dependencies']=closures
write(BASE/'surface.json',surface)
print(json.dumps({'body_files':5,'projected_records':len(records),'source_counts':chain['source_counts'],'factory_roots':len(factory_roots)}))
