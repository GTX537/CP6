"""New sidecar contracts; original Owner objects are carried without reshaping."""
import argparse
import hashlib
import json
import re
from pathlib import Path


def build(book,repo):
    out=book/'remediation/assets/shared-consumers';out.mkdir(parents=True,exist_ok=True)
    def save(name,value): (out/name).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    def R(n): return {'$ref':'#/$defs/'+n}
    def O(p): return {'type':'object','properties':p,'required':list(p),'additionalProperties':False}
    def A(n): return {'type':'array','items':R(n),'uniqueItems':True}
    def N(n): return {'anyOf':[R(n),{'type':'null'}]}
    D={'Text':{'type':'string'},'Hash':{'type':'string','pattern':'^[0-9a-f]{64}$'},'Uuid':{'type':'string','pattern':'^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'},'Seq':{'type':'string','pattern':'^(0|[1-9][0-9]*)$'},'Scope':O({'environmentId':R('Text'),'tenantId':R('Uuid')})}
    D['ContractPin']=O({'contractId':R('Text'),'version':R('Text'),'schemaSha256':R('Hash')})
    D['NativeReference']=O({'contract':R('ContractPin'),'canonicalReferenceBase64':R('Text'),'rawSha256':R('Hash'),'byteLength':{'type':'integer','minimum':1}})
    D['Digest']=O({'algorithm':{'const':'sha256'},'canonicalizationVersion':R('Text'),'value':R('Hash')})
    D['CandidateMember']=O({'candidateId':R('Text'),'candidateVersion':R('Text'),'technicalReference':R('NativeReference'),'originalDigest':R('NativeReference')})
    D['CandidateAggregate']=O({'schema':{'const':'ITER-CANDIDATE-AGGREGATE/1'},'scope':R('Scope'),'iterationId':R('Text'),'roundId':R('Text'),'aggregateId':R('Uuid'),'aggregateVersion':R('Seq'),'members':A('CandidateMember'),'aggregateDigest':R('Hash')})
    D['IterOaBinding']=O({'schema':{'const':'ITER-OA-APPLICATION/1'},'mappingVersion':{'const':'1.0.0'},'scope':R('Scope'),'ownerRegistrationRef':R('NativeReference'),'roundBindingId':R('Uuid'),'roundId':R('Text'),'iterationId':R('Text'),'aggregateId':R('Uuid'),'aggregateVersion':R('Seq'),'aggregateDigest':R('Hash'),'originalReviewPackageRef':R('NativeReference'),'reviewPackageDigest':R('Digest'),'originalPolicyRef':R('NativeReference'),'policyVersion':R('Text'),'policyDigest':R('Digest'),'technicalManifestDigest':N('Digest'),'rejectDisposition':{'enum':['RETURN','REJECT']},'submissionId':R('Uuid'),'applicationUnit':{'const':'PLM_ITER_REVIEW_DECISION'}})
    D['IterOaBinding']['properties']['oaRoundKey']={'type':'string','minLength':1,'maxLength':100}
    D['IterOaBinding']['required'].append('oaRoundKey')
    D['RowVersion']={'type':'string','pattern':'^[A-Za-z0-9+/]{11}=$'}
    D['SpaceStampVersion']=O({'id':R('Uuid'),'securityStamp':R('Seq'),'rowVersion':R('RowVersion')})
    D['SpaceGrantVersion']=O({'grantId':R('Uuid'),'grantVersion':R('Seq'),'grantRowVersion':R('RowVersion'),'fieldPolicyId':N('Uuid'),'fieldPolicyVersion':R('Seq'),'fieldPolicyRowVersion':N('RowVersion')})
    D['SpaceVersionVector']=O({'organization':R('SpaceStampVersion'),'membership':R('SpaceStampVersion'),'grants':A('SpaceGrantVersion'),'grantSetMembershipRef':R('NativeReference'),'authorizationVersion':R('Text')})
    D['SpaceGrantClause']=O({'grantId':R('Uuid'),'siteId':R('Uuid'),'resourceType':{'enum':['PublishedScene','Stock','Task']},'rowScopeRef':R('NativeReference'),'fieldPolicyRef':N('NativeReference'),'canExport':{'type':'boolean'},'validFromUtc':{'type':'string','format':'date-time'},'validToUtc':{'anyOf':[{'type':'string','format':'date-time'},{'type':'null'}]},'version':R('Seq')})
    D['SpaceS5Binding']=O({'schema':{'const':'SPACE-S5-AUTHORIZATION/1'},'scope':R('Scope'),'actorId':R('Uuid'),'organizationId':R('Uuid'),'siteId':R('Uuid'),'purpose':R('Text'),'s5ProviderPin':R('ContractPin'),'s5PolicyRef':R('NativeReference'),'s5FieldRegistryRef':R('NativeReference'),'s5CurrentAuthorityVector':A('NativeReference'),'spaceVersions':R('SpaceVersionVector'),'grantClauses':A('SpaceGrantClause'),'observedAtUtc':{'type':'string','format':'date-time'},'validUntilUtc':{'type':'string','format':'date-time'},'externalWriteAllowed':{'const':False}})
    D['FieldDecision']=O({'resourceType':{'enum':['PublishedScene','Stock','Task']},'fieldName':R('Text'),'s5Access':{'enum':[1,2,3]},'spaceMasking':{'enum':[0,1,2,3]},'readAllowed':{'type':'boolean'},'writeAllowed':{'const':False},'dtoPaths':A('Text')})
    D['SpaceReadDecision']=O({'schema':{'const':'SPACE-S5-READ-DECISION/1'},'outcome':{'enum':['ALLOW_READ','DENY','UNAVAILABLE','STALE']},'binding':N('SpaceS5Binding'),'fieldDecisions':A('FieldDecision'),'matchedWholeGrantIds':A('Uuid'),'rowScopeRefs':A('NativeReference'),'auditRef':R('NativeReference')})
    D['EngInput']=O({'inputOccurrenceId':R('Text'),'itemRevisionRef':R('NativeReference'),'specRef':R('NativeReference')})
    D['EngOperation']=O({'operationOccurrenceId':R('Text'),'operationRef':R('NativeReference'),'configurationRef':R('NativeReference')})
    D['EngOutput']=O({'outputOccurrenceId':R('Text'),'productIdentity':R('NativeReference'),'itemRevisionRef':R('NativeReference'),'specRef':R('NativeReference'),'generatingOperationOccurrenceId':R('Text'),'configurationRef':R('NativeReference'),'inputOccurrenceIds':A('Text'),'relationEvidence':R('NativeReference'),'generationWithoutInputBasis':N('NativeReference')})
    D['OutputRelations']=O({'schema':{'const':'ENG-OUTPUT-RELATIONS/1'},'scope':R('Scope'),'relationId':R('Uuid'),'version':R('Seq'),'configurationRef':R('NativeReference'),'inputs':A('EngInput'),'operations':A('EngOperation'),'outputs':A('EngOutput'),'bodyDigest':R('Hash')})
    D['BaselineOutputBinding']=O({'schema':{'const':'ENG-BASELINE-OUTPUT-BINDING/1'},'scope':R('Scope'),'baselineRef':R('NativeReference'),'originalManifestRef':R('NativeReference'),'originalManifestDigest':R('NativeReference'),'relationId':R('Uuid'),'relationVersion':R('Seq'),'relationBodyDigest':R('Hash'),'freezeCommandId':R('Uuid'),'configurationEvidence':A('NativeReference'),'bodyDigest':R('Hash')})
    save('sidecar-1.0.0.schema.json',{'$schema':'https://json-schema.org/draft/2020-12/schema','$id':'urn:cp6:shared-consumers:1.0.0','$defs':D,'oneOf':[R(n) for n in ['CandidateAggregate','IterOaBinding','SpaceS5Binding','SpaceReadDecision','OutputRelations','BaselineOutputBinding']]})
    code=repo/'CP6.Space.Application/SpaceExternalPortal.cs'
    dto=repo/'CP6.Space.Contracts/SpaceExternalPortalContracts.cs'
    catalog=re.findall(r'(Text|Scalar)\(SpaceResourceType\.(\w+), "([^"]+)"\)',code.read_text(encoding='utf-8-sig'))
    collections={'floor':'floors[*]','zone':'floors[*].zones[*]','aisle':'floors[*].aisles[*]','rack':'floors[*].racks[*]','rackLevel':'floors[*].rackLevels[*]','location':'floors[*].locations[*]','element':'floors[*].elements[*]'}
    compound={'rack.position':['x','y','z'],'rack.dimensions':['width','depth','height'],'rackLevel.geometry':['levelNo','bottomZ','clearHeight','binCount','depthCount','cellWidth','cellDepth','beamHeight'],'location.position':['columnNo','levelNo','depthNo'],'location.dimensions':['width','height','depth'],'anchor':['anchorXMillimeters','anchorYMillimeters','anchorZMillimeters']}
    mapping=[]
    for kind,resource,field in catalog:
        if resource=='PublishedScene':
            prefix,leaf=field.split('.',1);paths=[collections[prefix]+'.'+name for name in compound.get(field,[leaf])]
        else: paths=['items[*].'+name for name in compound.get(field,[field])]
        mapping.append({'resource':resource,'space_field':field,'s5_resource':'SPACE.PORTAL.'+resource,'s5_field':field,'kind':kind,'dto_paths':paths,'export_paths':paths,'derived_search_sort_aggregate_dependencies':paths,'hidden_behavior':'omit all mapped paths and derived outputs; never emit zero/default','structural_identity_rule':'row/object read gate separately protects IDs and control metadata','actual_registration':'UNPROVEN'})
    assert len(mapping)==61
    save('space-field-mapping-1.0.0.json',{'mapping_version':'1.0.0','role':'NEW_REQUIRED_CONSUMER_REGISTRATION_NOT_RUNTIME','source_files':[{'path':str(p.relative_to(repo)).replace('\\','/'),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in [code,dto]],'fields':mapping,'structural_allowlist':{'PublishedScene':['siteId','publishedVersionId','authorizationVersion','floors[*].logicalId','floors[*].zones[*].logicalId','floors[*].zones[*].floorLogicalId','floors[*].aisles[*].logicalId','floors[*].aisles[*].zoneLogicalId','floors[*].racks[*].logicalId','floors[*].racks[*].floorLogicalId','floors[*].racks[*].zoneLogicalId','floors[*].racks[*].aisleLogicalId','floors[*].rackLevels[*].logicalId','floors[*].rackLevels[*].rackLogicalId','floors[*].locations[*].logicalId','floors[*].locations[*].floorLogicalId','floors[*].locations[*].rackLogicalId','floors[*].elements[*].logicalId','floors[*].elements[*].floorLogicalId','floors[*].elements[*].parentLogicalId'],'Stock':['siteId','publishedVersionId','authorizationVersion','source.observedAtUtc','source.receivedAtUtc','source.delayMilliseconds','source.isAvailable','items[*].locationLogicalId','items[*].floorLogicalId'],'Task':['siteId','publishedVersionId','authorizationVersion','source.observedAtUtc','source.receivedAtUtc','source.delayMilliseconds','source.isAvailable','items[*].locationLogicalId','items[*].floorLogicalId','items[*].zoneLogicalId']},'unknown_dto_member':'DENY_NEW_ROUTE_UNTIL_REGISTERED','version_rules':'S5 MIN on actual rows before defaults; resource action + exact whole clause + row scope + field intersection, external write false'})
    cases=[
      ('OA-CANDIDATE-SET','PLM two native candidates in one frozen Round','one aggregate with two exact members; neither first-only selection nor duplicate submission'),
      ('OA-REJECT-MISSING','REJECTED with no frozen rejectDisposition','BLOCKED_POLICY_MAPPING; no stage write'),
      ('OA-LATE-APPROVED','withdraw commits before old APPROVED arrives','STALE_RECEIPT and no new-round mutation'),
      ('OA-REPLAY','same decision with another transport event','original ApplicationReceipt, one application unit'),
      ('OA-CHANGED-BODY','same event ID with different bytes','CONFLICT; retain both audit observations'),
      ('OA-APPLIED-WITHDRAW','apply commits first then withdrawal','409 plus original receipt; no erase'),
      ('SPACE-MIN-MISSING','role A absent, role B Access3','3 then omit full composite paths'),
      ('SPACE-MIN-REAL','role A2, role B3','2; external write still false'),
      ('SPACE-MIN-DEFAULT','all roles absent, complete registry','1 with independent action/row/purpose checks'),
      ('SPACE-UNKNOWN','unknown registry or member mapping','UNAVAILABLE/DENY no cache fallback'),
      ('SPACE-CLAUSE','different grants separately match site and resource','DENY; no clause stitching'),
      ('SPACE-REVOKE','membership changes after export but before download','deny download despite original success and URL'),
      ('SPACE-CURSOR','current authority vector differs from cursor','412 and new read'),
      ('ENG-MULTI','same product generated at two operations','two distinct output occurrences; original v1 manifest hash unchanged'),
      ('ENG-ORPHAN','output references absent operation or input','reject freeze; no baseline'),
      ('ENG-OLD-BASELINE','attempt to attach relations to frozen old baseline','reject mutation; follow successor baseline process'),
      ('ENG-UNKNOWN-COMMIT','freeze response lost','query original command, no second baseline'),
    ]
    save('acceptance.json',{'cases':[{'id':k,'scenario':s,'expected':e,'verification_level':'REQUIRED_RUNTIME','result':'NOT_RUN'} for k,s,e in cases]})
    save('native-reference-rules.json',{'canonicalReferenceBase64':'Exact original native reference JSON UTF8, with no reshaping; decode once and validate strict original pinned schema including duplicate-member rejection','rawSha256':'SHA256 of exact decoded bytes','byteLength':'UTF8 byte count not character count','contract':'registration must pin complete original schema and verifier; unknown pin fenced','identity':'compare complete native reference bytes under original canonical profile, not hash alone','purpose':'sidecar carrier only; does not claim original Owner signs this new wrapper','transport':'no new public endpoint; dispatch/read/apply via exact registered original Owner ports and permissions described in 07-shared-consumer-mappings.md'})
    oa=Path('D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/0c/0cc2a482fd1dc879__OA-APP-01_业务审批绑定与回调_前后端开发Spec_v1.1.2_R2_REVIEW.md')
    save('oa-field-mapping-1.0.0.json',{'mapping_version':'ITER-OA-APPLICATION/1','source':{'source_path':oa.as_posix(),'sha256':hashlib.sha256(oa.read_bytes()).hexdigest(),'line':91,'end_line':210},'registration':'PLM Owner original service + exact Scope/BizType/PLM_ITER_REVIEW; UNPROVEN until real consumer registered','fields':{
      'SubmitCommand.schema':'constant oa.app/2.0', 'scope':'trusted original environment/tenant, same as Binding/Aggregate',
      'commandId':'UUID persisted once for Owner submission command; original retry unchanged', 'submissionId':'Binding.submissionId allocated before snapshot freeze',
      'subject.businessOwner,bizType':'exact deployed original Owner registration, never browser values', 'subject.subjectKind':'PLM_ITER_REVIEW',
      'subject.subjectId':'Binding.oaRoundKey', 'subject.businessRevision':'Binding.roundBindingId',
      'subject.contentDigest':'sha256 of exact new snapshot canonical UTF8 bytes; canonicalizationVersion ITER-OA-SNAPSHOT/1; never overwrite native package digest',
      'policy.ref':'original native policy Ref if OA Ref domain-valid; otherwise immutable alias Ref registered by PLM resolving complete native bytes',
      'policy.version,digest':'Binding.policyVersion/policyDigest; unregistered/non-sha256 algorithm is FENCED, never coerced',
      'roundId,engineering.roundId':'Binding.oaRoundKey; equal to subject.subjectId',
      'replacesSubmissionId':'original explicitly superseded prior submission or null', 'businessSubmittedRowVersion':'exact frozen Round 8-byte Base64 RV',
      'starterId,submitAuthorizationRef,separationSubjectIds':'trusted Owner actor/current submit authorization/full native author and SOD set',
      'snapshot.schema':'ITER-OA-SNAPSHOT/1', 'snapshot.canonicalBytesBase64':'canonical UTF8 of closed {schema,aggregate,binding}, complete objects',
      'snapshot.digest':'same exact byte digest as subject.contentDigest', 'snapshot.immutableEvidenceRefs':'full OA-domain references to original package/policy and native evidence, using registered aliases only when required',
      'engineering.kind':'ITER', 'engineering.candidateId,candidateVersion':'Binding.aggregateId/aggregateVersion',
      'engineering.digest,engineering.reviewPackageDigest':'Binding.reviewPackageDigest from original immutable package, not aggregateDigest',
      'engineering.reviewPackageRef':'exact original package Ref or registered immutable alias preserving full native reference',
      'engineering.policyVersion':'Binding.policyVersion', 'engineering.technicalManifestDigest':'exact Binding nullable technical digest',
      'engineering.purpose':'registered original review purpose, not arbitrary browser input',
      'reason':'current explicit user reason within OA <=500 Unicode characters', 'ownerOutboxId':'immutable UUID saved in same Owner transaction', 'ownerFrozenAt':'actual Owner freeze transaction UTC with milliseconds',
      'DecisionEnvelope':'full original OA envelope retained; require exact Scope/submission/subject/policy/round/engineering snapshot binding before applying',
      'ApplicationReceipt':'original OA shape; action PLM_ITER_REVIEW_DECISION; subject from original submission; actual result/revision/audit/actor and previousReceiptId from Owner transaction; never copied from decision as proof of application'},
      'alias_rule':'Persist Scope + reference kind + complete native reference bytes/schema pin/digest + OA UUID key/version. Original domain-valid Round key is kept; otherwise unique persistent UUID alias is stored as oaRoundKey. Alias lookup is current-authorized immutable resolution, cannot claim original Owner emitted the alias. Missing or conflicting alias blocks submit. No substring/hash-as-ID fallback.',
      'limits':'OA 1MiB body,256KiB snapshot,1000 ID entries; full candidate/evidence set required. Oversize blocks this route pending separately registered transport; no truncation.',
      'runtime_status':'NOT_RUN'})
    return {'schemas':len(D),'space_fields':len(mapping),'runtime_cases':len(cases)}


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--handbook',type=Path,required=True);p.add_argument('--repo',type=Path,required=True);a=p.parse_args();print(build(a.handbook,a.repo))
