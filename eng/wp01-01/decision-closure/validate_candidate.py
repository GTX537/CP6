"""Local task-private verification; writes only private evidence and manifest.

No production manager import, mutation, DB, network or remote verification.
"""
import argparse
import datetime
import hashlib
import importlib.util
import json
from pathlib import Path
import platform
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'docs/development/evidence/WP01-01/decision-closure'
CODE=ROOT/'eng/wp01-01/decision-closure'
BASE='99fc0ce383d2d02c1404a8adbb3abe971b41d332'
spec=importlib.util.spec_from_file_location('gate_candidate',CODE/'gate_candidate.py')
gate=importlib.util.module_from_spec(spec);spec.loader.exec_module(gate)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def ref(path):
    data=path.read_bytes()
    return {'path':path.relative_to(ROOT).as_posix(),'sha256':sha(data),'bytes':len(data),'representation':'RAW_FILE_BYTES'}


def save(name,data):
    (OUT/name).write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')


def git(root,*args):
    return subprocess.run(['git','-c','core.longpaths=true',*args],cwd=root,capture_output=True,check=True).stdout


def protected():
    files=[ROOT/p for p in ['docs/development/roadmap.json','docs/development/task-definitions.json','docs/development/tools/manage_roadmap.py',
        'docs/project-memory/PROJECT_STATE.md','docs/project-memory/05-Completed.md','docs/project-memory/06-Todo.md','docs/project-memory/CHANGELOG-AI.md']]
    for folder in ['eng/wp01-01/preparation','docs/development/evidence/WP01-01/preparation','docs/development/task-deliveries/WP05-01','.github/workflows']:
        files.extend(p for p in (ROOT/folder).rglob('*') if p.is_file())
    return {p.relative_to(ROOT).as_posix():sha(p.read_bytes()) for p in files if p.is_file()}


def run(name,args,inputs):
    start=datetime.datetime.now(datetime.timezone.utc).isoformat()
    logname=name+'-'+datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    process=subprocess.run(args,cwd=ROOT,capture_output=True)
    for suffix,data in [('.stdout.txt',process.stdout),('.stderr.txt',process.stderr)]:
        with (OUT/(logname+suffix)).open('xb') as stream:stream.write(data)
    record={'command':args,'started_at':start,'finished_at':datetime.datetime.now(datetime.timezone.utc).isoformat(),
      'exit_code':process.returncode,'source_hashes':inputs,'stdout':ref(OUT/(logname+'.stdout.txt')),'stderr':ref(OUT/(logname+'.stderr.txt')),
      'scope':'PRIVATE_LOCAL_STATIC_OR_SYNTHETIC_ONLY'}
    save(logname+'.run.json',record)
    save(name+'.run.json',record)  # current-result pointer; immutable logname record is retained
    return record


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--platform-repo',required=True,type=Path)
    parser.add_argument('--crm-repo',required=True,type=Path)
    parser.add_argument('--archive-root',required=True,type=Path)
    args=parser.parse_args()
    repos={'Main':ROOT,'Platform':args.platform_repo,'CRM':args.crm_repo,'Archive':args.archive_root}
    before=protected(); errors=[];checks=[];cache={};parsed={}
    def check(condition,message):
        checks.append(message)
        if not condition:errors.append(message)
    def verify_pin(item):
        if not isinstance(item,dict):return
        if all(k in item for k in ('path','sha256','bytes')):
            key=(item.get('repository','Main'),item.get('revision'),item['path'],item.get('representation','RAW_FILE_BYTES'))
            if key not in cache:
                if key[3]=='GIT_BLOB_BYTES':
                    cache[key]=git(repos[key[0]],'show',key[1]+':'+key[2])
                else:
                    path=(repos[key[0]]/key[2]).resolve()
                    check(path.is_relative_to(repos[key[0]].resolve()),'contained source '+key[2])
                    cache[key]=path.read_bytes()
            data=cache[key]
            check(sha(data)==item['sha256'] and len(data)==item['bytes'],'source bytes '+key[2])
            if item.get('git_blob_sha256'):
                gkey=(key[0],key[1],key[2],'GIT_BLOB_BYTES')
                if gkey not in cache:cache[gkey]=git(repos[key[0]],'show',key[1]+':'+key[2])
                check(sha(cache[gkey])==item['git_blob_sha256'],'fixed Git source '+key[2])
            if 'excerpt' in item and 'line_start' in item:
                lines=data.decode('utf-8-sig').splitlines()
                check('\n'.join(lines[item['line_start']-1:item.get('line_end',item['line_start'])])==item['excerpt'],'exact source anchor '+key[2])
            if 'json_pointer' in item:
                if key not in parsed:parsed[key]=gate.strict_json(data)
                value=parsed[key]
                for part in item['json_pointer'].strip('/').split('/'):
                    part=part.replace('~1','/').replace('~0','~')
                    value=value[int(part)] if isinstance(value,list) else value[part]
        for value in item.values():
            if isinstance(value,dict):verify_pin(value)
            elif isinstance(value,list):
                for nested in value:
                    if isinstance(nested,dict):verify_pin(nested)
    names=['decisions.json','postgresql-mapping.json','current-source-audit.json','provider-consumer-closure.json','basis-delta.json','gate-contract.candidate.json','digest-vectors.candidate.json']
    docs={name:gate.strict_json((OUT/name).read_bytes()) for name in names}
    for name,doc in docs.items():verify_pin(doc)
    tables={e['entity']:e for e in docs['postgresql-mapping.json']['entities']}
    for name,table in tables.items():
        cols={c['name']:c for c in table['columns']}
        check(len(cols)==len(table['columns']),'unique physical column declarations '+name)
        keys=[table['primary_key']['columns']]+[u['columns'] for u in table['unique_constraints'] if not u.get('predicate')]
        for kind in ['primary_key','cas']:
            used=table[kind].get('columns',table[kind].get('predicate_columns',[]))
            check(set(used)<=set(cols),'declared '+kind+' columns '+name)
        check(all(not cols[c]['nullable'] for c in table['primary_key']['columns']),'PK nonnull '+name)
        check(cols['tenant_id']['postgresql']=='uuid' and not cols['tenant_id']['nullable'],'explicit TenantId '+name)
        for item in table['unique_constraints']+table['indexes']+table['checks']:
            check(set(item['columns']+item.get('predicate_columns',[]))<=set(cols),'declared constraint columns '+name+'/'+item['name'])
        for field in table.get('fields',[]):
            check(set(field['physical_columns'])<=set(cols),'field physical storage '+name+'/'+field['field'])
        for foreign in table['foreign_keys']:
            check(set(foreign['columns'])<=set(cols),'local FK columns '+name)
            target=tables[foreign['target_entity']];targetcols={c['name']:c for c in target['columns']}
            targetkeys=[target['primary_key']['columns']]+[u['columns'] for u in target['unique_constraints'] if not u.get('predicate')]
            check(foreign['target_columns'] in targetkeys,'FK exact target PK/UQ '+name+'->'+target['entity'])
            check(len(foreign['columns'])==len(foreign['target_columns']) and all(cols[a]['postgresql']==targetcols[b]['postgresql'] for a,b in zip(foreign['columns'],foreign['target_columns'])),'FK physical types '+name)
            check('tenant_id' in foreign['columns'] and 'tenant_id' in foreign['target_columns'],'FK retains TenantId '+name)
    expected_unique={'Project':['tenant_id','project_no_key'],'Iteration':['tenant_id','iteration_no_key'],
        'ReviewRound':['tenant_id','iteration_storage_id','round_no_key'],
        'ExternalRequestReceipt':['tenant_id','operation_type_key','aggregate_source_scope_key','request_key_id'],
        'ReleaseRecord':['tenant_id','release_identity_key','release_revision_key'],
        'ReleaseTarget':['tenant_id','release_storage_id','target_identity_key','target_scope_version_key']}
    for name,cols in expected_unique.items():check(any(u['columns']==cols for u in tables[name]['unique_constraints']),'exact inherited unique domain '+name)
    check(any(f['columns']==['tenant_id','storage_id','current_evaluation_storage_id'] and f['target_entity']=='Evaluation' and f['target_columns']==['tenant_id','iteration_storage_id','storage_id'] for f in tables['Iteration']['foreign_keys']),'current Evaluation exact iteration FK')
    check(not any(u['columns']==['tenant_id','review_round_storage_id'] for u in tables['Evaluation']['unique_constraints']),'optional review round not falsely unique evaluation domain')
    decision=docs['decisions.json'];check(len(decision['decisions'])==8,'all eight DP dispositions')
    allowed={'INHERITED_ACCEPTED_RULE','NEW_CANDIDATE_CHOICE','ACTUAL_CONFIGURATION_PENDING','PROVIDER_IMPLEMENTATION_MISSING','RUNTIME_UNVERIFIED'}
    check(all(c['category'] in allowed and c['source_pins'] for d in decision['decisions'] for c in d['clauses']),'clause category and source complete')
    matrix=gate.strict_json((ROOT/'docs/development/evidence/WP01-01/preparation/obligation-matrix.json').read_bytes())
    check(len(matrix['rows'])==577,'577 unchanged navigation rows')
    check(sha((ROOT/'docs/development/evidence/WP01-01/preparation/obligation-matrix.json').read_bytes())=='0f6e160d49b9fb0305c94f9a3ccbfc71ebae312a26bd1613fd38ce4043b57063','immutable matrix hash')
    contract=docs['gate-contract.candidate.json'];delta=docs['basis-delta.json'];graph=docs['provider-consumer-closure.json']
    check(delta['removed_obligations']==[] and delta['additional_obligations']==contract['additional_obligations'],'exact delta and no dropped obligation')
    check(len(graph['nodes'])==214 and len(graph['edges'])==1256 and graph['cross_repository_edge_count']==17,'fixed declared DAG counts')
    roadmap=gate.strict_json((ROOT/'docs/development/roadmap.json').read_bytes())
    definitions=gate.strict_json((ROOT/'docs/development/task-definitions.json').read_bytes())
    tasks={t['id']:t for t in roadmap['implementation_tasks']};tasks.update({t['id']:t for t in definitions['tasks']})
    expected={(n['id'],p) for n in graph['nodes'] for p in tasks[n['id']]['dependencies']}
    check(expected=={(e['consumer_task'],e['provider_task']) for e in graph['edges']},'exact declared provider edge set')
    for e in graph['edges']:
        expected_binding=[b for b in tasks[e['consumer_task']].get('prerequisite_bindings',[]) if e['provider_task'] in b.get('task_ids',[])]
        check(e['bindings']==expected_binding,'exact prerequisite binding '+e['id'])
        check(e['provider_repository']==tasks[e['provider_task']]['repository'] and e['consumer_repository']==tasks[e['consumer_task']]['repository'],'repository ownership '+e['id'])
    for target in contract['target_whitelist']:
        check(set(contract['target_edges'][target])=={e['id'] for e in graph['edges'] if target in e['targets']},'target exact edge set '+target)
    for vector in docs['digest-vectors.candidate.json']['vectors']:
        check(sha(bytes.fromhex(vector['preimage_hex']))==vector['sha256'] and vector['synthetic'] is True,'synthetic digest vector integrity')
    contractref=ref(OUT/'gate-contract.candidate.json');save('contract-ref.json',contractref)
    actual=gate.evaluate_candidate(ROOT,contractref,gate.strict_json((OUT/'submission.actual-unadopted.json').read_bytes()),archive_root=args.archive_root)
    save('actual-baseline-result.json',actual)
    check(actual['candidate_valid'] is False and actual['actual_adoption'] is False and actual['actual_completion'] is False and actual['production_enabled'] is False,'actual unadopted baseline rejects')
    # Independently verify all inherited and new obligation locators, for both targets.
    source_inventory=gate.read_ref(ROOT,contract['inherited_source_inventory'],as_json=True)['inputs'];source_cache={}
    for row in matrix['rows']+contract['additional_obligations']:
        refs=list(row.get('source_refs',[]))
        if row.get('owner_authority'):
            a=row['owner_authority'];refs.append({'path':a['source_path'],'sha256':a['source_sha256'],'line_start':a['source_line'],'excerpt':a['observation']})
        check(bool(refs),'obligation original sources present '+row['id'])
        for source in refs:
            gate.read_source(ROOT,source,source_inventory,source_cache,args.archive_root)
            check(True,'obligation original hash and locator '+row['id'])
    testinputs={p.relative_to(ROOT).as_posix():sha(p.read_bytes()) for p in [CODE/'gate_candidate.py',*sorted((CODE/'tests').glob('*.py'))]}
    testrecord=OUT/'tests-final.run.json'
    if testrecord.exists() and gate.strict_json(testrecord.read_bytes())['source_hashes']==testinputs:
        tests=gate.strict_json(testrecord.read_bytes());tests=dict(tests,reuse='Exact code/test hashes unchanged; not rerun')
    else:tests=run('tests-final',[sys.executable,'-B','-X','utf8','-m','unittest','discover','-s','eng/wp01-01/decision-closure/tests','-v'],testinputs)
    check(tests['exit_code']==0,'private tests pass')
    roadmap_inputs={k:v for k,v in before.items() if k in {'docs/development/roadmap.json','docs/development/task-definitions.json','docs/development/tools/manage_roadmap.py'}}
    if (OUT/'manage-roadmap.run.json').exists():
        manager=gate.strict_json((OUT/'manage-roadmap.run.json').read_bytes())
        check(manager['source_hashes']==roadmap_inputs,'read-only manager input unchanged; do not rerun automatically')
        manager=dict(manager,reuse='Single actual read-only run; reused while protected inputs unchanged')
    else:manager=run('manage-roadmap',[sys.executable,'-B','-X','utf8','docs/development/tools/manage_roadmap.py','--root','.'],roadmap_inputs)
    check(manager['exit_code']==0,'read-only roadmap validation')
    check(before==protected(),'all protected inputs unchanged')
    changed=git(ROOT,'diff','--name-only',BASE).decode().splitlines()
    check(all(p.startswith(('eng/wp01-01/decision-closure/','docs/development/evidence/WP01-01/decision-closure/')) for p in changed),'tracked diff within authorized task paths')
    wave6=gate.strict_json((ROOT/'docs/development/evidence/WP01-01/preparation/validation.json').read_bytes())
    inherited={'scope':'REUSED_NOT_RERUN','counts':{'static_checks':3509,'comparator_tests':23,'bundle_tests':10},
       'validation':ref(ROOT/'docs/development/evidence/WP01-01/preparation/validation.json'),
       'input_lock':ref(ROOT/'docs/development/evidence/WP01-01/preparation/input-lock.json'),
       'source_revision':wave6['source_revision'],'original_commands':wave6['commands'],'boundary':'Original source time/input hashes only. Not current runtime proof.'}
    result={'schema':'WP01-01-PRIVATE-VALIDATION/1','result':'FAIL' if errors else 'PASS','checked_at':datetime.datetime.now(datetime.timezone.utc).isoformat(),
      'source_revision':BASE,'python':sys.version,'executable':sys.executable,'platform':platform.platform(),
      'checks':len(checks),'errors':errors,'commands':[tests,manager],'inherited_validation':inherited,
      'protected_input_hashes_before':before,'protected_input_hashes_after':protected(),'protected_inputs_unchanged':before==protected(),
      'actual_baseline':ref(OUT/'actual-baseline-result.json'),'production_enabled':False,'actual_adoption':False,'actual_completion':False,
      'earlier_runs':{'tdd-red':{'exit_code':1,'reason':'29 tests asserted adapter missing before implementation'},'tests-first':{'exit_code':0,'tests':29},
       'tdd-bindings-red':{'exit_code':1,'reason':'strict JSON/file binding/source assertion negatives failed before implementation'},'tests-bindings':{'exit_code':0,'tests':39},
       'tdd-deep-red':{'exit_code':1,'record':'tdd-deep-red.run.json'},'build':{'exit_code':1,'reason':'Platform fixed revision file absent in current checkout; corrected to git blob read'},
       'initial_unredirected_build':{'exit_code':1,'reason':'S01 definitions reside in roadmap, fixed task union; traceback remains in tool transcript, no fabricated raw file'},
       'initial_source_snapshot_limit':'First two red phases pre-change source hash not captured; raw logs preserved, exact subsequent phase source hashes recorded. Do not claim a missing historical snapshot.'},
      'targeted_spec_repair':{'source_red_runs':[ref(p) for p in sorted(OUT.glob('spec-source-red-*.run.json'))],
        'static_failure_logs':[ref(p) for p in sorted(OUT.glob('spec-fix-validation-*.stdout.txt'))],
        'scope':'Only concrete PG column/constraint completeness and per-obligation original source binding; gate runtime/production remains disabled',
        'source_materialization':'Original archives explicitly read via supplied --archive-root; no original files copied or edited; absent originals reject separately from permanent legacy30/48 gaps'},
      'not_run':['product compilation/tests','real PostgreSQL/DDL/migration/roles/RLS/precision/CAS/recovery','provider/consumer runtime','load/SLO','authority authenticity','remote ancestry','Actions','deployment']}
    save('validation.json',result)
    excludes={'manifest.json','intake.json'}
    files=[p for folder in [OUT,CODE] for p in folder.rglob('*') if p.is_file() and p.name not in excludes and not p.name.startswith(('review','publication','final-')) and '__pycache__' not in p.parts]
    manifest={'schema':'WP01-01-CORE-MANIFEST/1','identity':'WP01-01-DECISION-CLOSURE/20261014-R1','basis_version':contract['version'],
      'status':'PROPOSED_NOT_ADOPTED','source_revision':BASE,'artifacts':[ref(p) for p in sorted(files)],
      'inherited_immutable_pins':[ref(ROOT/'docs/development/task-deliveries/WP05-01/basis-manifest.json'),ref(ROOT/'docs/development/task-deliveries/WP05-01/supplements/review-r1/manifest.json'),ref(ROOT/'docs/development/evidence/WP01-01/preparation/preparation-manifest.json'),ref(ROOT/'docs/development/evidence/WP01-01/preparation/obligation-matrix.json')],
      'exclusions':['manifest.json (no self hash)','parent intake.json, review*, publication*, final-*','future parent coordination metadata not automatically core'],
      'production_enabled':False,'actual_adoption':False,'actual_completion':False}
    save('manifest.json',manifest)
    print(json.dumps({'result':result['result'],'checks':len(checks),'errors':errors,'manifest_sha256':sha((OUT/'manifest.json').read_bytes())}))
    return bool(errors)


if __name__=='__main__':
    raise SystemExit(main())
