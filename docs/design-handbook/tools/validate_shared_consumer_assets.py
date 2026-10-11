"""Check sidecar schemas, source DTO paths and deterministic design examples."""
import argparse
import base64
import copy
import hashlib
import json
import re
from pathlib import Path
from jsonschema import Draft202012Validator


def canonical(x): return json.dumps(x,ensure_ascii=False,sort_keys=True,separators=(',',':')).encode('utf-8')
def digest(x): return hashlib.sha256(x).hexdigest()


def main(book,repo):
    folder=book/'remediation/assets/shared-consumers'
    schema=json.loads((folder/'sidecar-1.0.0.schema.json').read_text(encoding='utf-8'))
    Draft202012Validator.check_schema(schema)
    mapping=json.loads((folder/'space-field-mapping-1.0.0.json').read_text(encoding='utf-8'))
    for source in mapping['source_files']: assert digest((repo/source['path']).read_bytes())==source['sha256']
    dto=(repo/'CP6.Space.Contracts/SpaceExternalPortalContracts.cs').read_text(encoding='utf-8-sig')
    records={m[0]:set(re.findall(r'\b([A-Z][A-Za-z0-9]*)\s*[,)]',m[1]+')')) for m in re.findall(r'record (SpacePortal\w+)\((.*?)\);',dto,re.S)}
    prefix_records={'floor':'SpacePortalFloorDto','zone':'SpacePortalZoneDto','aisle':'SpacePortalAisleDto','rack':'SpacePortalRackDto','rackLevel':'SpacePortalRackLevelDto','location':'SpacePortalLocationDto','element':'SpacePortalElementDto'}
    paths=0
    for field in mapping['fields']:
        record=prefix_records[field['space_field'].split('.')[0]] if field['resource']=='PublishedScene' else 'SpacePortal'+field['resource']+'ItemDto'
        for path in field['dto_paths']:
            name=path.rsplit('.',1)[1];assert name[0].upper()+name[1:] in records[record],(record,path)
            paths+=1
    assert len(mapping['fields'])==61
    def s5(rows,known=True):
        if not known or any(v not in (1,2,3,None) for v in rows):return None
        actual=[v for v in rows if v is not None]
        return min(actual) if actual else 1
    checks=[]
    for rows,known,expected in [([None,3],True,3),([None,2],True,2),([2,3],True,2),([None,None],True,1),([1,3],True,1),([None,None],False,None),([4],True,None)]:
        assert s5(rows,known)==expected
        checks.append({'case':'S5 actual-row aggregation','roles':rows,'registry_known':known,'expected':expected,'result':'PASS'})
    clauses=[{'site':'A','resource':'Stock'},{'site':'B','resource':'Task'}]
    assert not any(c['site']=='A' and c['resource']=='Task' for c in clauses)
    checks.append({'case':'whole grant clause, no cross-clause union','result':'PASS'})
    # Closed new contracts transport full original bytes, not a guessed common Ref.
    raw=canonical({'owner':'DEMO_OWNER','id':'original-long-identifier','version':'opaque/v3'})
    native={'contract':{'contractId':'DEMO_NATIVE_REF','version':'1','schemaSha256':'1'*64},'canonicalReferenceBase64':base64.b64encode(raw).decode(),'rawSha256':digest(raw),'byteLength':len(raw)}
    def N(name):
        r=copy.deepcopy(native); b=canonical({'owner':'DEMO_OWNER','id':name,'version':'opaque/v3'});r.update(canonicalReferenceBase64=base64.b64encode(b).decode(),rawSha256=digest(b),byteLength=len(b));return r
    scope={'environmentId':'DEMO','tenantId':'00000000-0000-4000-8000-000000000001'}
    cfg=N('cfg1')
    U=lambda n:f'00000000-0000-4000-8000-{n:012d}'
    version_vector={'organization':{'id':U(20),'securityStamp':'3','rowVersion':'AAAAAAAAAAM='},'membership':{'id':U(21),'securityStamp':'7','rowVersion':'AAAAAAAAAAc='},'authorizationVersion':'original-opaque-space-version','grantSetMembershipRef':N('complete-grant-set'), 'grants':[{'grantId':U(i),'grantVersion':str(v),'grantRowVersion':'AAAAAAAAAAE=','fieldPolicyId':U(30),'fieldPolicyVersion':'9','fieldPolicyRowVersion':'AAAAAAAAAAk='} for i,v in [(22,2),(23,8)]]}
    assert not list(Draft202012Validator({'$schema':schema['$schema'],'$defs':schema['$defs'],'$ref':'#/$defs/SpaceVersionVector'}).iter_errors(version_vector))
    def versions_valid(vec,clause_ids):
        gs=vec['grants'];ids=[g['grantId'] for g in gs]
        if ids!=sorted(set(ids)) or set(ids)!=set(clause_ids):return False
        policies={}
        for g in gs:
            pid=g['fieldPolicyId'];pair=(g['fieldPolicyVersion'],g['fieldPolicyRowVersion'])
            if pid is None:
                if pair!=('0',None):return False
            elif pid in policies and policies[pid]!=pair:return False
            elif pair[0]=='0' or pair[1] is None:return False
            policies[pid]=pair
        return True
    assert versions_valid(version_vector,[U(22),U(23)])
    removed=copy.deepcopy(version_vector);removed['grants'].pop()
    assert not versions_valid(removed,[U(22),U(23)])
    bad_policy=copy.deepcopy(version_vector);bad_policy['grants'][1]['fieldPolicyVersion']='10'
    assert not versions_valid(bad_policy,[U(22),U(23)])
    changed=copy.deepcopy(version_vector)
    for g in changed['grants']:g['fieldPolicyVersion']='10'
    assert versions_valid(changed,[U(22),U(23)]) and canonical(changed)!=canonical(version_vector)
    none_policy=copy.deepcopy(version_vector)
    for g in none_policy['grants']:g.update(fieldPolicyId=None,fieldPolicyVersion='0',fieldPolicyRowVersion=None)
    assert versions_valid(none_policy,[U(22),U(23)])
    checks.extend([{'case':name,'result':'PASS'} for name in ['two grants keep independent versions','missing current grant member rejected','shared policy versions must agree','changed policy marks old vector stale','legitimate no-policy grant preserves null/zero boundary']])
    relations={'schema':'ENG-OUTPUT-RELATIONS/1','scope':scope,'relationId':'00000000-0000-4000-8000-000000000002','version':'1','configurationRef':cfg,'inputs':[{'inputOccurrenceId':'input1','itemRevisionRef':N('itemIn1'),'specRef':N('specIn1')}],'operations':[{'operationOccurrenceId':k,'operationRef':N(k),'configurationRef':cfg} for k in ['op1','op2']],'outputs':[{'outputOccurrenceId':k,'productIdentity':N('same-product'),'itemRevisionRef':N('revision1'),'specRef':N('spec1'),'generatingOperationOccurrenceId':op,'configurationRef':cfg,'inputOccurrenceIds':['input1'],'relationEvidence':N('relation-'+k),'generationWithoutInputBasis':None} for k,op in [('out1','op1'),('out2','op2')]],'bodyDigest':'0'*64}
    def body_hash(v):return digest((v['schema']+'\n').encode()+canonical({k:x for k,x in v.items() if k!='bodyDigest'}))
    relations['bodyDigest']=body_hash(relations)
    def valid_relations(x):
        errors=list(Draft202012Validator({'$schema':schema['$schema'],'$defs':schema['$defs'],'$ref':'#/$defs/OutputRelations'}).iter_errors(x))
        if errors:return False
        ins={a['inputOccurrenceId'] for a in x['inputs']}; ops={a['operationOccurrenceId'] for a in x['operations']}
        outs=[a['outputOccurrenceId'] for a in x['outputs']]
        return (len(ins)==len(x['inputs']) and len(ops)==len(x['operations']) and len(set(outs))==len(outs)
            and all(o['generatingOperationOccurrenceId'] in ops and set(o['inputOccurrenceIds'])<=ins and o['configurationRef']==x['configurationRef'] and (o['inputOccurrenceIds'] or o['generationWithoutInputBasis'] is not None) for o in x['outputs'])
            and all(o['configurationRef']==x['configurationRef'] for o in x['operations']) and x['bodyDigest']==body_hash(x))
    assert valid_relations(relations)
    baseline_binding={'schema':'ENG-BASELINE-OUTPUT-BINDING/1','scope':scope,'baselineRef':N('new-baseline'),'originalManifestRef':N('original-manifest'),'originalManifestDigest':N('original-manifest-digest'),'relationId':relations['relationId'],'relationVersion':relations['version'],'relationBodyDigest':relations['bodyDigest'],'freezeCommandId':U(40),'configurationEvidence':[cfg],'bodyDigest':'0'*64}
    baseline_binding['bodyDigest']=body_hash(baseline_binding)
    def binding_valid(x,relation,baseline_configuration,original_primary_product):
        return (not list(Draft202012Validator({'$schema':schema['$schema'],'$defs':schema['$defs'],'$ref':'#/$defs/BaselineOutputBinding'}).iter_errors(x)) and x['bodyDigest']==body_hash(x)
            and x['scope']==relation['scope'] and (x['relationId'],x['relationVersion'],x['relationBodyDigest'])==(relation['relationId'],relation['version'],relation['bodyDigest'])
            and baseline_configuration==relation['configurationRef'] and baseline_configuration in x['configurationEvidence']
            and any(o['productIdentity']==original_primary_product for o in relation['outputs']))
    assert binding_valid(baseline_binding,relations,cfg,N('same-product'))
    assert not binding_valid(baseline_binding,relations,cfg,N('other-product'))
    assert not binding_valid(baseline_binding,relations,N('different-config'),N('same-product'))
    checks.extend([{'case':name,'result':'PASS'} for name in ['baseline-relation complete tuple equality','wrong original primary PRODUCT rejected','baseline configuration drift rejected']])
    examples=[{'id':'ENG-TWO-OCCURRENCES','schema':'OutputRelations','body':relations,'expected_valid':True}]
    for case,mutate in [('ENG-ABSENT-OP',lambda x:x['outputs'][0].update(generatingOperationOccurrenceId='absent')),('ENG-ABSENT-INPUT',lambda x:x['outputs'][0].update(inputOccurrenceIds=['absent'])),('ENG-DUP-OCCURRENCE',lambda x:x['outputs'][1].update(outputOccurrenceId='out1')),('ENG-CONFIG-DRIFT',lambda x:x['outputs'][0].update(configurationRef=N('cfg2'))),('ENG-UNPROVEN-NO-INPUT',lambda x:x['outputs'][0].update(inputOccurrenceIds=[]))]:
        bad=copy.deepcopy(relations);mutate(bad);bad['bodyDigest']=body_hash(bad);assert not valid_relations(bad)
        examples.append({'id':case,'schema':'OutputRelations','body':bad,'expected_valid':False})
    aggregate={'schema':'ITER-CANDIDATE-AGGREGATE/1','scope':scope,'iterationId':'ITER-DEMO','roundId':'ROUND-DEMO','aggregateId':'00000000-0000-4000-8000-000000000003','aggregateVersion':'1','members':[{'candidateId':k,'candidateVersion':'opaque1','technicalReference':N(k),'originalDigest':N(k+'-originalDigest')} for k in ['candidate-A','candidate-B']],'aggregateDigest':'0'*64}
    aggregate['aggregateDigest']=digest(b'ITER-CANDIDATE-AGGREGATE/1\n'+canonical({k:v for k,v in aggregate.items() if k!='aggregateDigest'}))
    assert not list(Draft202012Validator({'$schema':schema['$schema'],'$defs':schema['$defs'],'$ref':'#/$defs/CandidateAggregate'}).iter_errors(aggregate))
    first_only=copy.deepcopy(aggregate);first_only['members']=first_only['members'][:1]
    assert digest(b'ITER-CANDIDATE-AGGREGATE/1\n'+canonical({k:v for k,v in first_only.items() if k!='aggregateDigest'}))!=aggregate['aggregateDigest']
    frozen_package_members=copy.deepcopy(aggregate['members'])
    def complete_candidates(members):
        ids=[(m['candidateId'],m['candidateVersion']) for m in members]
        return len(set(ids))==len(ids) and sorted(canonical(m) for m in members)==sorted(canonical(m) for m in frozen_package_members)
    assert complete_candidates(aggregate['members']) and not complete_candidates(first_only['members'])
    wrong=copy.deepcopy(aggregate['members']);wrong[1]['candidateVersion']='different'
    assert not complete_candidates(wrong)
    checks.extend([{'case':name,'result':'PASS'} for name in ['aggregate exactly equals frozen package candidate set','missing candidate rejected','same candidate wrong version rejected']])
    examples.append({'id':'OA-EXACT-CANDIDATE-SET','schema':'CandidateAggregate','body':aggregate,'expected_valid':True})
    package_digest={'algorithm':'sha256','canonicalizationVersion':'DEMO-NATIVE/1','value':digest(b'DEMO native package')}
    oa_binding={'schema':'ITER-OA-APPLICATION/1','mappingVersion':'1.0.0','scope':scope,'ownerRegistrationRef':N('DEMO-plm-registration'),'roundBindingId':'00000000-0000-4000-8000-000000000009','roundId':aggregate['roundId'],'oaRoundKey':aggregate['roundId'],'iterationId':aggregate['iterationId'],'aggregateId':aggregate['aggregateId'],'aggregateVersion':aggregate['aggregateVersion'],'aggregateDigest':aggregate['aggregateDigest'],'originalReviewPackageRef':N('package'),'reviewPackageDigest':package_digest,'originalPolicyRef':N('policy'),'policyVersion':'1','policyDigest':package_digest,'technicalManifestDigest':None,'rejectDisposition':'RETURN','submissionId':'00000000-0000-4000-8000-000000000010','applicationUnit':'PLM_ITER_REVIEW_DECISION'}
    binding_schema=Draft202012Validator({'$schema':schema['$schema'],'$defs':schema['$defs'],'$ref':'#/$defs/IterOaBinding'})
    assert not list(binding_schema.iter_errors(oa_binding))
    oversized=copy.deepcopy(oa_binding);oversized['oaRoundKey']='X'*101
    assert list(binding_schema.iter_errors(oversized))
    snapshot=canonical({'schema':'ITER-OA-SNAPSHOT/1','aggregate':aggregate,'binding':oa_binding})
    assert len(snapshot)<256*1024 and digest(snapshot)!=aggregate['aggregateDigest']
    assert digest(snapshot)!=package_digest['value']
    checks.extend([{'case':name,'result':'PASS'} for name in ['complete OA binding shape','oversized Round wire key rejected','Subject snapshot digest distinct from aggregate/native package digest']])
    examples.append({'id':'OA-COMPLETE-BINDING','schema':'IterOaBinding','body':oa_binding,'expected_valid':True})
    examples.append({'id':'SPACE-MULTI-GRANT-VECTOR','schema':'SpaceVersionVector','body':version_vector,'expected_valid':True})
    examples.append({'id':'ENG-COMPLETE-BASELINE-BINDING','schema':'BaselineOutputBinding','body':baseline_binding,'expected_valid':True})
    for e in examples:e.update(role='NEW_DEMO_DESIGN_VECTOR',runtime_status='NOT_RUN')
    (folder/'examples.json').write_text(json.dumps({'examples':examples},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    report={'result':'PASS','scope':'sidecar meta-schema, 61 source field mappings and concrete DTO paths, permission algebra, output graph and candidate-set design examples only','schemas':len(schema['$defs']),'field_count':61,'dto_paths':paths,'policy_checks':checks,'examples':len(examples),'runtime_acceptance':'NOT_RUN','original_owner_manifest_bytes_modified':False}
    (folder/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');print(json.dumps({k:v for k,v in report.items() if k in ['result','field_count','dto_paths','examples']}))


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--handbook',type=Path,required=True);p.add_argument('--repo',type=Path,required=True);a=p.parse_args();main(a.handbook,a.repo)
