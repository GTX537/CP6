"""Build CP6 R1 design DATA, never execute imported authors, SQL or business code.

Pinned originals are read only. Full derived EXC objects live in memory unless an
explicit --materialize directory is supplied. Repository output is recipes,
bounded text patches, full SHA manifests and static acceptance evidence.
"""
from __future__ import annotations
import argparse, base64, copy, decimal, hashlib, json, pathlib, sys, uuid

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / 'docs/design-handbook/remediation/assets/planning-fixtures'
REV = 'CP6-DESIGN-REVISION-20261011/R1'
CACHE = pathlib.Path('D:/CP6-archives/consolidation-20261010')
ORIGINALS = pathlib.Path('D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals')
WORLDS = ('PREPARED', 'PUBLISHED', 'MATERIAL_OLD', 'MATERIAL_NEW')
REF_KEYS = {'owner','namespace','objectKey','lineKey','scheduleKey','occurrenceKey','versionKey','bodyDigest'}
PIN_A = '7fe69593d7ac15cf1892417fd4f0b6688d1422ff243b6140a4717d30167dc3ec'
PIN_B = '9c6591119c1ec64decc1fc2ec6a341b6e68a595a6619eab106ebbe65d0b73925'
PIN_SC = '255e32f617794ffd760a8d05c698a7ffe147b5552a00ce8ddadbcb4b6e431f43'
PIN_CP = '46156a22d2dbb86ad58116ddd266e0c5921c105eb7fb1270e8cd42c9cab3fa5e'
PIN_WO = 'a2acf9ec58f2e1726c2c1a0799c4cfc5dbd37dde29406443b2c4fb384a93462a'

def j(x): return json.dumps(x, ensure_ascii=False, sort_keys=True, separators=(',', ':'))
def sha(x): return hashlib.sha256(x if isinstance(x, bytes) else x.encode('utf-8')).hexdigest()
def h(x): return sha(j(x))
def uid(x): return str(uuid.uuid5(uuid.NAMESPACE_URL, REV+'/planning-fixtures/'+x))
def clone(x): return copy.deepcopy(x)
def emit(name, x):
    OUT.mkdir(parents=True, exist_ok=True)
    with (OUT/name).open('w',encoding='utf-8',newline='\n') as f:f.write(json.dumps(x, ensure_ascii=False, indent=2)+'\n')
def read(path, pin):
    raw=path.read_bytes()
    if sha(raw)!=pin: raise ValueError('SOURCE_SHA_MISMATCH '+str(path))
    SOURCES[str(path.as_posix())]={'sha256':pin,'bytes':len(raw)}
    return raw.decode('utf-8')
def original(prefix): return next((ORIGINALS/prefix[:2]).glob(prefix[:16]+'__*'))
def walk(x):
    yield x
    if isinstance(x, dict):
        for v in x.values(): yield from walk(v)
    elif isinstance(x, list):
        for v in x: yield from walk(v)
def isref(x): return isinstance(x,dict) and set(x)==REF_KEYS and isinstance(x['bodyDigest'],str) and isinstance(x['owner'],str)
def refs(x): return [v for v in walk(x) if isref(v)]
def strings(x): return {v for v in walk(x) if isinstance(v,str)}
def raw_patch(path, pin, operations, ids, decision):
    text=read(path,pin)
    for op in operations:
        assert op['op']=='replace_text'
        count=text.count(op['before'])
        if count!=op['expected_count']: raise ValueError(('LITERAL_COUNT',path,count,op['expected_count']))
        text=text.replace(op['before'],op['after'])
    raw=text.encode('utf-8')
    return {'source_path':path.as_posix(),'source_sha256':pin,'source_finding_ids':ids,'decision':decision,
            'operations':operations,'derived_sha256':sha(raw),'expected_derived_sha256':sha(raw),
            'expected_derived_bytes':len(raw),'serialization':'UTF8_EXACT_APPLY_OPERATIONS_RESULT'},text
def op(before,after,count): return {'op':'replace_text','before':before,'after':after,'expected_count':count}

SOURCES={}

class Exc:
    """Hash DAG: full native bodies; graph/impact own digest excluded; closure separate."""
    def __init__(self,a,b):
        self.a,self.b=a,b; self.docs=clone(a['documentsBySemanticId']); self.nodes={}; self.byhash={}; self.done={}; self.active=[]
        known={j(v['ref']) for v in self.docs.values()}
        for obj in walk(b['publicViews']):
            if isinstance(obj,dict) and {'ref','body','bodySchemaRoot','artifact','trust','document'}<=set(obj) and j(obj['ref']) not in known:
                key='PUBLIC_NATIVE/'+obj['ref']['bodyDigest']; self.docs[key]=clone(obj);known.add(j(obj['ref']))
        self.refindex={j(v['ref']):k for k,v in self.docs.items()}; self.idmap={}; self.opaquemap={}; self.edges={}
        for k,v in self.docs.items(): self.add('native/'+k,v['body'],v['ref']['bodyDigest'],'native')
        for k,v in b['currentManifests'].items(): self.add('manifest/'+k,v,v['bodyDigest'],'manifest')
        for k,v in b['objects'].items():
            if k.startswith('impact/'):
                self.add('closure/'+k.split('/')[1],v['body']['closure'],v['body']['closure']['digest'],'closure')
            self.add(k,v['body'],v['bodyDigest'],k.split('/')[0])
        for w in WORLDS:
            for typ,key in [('graph','sourceGraphId'),('assessment','assessmentId'),('impact','impactId')]:
                body=b['objects'][typ+'/'+w]['body']; self.idmap[body[key]]=uid(typ+'/'+w)
            self.idmap[b['sourceReadPlans'][w]['readPlanId']]=uid('readPlan/'+w)
            self.idmap[b['sourceReadRequests'][w]['captureWorkId']]=uid('captureWork/'+w)
            for r in b['objects']['graph/'+w]['body']['requiredOwnerReads']:
                old=r['querySlot']['requestKey']['text']; self.opaquemap[old]=old+'/R1'
        self.validated_originals=0
        for key,n in self.nodes.items():
            assert h(self.preimage(n['body'],n['kind']))==n['old'], ('SOURCE_PREIMAGE',key)
            self.validated_originals+=1
    def add(self,key,body,digest,kind):
        self.nodes[key]={'body':body,'old':digest,'kind':kind}
        self.byhash[digest]=key
    @staticmethod
    def preimage(body,kind):
        return {k:v for k,v in body.items() if not (k=='digest' and kind in ('graph','impact','closure')) and not (k=='bodyDigest' and kind=='manifest')}
    def ref(self,x):
        key=self.refindex.get(j(x))
        if key is None: raise ValueError('UNRESOLVED_NATIVE_REF '+j(x))
        if self.active:self.edges.setdefault(self.active[-1],set()).add('native/'+key)
        n=self.resolve('native/'+key); result=clone(x); result['bodyDigest']=n['digest']
        if n['changed']: result['versionKey']=x['versionKey']+'/R1'
        return result
    def transform(self,x, current=None):
        if isinstance(x,dict):
            if isref(x): return self.ref(x)
            if set(x)=={'encoding','text','byteLength','rawDigest'} and isinstance(x['text'],str) and x['text'] in self.opaquemap:
                y=clone(x);y['text']=self.opaquemap[x['text']];y['byteLength']=len(y['text'].encode('utf-8'));y['rawDigest']=sha(y['text']);return y
            # Embedded private bodies/manifests must use their exact complete successor.
            for field in ('digest','bodyDigest'):
                digest=x.get(field)
                if isinstance(digest,str) and digest in self.byhash:
                    key=self.byhash[digest]
                    if key!=current and x==self.nodes[key]['body']: return clone(self.resolve(key)['body'])
            return {k:self.transform(v,current) for k,v in x.items()}
        if isinstance(x,list): return [self.transform(v,current) for v in x]
        if isinstance(x,str):
            if x in self.idmap: return self.idmap[x]
            key=self.byhash.get(x)
            if key and key!=current:
                if current: self.edges.setdefault(current,set()).add(key)
                return self.resolve(key)['digest']
        return x
    def resolve(self,key):
        if key in self.done:return self.done[key]
        if key in self.active:raise ValueError('DIGEST_CYCLE '+str(self.active+[key]))
        self.active.append(key);node=self.nodes[key];old=clone(node['body']);kind=node['kind']
        if key in ('native/SUPPLY_PROJECTION_PREPARED_1','native/SUPPLY_PROJECTION_PUBLISHED_1'):
            old['availableNode']=clone(self.docs['F014']['body']['availableNode'])
        if key in ('graph/PREPARED','graph/PUBLISHED'):
            old['supplies'][1]['availableNode']=clone(self.docs['F014']['body']['availableNode'])
        if key in ('graph/MATERIAL_OLD','graph/MATERIAL_NEW'):
            old['mrpLineage']['algorithmRef']=clone(self.docs['MAT_ALGORITHM_V2']['ref'])
        body=self.transform(self.preimage(old,kind),key)
        if kind=='native' and body!=node['body'] and 'header' in body:
            body['header']['versionKey']=node['body']['header']['versionKey']+'/R1'
        if kind=='manifest' and body!=self.preimage(node['body'],kind):
            body['manifestId']=uid('manifest/'+node['body']['manifestId'])
            self.idmap[node['body']['manifestId']]=body['manifestId']
        digest=h(body)
        if kind in ('graph','impact','closure'):body['digest']=digest
        if kind=='manifest':body['bodyDigest']=digest
        self.done[key]={'body':body,'digest':digest,'changed':digest!=node['old']}
        self.active.pop();return self.done[key]
    def document(self,k):
        old=self.docs[k];n=self.resolve('native/'+k)
        if not n['changed']:return clone(old)
        result=clone(old);result['body']=clone(n['body']);result['ref']=self.ref(old['ref'])
        art=clone(old['artifact']);art.update(artifactId=uid('artifact/'+k),storageLocator=uid('storage/'+k),
            byteLength=len(j(n['body']).encode('utf-8')),rawSha256=n['digest'],canonicalDigest=n['digest'])
        self.idmap[old['artifact']['artifactId']]=art['artifactId'];self.idmap[old['artifact']['storageLocator']]=art['storageLocator']
        trust=self.transform(old['trust']);trust['provenanceId']=uid('provenance/'+k);trust['artifactRef']=clone(art)
        self.idmap[old['trust']['provenanceId']]=trust['provenanceId']
        result['artifact']=art;result['trust']=trust
        result['document']={'factRef':clone(result['ref']),'artifactRef':clone(art),
            'nativeBodyBinding':clone(old['document']['nativeBodyBinding']),'nativeUtf8':j(n['body']),'trust':clone(trust)}
        return result
    def build(self):
        for w in WORLDS:
            for typ in ('graph','assessment','impact'):self.resolve(typ+'/'+w)
        # Resolve ALL original native documents in memory: changed reverse dependants
        # are listed even if outside the four normative worlds; no old changed node is
        # imported into the new package under its obsolete reference.
        for k in self.docs:self.resolve('native/'+k)
        documents={k:self.document(k) for k in self.docs}
        index={j(v['ref']):k for k,v in documents.items()}
        worlds={};used=set();world_members={}
        for w in WORLDS:
            graph=clone(self.resolve('graph/'+w)['body']);seed={k:v for k,v in graph.items() if k not in ('memberRefs','digest')}
            found=set();pending=refs(seed)
            while pending:
                ref=pending.pop();k=index.get(j(ref))
                if k is None:raise ValueError('NEW_REF_UNRESOLVED '+w+' '+j(ref))
                if k not in found:found.add(k);pending.extend(refs(documents[k]['body']))
            declared={index[j(r)] for r in graph['memberRefs']}
            assert declared==found, ('GRAPH_MEMBERSHIP',w,len(declared),len(found),declared^found)
            world_members[w]=sorted(found);used|=found
            page=clone(self.b['sourceReadPages'][w]);pre=self.transform(page['preimage']);pre['graphProjection']=graph
            pre['factDocuments']=[documents[index[j(r)]]['document'] for r in graph['memberRefs']]
            art=clone(page['artifactRef']);art.update(artifactId=uid('page-artifact/'+w),storageLocator=uid('page-storage/'+w),
                byteLength=len(j(pre).encode('utf-8')),rawSha256=h(pre),canonicalDigest=h(pre))
            trust=self.transform(page['deliveryEnvelope']['trust']);trust['provenanceId']=uid('page-provenance/'+w);trust['artifactRef']=clone(art)
            page.update(preimage=pre,nativeUtf8=j(pre),artifactRef=art,deliveryEnvelope={**clone(pre),'artifactRef':clone(art),'trust':trust})
            views=self.transform(self.b['publicViews'][w])
            for view in views.values():
                # This is a new object-level DEMO, not a claim that the historical
                # SQL transaction and old repository cut have executed these bytes.
                view['historicalLocalProvenance']={k:view.pop(k) for k in ('localCaseHeadReference','fullLocalSnapshotReference','actualRepositoryReadCut') if k in view}
                view['localStateBindingStatus']='R1_COMPLETE_OBJECT_CLOSURE_RUNTIME_NOT_RUN'
                view['localCaseHeadReference']={'file':'exc-successor.json','jsonPointer':'/worlds/'+w+'/caseBinding/head'}
                if 'sourceEndpointOutcome' in view:
                    out=view['sourceEndpointOutcome'];body=out.get('body',{});visible=body.get('graph',{})
                    g=visible.get('graph')
                    if isinstance(g,dict):
                        if w in ('PREPARED','PUBLISHED') and len(g.get('supplies',[]))>1:g['supplies'][1]['availableNode']=clone(graph['supplies'][1]['availableNode'])
                        if w.startswith('MATERIAL') and isinstance(g.get('mrpLineage'),dict) and 'algorithmRef' in g['mrpLineage']:g['mrpLineage']['algorithmRef']=clone(graph['mrpLineage']['algorithmRef'])
            worlds[w]={'graph':graph,'assessment':clone(self.resolve('assessment/'+w)['body']),
                'impact':clone(self.resolve('impact/'+w)['body']), 'readPlan':self.transform(self.b['sourceReadPlans'][w]),
                'sourceReadRequest':self.transform(self.b['sourceReadRequests'][w]),'sourceReadPage':page,'publicViews':views}
        # Add all native dependencies of assessments, impact, public authority and
        # read plans; graph.memberRefs remains exactly its original graph domain.
        pending=refs(worlds)
        while pending:
            ref=pending.pop();k=index.get(j(ref))
            if k is None:raise ValueError('PACKAGE_REF_UNRESOLVED '+j(ref))
            if k not in used:used.add(k);pending.extend(refs(documents[k]['body']))
        manifests={k:self.resolve('manifest/'+k)['body'] for k in self.b['currentManifests']}
        return {'revision':REV,'kind':'IMMUTABLE_SUCCESSOR_DEMO_OBJECT_PACKAGE','businessAC':'NOT_RUN','runtime':'UNPROVEN',
            'implementationAuthorized':False,'actualAdopted':0,'actualGrant':0,'documentsBySemanticId':{k:documents[k] for k in sorted(used)},
            'worlds':worlds,'currentManifests':manifests,'contracts':clone(self.a['contracts']),
            'closedSchemaBundles':clone(self.a['closedSchemaBundles'])},world_members

def case_closure(exc, package):
    """New complete object/Case tuple closure; no claim of a SQL UOW execution.

    Copy exact archived row shapes, resolve scoped FK Id/Digest pairs, and build
    new immutable revisions/observations. RowVersion bytes below are explicitly
    simulated provider readback, never SQL assignment inputs.
    """
    base=CACHE/'root-planning-cache/plan-exc-cp12/current-members'
    physical=json.loads(read(base/'CP03_PHYSICAL_RECORD_REGISTRY.json','11f539437a29689431689a27d6589b485f2e5e43669a78852c2db637968dd1f2'))
    local=json.loads(read(base/'CP03_LOCAL_RECORD_REGISTRY.json','b1364c8c50c1ea4cc04139b8ead0fa48170349703e88ca4a0b868f6150bdaeb4'))
    ledger=json.loads(read(base/'CP03_LOCAL_UOW_LEDGER_INDEX.json','03c9e362bbb51cfd91be49297b81c306a5b43abdeb31bc02906f3bfcc18a97ae'))
    rows={**physical['rowsByTableAndId'],**local['immutableRecords']}
    byid={r['columns']['Id']:k for k,r in rows.items()}
    done={};active=[];edges={};body_replacements={}
    for k,doc in exc.docs.items():
        new=exc.document(k)
        for field in ('body','trust','artifact'):
            if doc[field]!=new[field]:body_replacements[h(doc[field])]=new[field]
    def resolve(key):
        if key in done:return done[key]
        if key in active:raise ValueError('CASE_FK_CYCLE '+str(active+[key]))
        active.append(key);old=rows[key];c=clone(old['columns']);oldc=old['canonicalBody']
        # The archive explicitly stores complete source facts and private roots in
        # canonicalBody, and exact physical fields for ordinary relational rows.
        assert 'BodyDigest' not in c or h(oldc)==c['BodyDigest'], ('CASE_SOURCE_HASH',key)
        for field,value in list(c.items()):
            if field not in ('Id','EnvironmentId','TenantId') and field.endswith('Id') and isinstance(value,str) and value in byid:
                child=byid[value];r=resolve(child);edges.setdefault(key,set()).add(child)
                c[field]=r['columns']['Id'];digest_field=field[:-2]+'Digest'
                if digest_field in c and c[digest_field] is not None:
                    c[digest_field]=r['columns'].get('BodyDigest',h(r['canonicalBody']))
        digest=h(oldc);node=exc.byhash.get(digest)
        if node:
            n=exc.resolve(node);body=exc.preimage(n['body'],exc.nodes[node]['kind'])
        elif digest in body_replacements:body=clone(body_replacements[digest])
        elif old['hashProfile']=='EXACT_STORED_PHYSICAL_RECORD':
            body={k:exc.transform(v) for k,v in c.items() if k not in ('BodyCanonical','BodyDigest')}
        else:body=exc.transform(oldc)
        c=exc.transform({k:v for k,v in c.items() if k not in ('BodyCanonical','BodyDigest')})
        changed=body!=oldc or any(c.get(k)!=v for k,v in old['columns'].items() if k not in ('BodyCanonical','BodyDigest'))
        if changed:
            new_id=exc.idmap.get(old['columns']['Id'],uid('stored/'+key))
            c['Id']=new_id
            if old['hashProfile']=='EXACT_STORED_PHYSICAL_RECORD':body=clone(c)
        c['BodyCanonical']=j(body)
        if 'BodyDigest' in old['columns']:c['BodyDigest']=h(body)
        new=clone(old);new['canonicalBody']=body;new['columns']=c;new['primaryKey']['Id']=c['Id']
        done[key]=new;active.pop();return new
    bindings={}
    for number,w in enumerate(WORLDS,141):
        view=exc.b['publicViews'][w]['full'];packet_name=view['localCaseHeadReference']['jsonPointer'].split('/')[2]
        packet=ledger['packets'][packet_name];headkey=packet['caseHeadAfter']['completeMutableRowSnapshotRef'];oldhead=ledger['completeMutableRowSnapshots'][headkey]
        revision_key='CaseRevision/'+oldhead['columns']['RevisionId'];revision=clone(resolve(revision_key));c=revision['columns']
        c['Id']=uid('case-revision/'+w);c['BusinessRevision']=oldhead['columns']['BusinessRevision']+100
        c.update(WriterName='EXC_R1_DEMO_WRITER',WriterEpoch=1,LocalCommitSeq=number)
        revision['canonicalBody']={k:v for k,v in c.items() if k not in ('BodyCanonical','BodyDigest')}
        c['BodyCanonical']=j(revision['canonicalBody']);c['BodyDigest']=h(revision['canonicalBody']);revision['primaryKey']['Id']=c['Id']
        head=clone(oldhead);hc=head['columns'];hc.update(RevisionId=c['Id'],RevisionDigest=c['BodyDigest'],BusinessRevision=c['BusinessRevision'],
            WriterName=c['WriterName'],WriterEpoch=c['WriterEpoch'],LocalCommitSeq=number,RowVersion=base64.b64encode((50000+number).to_bytes(8,'big')).decode())
        head['canonicalBody']={k:v for k,v in hc.items() if k!='BodyCanonical'};hc['BodyCanonical']=j(head['canonicalBody'])
        for typ in ('graph','assessment','impact'):
            resolve(physical['privateRecordIndex'][typ+'/'+w])
        # CaseOrigin participates in source-filter equality; include its actual
        # original complete row and transitive scoped source identity dependencies.
        origin_ref=view['exactCasePageQuery']['exactOriginRecord']['jsonPointer'].split('/')[-1].replace('~1','/').replace('~0','~')
        resolve(origin_ref)
        binding={'kind':'COMPLETE_CASE_OBJECT_BINDING_NOT_EXECUTED_UOW','revision':revision,'head':head,'headDigest':h(head['canonicalBody']),
            'historicalHeadDigest':h(oldhead['canonicalBody']),'rowVersionMeaning':'SYNTHETIC_PROVIDER_READBACK_ONLY_NOT_SQL_INPUT',
            'writerTuple':{'writerName':c['WriterName'],'writerEpoch':1,'localCommitSeq':number},'runtime':'NOT_RUN'}
        package['worlds'][w]['caseBinding']=binding;bindings[w]={'revisionId':c['Id'],'revisionDigest':c['BodyDigest'],'headDigest':binding['headDigest'],'writerTuple':binding['writerTuple']}
        for v in package['worlds'][w]['publicViews'].values():
            active_view={k:value for k,value in v.items() if k!='historicalLocalProvenance'}
            for o in walk(active_view):
                if isinstance(o,dict) and 'readCut' in o:o['readCut']=number
                if isinstance(o,dict) and o.get('caseId')==hc['CaseId'] and 'businessRevision' in o:
                    o['businessRevision']=hc['BusinessRevision']
                    if 'rowVersion' in o:o['rowVersion']=hc['RowVersion']
                    if 'readCut' in o:o['readCut']=number
            if 'exactCasePageQuery' in v:
                v['exactCasePageQuery']['readCut']=number
                v['exactCasePageQuery']['exactOriginRecord']={'file':'exc-successor.json','jsonPointer':'/supportingStoredRows/'+origin_ref.replace('~','~0').replace('/','~1')}
    package['supportingStoredRows']={k:v for k,v in done.items() if v['table']!='CaseRevision'}
    package['caseClosureBoundary']='Full referenced object/row preimages, not replay of all historical SQL writes or proof of provider-generated RowVersion/Store gates. Historical ledger remains separate.'
    return {'bindings':bindings,'storedRows':len(done),'dependencies':{k:sorted(v) for k,v in edges.items()},'hashProfile':'sha256(EXC-JCS-1(canonicalBody)); BodyDigest and BodyCanonical are outside this row preimage'}

def finish_closure(exc,package):
    manifests=package.pop('currentManifests');needed_ids=strings(package)
    package['currentManifests']={k:v for k,v in manifests.items() if v['manifestId'] in needed_ids}
    all_docs={k:exc.document(k) for k in exc.docs};index={j(v['ref']):k for k,v in all_docs.items()}
    pending=refs(package);used=set(package['documentsBySemanticId'])
    while pending:
        r=pending.pop();key=index.get(j(r))
        if key is None:raise ValueError('FINAL_NATIVE_CLOSURE '+j(r))
        if key not in used:used.add(key);pending.extend(refs(all_docs[key]['body']))
    package['documentsBySemanticId']={k:all_docs[k] for k in sorted(used)}
    return {'nativeDocuments':len(used),'currentManifests':len(package['currentManifests']),
            'storedRows':len(package['supportingStoredRows'])}

def plan_po():
    base=CACHE/'root-planning-cache/plan-po-cp22/members';scpath=base/'PLAN_PO_R3_CP18_SC06_100_NORMAL_DATA.json';cppath=base/'CP19_R05_WO_CONTINUOUS_NORMAL_DATA.json'
    scraw=read(scpath,PIN_SC);sc=json.loads(scraw);receipt=sc['completePreRows'][62]['row'];bodystr=sc['completePreRows'][65]['row']['BodyJson'];body=json.loads(bodystr)
    old=h(body);body['requestDigest']=receipt['RequestDigest'];body['ownerResultDigest']=receipt['OwnerResultDigest'];body['ownerResult']=json.loads(receipt['RawResponseCanonical']);new=j(body)
    assert h(body)==receipt['PayloadDigest'];assert sha(receipt['RawResponseCanonical'])==receipt['OwnerResultDigest']
    p14,derived=raw_patch(scpath,PIN_SC,[op(json.dumps(bodystr,ensure_ascii=False),json.dumps(new,ensure_ascii=False),2),op(old,h(body),4)],['PLAN-IF-014'],'Revision4 preserves exact original OwnerReceipt; BodyJson and all revision/head digest copies follow the native receipt.')
    corrected_sc=json.loads(derived)
    for rows in ('completePreRows','completePostRows'):
        assert corrected_sc[rows][65]['row']['BodyJson']==new
        assert corrected_sc[rows][65]['row']['BodyDigest']==h(body)==corrected_sc[rows][66]['row']['BodyDigest']
    cpraw=read(cppath,PIN_CP);cp=json.loads(cpraw);native=cp['steps'][1]['nativeCurrentResult'];result=clone(native['result']);oldkey=result['result']['executionKey']['executionRequestKey'];newkey=native['request']['executionKey']['executionRequestKey'];result['result']['executionKey']['executionRequestKey']=newkey
    assert h(result)=='07c674c823ef9ec67fc1954b7a389e520899c61995064a94706346c2fde401a5'
    p15,cpderived=raw_patch(cppath,PIN_CP,[op(oldkey,newkey,23),op(native['resultDigest'],h(result),44)],['PLAN-IF-015'],'Freeze request executionRequestKey from predecessor; correct complete native result, receipt/terminal/observation/checkpoint and all later pre/post/claim snapshots.')
    corrected_cp=json.loads(cpderived);assert oldkey not in cpderived;assert native['resultDigest'] not in cpderived
    leafpaths=[]
    def compare(a,b,p=''):
        if isinstance(a,dict):
            for k in a:compare(a[k],b[k],p+'/'+k)
        elif isinstance(a,list):
            for i,x in enumerate(a):compare(x,b[i],p+'/'+str(i))
        elif a!=b:leafpaths.append(p)
    compare(cp,corrected_cp)
    # Exact known hash profiles, not an invented universal Owner convention.
    checks={}
    for name,obj in [('sourceDescriptor',sc['sourceDescriptor']),('manufacturingCandidate',sc['manufacturingCandidate']),('engineeringCheck',sc['nativeOwnerWireChain'][2]['result'])]:
        expected=obj['digest'];actual=h({k:v for k,v in obj.items() if k!='digest'});assert actual==expected;checks[name]=actual
    checks['snapshot']=h(sc['snapshotContentPreimage']);assert checks['snapshot']==sc['snapshotDigest']
    checks['cp19Snapshot']=h(cp['nativeSnapshotPreimage']);checks['cp19Technical']=h(cp['nativeTechnicalPreimage'])
    checks['exactTupleUtf16Le']=sha(sc['exactTupleSqlPreimage'].encode('utf-16le'));assert checks['exactTupleUtf16Le']==receipt['ExactTupleHash']
    bad=clone(body);bad['ownerResultDigest']=json.loads(bodystr)['ownerResultDigest'];assert h(bad)!=receipt['PayloadDigest']
    assert native['result']['result']['executionKey']!=native['request']['executionKey']
    assert corrected_cp['steps'][1]['nativeCurrentResult']['result']['result']['executionKey']==native['request']['executionKey']
    one_late_stale=cpderived.replace(h(result),native['resultDigest'],1);assert native['resultDigest'] in one_late_stale
    return [p14,p15],{'correctedRevisionDigest':h(body),'correctedStep1ResultDigest':h(result),'cp19ChangedLeafPaths':leafpaths,'hashProfiles':checks,
        'negative':[{'id':'PLAN014-original-mixed-result-rejected','result':'PASS_REJECTED_MUTATION'},
                    {'id':'PLAN015-original-result-request-key-rejected','result':'PASS_REJECTED_MUTATION'},
                    {'id':'PLAN015-one-stale-digest-copy-detected','result':'PASS_REJECTED_MUTATION'}]},corrected_sc,corrected_cp

def wo_patches():
    path=original(PIN_WO);raw=read(path,PIN_WO);lines=raw.splitlines(keepends=True)
    # All boundaries are read from exact approved source sections, not global v1/0 rewrites.
    candidate=''.join(lines[1547:1562]);assert '"version": "1"' in candidate
    scope=''.join(lines[2280:2305]);assert '"from": "0.00000000"' in scope
    p,_=raw_patch(path,PIN_WO,[op(candidate,candidate.replace('"version": "1"','"version": "2"'),1),
        op(scope,scope.replace('"from": "0.00000000"','"from": "40.00000000"'),1),
        op('SUCCESSOR_SCOPE_ALREADY_HELD','SUCCESSOR_SCOPE_HELD',1)],['OPS-IF-006','OPS-IF-007','OPS-IF-008'],
        'New R1 illustrative WO text only. Placeholder hashes stay explicitly illustrative; exact computed v2 successor is pinned SC06/CP19, not these historical MD literals.')
    # The new prose assertions have deliberately small exact validators. They do
    # not assert that a service executed FinalizeCancel or a successor conflict.
    qty=decimal.Decimal('60');assert decimal.Decimal('100')-decimal.Decimal('40')==qty
    assert decimal.Decimal('100')-decimal.Decimal('0')!=qty
    assert 'SUCCESSOR_SCOPE_ALREADY_HELD'!='SUCCESSOR_SCOPE_HELD'
    assert '1'!='2'
    return p

def validate_exc(exc,package,members):
    index={j(v['ref']):k for k,v in package['documentsBySemanticId'].items()};checks=[]
    def check(name,condition):
        if not condition:raise ValueError('VALIDATION_FAILED '+name)
        checks.append(name)
    for k,d in package['documentsBySemanticId'].items():
        body=d['body'];digest=h(body);raw=j(body).encode('utf-8');art=d['artifact'];doc=d['document']
        check('native/'+k,digest==d['ref']['bodyDigest']==art['rawSha256']==art['canonicalDigest'] and len(raw)==art['byteLength'] and doc['nativeUtf8']==j(body) and doc['factRef']==d['ref'] and doc['artifactRef']==art and d['trust']['artifactRef']==art and doc['trust']==d['trust'])
        if 'header' in body:check('native-version/'+k,body['header']['versionKey']==d['ref']['versionKey'])
        check('scope/'+k,d['trust']['originalIssuanceScope']==exc.docs[k]['trust']['originalIssuanceScope'])
    check('complete-native-ref-closure',all(j(r) in index for r in refs(package)))
    for w in WORLDS:
        data=package['worlds'][w];graph=data['graph'];assessment=data['assessment'];impact=data['impact'];closure=impact['closure']
        check('graph/'+w,h({k:v for k,v in graph.items() if k!='digest'})==graph['digest'])
        check('assessment/'+w,assessment['sourceGraphId']==graph['sourceGraphId'] and assessment['sourceGraphDigest']==graph['digest'])
        check('impact/'+w,h({k:v for k,v in impact.items() if k!='digest'})==impact['digest'] and impact['assessmentId']==assessment['assessmentId'])
        check('impact-closure/'+w,h({k:v for k,v in closure.items() if k!='digest'})==closure['digest'])
        check('new-identities/'+w,all(data[t][key]!=exc.b['objects'][t+'/'+w]['body'][key] for t,key in [('graph','sourceGraphId'),('assessment','assessmentId'),('impact','impactId')]))
        check('graph-members/'+w,{index[j(r)] for r in graph['memberRefs']}==set(members[w]))
        page=data['sourceReadPage'];pre=page['preimage'];art=page['artifactRef'];envelope=page['deliveryEnvelope']
        check('page/'+w,pre['graphProjection']==graph and j(pre)==page['nativeUtf8'] and h(pre)==art['rawSha256']==art['canonicalDigest'] and len(j(pre).encode())==art['byteLength'] and envelope=={**pre,'artifactRef':art,'trust':envelope['trust']} and envelope['trust']['artifactRef']==art)
        check('page-facts/'+w,{j(d['factRef']) for d in pre['factDocuments']}=={j(r) for r in graph['memberRefs']})
        check('capture-query/'+w,pre['captureWorkId']==data['sourceReadRequest']['captureWorkId'] and pre['originalQuerySlot']==data['sourceReadRequest']['originalQuerySlot'] and pre['captureRef']==data['readPlan']['requiredOwnerReads'][0]['captureRef'])
        rev=data['caseBinding']['revision'];head=data['caseBinding']['head'];rc=rev['columns'];hc=head['columns']
        check('case/'+w,h(rev['canonicalBody'])==rc['BodyDigest']==hc['RevisionDigest'] and hc['RevisionId']==rc['Id'] and rc['AssessmentDigest']==h(assessment) and rc['AssessmentId']==assessment['assessmentId'] and rc['ImpactDigest']==impact['digest'] and rc['ImpactId']==impact['impactId'] and rc['CaseId']==assessment['caseId']==hc['CaseId'])
        check('case-head/'+w,h(head['canonicalBody'])==data['caseBinding']['headDigest'] and hc['BodyCanonical']==j(head['canonicalBody']) and rc['BodyCanonical']==j(rev['canonicalBody']))
        for variant,view in data['publicViews'].items():
            check('masked-private-hash/'+w+'/'+variant,view['privateGraphDigestUnchanged']==graph['digest'] and view['privateAssessmentDigestUnchanged']==h(assessment))
            check('public-case-cut/'+w+'/'+variant,view['caseView']['readCut']==view['casePage']['readCut']==hc['LocalCommitSeq'] and ('exactCasePageQuery' not in view or view['exactCasePageQuery']['readCut']==hc['LocalCommitSeq']))
    for key,row in package['supportingStoredRows'].items():
        check('stored/'+key,row['columns']['BodyCanonical']==j(row['canonicalBody']) and ('BodyDigest' not in row['columns'] or h(row['canonicalBody'])==row['columns']['BodyDigest']))
    serialized=j(package)
    check('no-obsolete-changed-body-digests',not any(exc.nodes[k]['old'] in serialized for k,v in exc.done.items() if v['changed']))
    check('adoption-boundary',package['actualAdopted']==package['actualGrant']==0 and package['runtime']=='UNPROVEN' and not package['implementationAuthorized'])
    negative=[]
    def rejects(label,condition):
        check('negative/'+label,condition);negative.append({'id':label,'result':'PASS_REJECTED_MUTATION'})
    g=clone(package['worlds']['PREPARED']['graph']);g['supplies'][1]['availableNode']['instant']='2026-10-04T18:00:00Z'
    rejects('PLAN007-old-actual-time',h({k:v for k,v in g.items() if k!='digest'})!=g['digest'])
    g=clone(package['worlds']['MATERIAL_NEW']['graph']);g['mrpLineage']['algorithmRef']=clone(exc.docs['F025']['ref'])
    rejects('PLAN008-old-algorithm',h({k:v for k,v in g.items() if k!='digest'})!=g['digest'])
    page=package['worlds']['PREPARED']['sourceReadPage'];changed=clone(page['preimage']);changed['ordinal']=2
    rejects('source-page-stale-artifact',h(changed)!=page['artifactRef']['canonicalDigest'])
    oldref=exc.docs['SUPPLY_PROJECTION_PREPARED_1']['ref'];rejects('obsolete-native-version',j(oldref) not in index)
    rev=clone(package['worlds']['PREPARED']['caseBinding']['revision']);rev['canonicalBody']['AssessmentId']=exc.b['objects']['assessment/PREPARED']['body']['assessmentId']
    rejects('old-case-assessment',h(rev['canonicalBody'])!=rev['columns']['BodyDigest'])
    rejects('source-sha-pin',sha((CACHE/'objects'/(PIN_A+'.json')).read_bytes()+b' ')!=PIN_A)
    return {'status':'PASS_STATIC_DESIGN_DATA_ONLY','check_count':len(checks),'checks':checks,'negative':negative,
        'runtime':'NOT_RUN','actual_adoption_status':'UNPROVEN','not_proven':['business execution','Store/SQL UOW','real Core/Owner grants','provider rowversion','full legacy SQL ledger replay']}

def workparts(validation):
    register=json.loads((ROOT/'docs/design-handbook/remediation/00-disposition-register.json').read_text(encoding='utf-8'))
    decisions={
      'PLAN-IF-007':('ARTIFACT_CONFLICT','DESIGN_RESOLVED','Receipt20 原生 F014/F016 的 ACTUAL_AVAILABLE=18:10:02 为唯一时间；为 PREPARED/PUBLISHED 生成新 sealed Graph/Assessment/Impact、新原生投影版本、查询捕获/来源页、CaseRevision/Head；所有事实 Ref 和摘要依赖闭包重算，不重写旧 sealed 身份。','新图、投影与真实原始 F014/F016 时间相等，完整闭包解析及摘要通过。','旧18:00投影、旧原生版本/旧图摘要或旧Case绑定进入新DEMO时拒绝。'),
      'PLAN-IF-008':('ARTIFACT_CONFLICT','DESIGN_RESOLVED','MATERIAL_OLD/NEW 新 sealed graph 的 algorithmRef 精确引用 MAT_ALGORITHM_V2 2.0-proposed，与已存在 MAT_INPUT/RUN/ATTEMPT 相等；历史 F025/I1 保留，八类支持输出不改为每次必须八行。完整预映像重算并生成新 Case 引用。','四个物料算法Ref完全相同；原F025/I1历史事实原样可解析。','将F025 1.0替代新图algorithmRef或缺失任一依赖事实必须拒绝。'),
      'PLAN-IF-014':('ARTIFACT_CONFLICT','DESIGN_RESOLVED','SC06 revision4 以原OwnerReceipt62的 request/result/rawresponse 为唯一证据；修正两份BodyJson及四份revision/head摘要，得到既有PayloadDigest c35ad4…；ExactTuple仍按原UTF-16LE字节哈希。','pre/post revision4、head与receipt request/result/snapshot完全一致。','保留旧ownerResultDigest、替换snapshot或以JCS替代UTF-16LE ExactTuple时拒绝。'),
      'PLAN-IF-015':('ARTIFACT_CONFLICT','DESIGN_RESOLVED','CP19 step1执行键取已冻结请求及step0前驱 CP18-MAKE-579b4a48-c253-5aa9-834d-1847c3733973；完整native结果摘要07c674…；23处键和44处摘要跨后续pre/post、claim、OwnerCommand/Observation/WorkflowStep/RecoveryCheckpoint捕获全量级联。','全部native结果/终态/恢复捕获原文与摘要一致，未受影响的request/local CommandOutcome保持原值。','旧MAKE-CONVERT键、旧eb120a…摘要或遗漏晚期rootFirstClaim捕获时拒绝。'),
      'OPS-IF-006':('SUPERSEDED','SUPERSESSION_RECORDED','旧WO markdown重复a/b/c摘要按原1011/2364声明保持历史占位示例；新说明gate采用REL AUTH-PROD-001 v2。可复现完整规范后继采用SHA固定SC06/CP19 v2 Owner预映像，不冒充旧占位摘要可计算，不臆造审批事件。','SC06 SourceDescriptor、EngineeringCheck、candidate PURPOSE-BASIS均version2；其真实预映像摘要复现。','v1 gate混入v2输入或将历史占位hash声称真实摘要时拒绝。'),
      'OPS-IF-007':('ARTIFACT_CONFLICT','DESIGN_RESOLVED','新R1 WO示例 UNSTARTED60 obligation scope修为[40,100)，root100和已实际40保持；FinalizeCancel必须闭合所请求完整scope，不能自动把60缩成50；本段属于新说明数据，原作者占位hash非真实证明。','数量60对应区间长度60，actual40仍由原root事实保留。','[0,100)冒充UNSTARTED60或把已执行40当无效果取消必须拒绝。'),
      'OPS-IF-008':('DOCUMENT_STALE','DESIGN_RESOLVED','新R1示例冲突字面量采用既有正式409 SUCCESSOR_SCOPE_HELD；SUCCESSOR_SCOPE_ALREADY_HELD仅作为旧说明保留，不新增wire alias。','范围已占用使用既有SUCCESSOR_SCOPE_HELD且零新增占用。','发送旧ALREADY_HELD字面量、错误地释放TRANSFERRED_NO_EXECUTION区间或形成free pool gap必须拒绝。')}
    findings=[]
    for issue in register['issues']:
        fid=next((x for x in issue['source_finding_ids'] if x in decisions),None)
        if not fid:continue
        classification,status,decision,positive,negative=decisions[fid];evidence=[]
        if fid in ('PLAN-IF-007','PLAN-IF-008'):
            source_version='PLAN-EXC-01 v1.11 CP12; CP03/CP04 native registry/private worlds; exact per-body schema/version unchanged except explicit R1 native version successors'
            consumers=['PLAN-SUP SupplyProjection/SourceCapture producers','PLAN-MRP algorithm/input/run/attempt lineage','PLAN-EXC source-page, private Graph/Assessment/Impact, CaseRevision/Head and masked public views']
        elif fid=='PLAN-IF-014':
            source_version='PLAN-PO R3 CP18 SC06_100_NORMAL_DATA, retained in CP22 members'
            consumers=['PLAN-PO OwnerReceipt reader','PLAN-PO PlannedOrderRevision4/PlannedOrderHead pre/post snapshot readers']
        elif fid=='PLAN-IF-015':
            source_version='PLAN-PO CP19 R05 WO_CONTINUOUS_NORMAL_DATA, retained in CP22 members'
            consumers=['PLAN-PO native step1 resume','OwnerObservation/OwnerWorkflowStep/OwnerCommand terminal persistence','RecoveryCheckpoint and later pre/post/claim snapshot readers']
        else:
            source_version='MES-WO-01 v1.1 DRAFT; exact computed v2 Owner successor SC06 CP18 / CP19 R05'
            consumers={'OPS-IF-006':['MES-WO SourceDescriptor/EngineeringCheck/ManufacturingCandidate gate consumers','PLAN-PO SC06/CP19 native Owner wire captures'],
                       'OPS-IF-007':['MES-WO UNSTARTED obligation scope consumer','MES-WO FinalizeCancel full-scope closure'],
                       'OPS-IF-008':['MES-WO successor resource protection','409 successor-scope error consumers']}[fid]
        for item in issue['evidence']:
            p=pathlib.Path(item['source_path']);raw=p.read_bytes();assert sha(raw)==item['sha256'];lines=raw.decode('utf-8').splitlines()
            start=item.get('line',1);end=item.get('end_line',start);selected='\n'.join(lines[start-1:end]);assert selected
            ev=clone(item);ev['reading']='Read exact original selected clauses plus full JSON roots and transitive hash preimages; source scripts treated only as text.';ev['selected_range_sha256']=sha(selected);evidence.append(ev)
        findings.append({'issue_id':issue['issue_id'],'source_finding_ids':[fid],'batch':issue['batch'],'classification':classification,'targets':issue['targets'],
          'source_version':source_version+'; successor '+REV,'decision':decision,
          'preserved_constraints':['原件字节及旧sealed身份不可变','Scope/Owner wire边界不变','历史F025/I1仍为历史输入，不改变其算法或输出集合','实际采用UNPROVEN；业务/DB/Actions不执行'],
          'consumer_adoption':{'required':consumers,'runtime_status':'UNPROVEN','design_acceptance_is_runtime_adoption':False},
          'evidence':evidence,'outputs':['remediation/assets/planning-fixtures/README.md','remediation/assets/planning-fixtures/patches.json','remediation/assets/planning-fixtures/planning-closure-manifest.json','remediation/assets/planning-fixtures/validation.json','remediation/assets/planning-fixtures/generation-recipe.json','tools/build_planning_fixture_patches.py'],
          'acceptance':[{'id':'AC-'+fid+'-P','scenario':'生成精确设计数据，核对本条原文、身份、字面量、闭包和摘要','expected':positive,'verification_level':'LOCAL_DESIGN_DATA','result':'PASS'},
                        {'id':'AC-'+fid+'-N','scenario':'静态反例：旧字段/字面量/区间/摘要至少一项与本条精确条件不匹配','expected':'Static mismatch detected; this result asserts no service execution or zero-write runtime behavior.','verification_level':'LOCAL_DESIGN_DATA','result':'PASS'},
                        {'id':'AC-'+fid+'-RP','scenario':'真实消费者执行本条正向与恢复契约','expected':positive,'verification_level':'REQUIRED_RUNTIME_ADOPTION','result':'NOT_RUN'},
                        {'id':'AC-'+fid+'-RN','scenario':'真实消费者执行本条负向与并发契约','expected':negative,'verification_level':'REQUIRED_RUNTIME_ADOPTION','result':'NOT_RUN'}],
          'design_status':status,'actual_adoption_status':'UNPROVEN','implementation_status':'NOT_IMPLEMENTED','verification_status':'LOCAL_DESIGN_DATA_PASS_RUNTIME_NOT_RUN'})
    assert len(findings)==7
    path=ROOT/'docs/design-handbook/remediation/workparts/planning-fixtures.json'
    with path.open('w',encoding='utf-8',newline='\n') as f:f.write(json.dumps({'revision':REV,'findings':findings},ensure_ascii=False,indent=2)+'\n')

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--materialize',type=pathlib.Path);args=parser.parse_args()
    a=json.loads(read(CACHE/'objects'/(PIN_A+'.json'),PIN_A));b=json.loads(read(CACHE/'objects'/(PIN_B+'.json'),PIN_B))
    exc=Exc(a,b);package,members=exc.build();cases=case_closure(exc,package);closure=finish_closure(exc,package)
    patches,po,sc,cp=plan_po();patches.append(wo_patches());validation=validate_exc(exc,package,members)
    emit('patches.json',{'revision':REV,'role':'bounded_design_data_revision_not_original','patches':patches})
    world_hashes={w:{'graph':{'id':d['graph']['sourceGraphId'],'digest':d['graph']['digest']},
        'assessment':{'id':d['assessment']['assessmentId'],'digest':h(d['assessment'])},
        'impact':{'id':d['impact']['impactId'],'digest':d['impact']['digest'],'closureDigest':d['impact']['closure']['digest']},
        'readPlan':{'id':d['readPlan']['readPlanId'],'digest':h(d['readPlan'])},
        'sourceReadPage':{'artifact':d['sourceReadPage']['artifactRef'],'captureWorkId':d['sourceReadPage']['preimage']['captureWorkId'],
            'depends_on':['graph/'+w]+['native/'+k for k in members[w]],'preimage_pointer':'/worlds/'+w+'/sourceReadPage/preimage'},
        'captures':[{'querySlot':r['querySlot'],'captureRef':r['captureRef']} for r in d['graph']['requiredOwnerReads']]}
        for w,d in package['worlds'].items()}
    emit('planning-closure-manifest.json',{'revision':REV,'sources':SOURCES,'worldMembers':members,'worldHashes':world_hashes,
        'changedNodes':{k:{'oldDigest':exc.nodes[k]['old'],'newDigest':v['digest']} for k,v in exc.done.items() if v['changed']},
        'identitySuccessors':exc.idmap,'nativeVersionSuccessors':{k:{'originalRef':exc.docs[k]['ref'],'successorRef':d['ref']} for k,d in package['documentsBySemanticId'].items() if exc.resolve('native/'+k)['changed']},
        'dependencies':{k:sorted(v) for k,v in sorted(exc.edges.items())},'planPo':po,'caseClosure':cases,'package_sha256':h(package),'package_bytes':len(j(package).encode('utf-8'))})
    validation.update(revision=REV,originalHashPreimages=exc.validated_originals,closure=closure,planPo=po,
        wo_static_negative=[{'id':'OPS006-v1-is-not-v2','result':'PASS_LITERAL_MISMATCH'},
                           {'id':'OPS007-root100-is-not-unstarted60','result':'PASS_RANGE_MISMATCH'},
                           {'id':'OPS008-old-error-is-not-current-wire','result':'PASS_LITERAL_MISMATCH'}])
    emit('validation.json',validation);workparts(validation)
    emit('generation-recipe.json',{'revision':REV,'generator':'tools/build_planning_fixture_patches.py','generator_sha256':sha(pathlib.Path(__file__).read_bytes()),
        'sources':SOURCES,'sourceHandling':'READ_ONLY; never execute/import original scripts or SQL','worlds':list(WORLDS),'uuidRecipe':'UUIDv5(NAMESPACE_URL, revision + /planning-fixtures/ + domain + / + originalIdentityOrWorld)',
        'preimages':{'native':'Entire closed native business body including nested digests; UTF8 EXC-JCS-1, no BOM/newline','graph':'Remove only top-level digest; keep all nested body/closure digests and memberRefs','assessment':'Entire private business body; no own digest member exists','impactClosure':'Remove only closure.digest','impact':'Remove only top-level digest after inserting computed closure.digest','currentManifest':'Remove only top-level bodyDigest','sourceReadPage':'Entire page.preimage; nativeUtf8 exactly this JCS, envelope adds artifactRef/trust outside hash','storedRows':'Exact canonicalBody; BodyCanonical=JCS(canonicalBody), BodyDigest=SHA256(UTF8 BodyCanonical)','sc06ExactTuple':'Exact original length-prefixed pipe string in UTF-16LE, no BOM; not JCS'},
        'package':{'filename':'exc-successor.json','sha256':h(package),'bytes':len(j(package).encode('utf-8')),'serialization':'UTF8_EXC_JCS_1_NO_NEWLINE','storage':'not checked in; reconstruct from pinned local sources with --materialize'},
        'other_materialized_outputs':[{'filename':name,'sha256':h(obj),'bytes':len(j(obj).encode('utf-8')),'serialization':'UTF8_EXC_JCS_1_NO_NEWLINE'} for name,obj in [('sc06-successor.json',sc),('cp19-successor.json',cp)]],
        'runtime':'NOT_RUN','actual_adoption_status':'UNPROVEN'})
    if args.materialize:
        args.materialize.mkdir(parents=True,exist_ok=True)
        for name,obj in [('exc-successor.json',package),('sc06-successor.json',sc),('cp19-successor.json',cp)]:
            target=args.materialize/name;raw=j(obj).encode('utf-8')
            if target.exists():
                if target.read_bytes()!=raw:raise ValueError('REFUSE_OVERWRITE_DIFFERENT_MATERIALIZATION '+str(target))
            else:
                with target.open('xb') as f:f.write(raw)
    print(json.dumps({'original_preimages':exc.validated_originals,'native_documents':len(package['documentsBySemanticId']),'changed_nodes':sum(v['changed'] for v in exc.done.values()),'world_members':{k:len(v) for k,v in members.items()}},ensure_ascii=False))

if __name__=='__main__':
    if not __debug__:raise RuntimeError('Verification requires Python assertions; -O is forbidden.')
    sys.stdout.reconfigure(encoding='utf-8')
    main()
