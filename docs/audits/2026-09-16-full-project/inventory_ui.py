import pathlib, re, json, csv, collections, argparse
parser=argparse.ArgumentParser(description='Read-only CP6 frontend inventory; artifacts are written only to --output.')
parser.add_argument('--core',required=True,help='Path to the fixed CP6 source snapshot')
parser.add_argument('--output',required=True,help='Directory for generated inventory artifacts')
args=parser.parse_args()
ROOT=pathlib.Path(args.core).resolve()
OUT=pathlib.Path(args.output).resolve()
OUT.mkdir(parents=True,exist_ok=True)
def rel(p): return p.relative_to(ROOT).as_posix()
def read(p): return p.read_text(encoding='utf-8-sig')
router=read(ROOT/'cp6.web/src/router/index.ts')
route_map=collections.defaultdict(list)
route_rows=[]
for match in re.finditer(r"'(/[^']*)':\s*\(\)\s*=>\s*import\('@/(.*?)'\)",router):
    route_rows.append({'kind':'viewModules','route':match[1],'component':'cp6.web/src/'+match[2],'line':router[:match.start()].count('\n')+1})
static_part=router.split('const staticRoutes:')[1].split('const router =')[0]
static_offset=router.index(static_part)
for match in re.finditer(r"path:\s*'(/[^']*)'(?:(?!path:).)*?component:\s*\(\)\s*=>\s*import\('@/(.*?)'\)",static_part,re.S):
    route_rows.append({'kind':'static_or_layout','route':match[1],'component':'cp6.web/src/'+match[2],'line':router[:static_offset+match.start()].count('\n')+1})
for row in route_rows: route_map[row['component']].append(row['route'])
all_sources=[p for p in (ROOT/'cp6.web/src').rglob('*') if p.suffix in ['.vue','.ts'] and not re.search(r'\.(test|spec)\.',p.name)]
incoming=collections.defaultdict(list)
api_deps=collections.defaultdict(set)
for p in all_sources:
    txt=read(p)
    for m in re.finditer(r"(?:from\s+|import\()['\"]([^'\"]+)['\"]",txt):
        name=m[1]
        q=ROOT/'cp6.web/src'/name[2:] if name.startswith('@/') else p.parent/name if name.startswith('.') else None
        if q:
            candidates=[q,q.with_suffix('.ts'),q.with_suffix('.vue'),q/'index.ts']
            found=next((x.resolve() for x in candidates if x.is_file()),None)
            if found:
                incoming[rel(found)].append(rel(p))
                api_deps[rel(p)].add(rel(found))
def all_api(path,seen=None):
    seen=seen or set()
    if path in seen:return set()
    seen.add(path)
    result=set()
    for dep in api_deps[path]:
        if dep.startswith('cp6.web/src/api/'):result.add(dep)
        elif dep.startswith('cp6.web/src/stores/') or dep.endswith('.vue') or dep.startswith('cp6.web/src/composables/'):
            result.update(all_api(dep,seen))
    return result
rows=[]
for p in sorted((ROOT/'cp6.web/src/views').rglob('*.vue')):
    path=rel(p); txt=read(p)
    rows.append({'path':path,'module':p.relative_to(ROOT/'cp6.web/src/views').parts[0] if len(p.relative_to(ROOT/'cp6.web/src/views').parts)>1 else '_root','lines':len(txt.splitlines()),'routes':' | '.join(route_map[path]),'referenced_by':' | '.join(sorted(set(incoming[path]))),'apis_transitive':' | '.join(sorted(all_api(path))),'template_components':' | '.join(sorted(set(re.findall(r'<(Cp\w+|Vol\w+)\b',txt)))),'permission_directives':len(re.findall(r'v-permission',txt)),'style_scoped':'<style scoped' in txt,'inline_style_count':len(re.findall(r'(?<![\w-])style=',txt)),'media_rules':len(re.findall(r'@media',txt))})
for name,data in [('ui-page-inventory.csv',rows),('ui-route-inventory.csv',route_rows)]:
    with (OUT/name).open('w',newline='',encoding='utf-8-sig') as f:
        w=csv.DictWriter(f,fieldnames=list(data[0]));w.writeheader();w.writerows(data)
mods=collections.defaultdict(lambda:collections.Counter())
for row in rows:
    c=mods[row['module']];c['vue_files']+=1;c['lines']+=row['lines'];c['direct_routed_components']+=bool(row['routes']);c['no_route_no_import']+=not row['routes'] and not row['referenced_by'];c['with_api_dependency']+=bool(row['apis_transitive']);c['cp_template_components']+=bool(row['template_components'] and 'Cp' in row['template_components'])
api_files=[p for p in (ROOT/'cp6.web/src/api').rglob('*.ts') if not re.search(r'\.(test|spec)\.',p.name)]
summary={'view_modules_route_keys':sum(x['kind']=='viewModules' for x in route_rows),'view_modules_unique_components':len(set(x['component'] for x in route_rows if x['kind']=='viewModules')),'static_or_layout_records':sum(x['kind']=='static_or_layout' for x in route_rows),'all_direct_routed_components':sum(bool(x['routes']) for x in rows),'views_vue_files':len(rows),'api_ts_non_test_files':len(api_files),'modules':dict(mods),'no_route_no_import':[x['path'] for x in rows if not x['routes'] and not x['referenced_by']],'src_tests':len([p for p in (ROOT/'cp6.web/src').rglob('*') if re.search(r'\.(test|spec)\.[jt]sx?$',p.name)]),'e2e_test_files':len(list((ROOT/'cp6.web/e2e').rglob('*.spec.ts')))}
summary['view_module_routes_by_folder']=dict(collections.Counter(pathlib.PurePosixPath(x['component']).parts[3] for x in route_rows if x['kind']=='viewModules'))
summary['all_src_vue_files']=len(list((ROOT/'cp6.web/src').rglob('*.vue')))
summary['space_design_module_vue_files']=len(list((ROOT/'cp6.web/src/modules/space-design').rglob('*.vue')))
summary['routed_ui_usage']={k:sum(bool(re.search(r'<'+k+r'\b',read(ROOT/x['path']))) for x in rows if x['routes']) for k in ['CpPageShell','CpListPage','CpFilterBar','CpFormDialog','CpTag','VolTable','VolForm','el-table','el-form']}
summary['top_view_sizes']=[{'path':x['path'],'lines':x['lines']} for x in sorted(rows,key=lambda x:-x['lines'])[:12]]
summary['native']={folder:{'cs_files':len(list((ROOT/folder).glob('*.cs'))),'xaml_files':len(list((ROOT/folder).glob('*.xaml')))} for folder in ['CP6.Desktop','CP6.Mobile','CP6.Client.Core','CP6.Client.Api','CP6.Client.Tests','CP6.Space.Client']}
api_rows=[]
for p in sorted(api_files):
    for i,line in enumerate(read(p).splitlines(),1):
        if re.search(r'http\.(get|post|put|delete|patch)\b',line):api_rows.append({'path':rel(p),'line':i,'code':line.strip()})
with (OUT/'ui-api-call-sites.csv').open('w',newline='',encoding='utf-8-sig') as f:
    w=csv.DictWriter(f,fieldnames=['path','line','code']);w.writeheader();w.writerows(api_rows)
summary['api_http_call_sites']=len(api_rows)
(OUT/'ui-inventory-summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False,indent=2))
