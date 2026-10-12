import argparse, datetime, hashlib, json, pathlib, re, subprocess, urllib.parse

parser = argparse.ArgumentParser(description='Verify the fixed S01 planning snapshot; local archive inputs are required.')
parser.add_argument('--root', type=pathlib.Path, default=pathlib.Path(__file__).resolve().parents[4])
parser.add_argument('--out', type=pathlib.Path, required=True)
args = parser.parse_args()
root = args.root.resolve()
out = args.out.resolve()
out.mkdir(parents=True, exist_ok=True)
base = '2df45097c8ba18728d30bc85f12cc874921c25e4'
errors = []
def git(*args):
    return subprocess.check_output(['git','-c','core.longpaths=true',*args],cwd=root,encoding='utf-8').strip()
def read(path):
    return json.loads((root/path).read_text(encoding='utf-8-sig'))
ledger = read('docs/development/roadmap.json')
before = json.loads(git('show',base+':docs/development/roadmap.json'))
for key in ['targets','revision_acceptances','sources','baseline_totals','implementation_assessment']:
    if ledger[key] != before[key]: errors.append('historical or business ledger changed: '+key)
if len(ledger['implementation_tasks']) != 13 or any(t['state'] != 'PLANNED' for t in ledger['implementation_tasks']):
    errors.append('unexpected initial task state/count')
source_index = read('docs/development/s01-input-index.json')
manifest = read('docs/design-handbook/remediation/assets/composed-derivatives.json')
checks = []
for entry in source_index['sources']:
    if entry not in manifest['sources']: errors.append('source selector drift: '+entry['source_path'])
    for path_key, hash_key in [('source_path','source_sha256'),('local_derived_path','derived_sha256')]:
        p=pathlib.Path(entry[path_key])
        if not p.is_file(): errors.append('missing selected file: '+str(p)); continue
        actual=hashlib.sha256(p.read_bytes()).hexdigest()
        if actual != entry[hash_key]: errors.append('selected file SHA mismatch: '+str(p))
        checks.append({'path':str(p),'sha256':actual,'result':'PASS' if actual==entry[hash_key] else 'FAIL'})
for entry in source_index['local_asset_pins']:
    actual=hashlib.sha256((root/entry['path']).read_bytes()).hexdigest()
    if actual != entry['sha256']: errors.append('local asset SHA mismatch: '+entry['path'])
cases={c['id'] for c in read('docs/design-handbook/remediation/08-acceptance-catalog.json')['cases'] if c['verification_level'].startswith('REQUIRED_RUNTIME')}
if set(source_index['runtime_case_ids']) - cases: errors.append('unknown runtime case')
changed = set(git('diff','--name-only',base).splitlines()) | set(git('ls-files','--others','--exclude-standard').splitlines())
links=0; checked_docs=[]
for path in sorted(changed):
    if not path.endswith('.md'): continue
    p=root/path
    text=p.read_text(encoding='utf-8-sig')
    # Only new memory sections belong to this task; legacy links retain prior evidence.
    if path.startswith('docs/project-memory/'):
        end=text.find('\n## ',text.find('\n## ')+1)
        if end!=-1:text=text[:end]
    text=re.sub(r'```.*?```','',text,flags=re.S)
    for match in re.finditer(r'\[[^\]]+\]\(([^)]+)\)',text):
        dest=match.group(1).strip('<>')
        if dest.startswith(('https://','http://','#','mailto:')): continue
        dest=urllib.parse.unquote(dest.split('#')[0])
        dest=re.sub(r':\d+$','',dest)
        target=(p.parent/dest).resolve()
        if not target.exists(): errors.append(path+': missing link '+dest)
        links+=1
    checked_docs.append(path)
plan=(root/'docs/superpowers/plans/2026-10-11-s01-task-breakdown.md').read_text(encoding='utf-8')
for task in ledger['implementation_tasks']:
    if not re.search(r'^## '+re.escape(task['id'])+'：',plan,re.M): errors.append('missing task card '+task['id'])
subprocess.run(['git','-c','core.longpaths=true','diff','--check'],cwd=root,check=True)
report={'at':datetime.datetime.now(datetime.timezone.utc).isoformat(),'baseline_sha':base,'result':'FAIL' if errors else 'PASS','scope':'Task inventory/cards, unchanged target/case/source state, selected original/derived SHA checks, changed Markdown links and diff whitespace. Does not execute business or database tests.','task_count':len(ledger['implementation_tasks']),'selected_composed_sources':len(source_index['sources']),'physical_hash_checks':checks,'selected_runtime_case_ids':len(source_index['runtime_case_ids']),'local_asset_pins':source_index['local_asset_pins'],'local_markdown_links':links,'checked_docs':checked_docs,'reused_inventory_evidence':'docs/development/assessments/20261011/evidence/inventory-validation.json; source catalogs and all existing target/case definitions unchanged, no repeat of full historical audit.','business_tests_executed':False,'errors':errors}
(out/'plan-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k not in ['physical_hash_checks','local_asset_pins','checked_docs']},ensure_ascii=False))
raise SystemExit(bool(errors))
