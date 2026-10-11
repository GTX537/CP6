"""Build design data only. Never imports or executes an archived source artifact."""
from pathlib import Path
import json, hashlib, copy

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
REGISTER = json.loads((ROOT / '00-disposition-register.json').read_text(encoding='utf-8'))
ISSUES = [x for x in REGISTER['issues'] if x['batch'] in ('W2', 'W3')]

def dump(name, value):
    (HERE / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

def obj(**properties):
    return {'type':'object', 'additionalProperties':False, 'required':list(properties), 'properties':properties}
def ref(name): return {'$ref':'#/$defs/'+name}
def enum(*values): return {'enum':list(values)}
def const(value): return {'const':value}
def arr(item, minimum=0, maximum=10000): return {'type':'array','items':item,'minItems':minimum,'maxItems':maximum,'uniqueItems':True}
def nullable(value): return {'oneOf':[value,{'type':'null'}]}
def string(maximum=200, pattern=None):
    x={'type':'string','minLength':1,'maxLength':maximum}
    if pattern: x['pattern']=pattern
    return x
def union(*names): return {'oneOf':[ref(n) for n in names]}

D={}
D['Key']=string(160,r'^[^\u0000-\u001f\u007f-\u009f]+$')
D['LongKey']=string(2048,r'^[^\u0000-\u001f\u007f-\u009f]+$')
D['Digest']=string(64,r'^[0-9a-f]{64}$')
D['Seq']=string(19,r'^[1-9][0-9]*$')
D['Counter']=string(19,r'^(0|[1-9][0-9]*)$')
D['Uuid']=string(36,r'^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')
D['Utc']=string(24,r'^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$')
D['Proof']=obj(owner=ref('Key'),id=ref('Key'),version=ref('Seq'),digest=ref('Digest'),contract=ref('Key'))
D['MatchRef']=obj(owner=ref('Key'),id=ref('Key'),version=ref('Key'))
D['MatchProofRef']=obj(ref=ref('MatchRef'),digest=ref('Digest'),contractRef=ref('MatchRef'))
D['NativeScope']=obj(tenant=ref('LongKey'),environment=ref('LongKey'),mode=enum('REAL','DEMO'),legalEntity=nullable(ref('LongKey')),site=nullable(ref('LongKey')),warehouse=nullable(ref('LongKey')),binding=ref('Proof'))
D['NativeUow']=obj(databaseBinding=ref('Proof'),connectionHandle=ref('Key'),transactionHandle=ref('Key'),transactionId=ref('Key'),principal=ref('Key'),isolation=const('SERIALIZABLE'),ownership=const('OUTER_TRANSACTION'),provenance=ref('Proof'))
D['Resource']=obj(resourceId=ref('Key'),physicalIdentity=ref('LongKey'),owner=ref('Key'),kind=enum('CATALOGUE_F_MINUS_1','MOVEMENT_F0','CANONICAL','SCOPE','DETAIL','LOCAL_TERMINAL'),mode=enum('S','X'),scopeDepth={'type':'integer','minimum':0,'maximum':64},orderKey=ref('LongKey'),expectedRevision=nullable(ref('Counter')),membershipProof=ref('Proof'),writerCoverage=ref('Proof'),storage=enum('APPLICATION_LOCK','ROW','KEY_RANGE','PREDICATE_GATE'),aliases=arr(ref('LongKey')))
D['Edge']=obj(before=ref('Key'),after=ref('Key'),reason=ref('Key'))
D['ResourcePlanBody']=obj(contract=const('CP6-COMPOSED-RESOURCE-PLAN/1'),operationKey=ref('Key'),scope=ref('NativeScope'),registry=ref('Proof'),nativeUow=ref('NativeUow'),resources=arr(ref('Resource'),1),dependencies=arr(ref('Edge')),acquisitionOrder=arr(ref('Key'),1),writerSetDigest=ref('Digest'),sourceSetDigest=ref('Digest'),capturedAt=ref('Utc'))
D['ResourcePlan']=obj(body=ref('ResourcePlanBody'),digest=ref('Digest'))
D['Acquisition']=obj(resourceId=ref('Key'),physicalIdentity=ref('LongKey'),mode=enum('S','X'),transactionId=ref('Key'),heldThroughCommit=const(True))
D['LockReceipt']=obj(contract=const('CP6-COMPOSED-LOCK-RECEIPT/1'),planDigest=ref('Digest'),nativeUow=ref('NativeUow'),acquisitions=arr(ref('Acquisition'),1),terminal=enum('COMMITTED','ROLLED_BACK','OUTCOME_UNKNOWN'),receipt=ref('Proof'))

# Source Credit definitions copied exactly, including 200-character identity domains.
credit_path=next(Path('D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7').glob('f768bfcd2f485e00*'))
credit=json.loads(credit_path.read_text(encoding='utf-8-sig'))['$defs']
D['CreditScope']=copy.deepcopy(credit['scope'])
D['CreditRef']=copy.deepcopy(credit['ref'])
D['GLKey']=string(128,r'^[^\u0000-\u001f\u007f-\u009f]+$')
D['GLScope']=obj(environmentId=ref('GLKey'),tenantId=ref('Uuid'),legalEntityKey=ref('GLKey'),ledgerKey=ref('GLKey'),mode=enum('REAL','DEMO'))
D['GLRef']=obj(owner=ref('GLKey'),id=ref('GLKey'),version=ref('GLKey'),digest=ref('Digest'),purpose=ref('GLKey'))
D['IdentityAlias']=obj(axis=enum('TENANT','ENVIRONMENT','LEGAL_ENTITY','LEDGER','REF_OWNER','REF_TYPE','REF_ID','REF_VERSION','REF_PURPOSE'),source=string(200),target=ref('GLKey'),assignment=ref('Proof'))
D['ScopeBindingBody']=obj(contract=const('FIN-CREDIT-GL-SCOPE-BINDING/1'),bindingId=ref('Uuid'),revision=ref('Seq'),state=enum('ACTIVE','RETIRED'),source=ref('CreditScope'),target=ref('GLScope'),aliases=arr(ref('IdentityAlias'),4,16),sourceAuthority=ref('Proof'),targetAuthority=ref('Proof'),validFrom=ref('Utc'),predecessor=nullable(ref('Proof')))
D['ScopeBinding']=obj(body=ref('ScopeBindingBody'),digest=ref('Digest'))
D['RefBindingBody']=obj(contract=const('FIN-CREDIT-GL-REF-BINDING/1'),bindingId=ref('Uuid'),revision=ref('Seq'),scopeBinding=ref('Proof'),original=ref('CreditRef'),originalWireUtf8Base64=string(65536,r'^[A-Za-z0-9+/]+={0,2}$'),originalWireDigest=ref('Digest'),originalContentProof=ref('Proof'),consumerContract=ref('GLKey'),allowedType=string(200),allowedPurpose=string(200),adapterOwner=const('CP6-CREDIT-GL-BINDING'),adapterId=ref('GLKey'),adapterVersion=ref('GLKey'),adapterPurpose=ref('GLKey'),aliases=arr(ref('IdentityAlias'),0,16),accountingAuthority=enum('NONE'))
D['RefBinding']=obj(body=ref('RefBindingBody'),digest=ref('Digest'),target=ref('GLRef'))
D['BindingUse']=obj(contract=const('FIN-CREDIT-GL-BINDING-USE/1'),scopeBinding=ref('Proof'),refBinding=ref('Proof'),operationKey=ref('Key'),requestDigest=ref('Digest'),sourceScope=ref('CreditScope'),targetScope=ref('GLScope'),sourceRef=ref('CreditRef'),targetRef=ref('GLRef'),purpose=enum('IDENTITY_ADAPTATION_ONLY'),receipt=ref('Proof'))

D['CorrectionIdentity']=obj(correctionKey=ref('Key'),inputDigest=ref('Digest'),correctionEpoch=ref('Seq'),planRef=ref('MatchProofRef'),planDigest=ref('Digest'),scopeDigest=ref('Digest'),originalRequestKey=ref('Key'),originalForwardFence=ref('MatchProofRef'),originalPostingSetDigest=ref('Digest'),currentSourceChangeSetDigest=ref('Digest'),authoritySetDigest=ref('Digest'))
D['CloseRequest']=obj(contract=const('MATCH-CORRECTION-CLOSE/1'),closeKey=ref('Uuid'),identity=ref('CorrectionIdentity'),correctionFence=ref('MatchProofRef'),expectedControlRevision=ref('Counter'),reason=string(500))
D['CloseQuery']=obj(contract=const('MATCH-CORRECTION-CLOSE-QUERY/1'),closeKey=ref('Uuid'),requestDigest=ref('Digest'))
D['ClosePath']=obj(pathId=ref('Key'),owner=ref('Key'),route=ref('Key'),capabilityVersion=ref('Key'),registrationEpoch=ref('Seq'),aliasOf=nullable(ref('Key')),bookId=ref('Key'),legKey=ref('Key'),admission=const('FENCED'),closeProof=ref('Proof'),writerCoverage=ref('Proof'))
D['DraftDisposition']=obj(dispositionKey=ref('Key'),sourceRoot=ref('MatchProofRef'),originalDraft=ref('Proof'),originalDraftWireDigest=ref('Digest'),state=const('SEALED_UNPOSTED'),prevents=const('ALL_FUTURE_EFFECTS_FROM_THIS_SOURCE_ROOT'),receipt=ref('Proof'))
D['LegClose']=obj(bookId=ref('Key'),legKey=ref('Key'),sourceRoot=ref('MatchProofRef'),required={'type':'boolean'},effectState=enum('NO_EFFECT','DRAFT_RETAINED','POSTED_RETAINED','POSTED_AND_REVERSED_RETAINED','NOT_APPLICABLE'),originalEffects=arr(ref('Proof')),draftDispositions=arr(ref('DraftDisposition')),ownerApplyReceipts=arr(ref('Proof')),noEffectProof=nullable(ref('Proof')),notApplicableProof=nullable(ref('Proof')),paths=arr(ref('ClosePath'),1),coveredExecutionKeys=arr(ref('Key')),executionSetProof=ref('Proof'))
D['CloseTerminalBody']=obj(contract=const('MATCH-CORRECTION-CLOSE-RECEIPT/1'),closeKey=ref('Uuid'),requestDigest=ref('Digest'),identity=ref('CorrectionIdentity'),correctionFence=ref('MatchProofRef'),fencedThroughRevision=ref('Counter'),catalogueGeneration=ref('Seq'),catalogueDigest=ref('Digest'),catalogueProof=ref('Proof'),originalObligationClaimRefs=arr(ref('Proof'),1),coveredLegs=arr(ref('LegClose'),1),uncoveredLegKeys=arr(ref('Key'),0,0),outstandingEffectRefs=arr(ref('Proof'),0,0),unresolvedExecutionKeys=arr(ref('Key'),0,0),retainedHistoricalEffectRefs=arr(ref('Proof')),originalResultSnapshot=nullable(ref('Proof')),originalForwardAdmission=const('FENCED'),futureEffectsClosed=const(True),allowsReopen=const(False),allowsReleaseOriginalObligation=const(False),result=enum('CLOSED_NO_EFFECT','CLOSED_WITH_RETAINED_HISTORY'),completedAt=ref('Utc'))
D['CloseTerminal']=obj(body=ref('CloseTerminalBody'),receipt=ref('Proof'))
D['ClosePending']=obj(state=enum('PENDING','UNKNOWN'),closeKey=ref('Uuid'),requestDigest=ref('Digest'),identity=ref('CorrectionIdentity'),fencedThroughRevision=ref('Counter'),reason=enum('DRAINING','DRAFT_DISPOSITION_PENDING','OWNER_APPLY_PENDING','COVERAGE_UNPROVEN','DEPENDENCY_UNAVAILABLE','COMMIT_CONFIRMATION_UNAVAILABLE'),outstandingEffectRefs=arr(ref('Proof')),uncoveredLegKeys=arr(ref('Key')),query=ref('CloseQuery'),canInferNoEffect=const(False))
D['CloseClosed']=obj(state=const('CLOSED'),closeKey=ref('Uuid'),requestDigest=ref('Digest'),terminal=ref('CloseTerminal'),canInferNoEffect=const(False))
D['CloseStatus']=union('ClosePending','CloseClosed')
D['QueryFound']=obj(kind=const('FOUND'),status=ref('CloseStatus'),canInferNoEffect=const(False))
D['QueryMissing']=obj(kind=enum('NOT_OBSERVED','UNAVAILABLE'),closeKey=ref('Uuid'),requestDigest=ref('Digest'),canInferNoEffect=const(False))
D['CloseQueryResult']=union('QueryFound','QueryMissing')
D['Problem']=obj(contract=const('CP6-FINANCE-RECOVERY-PROBLEM/1'),status=enum(400,401,403,404,409,422,503),code=enum('INVALID_WIRE','AUTHENTICATION_REQUIRED','FORBIDDEN','NOT_VISIBLE','CLOSE_KEY_CONFLICT','CORRECTION_IDENTITY_MISMATCH','FENCE_STALE','SCOPE_BINDING_MISSING','SCOPE_BINDING_AMBIGUOUS','BINDING_RETIRED','REFERENCE_TYPE_UNREGISTERED','REFERENCE_PROOF_INVALID','LOCK_PLAN_UNPROVEN','RESOURCE_SET_CHANGED','NATIVE_UOW_UNPROVEN','COVERAGE_UNPROVEN','DEPENDENCY_UNAVAILABLE'),retry=enum('NONE','QUERY_ORIGINAL','REPREPARE'),query=nullable(ref('CloseQuery')),canInferNoEffect=const(False),traceId=ref('Uuid'))
schema={'$schema':'https://json-schema.org/draft/2020-12/schema','$id':'urn:cp6:finance-recovery:1.0.0','title':'New CP6 W2/W3 supplement; original wire unchanged','$defs':D,'oneOf':[ref(n) for n in ('ResourcePlan','LockReceipt','ScopeBinding','RefBinding','BindingUse','CloseRequest','CloseQuery','CloseStatus','CloseQueryResult','Problem')]}
dump('contracts.schema.json',schema)

paths={}
for path,op,req,res,success in [('/internal/finance/match-adjustments/closures/v1/fence-and-close','fenceAndCloseCorrection','CloseRequest','CloseStatus',['200','202']),('/internal/finance/match-adjustments/closures/v1/query','queryCorrectionClose','CloseQuery','CloseQueryResult',['200'])]:
    paths[path]={'post':{'operationId':op,'security':[{'serviceIdentity':[]}],'requestBody':{'required':True,'content':{'application/json':{'schema':{'$ref':'contracts.schema.json#/$defs/'+req}}}},'responses':{code:{'description':'Original identity-bound result; no-effect only terminal proof','content':{'application/json':{'schema':{'$ref':'contracts.schema.json#/$defs/'+res}}}} for code in success}}}
    for code in ('400','401','403','404','409','422','503'):
        paths[path]['post']['responses'][code]={'description':'Typed failure; never proof of no effect','content':{'application/json':{'schema':{'$ref':'contracts.schema.json#/$defs/Problem'}}}}
dump('contracts.openapi.json',{'openapi':'3.1.0','info':{'title':'CP6 correction close supplement','version':'1.0.0'},'paths':paths,'components':{'securitySchemes':{'serviceIdentity':{'type':'mutualTLS'}}}})

# Data-only literal patches: read immutable source; never write or compile it.
patches=[]
def patch(issue,path,before,after,expect=1):
    p=Path(path);raw=p.read_bytes();text=raw.decode('utf-8-sig')
    assert text.count(before)==expect,(issue,text.count(before))
    patches.append({'finding':issue,'source_path':str(p).replace('\\','/'),'source_sha256':hashlib.sha256(raw).hexdigest(),'encoding':'UTF-8; preserve original BOM and newline bytes','expected_occurrences':expect,'before':before,'after':after,'mode':'LITERAL_REPLACE_DATA_ONLY','applied_to_original':False})
base=Path('D:/CP6-archives/consolidation-20261010/root-planning-cache')
patch('PLAN-IF-005',base/'atp-r11/PLAN-ATP-POSTGRESQL-DDL.sql',
      '  SELECT * INTO STRICT h FROM plan_atp.evaluation\n   WHERE organization_id=p_organization_id AND evaluation_id=p_evaluation_id FOR UPDATE;\n  PERFORM 1 FROM plan_atp.replay_record WHERE organization_id=h.organization_id AND evaluation_id=h.evaluation_id FOR UPDATE;',
      '  PERFORM 1 FROM plan_atp.replay_record\n   WHERE organization_id=p_organization_id AND evaluation_id=p_evaluation_id\n   ORDER BY actor_id COLLATE "C",delegation_key COLLATE "C",operation COLLATE "C",request_key COLLATE "C" FOR UPDATE;\n  IF NOT FOUND THEN RAISE EXCEPTION \'REPLAY_BINDING_MISSING\' USING ERRCODE=\'23514\'; END IF;\n  SELECT * INTO STRICT h FROM plan_atp.evaluation\n   WHERE organization_id=p_organization_id AND evaluation_id=p_evaluation_id FOR UPDATE;')
atp_path=base/'atp-r11/PLAN-ATP-POSTGRESQL-DDL.sql'
ordered_replay_eval='  PERFORM 1 FROM plan_atp.replay_record\n   WHERE organization_id=p_organization_id AND evaluation_id=p_evaluation_id\n   ORDER BY actor_id COLLATE "C",delegation_key COLLATE "C",operation COLLATE "C",request_key COLLATE "C" FOR UPDATE;\n  IF NOT FOUND THEN RAISE EXCEPTION \'REPLAY_BINDING_MISSING\' USING ERRCODE=\'23514\'; END IF;\n  PERFORM 1 FROM plan_atp.evaluation\n   WHERE organization_id=p_organization_id AND evaluation_id=p_evaluation_id FOR UPDATE;\n'
patch('PLAN-IF-005',atp_path,
      "  IF p_expected_etag_version<>2 THEN RAISE EXCEPTION 'EXPECTED_CAS_MISMATCH' USING ERRCODE='40001'; END IF;\n  PERFORM plan_atp.recover_stage_integrity(p_organization_id,p_evaluation_id);",
      "  IF p_expected_etag_version<>2 THEN RAISE EXCEPTION 'EXPECTED_CAS_MISMATCH' USING ERRCODE='40001'; END IF;\n"+ordered_replay_eval+'  PERFORM plan_atp.recover_stage_integrity(p_organization_id,p_evaluation_id);')
patch('PLAN-IF-005',atp_path,
      "    RAISE EXCEPTION 'RECOVERY_IDENTITY_DIGEST_MISMATCH' USING ERRCODE='23514';\n  END IF;\n  PERFORM plan_atp.recover_integrity(p_organization_id,p_evaluation_id);",
      "    RAISE EXCEPTION 'RECOVERY_IDENTITY_DIGEST_MISMATCH' USING ERRCODE='23514';\n  END IF;\n"+ordered_replay_eval+'  PERFORM plan_atp.recover_integrity(p_organization_id,p_evaluation_id);')
patch('PLAN-IF-005',atp_path,
      "     AND replay_envelope_digest=req#>>'{replay,replayEnvelopeDigest}'\n   FOR KEY SHARE;",
      "     AND replay_envelope_digest=req#>>'{replay,replayEnvelopeDigest}'\n   FOR UPDATE;")
intake_before="""  SELECT r.evaluation_id,e.status INTO p_evaluation_id,p_result_state
    FROM plan_atp.replay_record r JOIN plan_atp.evaluation e USING(organization_id,evaluation_id)
   WHERE r.organization_id=p_organization_id AND r.actor_id=t.actor_id
     AND r.delegation_id IS NOT DISTINCT FROM t.delegation_id
     AND r.operation='ATP_EVALUATE' AND r.request_key=req#>>'{replay,requestKey}'
     AND r.request_body_digest=expected_body
     AND r.replay_envelope_digest=expected_replay
   FOR UPDATE OF r,e;
  IF FOUND THEN p_outcome:='REPLAY'; RETURN; END IF;"""
intake_after="""  SELECT r.evaluation_id INTO p_evaluation_id
    FROM plan_atp.replay_record r
   WHERE r.organization_id=p_organization_id AND r.actor_id=t.actor_id
     AND r.delegation_id IS NOT DISTINCT FROM t.delegation_id
     AND r.operation='ATP_EVALUATE' AND r.request_key=req#>>'{replay,requestKey}'
     AND r.request_body_digest=expected_body
     AND r.replay_envelope_digest=expected_replay
   FOR UPDATE;
  IF FOUND THEN
    SELECT e.status INTO STRICT p_result_state FROM plan_atp.evaluation e
     WHERE e.organization_id=p_organization_id AND e.evaluation_id=p_evaluation_id FOR UPDATE;
    p_outcome:='REPLAY'; RETURN;
  END IF;"""
patch('PLAN-IF-005',atp_path,intake_before,intake_after)
p=base/'plan-po-cp22/members'
cs_path=p/'PLAN_PO_R3_CP18_ACTION_HTTP_DESIGN.cs'
cs_before=next(line for line in cs_path.read_text(encoding='utf-8-sig').splitlines() if 'public static async Task<HttpResponseCp18> PlanningAction(' in line)
cs_after=cs_before.replace('var tx=await p.BeginSerializable();try{','var tx=await p.BeginSerializable();var commitConfirmed=false;try{')
cs_after=cs_after.replace('if(old is not null){Parse(api=="PO13"?"ConversionPreview":"CommandOutcome",old);var confirmed=await tx.CommitConfirmed();return','if(old is not null){Parse(api=="PO13"?"ConversionPreview":"CommandOutcome",old);Authorized(a,p,operation,S(b,"plannedOrderId"));var confirmed=await tx.CommitConfirmed();commitConfirmed=confirmed;if(commitConfirmed)Authorized(a,p,operation,S(b,"plannedOrderId"));return')
cs_after=cs_after.replace('var commit=await tx.CommitConfirmed();return','var commit=await tx.CommitConfirmed();commitConfirmed=commit;if(commitConfirmed)Authorized(a,p,operation,S(b,"plannedOrderId"));return')
cs_after=cs_after.replace('catch{await tx.Rollback();throw;}','catch{if(!commitConfirmed)await tx.Rollback();throw;}')
patch('PLAN-IF-010',cs_path,cs_before,cs_after)
ts_path=p/'PLAN_PO_R3_CP18_HTTP_HANDLERS_DESIGN.ts'
ts_lines=ts_path.read_text(encoding='utf-8-sig').splitlines()
ts_before='\n'.join(ts_lines[104:108])
assert ts_before.startswith('export async function handlePlanningActionCp18(')
ts_after=ts_before.replace('const tx=await p.beginSerializable();try{','const tx=await p.beginSerializable();let commitConfirmed=false;try{')
ts_after=ts_after.replace("if(old!==null){(parseCp18Boundary(api==='PO13'?'ConversionPreview':'CommandOutcome',old) as J);const commit=await tx.commit();return","if(old!==null){(parseCp18Boundary(api==='PO13'?'ConversionPreview':'CommandOutcome',old) as J);authenticated(auth,p,operation,b.plannedOrderId,p.nowUtc());const commit=await tx.commit();return")
ts_after=ts_after.replace("const commit=await tx.commit();return","const commit=await tx.commit();commitConfirmed=commit==='CONFIRMED';if(commitConfirmed)authenticated(auth,p,operation,b.plannedOrderId,p.nowUtc());return")
ts_after=ts_after.replace('catch(e){await tx.rollback();throw e;}','catch(e){if(!commitConfirmed)await tx.rollback();throw e;}')
patch('PLAN-IF-010',ts_path,ts_before,ts_after)
patch('PLAN-IF-011',p/'PLAN_PO_R3_CP18_NATIVE_KERNEL_SQL_DESIGN.sql',
      '  IF @NextCommanded=1 EXEC plan_po.EnqueueOwnerCommandCp18 @EnvironmentId,@TenantId,@PlannedOrderId,@ConversionLegId,@NextStepOrdinal,@Owner,@NextOwnerCommandKey,@Now;\n  COMMIT;',
      '  IF @NextCommanded=1 EXEC plan_po.EnqueueOwnerCommandCp18 @EnvironmentId,@TenantId,@PlannedOrderId,@ConversionLegId,@NextStepOrdinal,@Owner,@NextOwnerCommandKey,@Now;\n  COMMIT;\n  SELECT @LocalTerminalCanonical AS TerminalCanonical,CAST(0 AS bit) AS Replayed;')
inv=next(Path('D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f3').glob('f31bcf82ae23555c*'))
patch('OPS-IF-029',inv,'"Schema": "CLOSED-WIRE-SCHEMA_R8.json#/$defs/FinalGateProblem"','"Schema": "CLOSED-WIRE-SCHEMA_R10.json#/$defs/FinalGateProblem"')
dump('source-patches.json',{'contract':'CP6-DESIGN-SOURCE-PATCH/1','execution':'NOT_RUN','application_semantics':'Group by source_path and original source_sha256. Verify the original once, preflight every before count, apply all non-overlapping literal replacements to one in-memory copy in listed order. Never re-check original SHA against an intermediate modified copy; never write the archive. A future adopter saves a separately versioned derivative only.','patches':patches})

# Every original finding receives a concrete disposition and two runtime acceptance cases.
ROWS=[
('COM-OWNER-LOCK-ORDER','ADOPTION_PENDING','BP R3 + EST + QTN accepted dependency DAG','采用 IAM→BP（含Mapping/ReverseClaim）→EST→QTN 已登记依赖；闭包预收集、循环或遗漏 LOCK_PLAN_UNPROVEN；禁止持EST回调BP。','BP/EST/QTN的CRUD、导入、legacy、映射及全部权限撤销writer使用同一门；逐域native连接证明。','BP writer与QTN消费同gate竞争','串行成功或重新准备，完整原依赖闭包持至提交','EST先锁后调用BP，或import绕门','LOCK_PLAN_UNPROVEN，零新效果'),
('COM-IC-PROTECTION-ADOPTION','ADOPTION_PENDING','INT-COM v1.0.2 IC-SOURCE-PROTECTION/1','仅 MAIN_INTAKE_NEW_EFFECT 与 CRM_RESULT_PROJECTION 两purpose采用；durable grant不因超时自行释放；本地COMMITTED/ABORTED原终态结算，权限即时撤销独立。','CRM/Main 全Control→ActiveGrant变更、policy/stream/反向索引及Execution消费；要求立即撤销在途效果的purpose禁用本协议。','撤销请求与已经获得grant的本地commit竞争','允许原grant先commit；source根据原终态结算，结果有证据','用grant超时推导ABORTED或复活权限','拒绝释放，不输出过期敏感数据'),
('COM-IC-BOUNDED-REBUILD','ADOPTION_PENDING','INT-COM v1.0.2 bounded rebuild','固定原H/V目录、完整CRM handoff/attribution/group/conflict与Main Intake/Quote/Commercial/Resolution集合，分页代际与CAS重核；UNCHANGED只刷新checkedAt。','目录所有增删改writer与重建同commit；完整页/切口/权限/取消/rebuild头；向量cut不宣称全局同时当前。','完成全部页且代际未变','原结果可被CAS采用，UNCHANGED不产生新商业事实','中途漏页或新的成员加入','废弃当前候选重建；空页不证明无效果'),
('COM-FUL-CATALOGUE-AND-CURRENT-FENCE','ADOPTION_PENDING','ERP-FUL v1.4 R3 FUL-SCHEDULE-CATALOGUE-1','采用完整Sales母集合和预建空site F−1门；F−1→全部F0→owner heads，严格当前仅native同库Serializable；跨库返回UNKNOWN。','ORD confirm/increase/decrease/cancel/transfer/recommit/correction、schedule拆合删建、过滤属性/时间生效/权限/import/admin/迟到writer均X；跨site按序。','空site快照与首次schedule加入竞争','成员门保护完整集合，结果可重核','Stock先拿F0再回头拿F−1或跨库冒充strict','拒绝重入；UNKNOWN且仅允许明确lastConfirmed视图'),
('COM-S6-PRECHECKPOINT-FINALAUTH','ADOPTION_PENDING','S6 v0.2 CRM precheckpoint + OwnerFinalAuthorizationGateV1','重复/旧/同版本成功前先真事务持久化checkpoint，transport/raw与content摘要独立；Reconciliation→Published→Projection→Inbox→checkpoint；最终授权必须真实native held。','Core权限撤销writer、CRM投影/intake/reconciliation所有writer；历史读取、可信接收登记、技术重投、新业务分别授权。','重复外部事务且原checkpoint完备','当前读权限下返回原receipt','同txId不同摘要或最终授权无真实gate','DRIFT quarantine撤销allow；无gate BLOCKED'),
('OPS-IF-003','DESIGN_GAP','New CP6-COMPOSED-RESOURCE-PLAN/1','显式替代RMA §9.1在Stock组合事务中的完整取锁序（含Scope-first与source/claim/IN位置）；F−1→F0→所有Canonical→所有Scope父到子→DETAIL owner/key ordinal→终态，按注册DAG验证完整闭包。','Stock十二WriteKind、RMA source/auth/claim/IN/QA/H30/correction、OUT/RSV/MAT/OUTPUT/PLM/MASTER/metric/capacity及legacy/import/worker全覆盖。','RMA receipt与Stock correction同资源竞争','同native Uow按统一次序串行，Stock+IN+RMA/H30+claim一并提交','漏canonical/物理alias分裂/先Scope后Canonical/中途增资源','LOCK_PLAN_UNPROVEN或RESOURCE_SET_CHANGED，全回滚重收集'),
('OPS-IF-004','ADOPTION_PENDING','RMA original five disposition adapters REQUIRED_FENCED','五类disposition逐一登记owner/route/capability/query/seal/correction；QA-NCR设计通过不构成报废/返修/转移运行授权，IN收货receipt不替代处置效果。','RMA五adapter及其Stock/H30/QA owner原命令query/close全路径证据齐全才逐项解门。','一个adapter全采用且当前授权有效','只开启该处置目的，原物理证据完整','用IN收货成功推导QA报废可执行','对应处置保持REQUIRED_FENCED，不伪造no-effect'),
('OPS-IF-005','ADOPTION_PENDING','WMS-DEL v1.3 R3 F0 integration','采用F0→K1→S2→S3→E4→D5→L6→O7原序及Movement解释epoch；Stock相关更正预收全Movement F0；新鲜Stock发现cut与OUT应用集合比较。','DEL/OUT/Stock全部相关correction、permit/closure/late apply writer同数据库连接Serializable；采用CP6组合计划的物理alias登记。','permit commit先于closure赢得同F0','恢复原已提交事实，再关闭未来effect','漏notify且OUT自称nogap或关闭时遗漏新Movement','WAITING_SOURCE/UNKNOWN，不接受旧permit'),
('OPS-IF-009','ADOPTION_PENDING','MAT PENDING-BASIS-SUCCESSION/1 + EXEC X44 CURRENT','保留PENDING_BASIS_SUCCESSION与NEW_CONSUMPTION分purpose；same EXEC-BASIS-DOMAIN保护未满足义务无gap转移，旧 CLOSED_SUPERSEDED_BEFORE_EFFECT保留历史。','MAT/EXEC normal consumption、correction、succession、queue/obligation/WO cursor全writer同quantityUow；M35–37设计后继不重开。','旧未满足义务转新basis，集合CAS一致','旧终态+新义务同Uow，数量不丢失','把superseded解释为物理no-effect或遗漏在途消费','拒绝；未知继续保护'),
('OPS-IF-010','ADOPTION_PENDING','MES-OUTPUT R1 E identity and measurement budget','E真实IdentityDomain/measurement budget在Basis/work前；Stock Canonical→Scope→E→Output grant→Stock fine→IN local，E B writer不得反向取Stock/WO。','T-REPORT/CORRECTION-SOURCE/COMPLETE、late closure/retraction/gap/fence/migration/correction-settle全writer登记；Facade仍受控新契约。','同物理identity的normal与late report竞争','measurement budget只消费一次','仅锁Basis或E writer反调Stock','拒绝不完整resourcePlan，无109AC运行通过声明'),
('OPS-IF-011','ADOPTION_PENDING','MES-OUTPUT R1 cumulative allowance close','缩量先在同allowance/source gate关闭所有可能Stock执行/aliases含空集合，禁新SP01/SP02，已POSTED/SEALED消费保留；version+1只更新净allowance。','所有执行注册/查询/seal/late posting、source registration与缩量writer同门；每grant seal不是全路径closure。','allowance100已消费40后缩90且所有执行已闭','consumed40保持、remaining50，无物理逆转','遗漏一个inflight执行或把缩量当退货','继续PENDING/UNKNOWN，不释放数量'),
('OPS-IF-017','SUPERSEDED','QA-NCR R12 root decision + proof addendum2','R12闭合旧profile/Uow proof gap：UTF8 domain+NUL+JCS完整非digest proof、INPUT9 current generation、真实同Uow；RETURN_TO_SOURCE仍不在Finance R10 H30枚举。','QA-NCR与26专业设计adoption须逐实际consumer证明；旧proof原字节保留。','REACCEPT/SCRAP/REWORK/HOLD按R12 proof核验','按已接受语义消费，记录真实Uow','RETURN_TO_SOURCE映射成HOLD或事后补注册证明原Uow','H30_QA_RETURN_SEMANTIC_UNMAPPED/PENDING'),
('OPS-IF-001','DESIGN_GAP','New FIN-CREDIT-GL scope/ref binding /1','完整Credit五维scope与GL五维scope经双向唯一不可变registry映射；PRODUCTION→REAL、SIMULATION→DEMO；六字段Ref原bytes及type/purpose经binding保存，五字段GL Ref引用新binding。','Credit/GL及全部中间consumer只用受信scope、批准alias和精确type/purpose注册；绑定不赋予会计生产者资格。','200字符Credit身份且存在合法可逆alias','无截断进入GL≤128及tenant UUID；原Ref可完整回溯','未知环境/跨mode/两个binding/丢type或套用原digest给新body','默认拒绝，无业务或财务效果'),
('OPS-IF-024','DESIGN_GAP','New MATCH-CORRECTION-CLOSE/1','CorrectionAdmission独立close/query/receipt：先自身FENCED，完整plan所有book/leg及route/alias/inflight闭合；Draft保留并SEALED_UNPOSTED；Posted先恢复OwnerApply，终态保留历史轴。','MATCH/AP/GL所有correction create/post/retry/import/manual/bridge/recovery writer用原source root控制门和原义务claim；原forward持续FENCED。','close与迟到GL Posted竞争，AP未Apply','回收原事实/OwnerApply，返回CLOSED_WITH_RETAINED_HISTORY，绝不再次post','只一条leg关闭或将Draft当NO_EFFECT/自动reverse','PENDING/UNKNOWN；无原义务释放，无自动reopen'),
('COM-CREDIT-INDEPENDENT-CASH-AND-DEMAND-SOURCE','ADOPTION_PENDING','ERP-CREDIT + unified detailed design stage100','现金退款完整current source proposal只作提议；真实5S/rights/currency/lineage原目录（含legacy/import/manual/alias/inflight/reservation/end）；新demand需原owner/node25真实source。','FIN-AR R2/RMA R3精确owner消费；QA仅carried不可直接；退款owner完整原集合，需求节点25的owner接受单独证明。','原履行100与新真实需求分别记录','旧履行不被覆盖，新demand有独立真实来源','伪造newDemandCreated Ref或建空cash目录声称完备','保持RequiredFenced，不创造商业需求/现金事实'),
('COM-MATCH-MRX-AND-ORIGINAL-OBLIGATION','ADOPTION_PENDING','PUR-RETURN R3 + MRX v0.2 shared acceptance','退货实物不VOID原GR，E=100不变；PENDING_RETURN MRX禁新forward但不自动FENCE旧admission；MATCH/RETURN/manual共享OriginalObligation budget，RETURNED_RETIRED不随credit/refund消失。','Stock→PhysicalFact→MRX→SourceReceipt→OUTResult真实同Uow；MRX原forward fence和共享AP义务claim全部writer采用。','已退货后收到finance credit/refund','实物退役和原义务保护保持','把退货当GR VOID或credit释放原MATCH allocation','拒绝；只能经独立完整REOPEN批准链'),
('COM-RETURN-CURRENT-KNOWLEDGE-CUT','ADOPTION_PENDING','PUR-RETURN R3 T15/T16','current集合是递归lineage terminal leaf的active/actual/unresolved union，非所有历史行；T15先generation失效，T16精确query/attempt/worker/CAS；新frontier必须新capture。','PUR-RETURN/OUT/Stock全部case/head/lineage/discovery writer与分页cut协调；迟到旧回复保历史且高frontier必须失效current。','旧请求晚到携带更高frontier14且目标需15','拒绝旧current，追赶后重新capture完整cut','OUT局部nogap直接将原旧cut标current','继续UNKNOWN，不触发新effect'),
('COM-RETURN-H31-COMPOSITE-RESOURCES','ADOPTION_PENDING','PUR-RETURN R3 H31 composite adopted design','全部warehouse祖先后location；DETAIL全局owner/key最强锁；每location metricSet含N/A取X，CONTROLLED完整metric/domain/head/occupancy/mapping/rule；一Part一Uow。','Stock/OUT/RETURN/MASTER/capacity/metricSet所有writer；集合数随地点变化（例60），不是固定49；SourceCapacityResult复用原Stock receipt。','多location+N/A metric并发改metric membership','完整resourcePlan串行或整Part回滚','只锁target位置/固定49/重复计SourceCapacity','拒绝；已永久REJECTED以原全路径no-effect证明，无假Closure'),
('COM-SUB-COST-RECEIPT-VS-ACCEPTANCE','INTENTIONAL_DIFFERENCE','PUR-SUB Recovery R2 cost/consumption cuts','Finance approved RECEIPT完整累计Q作cost，Pcum+sameM仅一次、累计舍入取delta；消耗依据独立COMMERCIAL_ACCEPTANCE_GROSS与冻结Qplan、unitUsage。','PUR-SUB/Finance cost/acceptance owners分别完整cut；真实return不VOID原receipt，pending/unknown分别表示。','PO100→80，已验60待40且原Qplan100','旧消耗分母100，cost使用approved cumulative receipt','用finishedQty或新PO80替代冻结消耗basis','拒绝推算，缺cut为PENDING/UNKNOWN'),
('OPS-IF-012','ADOPTION_PENDING','FIN-COST R2 source/GL completion controls','全部pre-source/Cost controls→FM/BP→cost heads真实sameUow；GL source完整原envelope；GL CompletionCurrent S到成本Apply，reverse writer X。','MAT/EXEC/OUTPUT/source domains、D06与D16各自revision、FIN-COST和GL reversal全部writer注册，不共享假revision。','GL完成读与reversal竞争','先apply原成本或见reverse后拒绝新apply','只读旧GL receipt或source门跨库','RequiredFenced，不声称原子Apply'),
('OPS-IF-020','ADOPTION_PENDING','S2 RP-CLOSE-03 bounded closed-period adjustment','新GL consumer扩展仅精确close adjustment授权：effect/date/period真status/fence/target/policy/approval/SOD/bankbudget和gl.close-adjustment.post；不将已关period OPEN。','GL及close源owner采用扩展新版本；旧wire保持closed，不加字段绕过period检查。','已关期且exact RP-CLOSE-03授权全部齐','仅批准的adjustment腿可执行，period仍closed','普通AP/AR凭close包绕过锁期','LOCKED_PERIOD_EXTENSION_UNPROVEN/拒绝'),
('OPS-IF-021','INTENTIONAL_DIFFERENCE','S2 FX_SCHEDULED_CHAIN','仅预登记FX次期计划镜像：原period immutable post+精确次期mirror、current original REVERSED、Owner applied、mirror current有效；period fulfillment、forward current、lifecycle三轴分离。','S2 FX/GL/period owner按purpose采用；DURABLE_NOT_DUE仅original仍Posted且未到期的特定policy，不扩散AP/AR。','FX原期已完成、次期mirror完成且原current反转','历史期fulfilled保持，current显示reversed','普通错误更正套FX例外或镜像失效仍fulfilled current','拒绝该例外，按各自purpose重新评估'),
('OPS-IF-022','ADOPTION_PENDING','S2 RP-REPORT-01 + FX historical exposure','report读完整ledger immutable forward/reverse effectiveDate+cut与opening覆盖，GL-only/manual/source-null也包含；FX取as-of历史敞口+owner全集+rates+signed credits。','GL所有角色/手工journal/银行及各敞口owner提供完整历史，缺任一required owner使整plan阻断。','当前reversed原分录属于历史报告区间','原分录按有效日仍纳入、逆转在自己有效日纳入','opening缺失置0/只取今日open/负敞口夹0','报告/FX计划不完整，禁止提交'),
('OPS-IF-023','ADOPTION_PENDING','FIN-AP R1 seven source branches + AP-FUNDS-EVIDENCE-1','保留七封闭source union（MATCH_FORWARD/CORRECTION、RETURN、PAYMENT、ALLOCATION、REVERSAL、REFUND），EffectTarget精确绑定；funds evidence为读，RECORD事实与未来GL目标分离。','AP/Bank/Cash/check owner按method的真实状态和current root/account/control gate采用；每branch原source/plan/manifest/leg绑定。','银行真实入账或已清算支票证据匹配','允许对应源判断，不执行现金','未清算check当真实carry或泛化成统一source形状','阻断财务新效果，保留原branch错误'),
('OPS-IF-025','ADOPTION_PENDING','FIN-AP R1 batch bridge/allocation reversal','batch reservation跨独立GL到全AP余额OwnerApply，GL成功本地失败保留bridge+reservation；A28只反allocation component，A29 paymentGL独立，cash refund独立。','AP各batch/GL bridge/OwnerApply/付款/分配/退款及manual writer共享原义务claim；同cut signed liability invoice−unallocated payment−bridge，prepayment asset独立。','GL成功AP Apply失败后query恢复','只补原OwnerApply，bridge不提前消失','数据库rollback宣称GL也撤销或allocation reverse自动退款','禁止；保留原已过账事实与保护'),
('OPS-IF-026','INTENTIONAL_DIFFERENCE','FIN-AR R2 per-event readiness','AR总体UNRESOLVED下policy-ready INVOICE可独立完成；INV独占COGS source，AR只是观察协调；不套AP整manifest完成门。','FIN-AR/GL按各event全部腿及policy验证；INV COGS owner独立证据，UI清楚区分总轴与event轴。','INVOICE ready但COGS source缺失','仅合法INVOICE event推进，AR总体仍unresolved','AR自行生成COGS或总UNRESOLVED一概视无GL效应','拒绝COGS伪造，保留已完成event'),
('OPS-IF-027','INTENTIONAL_DIFFERENCE','FIN-AR R2 R-GL-AR-LEDGER-CUT-1','全历史role mapping+GL-only/manual source-null入账，原reversed仍留历史；strict切口需要同native snapshot，CUT_VECTOR_ONLY必须明确诊断选择。','AR/GL账本所有角色历史与开账范围完整，100k上限超出阻断而非截断；跨owner读取不能静默降级。','完整同native切口包含历史原+逆转','严格对账可采用','独立向量假冒strict或超过100k悄悄截断','拒绝strict，需显式诊断模式/缩小有效范围'),
('OPS-IF-028','ADOPTION_PENDING','FIN-AR R2 AR_RECEIPT_CONFIRM + D25/D12/D13','真实funds/GL Posted不替代业务OA；独立receipt确认新baseline+claims+receipt same finalUow；未Apply compensation不恢复余额，已Apply allocation走inverse。','AR/OA/Core/GL/funds owner分别current gates；旧decision/plan stale不得携带，blocked保留GL及claims。','GL已Posted、OA暂缺后获得新批准','先保留原事实；新完整批准后只Apply一次','复用旧OA或把未Apply补偿当余额恢复','阻断Apply且不释放claims'),
('OPS-IF-029','SUPERSEDED','FIN-INV R10 narrow R8 reference supersession','仅handlercontext/cold C09争议由R10替代；cold按Table/Row重建三durable Uow一致，六TransactionUowId只write envelope；API final-gate引用按R10精确schema解析。','INV/AP/Cost consumer用R10闭wire与原语义；scope slot policy不进入economic key；提供精确API R8→R10引用patch，不全盘覆盖R8。','R10冷重建与三Uow一致，FinalGateProblem原code一致','接受该窄profile，保留其他历史字段语义','要求cold持六write envelope事务id或使用旧schema文件解析R10','拒绝错误reader；不能扩大supersession范围'),
('OPS-IF-031','INTENTIONAL_DIFFERENCE','Finance shared X5 GL measurement profiles','货币component qty=abs(amount)、unitprice1需已支持currencyunit basis；UOP实际qty+未舍入rate+residual最终舍入amount；allocation按posting incidence purpose分侧。','GL profile registry按真实source/purpose采用；不加NA/额外unit字段，missing qty仍unresolved，不能统一qty1。','货币profile与真实UOP分别解析','各自精确守恒且不重复业务qty','UOP缺qty补1或两会计侧累计双份数量','UNRESOLVED/拒绝profile'),
('OPS-IF-032','ADOPTION_PENDING','FIN-BANK RESTORE_OWNERSHIP_AND_APPLY','全冲突组G原claim恢复无FREE间隙，所有maker SOD及AND capabilities；原GL current不变，不生成mirror更正；step receipt重放从apply继续。','Bank全组claim/recovery/apply、权限writer和原GL当前性；不可只恢复一个member。','restore已提交后apply失败重试','读原step receipt，仅补apply，无再次释放','部分组恢复、漏maker或新建反向GL','拒绝，整组原claim持续保护'),
('OPS-IF-033','INTENTIONAL_DIFFERENCE','FIN-ASSET final depreciation current chain','区分current完整链与历史period fulfilled；REUSE_CURRENT/REUSE_CORRECTED/BLOCKED/CREATE_EXCLUSIVE；缺mirror阻断不重造，借用普通run不属处置。','ASSET/GL/final depreciation/ordinary run与disposal ownership全部current gates，逆转仅本处置独占final。','普通run已存在且当前有效','复用并记录借用，处置reverse不反普通run','mirror不完整却新建final或反借用run','BLOCKED，禁止重复折旧/错误逆转'),
('OPS-IF-034','ADOPTION_PENDING','FIN-BUD shared final gate','policy显式NA/required；同budget共享gate持到GL commit，normal/automatic/inverse/policy变更全writer；read预检不是permit。','FIN-BUD/GL全部预算影响及policy writer登记，真实sameUow/同资源；无登记维持fenced。','remaining100，两笔各60并发','只有一笔提交，另一重新评估拒绝','预算预检后释放gate再post','拒绝新GL效果'),
('PLAN-IF-005','ARTIFACT_CONFLICT','PLAN-ATP recovery R11 source patch','终结入口改成同组织evaluation关联ReplayRecord按完整自然键序锁定后Evaluation；缺replay拒绝；保留授权ticket与后续CAS、原stage/helper序。','A02/A04、binder、recovery、intake所有涉及双方的入口共同Replay→Evaluation；禁止绑定后重新关联新ReplayRecord，原包SQL不执行。','A02终结与bind_snapshot同时针对同evaluation','两者Replay→Evaluation，不形成原ABBA','终结仍Evaluation→Replay或缺replay','静态patch匹配；未来并发门禁必须无死锁且拒绝缺绑定'),
('PLAN-IF-010','ARTIFACT_CONFLICT','PLAN-PO CP18 C#/TS replay authorization patch','old-result分支parse后、外事务commit前补原Authorized/authenticated；confirmed后输出前再次检查时效；过期不输出，保留原receipt，不重跑业务。','C#/TS所有PO11–14重放分支与现有current Core/provider门共同采用；原权限目录回放fresh对象/action/field mask保持。','权限有效且原请求identity匹配','返回原canonical bytes，replayed字段不改','capture/commit等待中授权过期','403/404按原目录，旧结果零泄露、原receipt不删除'),
('PLAN-IF-011','ARTIFACT_CONFLICT','PLAN-PO CP18 ApplyOwnerIntermediate SQL patch','新success在COMMIT后返回已校验且写入CommandSlot的@LocalTerminalCanonical及Replayed=0；与replay相同一结果集一行两列，consumer只取原TerminalCanonical。','PrWoRecovery C#/TS Execute结果shape与outer CommitConfirmed保持；SQL内COMMIT不替代外层确认，enqueue子过程不得额外结果集。','新success与重放读取相同CommandOutcome bytes','均一结果集/一行，业务terminal完全相同','遗漏结果集/子过程额外结果集/外commit unknown','SQL_RESULT_SHAPE或503 query原key，不伪造成功'),
]

records=[]
for ident,classification,version,decision,adoption,pos,pex,neg,nex in ROWS:
    issue=next(x for x in ISSUES if ident in x['source_finding_ids'])
    evidence=[]
    for e in issue['evidence']:
        p=Path(e['source_path']);raw=p.read_bytes();assert hashlib.sha256(raw).hexdigest()==e['sha256']
        lines=raw.decode('utf-8-sig').splitlines();a=e.get('line',1);z=e.get('end_line',min(len(lines),a+4))
        evidence.append({'source_path':e['source_path'],'sha256':e['sha256'],'line':a,'end_line':z,'reading':'DIRECT_PRIMARY_CLAUSE_AND_RELATED_CONTRACT_READ; source SHA checked again by local static validation'})
    extras={
      'OPS-IF-003':[('dc0ca1dd23d08f92',350,359),('dc0ca1dd23d08f92',699,715),('5aed7ccb52e1878b',474,477),('5aed7ccb52e1878b',1414,1455),('735749099f867bc9',7328,7332),('c30b6885dde887be',15270,15322),('7e25b78ea1261553',905,929),('9a86201bc6969186',440,453)],
      'OPS-IF-001':[('f768bfcd2f485e00',215,337),('9442439f12ceb101',123,145),('dabe7fa5cc4da4e4',26,50),('dabe7fa5cc4da4e4',250,266)],
      'OPS-IF-024':[('11fae598ec00ef49',89,103),('11fae598ec00ef49',232,246),('11fae598ec00ef49',458,468),('11fae598ec00ef49',515,529),('f8bfbd77ef015500',241,266),('f8bfbd77ef015500',306,346),('f8bfbd77ef015500',790,843),('dabe7fa5cc4da4e4',285,354)],
      'OPS-IF-029':[('c9d7ce48b1b68c52',1244,1252),('f31bcf82ae23555c',598,623)]
    }.get(ident,[])
    originals=Path('D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals')
    for prefix,a,z in extras:
        p=next((originals/prefix[:2]).glob(prefix+'*'));raw=p.read_bytes()
        evidence.append({'source_path':str(p).replace('\\','/'),'sha256':hashlib.sha256(raw).hexdigest(),'line':a,'end_line':z,'reading':'DIRECT_PRIMARY_CONTRACT_READ'})
    if ident=='PLAN-IF-005':
        for a,z in [(903,950),(1336,1355),(2020,2053),(2062,2109),(2163,2195),(2271,2290)]:
            evidence.append({'source_path':str(atp_path).replace('\\','/'),'sha256':hashlib.sha256(atp_path.read_bytes()).hexdigest(),'line':a,'end_line':z,'reading':'DIRECT_CALL_CHAIN_READ; includes implicit Evaluation lock in recovery validators'})
    status='SUPERSESSION_RECORDED' if classification=='SUPERSEDED' else 'BOUNDARY_RECORDED' if classification in ('ADOPTION_PENDING','INTENTIONAL_DIFFERENCE') else 'DESIGN_RESOLVED'
    chapter='03-transactions-and-recovery.md' if issue['batch']=='W2' else '04-finance-correction-contracts.md'
    records.append({'issue_id':issue['issue_id'],'source_finding_ids':[ident],'batch':issue['batch'],'classification':classification,'targets':issue['targets'],'source_version':version,'decision':decision,'preserved_constraints':[adoption],'consumer_adoption':{'required':adoption,'runtime_status':'UNPROVEN','design_acceptance_is_runtime_adoption':False},'evidence':evidence,'outputs':['remediation/'+chapter,'remediation/assets/finance-recovery/owner-adoption.json','remediation/assets/finance-recovery/contracts.schema.json']+(['remediation/assets/finance-recovery/source-patches.json'] if ident.startswith('PLAN') or ident=='OPS-IF-029' else []),'acceptance':[{'id':ident+'-POS','scenario':pos,'expected':pex,'verification_level':'REQUIRED_RUNTIME','result':'NOT_RUN'},{'id':ident+'-NEG','scenario':neg,'expected':nex,'verification_level':'REQUIRED_RUNTIME','result':'NOT_RUN'}],'design_status':status,'actual_adoption_status':'UNPROVEN','implementation_status':'NOT_IMPLEMENTED','verification_status':'STATIC_DATA_CHECK_ONLY; RUNTIME_NOT_RUN'})
assert {r['source_finding_ids'][0] for r in records}=={x['source_finding_ids'][0] for x in ISSUES}
dump('owner-adoption.json',{'contract':'CP6-W2-W3-ADOPTION-REGISTER/1','enable_rule':'Every named consumer and every conflicting writer has exact physical-resource/native-Uow/current-authority evidence; missing coverage stays REQUIRED_FENCED. This task proves no runtime adoption.','findings':records})
(ROOT/'workparts'/'finance-recovery.json').write_text(json.dumps({'findings':records},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

registry={'contract':'CP6-COMPOSED-RESOURCE-REGISTRY/1','status':'DESIGN_ONLY_RUNTIME_UNPROVEN','profiles':[
 {'profile':'RMA_STOCK_COMPOSITE_1','rank':['CATALOGUE_F_MINUS_1','MOVEMENT_F0','CANONICAL','SCOPE_PARENT_BEFORE_CHILD','DETAIL_OWNER_KEY_ORDINAL','LOCAL_TERMINAL'],'supersedes':'RMA §9.1 acquisition sequence only when composed with Stock, including Scope-first and fine-resource positions; preserve all original resource identities, predicates and held-gate requirements','writer_families':['POSTING','CLAIM_CREATE','CLAIM_BACKING','CLAIM_ALLOCATE','CLAIM_PICK','CLAIM_RELEASE','CLAIM_REBIND','HOLD_PLACE','HOLD_RELEASE','CORRECTION_APPLY','RECONCILIATION_APPLY','SOURCE_APPLICATION','RMA_AUTH_SOURCE_ENTITLEMENT','RMA_RECEIPT_SEAL_CORRECTION_H30','IN_ALL_SOURCE_ADAPTERS','OUT_RSV_DEL','QA_PLM_CONTROLS','MASTER_CAPACITY_METRIC_MEMBERSHIP','MAT_EXEC_OUTPUT','LEGACY_IMPORT_MANUAL_WORKER_CUTOVER'], 'missing_path':'LOCK_PLAN_UNPROVEN'},
 {'profile':'BP_EST_QTN_1','rank':['IAM','BP_SCOPE_AND_MAPPING','EST_SOURCE_AND_SCOPE','QTN','LOCAL_TERMINAL'],'rule':'Import the exact original per-owner dependency DAG; the coarse rank does not remove original command/reverse-claim/root constraints; reject unresolved union cycles.'},
 {'profile':'FINANCE_CORRECTION_1','rank':['AUTHORITY','MATCH_CORRECTION_AND_ORIGINAL_FORWARD_SOURCE_CONTROLS','PERIOD_BANK_BUDGET','FM_SCOPE','BP_SCOPE','REGISTERED_POST','BUSINESS','LOCAL_TERMINAL'],'rule':'Expand exact FIN-AP RequiredGate orderGroup/orderKey and GL accepted local dependency graph, validate full union; do not claim this profile uses Stock fine-key ordering.'}],
 'resource_identity':'Physical databaseBinding + database principal + exact lock resource bytes or row/range predicate identity; owner alias strings never create distinct locks for the same resource.',
 'lock_rules':['Precollect complete canonical/source/route/metric/claim/authority membership including absent-key guards','Coalesce physical aliases and use strongest S/X before first acquisition','Include implicit UPDATE/FK/trigger/unique-index/predicate locks and allocator locks in the registry DAG','Acquire deterministic registered topological order; mandatory rank edges are not removable','SCOPE parent before all children across every warehouse/site; DETAIL use accepted global ordinal owner/key ordering','No upgrades, no acquire earlier rank after later; reentry allowed only same native outer transaction, same physical resource and sufficient held mode','Membership changed under gate: rollback entire operation, recollect outside locks, retry same business identity','Cross database/connection/provider/unknown writer coverage: REQUIRED_FENCED, no distributed atomicity inference','New writer route may register only under exclusive catalogue/control gate; tombstoned correction root cannot gain executable aliases','No automatic fairness or timeout expiry inference: lock failure rolls back; outcome unknown queries original operation']}
dump('resource-registry.json',registry)

print(f'Generated contracts, {len(records)} dispositions, resource registry and data-only source patches.')
