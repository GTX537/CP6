"""Read-only CP6-family worktree/copy inventory. Never fetches, removes or prunes.

Python 3.10+ and Git; Windows process inspection uses PowerShell. Explicit
--output writes JSON and Markdown reports only. Missing/error evidence is retained.
"""
import argparse, collections, concurrent.futures, datetime, json, os, re, shutil, subprocess, sys
from pathlib import Path

ALLOWED = {'cp6': 'CP6', 'cp6.crm': 'CP6.CRM', 'cp6.platform': 'CP6.Platform', 'cp6.ai-handoff': 'CP6.AI-Handoff'}
DEFAULT_ROOTS = ['D:/CP6', 'D:/CP6.CRM', 'D:/CP6/CP6.Platform', 'D:/CP6.AI-Handoff']
DEFAULT_CONTAINERS = ['D:/CP6-worktrees', 'D:/CP6.CRM-worktrees', 'D:/CP6.Platform-worktrees', 'D:/CP6.AI-Handoff-worktrees', 'D:/CP6/CP6.CRM', 'D:/CP6/tmp/clones', 'D:/CP6/tmp/worktrees', 'D:/CP6/.claude/worktrees', str(Path.home()/'.codex/worktrees')]

def norm(p):
    value=os.path.normcase(os.path.abspath(p)).replace('\\', '/').casefold()
    return value[4:] if value.startswith('//?/') else value
def fs_path(p):
    value=os.path.abspath(p)
    return '\\\\?\\'+value if os.name=='nt' and not value.startswith('\\\\?\\') else value
def git(p, *args, check=True):
    env = dict(os.environ, GIT_OPTIONAL_LOCKS='0')
    r = subprocess.run(['git', '-c', 'core.longpaths=true', '-c', 'core.quotepath=false', '-c', 'http.version=HTTP/1.1', '-c', 'http.proxy=', '-C', str(p), *args], capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=180, env=env)
    if check and r.returncode: raise RuntimeError(r.stderr.strip()[:600])
    return r.stdout.rstrip('\r\n') if check else r
def repository(url):
    m = re.fullmatch(r'(?:https://github\.com/|git@github\.com:|ssh://git@github\.com/)GTX537/([^/]+?)(?:\.git)?/?', url, re.I)
    return ALLOWED.get(m.group(1).lower()) if m else None
def registered(root):
    result=[]
    for block in git(root, 'worktree', 'list', '--porcelain').split('\n\n'):
        item=dict(line.partition(' ')[::2] for line in block.splitlines())
        if 'worktree' in item: result.append(item)
    return result
def find_copies(container, depth=3):
    if not container.exists() or container.is_symlink() or getattr(container, 'is_junction', lambda:False)(): return []
    if (container/'.git').exists(): return [container]
    if depth==0:return []
    result=[]
    for p in container.iterdir():
        if p.is_dir() and not p.is_symlink() and not getattr(p,'is_junction',lambda:False)(): result.extend(find_copies(p,depth-1))
    return result
def discover(roots, containers):
    seeds=[Path(p) for p in roots]; exclusions=[]; commons={}; records={}
    for p in containers:
        try: seeds.extend(find_copies(Path(p)))
        except OSError as e: exclusions.append({'path':p,'error':str(e)})
    # Direct CP6-prefixed clones are only admitted after checking their exact remote.
    if Path('D:/').exists():seeds.extend(p for p in Path('D:/').iterdir() if p.is_dir() and p.name.casefold().startswith('cp6') and (p/'.git').exists())
    for p in seeds:
        if not p.exists():continue
        try:
            origin=git(p,'remote','get-url','origin');repo=repository(origin)
            if not repo: exclusions.append({'path':str(p),'reason':'remote not allowlisted'});continue
            common=git(p,'rev-parse','--path-format=absolute','--git-common-dir')
            if norm(common) in commons:continue
            trees=registered(p);commons[norm(common)]={'repository':repo,'origin':origin,'common_dir':common,'root':str(p),'primary':trees[0]['worktree']}
            for t in trees:records[norm(t['worktree'])]={**t,**commons[norm(common)],'path':t['worktree'],'is_primary':norm(t['worktree'])==norm(trees[0]['worktree'])}
        except (OSError,RuntimeError,subprocess.TimeoutExpired) as e:exclusions.append({'path':str(p),'error':str(e)})
    return list(records.values()),commons,exclusions
def size_scan(root, allroots):
    total=files=0; groups=collections.Counter();errors=[];links=[];nested=[];stack=[(fs_path(root),'other')]
    while stack:
        current,category=stack.pop()
        try:
            with os.scandir(current) as entries:
                for e in entries:
                    try:
                        st=e.stat(follow_symlinks=False)
                        if getattr(st,'st_file_attributes',0)&0x400 or e.is_symlink():links.append(e.path);continue
                        if e.is_dir(follow_symlinks=False):
                            if norm(e.path) in allroots:nested.append(e.path);continue
                            cat=e.name.lower() if category=='other' and e.name.lower() in ['docs','bin','obj','node_modules','.git'] else category
                            stack.append((e.path,cat))
                        elif e.is_file(follow_symlinks=False):total+=st.st_size;files+=1;groups[category]+=st.st_size
                    except OSError as e2:errors.append(str(e2))
        except OSError as e:errors.append(str(e))
    return dict(logical_bytes=total,file_count=files,categories=dict(groups),scan_errors=errors,reparse_points=links,nested_roots=nested)

def unregistered_candidates(containers, records):
    """Report physical leftovers without assuming repository ownership or deletion safety."""
    known={norm(r['path']) for r in records};result=[];seen=set()
    for container in containers:
        c=Path(container)
        if not c.is_dir() or c.is_symlink() or getattr(c,'is_junction',lambda:False)():continue
        try:
            children=list(c.iterdir())
        except OSError as e:
            result.append(dict(path=str(c),classification='unregistered-scan-error',error=str(e)));continue
        for p in children:
            key=norm(p)
            if not p.is_dir() or key in seen or any(k==key or k.startswith(key+'/') for k in known):continue
            seen.add(key)
            if p.is_symlink() or getattr(p,'is_junction',lambda:False)():
                result.append(dict(path=str(p),classification='unregistered-link',repository=None));continue
            try:
                # A Git copy, including an excluded repository, is handled by origin discovery.
                if find_copies(p):continue
                result.append(dict(path=str(p),classification='unregistered-needs-provenance',repository=None,**size_scan(p,known)))
            except OSError as e:
                result.append(dict(path=str(p),classification='unregistered-scan-error',error=str(e)))
    return result
def processes():
    if os.name!='nt':return [],'not available on this platform'
    r=subprocess.run(['powershell','-NoProfile','-Command','Get-CimInstance Win32_Process | Select-Object ProcessId,Name,CommandLine,ExecutablePath | ConvertTo-Json -Compress'],capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=60)
    if r.returncode:return [],'process query failed'
    return json.loads(r.stdout or '[]'),None
def inspect(record, allroots, refs, process_list, active_paths):
    r=dict(record);r['registered_head']=r.pop('HEAD',None);p=Path(r['path']);r['errors']=[]
    if not p.exists():return dict(r,classification='missing-registration',reason='directory absent; prune is metadata-only',logical_bytes=0)
    r.update(size_scan(p,allroots))
    try:
        assert repository(git(p,'remote','get-url','origin'))==r['repository']
        r['head']=git(p,'rev-parse','HEAD');r['branch']=git(p,'branch','--show-current') or '(detached)'
        r['task_hint']=git(p,'log','-1','--format=%s')
        r['status']=git(p,'status','--porcelain=v1','-z','--untracked-files=all').split('\0');r['status']=[x for x in r['status'] if x]
        r['untracked']=git(p,'ls-files','--others','--exclude-standard','--directory','-z').split('\0');r['untracked']=[x for x in r['untracked'] if x]
        r['ignored']=git(p,'ls-files','--others','--ignored','--exclude-standard','--directory','-z').split('\0');r['ignored']=[x for x in r['ignored'] if x]
        r['local_only_count_vs_tracking_refs']=int(git(p,'rev-list','--count','HEAD','--not','--remotes'))
        r['local_only_commits_vs_tracking_refs']=git(p,'log','--format=%H %s','HEAD','--not','--remotes').splitlines()
        r['main_ahead_count']=git(p,'rev-list','--count','origin/main..HEAD',check=False).stdout.strip() or None
        remote=refs[norm(r['common_dir'])];r['remote_probe_error']=remote.get('error');r['remote_saved_refs']=[]
        # Only test commits advertised by the actual server, never infer from stale refs.
        containing={line.split('\t')[0].replace('refs/remotes/origin/','refs/heads/',1):line.split('\t')[1] for line in git(p,'for-each-ref','--contains='+r['head'],'--format=%(refname)%09%(objectname)','refs/remotes/origin').splitlines()}
        for ref,sha in remote.get('heads',{}).items():
            if sha==r['head'] or containing.get(ref)==sha:r['remote_saved_refs'].append(ref)
        r['saved_in_remote_main']='refs/heads/main' in r['remote_saved_refs']
        r['unique_to_current_remote_heads_count']=None
        known=remote.get('known_heads',[])
        if known:r['unique_to_current_remote_heads_count']=int(git(p,'rev-list','--count',r['head'],'--not',*known))
        r['agents_files']=[str(f) for f in [p/'AGENTS.md',*(parent/'AGENTS.md' for parent in p.parents)] if f.is_file()]
    except (RuntimeError,OSError,AssertionError,subprocess.TimeoutExpired) as e:r['errors'].append(str(e))
    needle=norm(p)
    r['process_refs']=[{'pid':x['ProcessId'],'name':x['Name']} for x in process_list if needle in str(x.get('CommandLine') or '').replace('\\','/').casefold() or needle in str(x.get('ExecutablePath') or '').replace('\\','/').casefold()]
    if r['is_primary']:r.update(classification='primary-retain',reason='primary checkout; never remove')
    elif norm(p) in active_paths or r['process_refs']:r.update(classification='in-use',reason='explicit retained/active path or process reference')
    elif r['errors'] or r['scan_errors'] or r.get('remote_probe_error'):r.update(classification='manual-review',reason='inspection or remote verification incomplete')
    elif r.get('status') or r.get('ignored') or r['reparse_points'] or r['nested_roots'] or 'locked' in r:r.update(classification='manual-review',reason='local files/links/lock/nested repository require preservation review')
    elif r.get('remote_saved_refs'):r.update(classification='saved-awaiting-task-review',reason='Git contents remotely recoverable; task completion/ownership still must be checked')
    else:r.update(classification='manual-review',reason='HEAD not proven saved on current remote branch')
    return r
def markdown(data):
    text=['# CP6 工作树只读盘点', '',data['observed_at'],'',data['measurement'],'', '| 仓库 | 路径 | GiB | 分支 / HEAD | 未提交 / 忽略 | 独有提交* | 状态 | 任务线索 |','|---|---|---:|---|---:|---:|---|---|']
    for r in data['worktrees']:
        clean=lambda s:str(s).replace('|','/').replace('\n',' ')
        text.append('| '+' | '.join(map(clean,[r['repository'],r['path'],round(r['logical_bytes']/2**30,3),r.get('branch','')+' / '+r.get('head',r.get('HEAD',''))[:12],str(len(r.get('status',[])))+' / '+str(len(r.get('ignored',[]))),r.get('unique_to_current_remote_heads_count','?'),r['classification'],r.get('task_hint','')]))+' |')
    text+=['','*独有提交是相对于当前服务器已广告且本地有对象的分支；远端对象缺失时可能高估。详见 JSON 中完整状态、忽略目录、远端保存引用、任务线索、进程引用和错误。保存到远端不自动代表任务结束。没有任何自动删除。','']
    if data.get('unregistered_candidates'):
        text+=['## 未登记实体目录（归属待核实）','','这些目录没有可直接核对的 Git origin，未计入已核实仓库容量；需核对历史来源、独有文件和任务后处置，不能因缺少登记而忽略或自动删除。','','| 路径 | 逻辑 GiB | 状态 |','|---|---:|---|']
        for r in data['unregistered_candidates']:
            text.append('| '+r['path'].replace('|','/')+' | '+str(round(r.get('logical_bytes',0)/2**30,3))+' | '+r['classification']+' |')
        text.append('')
    return '\n'.join(text)
def main():
    ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--root',action='append');ap.add_argument('--container',action='append');ap.add_argument('--active',action='append',default=[]);ap.add_argument('--output',type=Path);ap.add_argument('--workers',type=int,default=4);ap.add_argument('--registry',type=Path);args=ap.parse_args()
    registry_path=args.registry or Path(__file__).resolve().parents[2]/'docs/development/worktrees/registry.json'
    registry=json.loads(registry_path.read_bytes()) if registry_path.is_file() else {}
    retained=[s['path'] for s in registry.get('slots',[]) if s.get('path') and s.get('status') in ['active','ready','reserved']]
    records,commons,exclusions=discover(args.root or DEFAULT_ROOTS,args.container or DEFAULT_CONTAINERS)
    refs={}
    for key,c in commons.items():
        try:
            heads={line.split()[1]:line.split()[0] for line in git(c['root'],'ls-remote','--heads','origin').splitlines()}
            local={line.split('\t')[1] for line in git(c['root'],'for-each-ref','--format=%(refname)%09%(objectname)','refs').splitlines()}
            refs[key]={'heads':heads,'known_heads':[sha for sha in set(heads.values()) if sha in local or git(c['root'],'cat-file','-e',sha,check=False).returncode==0]}
            print('Verified remote: '+c['repository']+' '+c['common_dir'],file=sys.stderr,flush=True)
        except (RuntimeError,OSError,subprocess.TimeoutExpired) as e:refs[key]={'error':str(e)}
    procs,perror=processes();allroots={norm(x['path']) for x in records};active={norm(x) for x in [os.getcwd(),*args.active,*retained]};results=[]
    with concurrent.futures.ThreadPoolExecutor(max_workers=max(1,min(args.workers,8))) as pool:
        futures=[pool.submit(inspect,r,allroots,refs,procs,active) for r in records]
        for f in concurrent.futures.as_completed(futures):
            results.append(f.result())
            if len(results)%10==0:print(f'Inventoried {len(results)}/{len(records)}',file=sys.stderr,flush=True)
    data=dict(observed_at=datetime.datetime.now(datetime.timezone.utc).isoformat(),measurement='Logical file lengths, no reparse traversal; nested inventoried roots excluded from parents. Hardlinks may be counted per path. Not allocated disk size or a deletion recommendation.',process_query_error=perror,process_limit='Command/executable references are clues, not proof that an idle editor has no open handles. Normal git worktree remove must still be used without force.',repositories=list(commons.values()),remote_probes=refs,excluded=exclusions,worktrees=sorted(results,key=lambda x:(x['repository'],x['path'])),volumes={v:dict(zip(['total','used','free'],shutil.disk_usage(v+':/'))) for v in ['C','D'] if Path(v+':/').exists()})
    data['registry_path']=str(registry_path) if registry else None
    data['unregistered_candidates']=unregistered_candidates(args.container or DEFAULT_CONTAINERS,records)
    data['total_logical_bytes']=sum(x['logical_bytes'] for x in results);data['classifications']=dict(collections.Counter(x['classification'] for x in results))
    if args.output:
        args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');args.output.with_suffix('.md').write_text(markdown(data),encoding='utf-8')
        print(json.dumps({'output':str(args.output),'trees':len(results),'logical_GiB':round(data['total_logical_bytes']/2**30,3),'classes':data['classifications']}))
    else:print(json.dumps(data,ensure_ascii=False,indent=2))
if __name__=='__main__':
    sys.stdout.reconfigure(encoding='utf-8');main()
