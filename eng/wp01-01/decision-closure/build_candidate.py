"""Reproduce task-private design data. Never edits inherited inputs or production.

Use only on this fixed candidate baseline. Manifest/validation is finalized by
validate_candidate.py after tests; no authority or approval is generated here.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT/'docs/development/evidence/WP01-01/decision-closure'
OLD = ROOT/'docs/development/evidence/WP01-01/preparation'
BASE = '99fc0ce383d2d02c1404a8adbb3abe971b41d332'
VERSION = 'CP6-WP05-01-ACCEPTANCE-BASIS/1.2.0-proposed'
REPOS = {'Main': (ROOT, BASE)}
PIN_CACHE = {}


def read(path):
    return json.loads((ROOT/path).read_bytes())


def write(name, value):
    (OUT/name).write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\n')


def pin(path, start=None, end=None, repo='Main'):
    root, sha = REPOS[repo]
    if (repo,path) not in PIN_CACHE:
        git = subprocess.run(['git','-c','core.longpaths=true','show',f'{sha}:{path}'], cwd=root, capture_output=True, check=True).stdout
        data = (root/path).read_bytes() if repo=='Main' else git
        PIN_CACHE[repo,path] = data,git
    data,git = PIN_CACHE[repo,path]
    item = {'repository':repo, 'revision':sha, 'path':path, 'sha256':hashlib.sha256(data).hexdigest(), 'bytes':len(data),
            'representation':'RAW_FILE_BYTES' if repo=='Main' else 'GIT_BLOB_BYTES', 'git_blob_sha256':hashlib.sha256(git).hexdigest()}
    if start:
        lines = data.decode('utf-8-sig').splitlines()
        item.update(line_start=start, line_end=end or start, excerpt='\n'.join(lines[start-1:end or start]))
    return item


def module(name, start, end=None):
    return pin('docs/design-handbook/modules/'+name+'.md',start,end)


def clause(ident, category, rule, source, tasks, missing=None):
    return {'id':ident,'category':category,'rule':rule,'source_pins':source,'successor_tasks':tasks,
            'missing':missing,'adoption':'INHERITED_SCOPE_ONLY' if category=='INHERITED_ACCEPTED_RULE' else 'NOT_ADOPTED',
            'runtime':'NOT_RUN'}


def decisions():
    admission=read('docs/development/evidence/wave7-dispatch-20261014/admission-plm.json')
    shared=pin('docs/design-handbook/remediation/07-shared-consumer-mappings.md',7,35)
    tenancy=pin('docs/design-handbook/architecture/02-postgresql-tenancy.md',1,155)
    inherited='INHERITED_ACCEPTED_RULE';candidate='NEW_CANDIDATE_CHOICE';config='ACTUAL_CONFIGURATION_PENDING';provider='PROVIDER_IMPLEMENTATION_MISSING';runtime='RUNTIME_UNVERIFIED'
    groups=[
      [clause('01.technical',inherited,'ENG technicalManifestDigest retains original schema/algorithm/value and raw manifest bytes; ITER treats it opaque. ReviewPackageDigest is the frozen package digest; neither equals aggregate or snapshot.',[module('PLM-ITER-01',46,48),shared],['WP05-12','WP05-19']),
       clause('01.aggregate',inherited,'ITER-CANDIDATE-AGGREGATE/1\\n + canonical UTF-8 JSON => SHA-256. Original member identities and versions survive; byte ordering/duplicate identity rejection/no Unicode normalization; complete bidirectional membership equality.',[shared],['WP05-19']),
       clause('01.snapshot',inherited,'Snapshot digest SHA-256 over canonical snapshot bytes only; no aggregate domain prefix. OA engineering.digest/reviewPackageDigest use original package digest; technicalManifestDigest independent nullable; OA candidateId/version use aggregate; roundBindingId is mapping identity.',[shared],['WP05-19']),
       clause('01.eng_candidate',candidate,'CD-ENG-01/1.0.0-proposed: new envelope preserves each original owner schema/ref/revision/digest as base64 bytes, canonical sorted JSON, no normalization; SHA-256("CP6-ENG-MANIFEST-CANDIDATE/1\\n"+canonical). Golden vectors supplied. This is not an accepted ENG algorithm and cannot be written into existing technicalManifestDigest routes.',[shared,module('PLM-ITER-01',83,87)],['WP05-03','WP05-12','WP05-19'],'Exact owner adoption and compatibility vectors across accepted ENG legal domain')],
      [clause('02.binding',inherited,'OA registration resolves unique Owner/BizType/SubjectKind; EXACT_PINNED or one-time RESOLVE_PUBLISHED_ON_SUBMIT. Freeze actual policy/round binding; no browser callback/tenant choice. rejectDisposition RETURN|REJECT is local only.',[module('OA-APP-01',57,75),shared],['WP12-06','WP12-07','WP05-19']),
       clause('02.actor_sod',inherited,'Use trusted HUMAN_TASK/SYSTEM_EXECUTION/SUBMITTER_WITHDRAWAL evidence. Preserve exact principal, actual actor, delegation grant, permission and SOD; current authority rechecked at apply; no inferred self-approval.',[module('OA-APP-01',57),module('SYS-IAM-01',49,59)],['WP12-19','WP05-20']),
       clause('02.real_policy',config,'Required configuration tuple: tenant/project, member directory version, actual member IDs, role assignment versions, delegation grant/ref/validity, SOD rule and separately authorized exceptions, published OA policy/version/digest, selectionMode, rejectDisposition. Missing binding rejects; missing qualified roles follow OA Suspended.',[module('OA-APP-01',63,75)],['WP12-06','WP12-07','WP12-19','WP05-19'],'Actual tenant/project member roster, approved role/delegation/SOD policy and selected published OA policy are absent; no synthetic defaults')],
      [clause('03.tenancy',inherited,'cp6-pg-tenancy-v1 is inherited bounded design. Dedicated LOGIN+pool; trusted TenantStorageBindingV1; session_user protected mapping/RLS; same connection/transaction across schemas; TenantId and original global uniqueness retained.',[tenancy],['WP02-02','WP02-03']),
       clause('03.mapping',candidate,'ITER-REL-PG-PHYSICAL/1.0.0-proposed supplies individual fields, exact raw identity/number/time storage, immutable revisions, UUID CAS token distinct from business versions, ownership/FK/index/write sets/lock order. No SQL Server rowversion, no DDL or EF edits.',[tenancy,module('PLM-ITER-01',31,48),module('PLM-REL-01',29,61)],['WP02-02','WP02-03','WP05-18','WP05-32']),
       clause('03.adoption',runtime,'Actual schema role/RLS, all writers, exact numeric driver, migration lineage, recovery and concurrency remain unverified. Existing PG compatibility code does not prove these mappings.',[tenancy],['WP02-02','WP02-03','WP02-10','WP02-13'],'Owner PostgreSQL integration/role/RLS/transaction/CAS/recovery evidence')],
      [clause('04.safety',inherited,'Closing/rejecting does not delete engineering evidence; corrections append. Privacy classification separates necessary nonPII, PII, secrets and copies. Destructive action requires approved scopeDigest/policyRef+version/target version and current legalHold decision under Owner fence; aggregate PARTIAL/UNKNOWN accurately.',[module('SYS-PRIV-01',29,48)],['WP02-13','WP05-21','WP05-36']),
       clause('04.values',config,'No engineering retention duration or anonymization cutoff is selected. CRM OriginalReceivedAt.AddMonths(24) applies only to its accepted CRM scope. Missing effective policy/hold judgment blocks cleanup, not authorized append/read. An authorized destruction replaces unavailable original with permissible tombstone; do not claim original hash re-verifiable afterward.',[module('SYS-PRIV-01',33,48)],['WP02-13'],'Engineering retention periods, jurisdiction/scope classification, approved destruction conditions and actual legalHold facts')],
      [clause('05.transport',inherited,'OA snapshot <=256KiB, body <=1MiB, IDs <=1000 remain exact wire boundaries. Complete review membership never truncates; unsupported oversized OA submission rejects without deleting legal drafts.',[module('OA-APP-01',46),shared],['WP05-19']),
       clause('05.capacity',candidate,'ITER-CAPACITY/1.0.0-proposed chooses UI page default 50, API page maximum 200, review streaming chunk 500, display reference chunk 50, deliverable display chunk 50. These are pagination/chunk budgets, NOT legal collection or input maxima. Cursor fixes snapshot revision and bytewise (identity,version) sort; expired snapshot fails restart-required, never mixes revisions.',[module('PLM-ITER-01',31,48),shared],['WP05-18','WP05-19']),
       clause('05.load',runtime,'Production SLO, memory/latency and largest real project/OA envelope are unmeasured. Verify exact max wire boundary ±1, complete retrieval, concurrent edits, timeout/recovery and real capacity; synthetic vectors prove none of this.',[shared],['WP05-18','WP05-19'],'Real workload distribution, accepted SLO and measured capacity report')],
      [clause('06.rules',inherited,'Safe closure binds original Plan/version, execution intent/key/content, root+complete child set, exact units/scope/profile, watermarks and no-late-execution fence. UNKNOWN, timeout, NOT_FOUND or root Cancelled alone cannot release/close. Recover original key, never create new Plan/Baseline/request.',[module('PLM-ITER-01',83,85),module('PLM-REL-01',37,59)],['WP05-21','WP05-24','WP08-15','WP08-18']),
       clause('06.provider',provider,'VER/MES complete child coverage and safe terminal facts are provider implementation/evidence gaps, not a user choice. Only dependent cancel/closure/release paths remain blocked.',[module('PLM-REL-01',37,59)],['WP05-21','WP05-24','WP08-15','WP08-18'],'Real VER/MES safe closure services and complete linked root/child/late-execution evidence'),
       clause('06.runtime',runtime,'Require joint Owner fault/race/recovery tests before enabling any safe-release path.',[module('PLM-REL-01',37,59)],['WP05-61'],'Real end-to-end safe terminal/release evidence')],
      [clause('07.workflow',inherited,'Stable FieldId/dictionary Value remain independent of translated Label. Tenant/key scope; edit returns draft; exact review set+entry version; authorized publish freezes complete immutable bundles then CAS manifest; rollback changes pointer to verified existing version.',[module('SYS-LANG-01',29,62)],['WP02-17','WP05-18','WP05-32']),
       clause('07.content',config,'Chinese source is the fixed review basis. Japanese/Korean content and review records are not fabricated; machine translation cannot publish itself.',[module('SYS-LANG-01',29,62)],['WP02-17'],'Exact required translations, qualified review actors and actual client bundle adoption')],
      [clause('08.audit',candidate,'Fixed current source audit separates REUSE/CHANGE/NEW. Current Main lacks CP6.Core/Services/Plm and PlmEngineeringService. Historical b7ec269 is reference only. Existing sync FirstOrDefault callbacks are not persistent OA application protocol.',[pin('CP6.Core/Services/Wf/ApprovalDispatcher.cs',13,41),pin('CP6.Core/Services/Wf/FormSubmissionService.cs',29,60)],['WP05-18','WP05-19','WP05-20','WP05-32','WP05-61']),
       clause('08.delivery',provider,'Product services/models/host/workers/UI/DB and exact Platform package adoption must be delivered by successor tasks. Static audit closes only inspected source uncertainty.',[pin('CP6.Core/CP6.Core.csproj')],['WP05-18','WP05-19','WP05-20','WP05-21','WP05-32','WP05-33','WP05-34','WP05-35','WP05-36','WP05-42','WP05-61'],'Implementation/consumer/runtime evidence, no assigned service or database resource in lane A')]
    ]
    result={'schema':'WP01-01-DECISIONS/1','identity':'WP01-01-DECISION-CLOSURE/20261014-R1','basis_version':VERSION,
            'production_enabled':False,'actual_adoption':False,'actual_completion':False,'actor':None,'signed_at':None,
            'decisions':[{'id':f'DP-ITER-{i+1:02d}','admission_category':admission['decision_points'][i]['category'],'clauses':g} for i,g in enumerate(groups)]}
    result['residuals']=[{'clause':c['id'],'category':c['category'],'missing':c['missing'],'successor_tasks':c['successor_tasks']} for g in groups for c in g if c['missing']]
    write('decisions.json',result)
    vectors=[]
    for value in [{'schema':'CD-ENG-01/1.0.0-proposed','members':[]}, {'schema':'CD-ENG-01/1.0.0-proposed','members':[{'ownerSchemaBase64':'5bel56iL','originalRefBase64':'QQBC','revisionBase64':'MDAx','digestBase64':'YWJj'}]}]:
        raw=json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(',',':')).encode()
        pre=b'CP6-ENG-MANIFEST-CANDIDATE/1\n'+raw
        vectors.append({'synthetic':True,'canonical_utf8_hex':raw.hex(),'preimage_hex':pre.hex(),'sha256':hashlib.sha256(pre).hexdigest()})
    write('digest-vectors.candidate.json',{'status':'SYNTHETIC_NOT_ACCEPTED_ENG_ALGORITHM','algorithm':'CD-ENG-01/1.0.0-proposed','vectors':vectors})


def mapping():
    specs={
      'PLM-ITER-01':[
       ('Project',35,'Id TenantId ProjectNo Title Objective ProductId OwnerId SiteIds Start End PlanVersion VersionToken'),
       ('Iteration',36,'Id TenantId ProjectId IterationNo Title Goal Origin ParentBaseline NoParentReason ChangeId PredecessorId OwnerId InputVersion PlanVersion Stage Hold EvaluationId VersionToken'),
       ('IterationInput',37,'InputVersion SourceOwner RequestId RequestRevision SourceDigest RequirementRefs AcceptanceRefs ResolutionState CapturedAt CapturedBy'),
       ('Candidate',38,'Id CandidateVersion ParentCandidate ParentBaseline Title ChangeSummary Assumptions ContentDigest DriftAssessmentRef VersionToken'),
       ('CandidateRef',38,'OwnerType ObjectId Revision Occurrence Digest Applicability'),
       ('EngineeringTask',39,'Id Title OwnerId DueDate Weight PlanVersion State DeliverableRefs ConfirmationId CandidateRef'),
       ('ReviewRound',42,'Id IterationId RoundNo InputVersion CandidateManifest PackageDigest ReviewPolicyVersion SnapshotDigest State OaInstanceId SubmittedAt ClosedAt VersionToken'),
       ('ReviewIssue',40,'Id RoundId ObjectPosition Text Severity DowngradeReason OwnerId DueDate State ResolutionRef VerifiedBy VerifiedAt'),
       ('Evaluation',41,'Id Outcome SelectedCandidateVersion ReviewRound VerificationRefs Reason HandoverRef DecisionBy DecisionAt SnapshotDigest ActiveSelection'),
       ('ExternalRequestReceipt',42,'OperationType RequestKey PayloadDigest Owner RequestRef State ResultId ResultVersion ResultDigest AttemptState LastCheckedAt'),
       ('AuditClosureAmendment',43,'EventId TenantId IterationId EventType Actor OccurredAt SourceVersion TargetVersion Before After Reason RequestRef FactRef IntegrityDigest')],
      'PLM-REL-01':[
       ('UseRequest',33,'RequestId RequestKey SourceBusinessVersion ImmutablePayload Baseline Manifest FrozenConfiguration TechnicalDigest ReviewPackageDigest ActivityKind ProductDestination Purpose PolicyRevision Site Customer Configuration ObjectScope Validity LimitMaximum UOM Profile VerificationRefs ConfigurationRefs DeviationRefs ConditionRefs'),
       ('UseAuthorization',34,'UseAuthorizationId Version RequestRef ApprovalRef Baseline Configuration Purpose PolicyRevision Scope Validity Limits Basis QuotaLineage ApprovedTotal Conditions HistoricalDecision CurrentEffect PolicyRef Signature SuccessorRef'),
       ('ExecutionIntent',35,'ExecutionRequestKey SourceObject ImmutablePayload Baseline Scope Site Purpose PlanVersion RequestedMaximum UOM ReservationRefs ClaimRefs'),
       ('Reservation',36,'ReservationId Version AuthorizationLineage ConsumptionBasis BasisVersion Q C R P ExecutionIntentRef UOM NonoverlappingShare'),
       ('MeasurementFact',37,'FactId Version Scope Mode Quantity UOM ManufacturingUnit MaterialSegment OperationOccurrence Profile FirstMeasurementOperation RuntimeBoundary OccurredAt RootChildren Reset Correction Uncertainty'),
       ('ExceptionCorrection',38,'Id X CoveredConsumption RealTotal Conformance Conflict OriginalEvent OriginalSettlement BeforeVersion AfterVersion SuccessorRef'),
       ('NoFurtherExecutionProof',39,'ExecutionRequestKey ContentIdentity RootWorkOrderOrExplicitAbsent Units Shares ActualCumulativeConsumption Basis CompleteChildCoverage UnoccurredTerminatedScope OriginalKeyFence OwnerVersion EvidenceRef CorrectionSuccessor'),
       ('ReleaseRecord',40,'ReleaseId ReleaseRevision SourceRelease ChangeReason Baseline TechnicalDigest PackageAvailability Purpose ProfessionalBasis TargetScopePermissionBindings'),
       ('ReleaseTarget',41,'TargetId SourceOwner Organization Site ResponsibleRole TargetScopeVersion ObjectScope Conditions Required AdoptionDefinition ClosureConditions RemovedDisposition Consequences'),
       ('TargetReceipt',42,'ReceiptId ReleaseRevision TargetId ScopeVersion SourceOwner Conclusion Reason OwnerVersion Delivery Acceptance Readiness ActualAdoption Conformance'),
       ('DeliveryAttempt',42,'AttemptId ReleaseRevision TargetId ScopeVersion SourceOwner State OccurredAt'),
       ('ActualAdoption',43,'AppliedBusinessReference ActualObject ActualLine ActualEvent ActualVersion ActualBaseline ActualConfiguration OccurredAt Covered Remaining Conflict Completeness'),
       ('ControlRecovery',44,'CaseId OwnerDispositionObject OriginalKey Content ScopeVersion TrustedWatermark Conflict BlockedObligations Consequences SafetyProof Resolution Remaining SuccessorRef')]
    }
    types={
     'opaque':{'postgresql':'bytea NOT NULL; nullable only where source permits','authority':'original encoded bytes + original schema/encoding identity','projection':'text COLLATE "C" only after valid UTF-8/no NUL; optional index digest never replaces identity','lossless':'No trim/casefold/normalization/truncation/UUID coercion. Registered raw equality under bucket lock prevents hash collision aliasing.'},
     'exact_number':{'postgresql':'original bytea + sign smallint + coefficient bytea (ASCII digits) + exponent bytea (signed decimal integer)','authority':'Original wire numeric string and exact coefficient/exponent; no .NET decimal narrowing','projection':'nullable numeric without typmod only after proved PostgreSQL range/scale fit; projection never authority','lossless':'No numeric(p,s), rounding, float or fixed18,2. PostgreSQL numeric has finite limits; preserve all accepted original values including outside projection range.'},
     'instant':{'postgresql':'original bytea + optional timestamptz projection','authority':'Exact original instant/offset/tick representation','projection':'UTC timestamptz only when exact microsecond representation; residual submicrosecond ticks retained','lossless':'Do not round original 100ns or greater precision; original bytes remain digest input.'},
     'cas':{'postgresql':'uuid NOT NULL','authority':'Owner-generated random concurrency token; UUID equality in UPDATE predicate','projection':None,'lossless':'Internal token only. Never business revision, SQL Server rowversion or xmin. Original wire opaque token needs registered versioned adapter.'},
     'tenant':{'postgresql':'uuid NOT NULL','authority':'Trusted Main stable TenantId; CRM OrganizationId explicit mapping','projection':None,'lossless':'No default A1; supplied body schema/tenant cannot select database principal.'},
     'local_id':{'postgresql':'uuid NOT NULL','authority':'New internal surrogate only','projection':None,'lossless':'External Id remains separate opaque bytes; not a conversion.'}}
    entities=[]
    numeric={'Weight','LimitMaximum','ApprovedTotal','RequestedMaximum','Q','C','R','P','Quantity','X','CoveredConsumption','RealTotal','ActualCumulativeConsumption'}
    markers={'Candidate':'Candidate / Ref','CandidateRef':'Candidate / Ref','UseRequest':'用途申请','MeasurementFact':'计量事实','ExceptionCorrection':'异常X与更正','TargetReceipt':'TargetReceipt / DeliveryAttempt','DeliveryAttempt':'TargetReceipt / DeliveryAttempt','ControlRecovery':'控制/恢复'}
    for target, entries in specs.items():
      for name,line,fields in entries:
        modulelines=(ROOT/('docs/design-handbook/modules/'+target+'.md')).read_text('utf-8-sig').splitlines()
        line=next(i for i,l in enumerate(modulelines,1) if l.startswith('|'+markers.get(name,name)+'|'))
        source=module(target,line)
        rows=[]
        for field in fields.split():
          kind='exact_number' if field in numeric else 'cas' if field=='VersionToken' else 'tenant' if field=='TenantId' else 'instant' if field in {'Start','End','DueDate','CapturedAt','VerifiedAt','DecisionAt','LastCheckedAt','OccurredAt'} else 'opaque'
          rows.append({'field':field,'storage_profile':kind,'source_pin':source,'nullability':'SOURCE_CONDITIONAL; source text is authoritative, no blanket NOT NULL inference','identity_scope':'original Owner/domain; do not widen or narrow','candidate_column':re.sub(r'(?<!^)(?=[A-Z])','_',field).lower()})
        rows.insert(0,{'field':'storage_id','storage_profile':'local_id','source_pin':pin('docs/design-handbook/architecture/02-postgresql-tenancy.md',49,53),'nullability':'NOT NULL','identity_scope':'new internal lookup, not original external identity','candidate_column':'storage_id'})
        entities.append({'entity':name,'target':target,'owner':'ITER' if target=='PLM-ITER-01' else 'REL','fields':rows,
         'physical_location':'tenant schema only if all original PK/UQ domains proved tenant-local; otherwise shared Owner domain + tenant RLS, no automatic split',
         'foreign_keys':'(TenantId, local parent storage_id[, local immutable revision_id]) to same Owner; shared IAM references qualified and tenant-checked; external refs preserve Owner/schema/version bytes and cannot claim distributed FK',
         'delete_rule':'RESTRICT; immutable history cannot cascade; explicit privacy disposition uses separate approved protocol',
         'indexes':['local storage_id PK; preserve original global domain when required','(TenantId, parent_storage_id, internal_revision_id)','source-specific unique identity via full-byte registry compare; digest bucket is not unique identity'],
         'migration_state':'NOT_EXECUTED','runtime':'NOT_RUN'})
    shared=pin('docs/design-handbook/remediation/07-shared-consumer-mappings.md',7,35)
    r1groups={'ReviewCandidateAggregate':'aggregateId aggregateVersion aggregateDigest members Scope Iteration Round',
      'RoundBinding':'roundBindingId oaRoundKey OriginalRoundIdentity Round aggregateId aggregateVersion aggregateDigest PackageRef PackageDigest PolicyRef PolicyVersion PolicyDigest technicalManifestDigest mappingVersion rejectDisposition',
      'ReviewSnapshot':'schema aggregate binding canonicalBytes snapshotDigest canonicalizationVersion',
      'OwnerApplication':'InboxId Producer EventId InstanceId SubjectRevision DecisionId ApplicationUnitId action ApplicationReceiptId revision previousReceiptId businessResultRef currentSubjectRevision OwnerApplicationOutboxId AuditRef'}
    for name, fields in r1groups.items():
      entities.append({'entity':name,'target':'PLM-ITER-01','owner':'ITER','fields':[{'field':f,'storage_profile':'opaque','source_pin':shared,'nullability':'as exact R1/OA mapping; technicalManifestDigest nullable only per contract'} for f in fields.split()],
       'physical_location':'Owner local business schema unless original UQ requires shared domain','foreign_keys':'TenantId included for parent binding, aggregate, round and immutable package relations; full Scope retained',
       'indexes':['R1 Scope+Producer+EventId','Instance+SubjectRevision+Decision','registered ApplicationUnitId APPLIED uniqueness; action renaming cannot bypass'],
       'delete_rule':'RESTRICT','runtime':'NOT_RUN'})
    write('postgresql-mapping.json',{'schema':'ITER-REL-PG-PHYSICAL/1.0.0-proposed','status':'CANDIDATE_NOT_DDL','tenancy_profile':'cp6-pg-tenancy-v1',
      'source':pin('docs/design-handbook/architecture/02-postgresql-tenancy.md',1,155),'storage_profiles':types,'entities':entities,
      'identity_registry':{'status':'CANDIDATE_REQUIRES_REVIEW_BEFORE_GLOBAL_ID_REGISTRATION_ADAPTATION','algorithm':'Classify original uniqueness domain first. Keep global objects shared. For unbounded opaque IDs index digest(domain+raw bytes) as nonunique bucket; transaction lock bucket then compare complete domain/schema/raw bytes before insert/reuse internal UUID. Every writer/import/recovery must use same registry; collision means same bucket, never same identity. No hash-only PK/UQ, no unbounded bytea btree assumption.','cross_tenant_conflict':'Safe identity conflict only; no disclosure of other tenant data/results, no new ID retry'},
      'tenant_roles':{'mapping':'TenantStorageBindingV1 trusted control-plane mapping; quoted allowlisted schema; dedicated tenant LOGIN and credential-specific pool; same physical connection+transaction; fail closed missing mapping/model/version','rls':'Protected role-to-tenant map keyed by session_user; tenant role nonowner/no BYPASSRLS/no superuser; FORCE RLS where applicable; caller app.tenant_id cannot authorize','shared_uniqueness':'Outbox MessageId and Inbox(ConsumerName,MessageId) remain globally unique within original queueDomain; no TenantId narrowing; global business PK/UQ also remain shared','recovery':'Tenant business schema plus same-tenant shared message/control slices restored consistently, pause route/workers, reconcile original keys/checkpoints; never restore schema alone and manufacture completion'},
      'cas':{'sql_example':'UPDATE trusted_schema.iteration SET stage=$1, concurrency_token=$2 WHERE tenant_id=$3 AND storage_id=$4 AND concurrency_token=$5 RETURNING concurrency_token','binding':'Typed Npgsql parameters; schema from trusted registry quoted identifier, never request input','zero_rows':'Read current authorization then return stale/conflict; no successful receipt or retry with new operation key'},
      'transactions':[
       {'operation':'ITER submit','writes':['frozen Package','Aggregate','Binding','Round','InReview','Owner OA Outbox','original CommandReceipt','Audit'],'locks':['trusted tenant/permission fence','current control head','Round/Binding','command identity','local append records'],'source':shared},
       {'operation':'ITER apply/withdraw','writes':['Inbox/ApprovalFact','business transition or stale outcome','ApplicationReceipt','OwnerApplicationOutbox','original command result','Audit'],'locks':['current control head','Round/Binding','Inbox/Fact','ApplicationReceipt','Outbox','Audit'],'source':shared},
       {'operation':'REL reserve/settle','writes':['authorization lineage aggregate','nonoverlapping reservation shares','original intent/result','measurement/proof/correction','audit','Owner outbox'],'locks':['current permission/control fence','sorted quota lineage IDs','sorted reservation IDs','original operation identity','append facts/receipt/outbox'],'source':module('PLM-REL-01',59,88)},
       {'operation':'REL publish/adoption','writes':['immutable Release revision','exact Target/Scope obligations','delivery attempt or authentic Owner receipt','independent axes projection','command receipt','audit/outbox'],'locks':['current Owner controls','Release head','Target IDs in stable byte order','original operation identity','append receipt/outbox'],'source':module('PLM-REL-01',39,44)}],
      'platform_lock_adaptation':{'status':'CHANGE_REQUIRED_NOT_COMPOSABLE_BY_ASSERTION','InboxV1':'Caller-owned new Serializable transaction; transaction advisory profile disallows pre-held registration/row/advisory profile. Audit all receive/reconcile/publish/revoke writers before choosing adaptation.','OutboxV2':'Existing Owner advisory + actual AuthorityRows FOR UPDATE precede RouteControl/root. Library never commits/retries. R1 business lock order needs explicit compatible integration; no nested second connection/transaction.','unknown_commit':'Separate ReadCommitted READ ONLY query under current read permission of original operation; no new request keys. Deadlock/serialization retry repeats original immutable intention only after outcome resolution.'},
      'runtime_verification_required':['real dedicated roles/RLS/raw SQL isolation','global uniqueness cross-tenant collisions','exact raw number/instant/NUL roundtrip via actual provider','concurrent same key different body','rollback at every write-set point','lost commit response and original-key recovery','late provider events after closure','tenant plus shared-slice restore'],
      'numeric_reference':'https://www.postgresql.org/docs/18/datatype-numeric.html','precision_scope':'No storage profile claims unbounded PostgreSQL numeric or universal legal input proof; authoritative byte representation is preserved; projection fit must be checked per accepted profile.',
      'production_enabled':False,'actual_adoption':False,'actual_completion':False})


def graph():
    definitions=read('docs/development/task-definitions.json')
    roadmap=read('docs/development/roadmap.json'); states={t['id']:t['state'] for t in roadmap['implementation_tasks']}
    taskmap={t['id']:t for t in roadmap['implementation_tasks']}
    recorded={t['id']:t for t in roadmap['implementation_tasks']}
    taskmap.update({t['id']:t for t in definitions['tasks']})
    roots={x['target_id']:x['implementation_task_ids'] for x in roadmap['target_task_coverage'] if x['target_id'] in {'PLM-ITER-01','PLM-REL-01'}}
    reach={}
    for target,ids in roots.items():
      seen=set();stack=list(ids)
      while stack:
        current=stack.pop()
        if current in seen:continue
        seen.add(current);stack.extend(taskmap[current]['dependencies'])
      reach[target]=seen
    allnodes=set.union(*reach.values()); edges=[]
    definitionpin=pin('docs/development/task-definitions.json')
    roadmappin=pin('docs/development/roadmap.json')
    available={
      'WP02-07':{'profile':'CallerTransactionInboxV1','version':'V1 fixed source at b28db763','source':pin('docs/WP02-07-CALLER-TRANSACTION-INBOX.md',repo='Platform')},
      'WP02-08':{'profile':'OutboxV2','version':'V2 fixed source at b28db763','source':pin('docs/WP02-08-OUTBOX-V2.md',repo='Platform')},
      'WP05-01':{'profile':'CP6-WP05-01-ACCEPTANCE-BASIS','version':'1.1.0-proposed','source':pin('docs/development/task-deliveries/WP05-01/supplements/review-r1/manifest.json')},
      'S02-00':{'profile':'cp6-pg-tenancy-v1','version':'1','source':pin('docs/design-handbook/architecture/02-postgresql-tenancy.md')},
    }
    for consumer in sorted(allnodes):
      task=taskmap[consumer]
      for provider in task['dependencies']:
        bindings=[b for b in task.get('prerequisite_bindings',[]) if provider in b.get('task_ids',[])]
        condition='; '.join(b.get('condition','') for b in bindings) or 'Declared task dependency; no exclusion authorized'
        historical=recorded[provider].get('delivery_evidence')
        history_file=pin(historical['path']) if isinstance(historical,dict) and historical.get('path') and (ROOT/historical['path']).is_file() else None
        edges.append({'id':consumer+'->'+provider,'consumer_task':consumer,'provider_task':provider,
          'consumer_repository':task['repository'],'provider_repository':taskmap[provider]['repository'],
          'targets':[t for t,s in reach.items() if consumer in s], 'bindings':bindings,'condition':condition,'exclusion_allowed':False,
          'declaration':dict(definitionpin,json_pointer='/tasks/'+str(next(i for i,x in enumerate(definitions['tasks']) if x['id']==consumer))+'/dependencies') if any(x['id']==consumer for x in definitions['tasks']) else dict(roadmappin,json_pointer='/implementation_tasks/'+str(next(i for i,x in enumerate(roadmap['implementation_tasks']) if x['id']==consumer))+'/dependencies'),
          'contract':{'profile':None,'version':None,'sha256':None,'state':'EXACT_DELIVERY_SELECTION_PENDING','requirement_sources':taskmap[provider].get('source_refs',[])},
          'available_provider_design':available.get(provider),
          'historical_provider_delivery':{'record':historical,'file':history_file,'scope':'Original recorded delivery only; not a new remote ancestry check or this consumer adoption'} if historical else None,
          'owner_transaction':'Consumer repository retains business Owner local transaction; provider package never becomes business Owner; exact lock/write profile selected before implementation',
          'evidence':{'provider_implementation':None,'provider_remote_main':None,'package_version_hash':None,'consumer_fixed_dependency':None,'consumer_application':None,'consumer_remote_main':None},
          'provider_task_state':states.get(provider,'UNKNOWN'),'consumer_task_state':states.get(consumer,'UNKNOWN'),
          'closure_state':'NOT_PROVEN_BY_TASK_STATE','scope_rule':'Declared planning prerequisite retained. This transitive graph is not a blanket runtime requirement for every scenario; exact authorized scenario binding must precede route activation.'})
    result={'schema':'WP01-01-PROVIDER-CONSUMER/1','baseline':BASE,'roots':roots,'source':definitionpin,
      'nodes':[{'id':n,'repository':taskmap[n]['repository'],'state':states.get(n,'UNKNOWN')} for n in sorted(allnodes)],'edges':edges,
      'edge_count':len(edges),'cross_repository_edge_count':sum(e['consumer_repository']!=e['provider_repository'] for e in edges),
      'conditional_exclusion_contract':{'required':['exact edge','target/scenario/obligation IDs','condition expression and observed input','authorized scope/source file hash','contract/profile/version/hash','current Owner authority','reviewed successor DAG change where needed'],
       'state':'NO_EXCLUSIONS_AUTHORIZED','no_private_rewrite':'TASK_DELIVERY bindings remain required; scenario assertion alone cannot delete declared dependency. Baseline presence alone creates no dependency.'},
      'known_profile_candidates':[{'tasks':['WP02-07','WP02-10'],'profile':'CallerTransactionInboxV1','source':pin('docs/WP02-07-CALLER-TRANSACTION-INBOX.md',repo='Platform'),'adoption':'UNPROVEN'},
         {'tasks':['WP02-08','WP02-12'],'profile':'OutboxV2','source':pin('docs/WP02-08-OUTBOX-V2.md',repo='Platform'),'adoption':'UNPROVEN'}],
      'package_identity':{'Main_declared':'CP6.Platform.Messaging/EntityFramework [0.10.2]','Platform_current_build_default':'0.8.0-alpha.2','released_artifact_sha256':None,'conclusion':'Source tree/build defaults are not restored/published package or real consumer adoption evidence'},
      'production_enabled':False,'actual_adoption':False,'actual_completion':False}
    write('provider-consumer-closure.json',result)
    return result


def audit():
    records=[]
    checks=[('CP6.Core/Services/Wf/FormSubmissionService.cs','SubmitAsync','CHANGE','Existing local request-key/transaction pattern; missing R1 immutable bindings, persistent decision application and receipt worker.',['WP05-19','WP12-06']),
      ('CP6.Core/Services/Wf/ApprovalDispatcher.cs','FirstOrDefault','CHANGE','Sync OnApprovedAsync/OnRejectedAsync; duplicate BizType can select first callback; not persistent OA application protocol.',['WP05-19','WP05-20','WP12-07']),
      ('CP6.Core/EFDbContext/CP6Context.cs','CorePostgreSqlModelConfiguration','REUSE','Bounded infrastructure only: tenant stamping/filter and conditional PG configuration do not prove dedicated-role schema/RLS or new entities.',['WP02-02','WP05-18','WP05-32']),
      ('CP6.Core/CP6.Core.csproj','CP6.Platform.Messaging','CHANGE','Exact declared [0.10.2] package references; no package restored/runtime hash or new profile adoption proof.',['WP02-10','WP02-12']),
      ('CP6.WebApi/Controllers/Oa/FormSubmissionController.cs','class FormSubmissionController','CHANGE','Existing OA HTTP entry must adapt trusted Owner registration and durable dispatch; no new route authorized here.',['WP12-06','WP05-19'])]
    for pattern in ['*PostgreSqlModelConfiguration.cs','*LangController.cs','*TenantMiddleware.cs']:
      for path in sorted(ROOT.rglob(pattern)):
        if '.git' in path.parts:continue
        rel=path.relative_to(ROOT).as_posix()
        checks.append((rel,'','CHANGE','Existing compatibility/publish/tenant infrastructure; inspect exact symbols below. Original defaults/precision/token behavior are not new ITER/REL legal-domain or tenancy proof.',['WP02-02','WP02-17','WP05-18']))
    for path,symbol,disposition,reason,tasks in checks:
      text=(ROOT/path).read_text('utf-8-sig').splitlines()
      anchors=[{'line':i,'text':l.strip()} for i,l in enumerate(text,1) if (symbol and symbol in l) or re.search(r'\[Route|\[Http|HasPrecision|rowversion|bytea|numeric\(|HasColumnType|Publish|Rollback|Review|DefaultTenant|session_user',l)]
      records.append({'repository':'Main','source':pin(path),'symbols_routes_models_writers':anchors,'disposition':disposition,'reason':reason,'successor_tasks':tasks,'evidence_class':'STATIC_SOURCE_AUDIT','runtime':'NOT_RUN'})
    records += [{'repository':'Main','revision':BASE,'path':'CP6.Core/Services/Plm','symbol':'PlmEngineeringService','exists':False,'disposition':'NEW','reason':'No current service directory/symbol. Historical b7ec269 candidate is reference only, not current implementation.','successor_tasks':['WP05-18','WP05-19','WP05-20','WP05-21','WP05-32','WP05-33','WP05-34','WP05-35','WP05-36','WP05-42','WP05-61']}]
    for repo,path in [('Platform','docs/WP02-07-CALLER-TRANSACTION-INBOX.md'),('Platform','docs/WP02-08-OUTBOX-V2.md'),('Platform','Directory.Build.props')]:
      records.append({'repository':repo,'source':pin(path,repo=repo),'disposition':'CHANGE','reason':'Available source profile; exact package/runtime adoption and compatible all-writer lock integration remain unproven.','successor_tasks':['WP02-10','WP02-12','WP05-19','WP05-34'],'runtime':'NOT_RUN'})
    write('current-source-audit.json',{'schema':'WP01-01-CURRENT-SOURCE/1','repository_baselines':{k:v[1] for k,v in REPOS.items()},'records':records,
      'scope':'Exact listed files/symbols only; not full product audit or proof every writer is enumerated. NEW entries are implementation obligations, not source existence.',
      'model_gap':'No ITER/REL new EF models or migrations added. Shared EF/migration histories remain read-only.',
      'historical_candidate':{'revision':'b7ec269','status':'REFERENCE_ONLY','current_source':False},'production_enabled':False,'actual_adoption':False,'actual_completion':False})


def basis_and_gate(g):
    old=read('docs/development/evidence/WP01-01/preparation/gate-contract.proposed.json')
    # Navigation rows are reused, not regenerated or counted as newly recovered originals.
    changes=[('DC-01','PLM-ITER-01',['Four digest meanings kept distinct; proposed ENG envelope never accepted implicitly','Capacity pagination profile cannot narrow legal collection size','Physical field identity and exact value roundtrip require actual Owner evidence']),
             ('DC-02','PLM-REL-01',['Physical exact quantity/profile/identity representation preserves original domain','Declared provider/consumer edges and exact package/remote adoption evidence required per authorized scope'])]
    extra=[{'id':ident,'target':target,'observations':obs,'status':'PROPOSED_NOT_ADOPTED','kind':'NEW_BASIS_DELTA_NOT_ORIGINAL_AC'} for ident,target,obs in changes]
    delta={'schema':'WP01-01-BASIS-DELTA/1','version':VERSION,'status':'PROPOSED_NOT_ADOPTED',
      'predecessors':[pin('docs/development/task-deliveries/WP05-01/basis-manifest.json'),pin('docs/development/task-deliveries/WP05-01/supplements/review-r1/manifest.json')],
      'unchanged_matrix':pin('docs/development/evidence/WP01-01/preparation/obligation-matrix.json'),
      'change_scope':'Only candidate technical choices and stronger file/authority/provider gate duties. No inherited business assertion removed, no reconstructed legacy member claimed.',
      'additional_obligations':extra,'removed_obligations':[],'historical_gaps':old['historical_gaps'],
      'adoption_requirements':['separate append-only exact version/manifest hash authority record','actual actor/time and verifiable delegated scope','COORD serial public gate integration window','business evidence remains independently required'],
      'production_enabled':False,'actual_adoption':False,'actual_completion':False}
    write('basis-delta.json',delta)
    sourcepaths=['docs/design-handbook/remediation/07-shared-consumer-mappings.md','docs/design-handbook/architecture/02-postgresql-tenancy.md','docs/design-handbook/modules/PLM-ITER-01.md','docs/design-handbook/modules/PLM-REL-01.md','docs/development/task-definitions.json']
    graphpath=(OUT/'provider-consumer-closure.json').relative_to(ROOT).as_posix()
    data=(ROOT/graphpath).read_bytes()
    graphref={'path':graphpath,'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data)}
    def generated_ref(name):
        path=OUT/name;raw=path.read_bytes()
        return {'path':path.relative_to(ROOT).as_posix(),'sha256':hashlib.sha256(raw).hexdigest(),'bytes':len(raw)}
    contract={'schema':'WP01-01-DISABLED-GATE/1','version':VERSION,'production_enabled':False,
      'matrix':pin('docs/development/evidence/WP01-01/preparation/obligation-matrix.json'),'sources':[pin(p) for p in sourcepaths],
      'decision_package':generated_ref('decisions.json'),'basis_delta':generated_ref('basis-delta.json'),
      'provider_graph':graphref,'target_edges':{t:[e['id'] for e in g['edges'] if t in e['targets']] for t in g['roots']},
      'additional_obligations':extra,'historical_gaps':old['historical_gaps'],'target_whitelist':['PLM-ITER-01','PLM-REL-01'],
      'predicates':{'candidate_delivery':'All candidate artifacts + local private validation + full task review; does not complete WP01-01',
       'WP01_01_task_DONE':'Exact basis adoption verified + authorized serial production adapter and regression + required ledger/source mapping updates + local gates + remote main inclusion. This candidate satisfies only preparatory design subset.',
       'WP01_package_inventory_closed':'123 original inventories satisfy unchanged original gates AND two target prospective bases explicitly adopted with exact inherited/new coverage; preserve 30/48 missing history. 123 originals + 2 new bases, never 125 recovered originals.',
       'target_DONE':'Whitelisted target exact adopted basis + all inherited and delta assertions + applicable R1 + real configuration/implementation/runtime evidence + exact declared provider-consumer closure + each required repository/consumer remote main/package binding + no FAIL/NOT_RUN/REFERENCE_ONLY/static/synthetic substituted.'},
      'actual_adoption':False,'actual_completion':False,'actual_authority_authenticity':'UNVERIFIED_REJECT_ACTUAL','actual_remote_ancestry':'UNVERIFIED_REJECT_ACTUAL'}
    write('gate-contract.candidate.json',contract)
    write('submission.actual-unadopted.json',{'basis_version':VERSION,'target':'PLM-ITER-01','synthetic':False,'adoption':None,'obligations':[],'edges':[],'historical_gaps':old['historical_gaps']})


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--platform-repo',type=Path,required=True)
    parser.add_argument('--crm-repo',type=Path,required=True)
    args=parser.parse_args()
    REPOS.update(Platform=(args.platform_repo,'b28db763f70ab6f164cb7c87d6ce49a6d97267f1'),CRM=(args.crm_repo,'1e4eea61c31d7547c3b3495d392eefbed99b7339'))
    OUT.mkdir(parents=True,exist_ok=True)
    for folder in [OUT,ROOT/'eng/wp01-01/decision-closure']:
      rules='* -text\n.gitattributes whitespace=cr-at-eol\n'
      if folder==OUT:
        rules+='tdd-deep-red.run.json whitespace=cr-at-eol\ntdd-consumer-remote-red.run.json whitespace=cr-at-eol\n'
      rules+='*.stdout.txt whitespace=-blank-at-eof,-blank-at-eol\n*.stderr.txt whitespace=-blank-at-eof,-blank-at-eol\n'
      (folder/'.gitattributes').write_text(rules,encoding='utf-8')
    decisions();mapping();g=graph();basis_and_gate(g)
    print(json.dumps({'status':'CANDIDATE_GENERATED','edges':len(g['edges']),'nodes':len(g['nodes']),'production_enabled':False}))


if __name__=='__main__':
    main()
