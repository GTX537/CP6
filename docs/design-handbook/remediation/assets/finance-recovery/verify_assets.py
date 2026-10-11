"""Self-contained STATIC design-data verifier. No archived code/SQL is imported or run.

Implements only the explicitly checked JSON Schema keyword subset used in this
asset; this is not a general JSON Schema engine or a runtime proof verifier.
Fixture JCS uses ASCII property names, no numbers except exact small integers.
"""
from pathlib import Path
import base64, copy, datetime, hashlib, json, re

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[1]
SCHEMA=json.loads((HERE/'contracts.schema.json').read_text(encoding='utf-8'))
DEFS=SCHEMA['$defs']
MAX64=9223372036854775807
ALLOWED={'$schema','$id','title','$defs','$ref','type','additionalProperties','required','properties','oneOf','const','enum','minLength','maxLength','pattern','minimum','maximum','items','minItems','maxItems','uniqueItems','$comment'}

def check_schema(s):
    assert not set(s)-ALLOWED,('unsupported keyword',set(s)-ALLOWED)
    if '$ref' in s:
        assert s['$ref'].startswith('#/$defs/') and s['$ref'].split('/')[-1] in DEFS
    if s.get('type')=='object':
        assert s['additionalProperties'] is False
        assert set(s['required'])==set(s['properties'])
    for v in s.get('properties',{}).values():check_schema(v)
    for v in s.get('$defs',{}).values():check_schema(v)
    for v in s.get('oneOf',[]):check_schema(v)
    if 'items' in s:check_schema(s['items'])

def validate(s,x):
    if '$ref' in s:
        name=s['$ref'].split('/')[-1];validate(DEFS[name],x)
        if name in ('Seq','Counter'): assert int(x)<=MAX64,'Int64 overflow'
        if name=='Utc':datetime.datetime.strptime(x,'%Y-%m-%dT%H:%M:%S.%fZ')
        return
    if 'oneOf' in s:
        ok=0
        for sub in s['oneOf']:
            try:validate(sub,x);ok+=1
            except (AssertionError,ValueError,TypeError):pass
        assert ok==1,('oneOf',ok)
    if 'const' in s: assert type(x)==type(s['const']) and x==s['const'],('const',x)
    if 'enum' in s: assert any(type(x)==type(v) and x==v for v in s['enum']),('enum',x)
    t=s.get('type')
    if t=='object':
        assert isinstance(x,dict),'object required'
        assert set(s['required'])<=set(x),'missing property'
        assert set(x)<=set(s['properties']),'additional property'
        for k,v in x.items():validate(s['properties'][k],v)
    elif t=='array':
        assert isinstance(x,list),'array required'
        assert s.get('minItems',0)<=len(x)<=s.get('maxItems',10**30),'array size'
        if s.get('uniqueItems'): assert len({json.dumps(v,sort_keys=True) for v in x})==len(x),'duplicate item'
        for v in x:validate(s['items'],v)
    elif t=='string':
        assert isinstance(x,str),'string required'
        assert s.get('minLength',0)<=len(x)<=s.get('maxLength',10**30),'string length'
        if 'pattern' in s: assert re.search(s['pattern'],x),'pattern'
    elif t=='integer':
        assert type(x)==int,'integer required'
        assert s.get('minimum',-10**30)<=x<=s.get('maximum',10**30),'integer range'
    elif t=='boolean':assert type(x)==bool,'boolean required'
    elif t=='null':assert x is None,'null required'

def canonical(x):
    # All fixture property names are ASCII; no floating-point inputs are used.
    return json.dumps(x,ensure_ascii=False,sort_keys=True,separators=(',',':')).encode('utf-8')
def digest(domain,body):return hashlib.sha256(domain.encode('ascii')+b'\0'+canonical(body)).hexdigest()
def unique(values):return len(values)==len(set(values))
def no_duplicates(pairs):
    out={}
    for k,v in pairs:
        assert k not in out,'duplicate JSON member';out[k]=v
    return out

def semantic(name,x,context=None):
    ctx=context or {}
    if name=='ResourcePlan':
        b=x['body'];rr=b['resources'];ids=[r['resourceId'] for r in rr];order=b['acquisitionOrder']
        assert unique(ids) and len(order)==len(ids) and set(order)==set(ids),'resource permutation'
        assert unique([r['physicalIdentity'] for r in rr]),'physical alias not coalesced'
        all_alias=[a for r in rr for a in r['aliases']]
        assert unique(all_alias),'cross-resource alias collision'
        rank={v:i for i,v in enumerate(['CATALOGUE_F_MINUS_1','MOVEMENT_F0','CANONICAL','SCOPE','DETAIL','LOCAL_TERMINAL'])}
        keys={r['resourceId']:(rank[r['kind']],r['scopeDepth'] if r['kind']=='SCOPE' else 0,r['owner'],r['orderKey']) for r in rr}
        assert order==sorted(ids,key=keys.get),'required rank / ordinal ordering'
        for e in b['dependencies']:
            assert e['before'] in ids and e['after'] in ids,'unbound edge'
            assert order.index(e['before'])<order.index(e['after']),'DAG inversion/cycle'
        assert x['digest']==digest('CP6-COMPOSED-RESOURCE-PLAN/1',b),'plan digest'
    elif name=='ScopeBinding':
        b=x['body'];s=b['source'];t=b['target'];pairs={'PRODUCTION':'REAL','SIMULATION':'DEMO'}
        assert pairs[s['mode']]==t['mode'],'cross-mode scope'
        axes={'TENANT':('tenantId','tenantId'),'ENVIRONMENT':('environmentId','environmentId'),'LEGAL_ENTITY':('legalEntityId','legalEntityKey'),'LEDGER':('ledgerId','ledgerKey')}
        aliases=b['aliases'];assert len(aliases)==4 and {a['axis'] for a in aliases}==set(axes),'all four scope alias assignments'
        for a in aliases:
            source,target=axes[a['axis']];assert a['source']==s[source] and a['target']==t[target],'alias identity mismatch'
        assert x['digest']==digest(b['contract'],b),'scope digest'
    elif name=='RefBinding':
        b=x['body'];raw=base64.b64decode(b['originalWireUtf8Base64'],validate=True)
        original=json.loads(raw.decode('utf-8'),object_pairs_hook=no_duplicates)
        validate(DEFS['CreditRef'],original)
        assert original==b['original'],'original ref loss'
        assert hashlib.sha256(raw).hexdigest()==b['originalWireDigest'],'raw digest'
        assert b['original']['digest']==b['originalContentProof']['digest'],'original content proof mismatch'
        assert b['allowedType']==b['original']['type'] and b['allowedPurpose']==b['original']['purpose'],'unregistered ref purpose/type'
        assert x['digest']==digest(b['contract'],b),'binding digest'
        expected={'owner':b['adapterOwner'],'id':b['adapterId'],'version':b['adapterVersion'],'purpose':b['adapterPurpose'],'digest':x['digest']}
        assert x['target']==expected,'target must identify new binding'
    elif name=='CloseTerminal':
        b=x['body'];legs=b['coveredLegs'];keys=[(r['bookId'],r['legKey']) for r in legs]
        assert unique(keys) and set(keys)=={tuple(k) for k in ctx['planLegKeys']},'full original plan leg set'
        paths=[p for r in legs for p in r['paths']];ids=[p['pathId'] for p in paths]
        assert unique(ids) and set(ids)==set(ctx['cataloguePaths']),'full registered path set'
        assert unique([k for r in legs for k in r['coveredExecutionKeys']]),'execution alias must coalesce'
        effects=[];has_history=False
        for leg in legs:
            assert all((p['bookId'],p['legKey'])==(leg['bookId'],leg['legKey']) for p in leg['paths']),'path leg binding'
            assert all(p['aliasOf'] is None or p['aliasOf'] in ids for p in leg['paths']),'unbound path alias'
            state=leg['effectState'];original=leg['originalEffects'];ds=leg['draftDispositions'];oa=leg['ownerApplyReceipts']
            assert all(d['sourceRoot']==leg['sourceRoot'] for d in ds),'draft source root mismatch'
            if state=='NO_EFFECT':assert leg['noEffectProof'] and not original and not ds and not oa and leg['notApplicableProof'] is None,'false no-effect'
            elif state=='NOT_APPLICABLE':assert leg['notApplicableProof'] and not original and not ds and not oa and leg['noEffectProof'] is None,'false N/A'
            elif state=='DRAFT_RETAINED':assert ds and original and leg['noEffectProof'] is None and leg['notApplicableProof'] is None,'unsealed draft'
            else:assert original and oa and leg['noEffectProof'] is None and leg['notApplicableProof'] is None,'unreconciled posting'
            if state not in ('NO_EFFECT','NOT_APPLICABLE'):has_history=True
            effects.extend(original)
        assert len(effects)==len({canonical(e) for e in effects}),'duplicate original effect'
        assert {canonical(e) for e in effects}=={canonical(e) for e in b['retainedHistoricalEffectRefs']},'retained effect union'
        assert (b['result']=='CLOSED_WITH_RETAINED_HISTORY')==has_history,'terminal result/history mismatch'
        assert x['receipt']['contract']==b['contract'] and x['receipt']['digest']==digest(b['contract'],b),'terminal proof digest'
    elif name=='CloseStatus':
        if x['state']=='CLOSED':
            semantic('CloseTerminal',x['terminal'],ctx)
            assert x['closeKey']==x['terminal']['body']['closeKey'] and x['requestDigest']==x['terminal']['body']['requestDigest'],'outer identity'
    elif name=='CloseQueryResult' and x['kind']=='FOUND':semantic('CloseStatus',x['status'],ctx)
    elif name=='Problem':
        statuses={'INVALID_WIRE':400,'AUTHENTICATION_REQUIRED':401,'FORBIDDEN':403,'NOT_VISIBLE':404,'CLOSE_KEY_CONFLICT':409,'CORRECTION_IDENTITY_MISMATCH':409,'FENCE_STALE':409,'RESOURCE_SET_CHANGED':409,'SCOPE_BINDING_MISSING':422,'SCOPE_BINDING_AMBIGUOUS':422,'BINDING_RETIRED':422,'REFERENCE_TYPE_UNREGISTERED':422,'REFERENCE_PROOF_INVALID':422,'LOCK_PLAN_UNPROVEN':503,'NATIVE_UOW_UNPROVEN':503,'COVERAGE_UNPROVEN':503,'DEPENDENCY_UNAVAILABLE':503}
        assert x['status']==statuses[x['code']],'problem status/code mismatch'

def p(name='proof',contract='TEST-PROOF/1'):
    return {'owner':'TEST-OWNER','id':name,'version':'1','digest':'a'*64,'contract':contract}
def mp(name):return {'ref':{'owner':'MATCH','id':name,'version':'v1'},'digest':'a'*64,'contractRef':{'owner':'MATCH','id':'CONTRACT','version':'v1'}}
def wrap(body):return {'body':body,'digest':digest(body['contract'],body)}
NOW='2026-10-11T12:00:00.000Z'
UID='11111111-1111-1111-1111-111111111111'
V=[]
def vector(ident,name,x,expected=True,ctx=None):V.append({'id':ident,'definition':name,'expected_valid':expected,'value':copy.deepcopy(x),'semantic_context':ctx or {},'verification_scope':'STATIC_SYNTHETIC_DATA_ONLY'})
def bad(ident,name,x,edit,ctx=None):
    y=copy.deepcopy(x);edit(y);vector(ident,name,y,False,ctx)

scope={'tenantId':'tenant '+'x'*190,'legalEntityId':'legal','ledgerId':'ledger','environmentId':'production-environment','mode':'PRODUCTION'}
target={'tenantId':UID,'legalEntityKey':'legal-1','ledgerKey':'ledger-1','environmentId':'prod-1','mode':'REAL'}
sb=wrap({'contract':'FIN-CREDIT-GL-SCOPE-BINDING/1','bindingId':UID,'revision':'1','state':'ACTIVE','source':scope,'target':target,'aliases':[{'axis':axis,'source':scope[a],'target':target[b],'assignment':p(axis)} for axis,a,b in [('TENANT','tenantId','tenantId'),('ENVIRONMENT','environmentId','environmentId'),('LEGAL_ENTITY','legalEntityId','legalEntityKey'),('LEDGER','ledgerId','ledgerKey')]],'sourceAuthority':p('source-authority'),'targetAuthority':p('target-authority'),'validFrom':NOW,'predecessor':None})
vector('scope-lossless-long-id','ScopeBinding',sb)
bad('scope-cross-mode','ScopeBinding',sb,lambda x:x['body']['target'].update(mode='DEMO'))
bad('scope-unknown-mode','ScopeBinding',sb,lambda x:x['body']['source'].update(mode='PROD'))
bad('scope-omits-legal-alias','ScopeBinding',sb,lambda x:x['body']['aliases'].pop(2))
bad('scope-target-too-long','ScopeBinding',sb,lambda x:x['body']['target'].update(ledgerKey='x'*129))
bad('scope-source-renamed','ScopeBinding',sb,lambda x:x['body']['source'].update(legalEntityKey='illegal-extra'))
original={'owner':'FIN-CREDIT','type':'Reservation','id':'x'*200,'version':'raw-version','purpose':'READ_CREDIT','digest':'a'*64}
raw=canonical(original)
rb=wrap({'contract':'FIN-CREDIT-GL-REF-BINDING/1','bindingId':UID,'revision':'1','scopeBinding':p('scope-binding'),'original':original,'originalWireUtf8Base64':base64.b64encode(raw).decode(),'originalWireDigest':hashlib.sha256(raw).hexdigest(),'originalContentProof':p('content'),'consumerContract':'registered-credit-reader/1','allowedType':'Reservation','allowedPurpose':'READ_CREDIT','adapterOwner':'CP6-CREDIT-GL-BINDING','adapterId':'alias-reservation-1','adapterVersion':'v1','adapterPurpose':'BOUND_CREDIT_REFERENCE','aliases':[],'accountingAuthority':'NONE'})
rb['target']={'owner':rb['body']['adapterOwner'],'id':rb['body']['adapterId'],'version':rb['body']['adapterVersion'],'purpose':rb['body']['adapterPurpose'],'digest':rb['digest']}
vector('ref-six-to-five-lossless','RefBinding',rb)
bad('ref-original-type-dropped','RefBinding',rb,lambda x:x['body']['original'].pop('type'))
bad('ref-target-extra-type','RefBinding',rb,lambda x:x['target'].update(type='Reservation'))
bad('ref-original-digest-reused-for-wrapper','RefBinding',rb,lambda x:x['target'].update(digest='a'*64))
bad('ref-type-whitelist-mismatch','RefBinding',rb,lambda x:x['body'].update(allowedType='Approval'))
bad('ref-is-not-accounting-authority','RefBinding',rb,lambda x:x['body'].update(accountingAuthority='GL_POST'))

uow={'databaseBinding':p('db'),'connectionHandle':'actual-adapter-handle','transactionHandle':'actual-adapter-tx','transactionId':'transaction-1','principal':'cp6-writer','isolation':'SERIALIZABLE','ownership':'OUTER_TRANSACTION','provenance':p('uow')}
resources=[{'resourceId':f'r{i}','physicalIdentity':f'database/principal/resource/{i}','owner':'OWNER','kind':kind,'mode':'X','scopeDepth':0 if kind!='SCOPE' else 1,'orderKey':f'key{i}','expectedRevision':'1','membershipProof':p('membership'),'writerCoverage':p('writers'),'storage':'APPLICATION_LOCK','aliases':[f'owner/alias/{i}']} for i,kind in enumerate(['CATALOGUE_F_MINUS_1','MOVEMENT_F0','CANONICAL','SCOPE','DETAIL','LOCAL_TERMINAL'])]
plan=wrap({'contract':'CP6-COMPOSED-RESOURCE-PLAN/1','operationKey':'receipt-1','scope':{'tenant':'t','environment':'e','mode':'REAL','legalEntity':None,'site':'site','warehouse':'warehouse','binding':p('scope')},'registry':p('registry'),'nativeUow':uow,'resources':resources,'dependencies':[{'before':'r0','after':'r1','reason':'FUL-before-DEL'},{'before':'r2','after':'r3','reason':'canonical-before-scope'}],'acquisitionOrder':[r['resourceId'] for r in resources],'writerSetDigest':'a'*64,'sourceSetDigest':'a'*64,'capturedAt':NOW})
vector('resource-full-prefix','ResourcePlan',plan)
bad('resource-old-rma-scope-first','ResourcePlan',plan,lambda x:x['body']['acquisitionOrder'].__setitem__(slice(2,4),['r3','r2']))
bad('resource-f0-before-f-minus1','ResourcePlan',plan,lambda x:x['body']['acquisitionOrder'].__setitem__(slice(0,2),['r1','r0']))
bad('resource-physical-alias-split','ResourcePlan',plan,lambda x:x['body']['resources'][4].update(physicalIdentity=x['body']['resources'][3]['physicalIdentity']))
bad('resource-independent-commit','ResourcePlan',plan,lambda x:x['body']['nativeUow'].update(ownership='INNER_COMMIT'))
bad('resource-unknown-edge','ResourcePlan',plan,lambda x:x['body']['dependencies'].append({'before':'missing','after':'r1','reason':'unknown'}))
bad('resource-int64-overflow','ResourcePlan',plan,lambda x:x['body']['resources'][1].update(expectedRevision='9223372036854775808'))

identity={'correctionKey':'correction-1','inputDigest':'b'*64,'correctionEpoch':'1','planRef':mp('plan'),'planDigest':'c'*64,'scopeDigest':'d'*64,'originalRequestKey':'original-forward-1','originalForwardFence':mp('forward-fence'),'originalPostingSetDigest':'a'*64,'currentSourceChangeSetDigest':'b'*64,'authoritySetDigest':'c'*64}
request={'contract':'MATCH-CORRECTION-CLOSE/1','closeKey':UID,'identity':identity,'correctionFence':mp('correction-fence'),'expectedControlRevision':'0','reason':'cancel remaining correction effects'}
rd=digest(request['contract'],request)
vector('close-request-native-match-key-version','CloseRequest',request)
bad('close-request-forward-epoch-leak','CloseRequest',request,lambda x:x.update(admissionEpoch='1'))
bad('close-request-correction-epoch-number','CloseRequest',request,lambda x:x['identity'].update(correctionEpoch=1))
query={'contract':'MATCH-CORRECTION-CLOSE-QUERY/1','closeKey':UID,'requestDigest':rd}
vector('close-query-original','CloseQuery',query)
vector('query-not-observed-is-unknown','CloseQueryResult',{'kind':'NOT_OBSERVED','closeKey':UID,'requestDigest':rd,'canInferNoEffect':False})
vector('query-not-observed-cannot-infer','CloseQueryResult',{'kind':'NOT_OBSERVED','closeKey':UID,'requestDigest':rd,'canInferNoEffect':True},False)
path={'pathId':'route-1','owner':'AP','route':'registered-correction','capabilityVersion':'v1','registrationEpoch':'1','aliasOf':None,'bookId':'book-1','legKey':'leg-1','admission':'FENCED','closeProof':p('path-close'),'writerCoverage':p('all-writers')}
leg={'bookId':'book-1','legKey':'leg-1','sourceRoot':mp('ap-correction-root'),'required':True,'effectState':'NO_EFFECT','originalEffects':[],'draftDispositions':[],'ownerApplyReceipts':[],'noEffectProof':p('no-effect'),'notApplicableProof':None,'paths':[path],'coveredExecutionKeys':[],'executionSetProof':p('complete-empty-executions')}
body={'contract':'MATCH-CORRECTION-CLOSE-RECEIPT/1','closeKey':UID,'requestDigest':rd,'identity':identity,'correctionFence':mp('correction-fence'),'fencedThroughRevision':'2','catalogueGeneration':'1','catalogueDigest':'e'*64,'catalogueProof':p('catalogue'),'originalObligationClaimRefs':[p('original-obligation-claim')],'coveredLegs':[leg],'uncoveredLegKeys':[],'outstandingEffectRefs':[],'unresolvedExecutionKeys':[],'retainedHistoricalEffectRefs':[],'originalResultSnapshot':None,'originalForwardAdmission':'FENCED','futureEffectsClosed':True,'allowsReopen':False,'allowsReleaseOriginalObligation':False,'result':'CLOSED_NO_EFFECT','completedAt':NOW}
terminal={'body':body,'receipt':p('terminal',body['contract'])};terminal['receipt']['digest']=digest(body['contract'],body)
ctx={'planLegKeys':[['book-1','leg-1']],'cataloguePaths':['route-1']}
vector('close-proven-no-effect','CloseTerminal',terminal,ctx=ctx)
bad('close-uncovered-leg','CloseTerminal',terminal,lambda x:x['body']['uncoveredLegKeys'].append('leg-2'),ctx)
bad('close-outstanding-late-effect','CloseTerminal',terminal,lambda x:x['body']['outstandingEffectRefs'].append(p('late')),ctx)
bad('close-reopens-original','CloseTerminal',terminal,lambda x:x['body'].update(allowsReopen=True),ctx)
bad('close-releases-obligation','CloseTerminal',terminal,lambda x:x['body'].update(allowsReleaseOriginalObligation=True),ctx)
bad('close-enables-forward','CloseTerminal',terminal,lambda x:x['body'].update(originalForwardAdmission='ENABLED'),ctx)
vector('close-missing-other-book','CloseTerminal',terminal,False,{'planLegKeys':[['book-1','leg-1'],['book-2','leg-2']],'cataloguePaths':['route-1']})
vector('close-missing-alias-path','CloseTerminal',terminal,False,{'planLegKeys':[['book-1','leg-1']],'cataloguePaths':['route-1','legacy-alias']})
draft=copy.deepcopy(terminal);db=draft['body'];dl=db['coveredLegs'][0];effect=p('original-draft');dl.update(effectState='DRAFT_RETAINED',originalEffects=[effect],noEffectProof=None,draftDispositions=[{'dispositionKey':'draft-seal-1','sourceRoot':mp('ap-correction-root'),'originalDraft':effect,'originalDraftWireDigest':'f'*64,'state':'SEALED_UNPOSTED','prevents':'ALL_FUTURE_EFFECTS_FROM_THIS_SOURCE_ROOT','receipt':p('disposition')}]);db.update(result='CLOSED_WITH_RETAINED_HISTORY',retainedHistoricalEffectRefs=[effect]);draft['receipt']['digest']=digest(db['contract'],db)
vector('close-retains-sealed-draft','CloseTerminal',draft,ctx=ctx)
bad('close-draft-not-no-effect','CloseTerminal',draft,lambda x:x['body'].update(result='CLOSED_NO_EFFECT'),ctx)
bad('close-draft-needs-disposition','CloseTerminal',draft,lambda x:x['body']['coveredLegs'][0].update(draftDispositions=[]),ctx)
posted=copy.deepcopy(terminal);pb=posted['body'];pe=p('original-posted');pb['coveredLegs'][0].update(effectState='POSTED_RETAINED',originalEffects=[pe],noEffectProof=None,ownerApplyReceipts=[p('original-owner-apply')]);pb.update(result='CLOSED_WITH_RETAINED_HISTORY',retainedHistoricalEffectRefs=[pe]);posted['receipt']['digest']=digest(pb['contract'],pb)
vector('close-posted-reconciled','CloseTerminal',posted,ctx=ctx)
bad('close-posted-needs-owner-apply','CloseTerminal',posted,lambda x:x['body']['coveredLegs'][0].update(ownerApplyReceipts=[]),ctx)
status={'state':'CLOSED','closeKey':UID,'requestDigest':rd,'terminal':posted,'canInferNoEffect':False}
vector('close-status-identity-bound','CloseStatus',status,ctx=ctx)
bad('close-status-wrong-request','CloseStatus',status,lambda x:x.update(requestDigest='0'*64),ctx)
problem={'contract':'CP6-FINANCE-RECOVERY-PROBLEM/1','status':409,'code':'CLOSE_KEY_CONFLICT','retry':'QUERY_ORIGINAL','query':query,'canInferNoEffect':False,'traceId':UID}
vector('close-conflict-is-not-no-effect','Problem',problem)
bad('close-conflict-false-http-success','Problem',problem,lambda x:x.update(status=400))

def planning_authorization_shape(language,text):
    """Check only the patched PlanningAction text; never compile or execute it."""
    if language=='C#':
        auth='Authorized(a,p,operation,S(b,"plannedOrderId"));'
        init='var tx=await p.BeginSerializable();var commitConfirmed=false;try{'
        start='if(old is not null){'
        end='}var now=p.NowUtc();'
        catches='}catch{if(!commitConfirmed)await tx.Rollback();throw;}}'
        commits=['var confirmed=await tx.CommitConfirmed();','var commit=await tx.CommitConfirmed();']
        flags=['commitConfirmed=confirmed;','commitConfirmed=commit;']
        capture_check='var now=p.NowUtc();'+auth
        returns=['return new(confirmed?api=="PO14"?202:200:503,confirmed?old:CommitUnknown(S(b,"commandId")));',
                 'return new(commit?api=="PO14"?202:200:503,commit?(string)body!:CommitUnknown(S(b,"commandId")));']
    else:
        assert language=='TypeScript'
        auth='authenticated(auth,p,operation,b.plannedOrderId,p.nowUtc());'
        init='const tx=await p.beginSerializable();let commitConfirmed=false;try{'
        start='if(old!==null){'
        end='}\n const now=p.nowUtc();'
        catches='}catch(e){if(!commitConfirmed)await tx.rollback();throw e;}}'
        commits=['const commit=await tx.commit();']*2
        flags=["commitConfirmed=commit==='CONFIRMED';"]*2
        capture_check='const now=p.nowUtc();authenticated(auth,p,operation,b.plannedOrderId,now);'
        returns=["return{status:commit==='CONFIRMED'?(api==='PO14'?202:200):503,body:commit==='CONFIRMED'?old:commitUnknown(b),commit};",
                 "return{status:commit==='CONFIRMED'?(api==='PO14'?202:200):503,body:commit==='CONFIRMED'?body:commitUnknown(b),commit};"]
    assert text.count(init)==1 and 'replayCommitConfirmed' not in text,'common flag must start before try'
    assert text.endswith(catches),'confirmed denial must not roll back'
    assert text[:text.index(init)].count(auth)==1,'initial authorization missing'
    replay_start=text.index(start)
    replay_end=text.index(end,replay_start)
    branches=[text[replay_start:replay_end],text[replay_end+1:text.index('}catch',replay_end)]]
    guard='if(commitConfirmed)'+auth
    for index,branch in enumerate(branches):
        # Adjacent statements pin the await/flag/fresh-check/output order. No await
        # or unconditional authorization can appear between the final check/output.
        expected=auth+commits[index]+flags[index]+guard+returns[index]
        assert branch.count(expected)==1,(language,index,'commit/flag/fresh-check/output order')
        assert branch.count(commits[index])==1 and branch.count(flags[index])==1
        assert branch.count(guard)==1
        assert branch.count(auth)==(3 if index==1 and language=='C#' else 2),'unexpected or missing authority check'
        if index==1:assert branch.count(capture_check)==1,'capture check must use fresh time'
    return {'authorization':auth,'guard':guard,'flags':flags,'commits':commits,
            'catch':catches,'return_selectors':returns}


def check_planning_authorization(patches):
    """Static source-shape checks plus an explicitly synthetic timing model."""
    vectors=[];mutations=[];source_checks=[]
    cases=[
        # initial, capture, precommit, commit completion, expiry, commit result
        ('valid-200',(0,1,2,3,4,'CONFIRMED'),'PO11',200,True,False,'ORIGINAL_CANONICAL'),
        ('valid-202',(0,1,2,3,4,'CONFIRMED'),'PO14',202,True,False,'ORIGINAL_CANONICAL'),
        ('capture-expired',(0,5,6,7,5,'CONFIRMED'),'PO13','AUTHORIZATION_DENIED',False,True,'NO_BODY'),
        ('commit-expired',(0,1,2,6,5,'CONFIRMED'),'PO13','AUTHORIZATION_DENIED',False,False,'NO_BODY'),
        ('commit-exact-expiry',(0,1,2,5,5,'CONFIRMED'),'PO14','AUTHORIZATION_DENIED',False,False,'NO_BODY'),
        ('unknown-valid',(0,1,2,3,4,'UNKNOWN'),'PO11',503,False,False,'ORIGINAL_COMMAND_QUERY'),
        ('unknown-expired',(0,1,2,6,5,'UNKNOWN'),'PO14',503,False,False,'ORIGINAL_COMMAND_QUERY'),
    ]
    for patch in patches:
        if patch['finding']!='PLAN-IF-010':continue
        language='C#' if patch['source_path'].endswith('.cs') else 'TypeScript'
        text=patch['after'];shape=planning_authorization_shape(language,text)
        source_checks.append({'language':language,'source_path':patch['source_path'],
            'source_sha256':patch['source_sha256'],'replacement_sha256':hashlib.sha256(text.encode('utf-8')).hexdigest(),
            'both_branches_commit_flag_before_fresh_output_check':'PASS_STATIC_ONLY',
            'unknown_503_original_command_selector_unchanged':'PASS_STATIC_ONLY',
            'confirmed_denial_skips_rollback':'PASS_STATIC_ONLY'})
        # Negative mutation tests include the review regression (new branch only).
        guard=shape['guard'];auth=shape['authorization']
        last=text.rfind(guard)
        bad_texts=[('missing-new-success-final-check',text[:last]+text[last:].replace(guard,'',1)),
            ('missing-replay-final-check',text.replace(guard,'',1)),
            ('flag-recorded-after-final-check',text.replace(shape['flags'][1]+guard,guard+shape['flags'][1])),
            ('confirmed-denial-rolls-back',text.replace('if(!commitConfirmed)await tx.','await tx.')),
            ('unknown-final-check-overrides-503',text.replace(guard,auth))]
        for name,mutant in bad_texts:
            rejected=False
            try:planning_authorization_shape(language,mutant)
            except AssertionError:rejected=True
            assert rejected,(language,name,'unsafe mutation accepted')
            mutations.append({'language':language,'id':name,'expected':'REJECT','observed':'REJECT','result':'PASS_STATIC_ONLY'})
        for branch in ('REPLAY','NEW_SUCCESS'):
            for name,times,api,status,sensitive,rollback,body in cases:
                initial,capture,precommit,completion,valid_to,commit=times
                trace=[];confirmed=False
                def check(at):
                    trace.append({'event':'AUTHORIZATION_CHECK','at_tick':at,'valid':at<valid_to})
                    return at<valid_to
                # The model represents the source ordering verified above. It does
                # not call original authorization, transactions, or business code.
                accepted=check(initial) and check(capture)
                if accepted and branch=='NEW_SUCCESS':accepted=check(precommit)
                if not accepted:
                    observed={'status':'AUTHORIZATION_DENIED','sensitive_body_returned':False,'rollback_invoked':True,'body':'NO_BODY'}
                else:
                    trace.append({'event':'COMMIT_RESULT','at_tick':completion,'result':commit})
                    confirmed=commit=='CONFIRMED'
                    trace.append({'event':'RECORD_COMMIT_CONFIRMED','value':confirmed})
                    if confirmed and not check(completion):
                        observed={'status':'AUTHORIZATION_DENIED','sensitive_body_returned':False,'rollback_invoked':False,'body':'NO_BODY'}
                    elif confirmed:
                        observed={'status':202 if api=='PO14' else 200,'sensitive_body_returned':True,'rollback_invoked':False,'body':'ORIGINAL_CANONICAL'}
                    else:
                        observed={'status':503,'sensitive_body_returned':False,'rollback_invoked':False,'body':'ORIGINAL_COMMAND_QUERY'}
                expected={'status':status,'sensitive_body_returned':sensitive,'rollback_invoked':rollback,'body':body}
                assert observed==expected,(language,branch,name,observed,expected)
                vectors.append({'id':language+'-'+branch+'-'+name,'language':language,'branch':branch,'api':api,
                    'input':{'initial_tick':initial,'capture_complete_tick':capture,'precommit_tick':precommit,
                             'commit_complete_tick':completion,'valid_to_tick':valid_to,'commit_result':commit},
                    'expected':expected,'observed':observed,'trace':trace,'result':'PASS_STATIC_ONLY'})
    assert len(source_checks)==2 and len(vectors)==28 and len(mutations)==10
    output={'contract':'CP6-PLAN-010-AUTHORIZATION-TIMING/1','synthetic':True,'runtime_proof':False,
            'method':'Literal source ordering checks and abstract timing simulation only; archived C#/TypeScript is never executed.',
            'source_checks':source_checks,'vectors':vectors,'negative_patch_mutations':mutations,
            'runtime_acceptance':'NOT_RUN','actual_adoption_status':'UNPROVEN'}
    (HERE/'authorization-timing-vectors.json').write_text(json.dumps(output,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    return {'result':'PASS_STATIC_ONLY','source_branches_checked':4,'timing_vectors':len(vectors),
            'unsafe_patch_mutations_rejected':len(mutations),'runtime_result':'NOT_RUN'}


def run():
    check_schema(SCHEMA)
    (HERE/'contract-vectors.json').write_text(json.dumps({'synthetic':True,'runtime_proof':False,'vectors':V},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    results=[]
    for v in V:
        accepted=True;reason=None
        try:validate({'$ref':'#/$defs/'+v['definition']},v['value']);semantic(v['definition'],v['value'],v['semantic_context'])
        except (AssertionError,ValueError,TypeError,KeyError) as e:accepted=False;reason=str(e)
        assert accepted==v['expected_valid'],(v['id'],accepted,reason)
        results.append({'id':v['id'],'result':'PASS_STATIC_ONLY','expected_valid':v['expected_valid'],'observed_valid':accepted,'rejection':reason})
    reg=json.loads((ROOT/'00-disposition-register.json').read_text(encoding='utf-8'))
    rows=json.loads((ROOT/'workparts/finance-recovery.json').read_text(encoding='utf-8'))['findings']
    assert {x['source_finding_ids'][0] for x in rows}=={x['source_finding_ids'][0] for x in reg['issues'] if x['batch'] in ('W2','W3')}
    assert len(rows)==36
    classes={'DESIGN_GAP','ARTIFACT_CONFLICT','ADOPTION_PENDING','INTENTIONAL_DIFFERENCE','SUPERSEDED','DOCUMENT_STALE'}
    statuses={'DESIGN_RESOLVED','BOUNDARY_RECORDED','SUPERSESSION_RECORDED'}
    hashes={}
    for row in rows:
        assert row['classification'] in classes and row['design_status'] in statuses
        assert row['actual_adoption_status']=='UNPROVEN' and row['implementation_status']=='NOT_IMPLEMENTED'
        assert len(row['acceptance'])>=2 and all(a['result']=='NOT_RUN' for a in row['acceptance'])
        for e in row['evidence']:
            path=e['source_path']
            if path not in hashes:hashes[path]=hashlib.sha256(Path(path).read_bytes()).hexdigest()
            assert hashes[path]==e['sha256']
            assert e['line']<=e['end_line']<=len(Path(path).read_text(encoding='utf-8-sig').splitlines())
        for output in row['outputs']:assert (ROOT.parent/output).exists(),output
    assert json.loads((HERE/'owner-adoption.json').read_text(encoding='utf-8'))['findings']==rows
    patchdata=json.loads((HERE/'source-patches.json').read_text(encoding='utf-8'));patch_results=[]
    derivatives={}
    for pp in patchdata['patches']:
        raw=Path(pp['source_path']).read_bytes();assert hashlib.sha256(raw).hexdigest()==pp['source_sha256']
        text=raw.decode('utf-8-sig');assert text.count(pp['before'])==pp['expected_occurrences']==1
        changed=text.replace(pp['before'],pp['after']);assert changed!=text and changed.count(pp['after'])==1
        candidate=derivatives.get(pp['source_path'],text)
        assert candidate.count(pp['before'])==1,'overlapping/conflicting literal patches'
        derivatives[pp['source_path']]=candidate.replace(pp['before'],pp['after'])
        patch_results.append({'finding':pp['finding'],'source_path':pp['source_path'],'source_sha256':pp['source_sha256'],'literal_match_count':1,'in_memory_application':'PASS_STATIC_ONLY','original_modified':False})
    atp=next(v for k,v in derivatives.items() if k.endswith('PLAN-ATP-POSTGRESQL-DDL.sql'))
    for entry,validator in [('calculate_projection_stage','recover_stage_integrity'),('recover_by_id','recover_integrity')]:
        start=atp.index('CREATE PROCEDURE plan_atp.'+entry+'(');end=atp.index('END $$;',start);body=atp[start:end]
        assert body.index('PERFORM 1 FROM plan_atp.replay_record')<body.index('PERFORM 1 FROM plan_atp.evaluation')<body.index('PERFORM plan_atp.'+validator+'(')
    authorization_timing=check_planning_authorization(patchdata['patches'])
    api=next(Path('D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f3').glob('f31bcf82ae23555c*'))
    inv=next(Path('D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a').glob('3a61c4b68b8f5c01*'))
    catalogue=json.loads(api.read_text(encoding='utf-8-sig'));wire=json.loads(inv.read_text(encoding='utf-8-sig'))
    assert set(catalogue['AuthoritativeFinalGateErrorUnion']['WireCodes'])=={b['properties']['code']['const'] for b in wire['$defs']['FinalGateProblem']['oneOf']}
    links=0
    for chapter in ['03-transactions-and-recovery.md','04-finance-correction-contracts.md']:
        text=(ROOT/chapter).read_text(encoding='utf-8')
        for target in re.findall(r'\]\(([^)]+)\)',text):
            if not target.startswith(('https:','#')) and not target.endswith('static-report.json'):assert (ROOT/target).exists(),target
            links+=1
    report={'contract':'CP6-W2-W3-STATIC-REPORT/1','checked_at_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'result':'PASS_STATIC_ONLY','finding_count':len(rows),'required_runtime_acceptance_cases':sum(len(r['acceptance']) for r in rows),'runtime_acceptance_result':'NOT_RUN','schema_definitions':len(DEFS),'schema_validation':'LOCAL_EXPLICIT_KEYWORD_SUBSET_AND_SELECTED_CROSS_FIELD_RULES; not general JSON Schema conformance certification','fixture_count':len(V),'positive_count':sum(v['expected_valid'] for v in V),'negative_count':sum(not v['expected_valid'] for v in V),'source_files_hash_checked':len(hashes),'source_patches':patch_results,'composed_patch_groups':len(derivatives),'atp_recovery_and_calculation_prelude_order':'PASS_STATIC_ONLY','replay_postcommit_denial_no_rollback':'PASS_STATIC_ONLY','links_checked':links,'inv_r10_final_gate_code_set':'EXACT_MATCH','fixture_results':results,'not_verified':['actual owner proof authenticity','full runtime alias and scope registry','all writer physical coverage','SQL/C#/TS compilation or execution','database concurrency/deadlocks','business correctness on real data','permissions and deployment adoption'],'originals_modified':False,'business_execution':'NOT_RUN','database_execution':'NOT_RUN','runtime_adoption':'UNPROVEN'}
    report['planning_action_postcommit_authorization']=authorization_timing
    (HERE/'static-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:report[k] for k in ('result','finding_count','fixture_count','positive_count','negative_count','source_files_hash_checked','links_checked')},ensure_ascii=True))

if __name__=='__main__':run()
