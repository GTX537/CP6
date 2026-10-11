"""Integrate explicit decisions, preserving every pinned historical finding and source."""
import argparse
import hashlib
import json
from collections import Counter
from pathlib import Path


def save(path,value):
    path.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


def main(book,allow_in_progress):
    folder=book/'remediation'
    register=json.loads((folder/'00-disposition-register.json').read_text(encoding='utf-8'))
    original={f:i for i in register['issues'] for f in i['source_finding_ids']}
    owners={};cases=[];outputs=[]
    for part in sorted((folder/'workparts').glob('*.json')):
        for row in json.loads(part.read_text(encoding='utf-8'))['findings']:
            key=original[row['source_finding_ids'][0]]['issue_id']
            assert key not in owners,('duplicate workpart',key)
            issue=original[row['source_finding_ids'][0]]
            assert set(issue['source_finding_ids'])==set(row['source_finding_ids'])
            old_evidence=issue['evidence']
            issue.update({k:v for k,v in row.items() if k not in ['acceptance','issue_id']})
            evidence={json.dumps(e,sort_keys=True):e for e in old_evidence+row['evidence']}
            issue['evidence']=list(evidence.values())
            issue['decision_workpart']=part.relative_to(book).as_posix()
            issue['outputs']=[('../../'+o if o.startswith('tools/') and not (book/o).exists() else o) for o in issue['outputs']]
            issue['supersedes_scope']=issue.get('supersedes_scope') or 'Only the explicitly named finding/consumer discrepancy. Original files, accepted domains and historical reviews retained.'
            issue['preserved_constraints']=issue.get('preserved_constraints') or ['Original evidence/accepted domain/authority retained','No business implementation or runtime acceptance in this task']
            issue['dependencies']=issue.get('dependencies') or ['DR-VERSION-AUTHORITY']+(['W2-COMPOSITE-TRANSACTION'] if issue['batch']=='W3' else [])
            issue['consumer_adoption']=issue.get('consumer_adoption') or {'required':'Register exact provider/consumer version, complete input mapping and every relevant final writer gate described by outputs','runtime_status':'UNPROVEN'}
            issue['acceptance_ids']=[]
            for case in row['acceptance']:
                id=key+'::'+case['id']
                assert id not in issue['acceptance_ids'],('duplicate acceptance',id)
                item={**case,'id':id,'original_acceptance_id':case['id'],'issue_id':key,'source_finding_ids':issue['source_finding_ids'],'consumer_targets':issue['targets']}
                item.setdefault('input',case['scenario'])
                item.setdefault('expected_writes','Exactly the typed contract write set; negative/replay cases create no unauthorized or duplicate business effect. See issue outputs for concrete records.')
                cases.append(item);issue['acceptance_ids'].append(id)
            owners[key]=part.name;outputs.extend(issue['outputs'])
    missing=[i['issue_id'] for i in register['issues'] if i['issue_id'] not in owners]
    if missing and not allow_in_progress:raise AssertionError(('unfinished issues',missing))
    # Specific sub-dispositions remain independently traceable without inflating original ID count.
    inq=original['COM-INQ-GENERATED-CONTRACT-ADOPTION']
    inq['sub_dispositions']=[{'id':inq['issue_id']+'/'+id,'classification':'ARTIFACT_CONFLICT','decision':decision,'outputs':inq['outputs'],'acceptance_ids':inq['acceptance_ids']} for id,decision in [
      ('OPENAPI-REFS','55 references rewritten to actual components definitions'),
      ('TS-EVENT','Boolean index-signature replaced by unknown; decoder remains strict'),
      ('CS-JSON-EQUALITY','Structural exact number/object equality for const/enum/uniqueItems; duplicate-key rejection retained'),
      ('UI-TERMINAL-NULL','Terminal protected expiry uses exact null-only wire'),
      ('PROOF-ETAG','Download proof and ETag use exact binding and original result'),
      ('LENGTH-PRECONDITIONS','Length negatives use valid idempotency keys so they reach intended validation'),
      ('ATTACHMENT-COMPILE','JsonObject/never annotation and seven empty arrays corrected without relaxing schemas')]]
    ncr=original['NCR-TYPE-01']; extra=Path('D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/93/93d5eabb725f756a__SOURCE-QA-SCHEMAS.json')
    e={'source_path':extra.as_posix(),'sha256':hashlib.sha256(extra.read_bytes()).hexdigest(),'json_pointer':'/$defs/ObservationInput','reading':'Additional direct source check: two const-null members found by isolated attachment compilation'}
    if e not in ncr['evidence']:ncr['evidence'].append(e)
    for issue in register['issues']:
        for evidence in issue['evidence']:
            if 'json_pointer' in evidence and Path(evidence['source_path']).suffix!='.json':
                evidence['code_member_locator']=evidence.pop('json_pointer')
                evidence['locator_interpretation']='Named code member from original finding, not an RFC6901 JSON pointer; source line range remains authoritative'
        issue['evidence']=list({json.dumps(e,sort_keys=True):e for e in issue['evidence']}.values())
    register.update(status='IN_PROGRESS' if missing else 'DESIGN_REVISION_COMPLETE_RUNTIME_NOT_RUN',issue_count=len(register['issues']),classification_counts=dict(sorted(Counter(i['classification'] for i in register['issues']).items())),completed_issue_families=len(owners),pending_issue_ids=missing)
    save(folder/'00-disposition-register.json',register)
    save(folder/'08-acceptance-catalog.json',{'revision':register['revision'],'scope':'Required design cases and actually performed static asset cases are separate; no business/runtime execution','case_count':len(cases),'cases':cases,'runtime_result':'NOT_RUN'})
    coverage=json.loads((folder/'10-target-coverage.json').read_text(encoding='utf-8'))
    valid_targets={t['target_id'] for t in coverage['targets']}
    for issue in register['issues']:
        assert set(issue['targets'])<=valid_targets,(issue['issue_id'],set(issue['targets'])-valid_targets)
    for target in coverage['targets']:
        issues=[i for i in register['issues'] if target['target_id'] in i['targets']]
        target['issue_ids']=[i['issue_id'] for i in issues]
        target['dispositions']=[{'issue_id':i['issue_id'],'classification':i['classification'],'design_status':i['design_status']} for i in issues]
        target['outputs']=sorted({o for i in issues for o in i['outputs']})
        target['coverage_status']='PENDING_REVISION' if any(i['issue_id'] in missing for i in issues) else 'DESIGN_DISPOSITION_RECORDED' if issues else 'EXISTING_READING_REUSED_NO_REGISTERED_CHANGE'
        target['inherited_reading_basis']=['indexes/required-inputs.json',target['source_module'],'indexes/module-navigation.json']
        target['coding_gate']='Bind exact native source + new supplement; actual provider/all-writer adoption and required runtime cases remain UNPROVEN/NOT_RUN' if issues else 'Reuse existing required-materials reading; this task does not claim a fresh full original reread or runtime pass'
        target['historical_missing_evidence']='R01071_UNRECOVERABLE_RETRIEVAL_CLOSED' if target['target_id']=='WMS-DEL-01' else 'Preserved in original missing-source-register'
    coverage['summary']={'targets':len(coverage['targets']),'affected':sum(bool(t['issue_ids']) for t in coverage['targets']),'unaffected_reading_reused':sum(not t['issue_ids'] for t in coverage['targets']),'new_full_reread_claim':False,'runtime_result':'NOT_RUN'}
    save(folder/'10-target-coverage.json',coverage)
    print(json.dumps({'issues_integrated':len(owners),'source_findings':sum(len(i['source_finding_ids']) for i in register['issues'] if i['issue_id'] in owners),'cases':len(cases),'pending':missing,'targets':coverage['summary']},ensure_ascii=False))


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--handbook',type=Path,required=True);p.add_argument('--allow-in-progress',action='store_true');a=p.parse_args();main(a.handbook,a.allow_in_progress)
