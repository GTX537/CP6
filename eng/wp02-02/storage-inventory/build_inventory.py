"""Join a real PostgreSQL metadata capture with the adopted EF models and source references.

The source index is deliberately conservative: a reference in a file containing writes
is a navigation aid, not proof that every reference writes or that a call is reachable.
No business rows, credentials, runtime configuration or database mutations are read.
"""
import argparse
from collections import Counter, defaultdict
import json
from pathlib import Path
import re
import subprocess

TOKEN = re.compile(r'[A-Za-z_][A-Za-z_0-9]*')
WRITE = re.compile(r'\b(?:INSERT\s+INTO|UPDATE\s+|DELETE\s+FROM|TRUNCATE\s+|COPY\s+|'
                   r'SaveChanges(?:Async)?|Execute(?:Sql\w*|Update\w*|Delete\w*|NonQuery\w*)|'
                   r'Bulk\w+|AddRange(?:Async)?|RemoveRange)\b', re.I)
TX = re.compile(r'\b(?:BeginTransaction\w*|UseTransaction\w*|ShareTransaction\w*|'
                r'Commit\w*|Rollback\w*|TransactionScope|BeginAuthorized\w*)\b')
DDL = re.compile(r'\bCREATE\s+(?:OR\s+REPLACE\s+)?(?:UNIQUE\s+)?'
                 r'(TABLE|VIEW|SEQUENCE|FUNCTION|PROCEDURE|TRIGGER|INDEX|SCHEMA)\s+'
                 r'(?:IF\s+NOT\s+EXISTS\s+)?([^\s(;]+)', re.I)
ROOTS = ('CP6.Core/', 'CP6.WebApi/', 'CP6.Entity/', 'CP6.Space.Application/',
         'CP6.Space.Infrastructure/', 'CP6.Space.Domain/', 'CP6.Persistence.PostgreSql/',
         'tools/CP6.TenantStorage/', 'eng/wp02-02/rma-tenant-archive/')


def load(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def clean(value):
    if isinstance(value, dict):
        return {k: clean(v) for k, v in value.items() if v is not None and v != [] and v != {}}
    if isinstance(value, list):
        return [clean(v) for v in value]
    return value


def write_jsonl(path, rows):
    path.write_text(''.join(json.dumps(clean(row), ensure_ascii=False, separators=(',', ':'))+'\n'
                            for row in rows), encoding='utf-8', newline='\n')


def build(args):
    root, out = Path(args.root).resolve(), Path(args.output).resolve()
    catalog, model_capture = load(args.catalog), load(args.models)
    models = model_capture['contexts']
    predicates = {(p['schema'],p['table'],p['index']):p['predicate'] for p in load(args.predicates)} if args.predicates else {}
    tracked = subprocess.check_output(['git', '-C', str(root), 'ls-files', '-z'], text=True).split('\0')
    files, token_files, text_by_id = {}, defaultdict(set), {}
    optional = []
    for path in sorted(p for p in tracked if p.startswith(ROOTS) and p.endswith(('.cs','.py','.sql'))):
        if '/Migrations/' in path and not path.startswith('CP6.Persistence.PostgreSql/'):
            continue  # SQL Server's historical migrations are not a PostgreSQL lineage.
        text = (root/path).read_text(encoding='utf-8-sig')
        lines = text.splitlines()
        key = 's'+str(len(files)+1)
        kind = 'migration' if path.startswith('CP6.Persistence.PostgreSql/') else (
            'recovery_tool' if path.startswith('eng/') else 'installer' if '/Persistence/Tenancy/' in path or path.startswith('tools/') else 'application')
        files[key] = {'id':key,'path':path,'kind':kind,
            'write_signal_lines':[n for n,line in enumerate(lines,1) if WRITE.search(line)],
            'transaction_signal_lines':[n for n,line in enumerate(lines,1) if TX.search(line)],
            'background':bool(re.search(r'\b(?:BackgroundService|IHostedService|ExecuteAsync|AddHostedService)\b',text)),
            'generic_persistence':bool(re.search(r'\b(?:Set<T>|DbSet<T>|IRepository<|RepositoryBase<)',text))}
        text_by_id[key] = text
        for token in set(TOKEN.findall(text)):
            token_files[token].add(key)
        if kind == 'installer':
            for match in DDL.finditer(text):
                optional.append({'record':'installation_ddl','kind':match[1].upper(),
                    'identifier_template':match[2], 'source':path,
                    'line':text.count('\n',0,match.start())+1,
                    'coverage':'SOURCE_TEMPLATE; not executed by cold DatabaseInit'})
    model_by_table = defaultdict(list)
    tenant_models=[]
    for context in models:
        if context['profile']!='fixed-shared':
            tenant_models.append(context)
            continue
        for table in context['tables']:
            model_by_table[(table['schema'],table['name'])].append({'context':context['context'],**table})

    def refs(tokens, schema=None):
        ids = set().union(*(token_files.get(token,set()) for token in tokens))
        if schema and schema != 'public':
            ids &= token_files.get(schema,set())
        return sorted(ids, key=lambda k: files[k]['path'])

    def located(ids, tokens):
        # First exact token occurrence in each source, with all write/transaction sites indexed separately.
        result = []
        pattern = re.compile(r'\b(?:'+ '|'.join(re.escape(t) for t in sorted(set(tokens))) +r')\b')
        for key in ids:
            match = pattern.search(text_by_id[key])
            if match:
                result.append({'source':key,'line':text_by_id[key].count('\n',0,match.start())+1})
        return result

    structural_mismatches, equivalent_index_names = [], []
    relation_rows = []
    physical = {(r['schema'],r['name']):r for r in catalog['relations']}
    for key, mappings in model_by_table.items():
        if key not in physical:
            structural_mismatches.append({'table':'.'.join(key),'problem':'EF table absent from native catalog'})
            continue
        columns = {c['name']:c for c in physical[key]['columns']+physical[key].get('system_columns',[])}
        for model in mappings:
            for col in model['columns']:
                if col['name'] not in columns:
                    structural_mismatches.append({'table':'.'.join(key),'column':col['name'],'problem':'EF column absent'})
                elif columns[col['name']]['not_null'] == col['nullable']:
                    structural_mismatches.append({'table':'.'.join(key),'column':col['name'],'problem':'nullability differs'})
                elif columns[col['name']]['type'] != col['type']:
                    structural_mismatches.append({'table':'.'.join(key),'column':col['name'],'problem':'PostgreSQL store type/facets differ',
                        'model':col['type'],'catalog':columns[col['name']]['type']})
            for unique in model['unique_keys']+model['unique_indexes']:
                predicate=predicates.get((*key,unique['name']),unique.get('filter'))
                matches=[i for i in physical[key]['indexes'] if i['unique']
                    and [c.strip('"') for c in i['key_columns']]==unique['columns']
                    and i.get('predicate')==predicate
                    and i['nulls_not_distinct']==unique.get('nulls_not_distinct',False)
                    and (not unique.get('primary') or i['primary'])]
                if not matches:
                    structural_mismatches.append({'table':'.'.join(key),'index':unique['name'],'problem':'EF unique columns/predicate/primary-key domain absent'})
                elif all(i['name']!=unique['name'] for i in matches):
                    equivalent_index_names.append({'table':'.'.join(key),'model':unique['name'],
                        'catalog':[i['name'] for i in matches],'columns':unique['columns']})

    for relation in catalog['relations']:
        row = dict(relation)
        key = (row['schema'],row['name'])
        mappings = model_by_table.get(key,[])
        entities = sorted({e for m in mappings for e in m['entities']})
        symbols = {row['name']} | {e.rsplit('.',1)[-1] for e in entities} | {s for m in mappings for s in m['dbsets']}
        ids = refs(symbols,row['schema'])
        runtime = [i for i in ids if files[i]['kind'] != 'migration']
        row.update(record='relation',placement='RETAIN_CURRENT_SHARED',
            owner_group=row['schema'] if row['schema']!='public' else (mappings[0]['context'].rsplit('.',1)[-1] if mappings else 'Core native/shared'),
            tenant_columns=[c['name'] for c in row['columns'] if c['name'].lower() in ('tenantid','tenant_id')],
            tenant_related_columns=[c['name'] for c in row['columns'] if re.search('tenant|organization',c['name'],re.I)],
            entities=entities,
            ef_contexts=sorted({m['context'] for m in mappings}),
            source_references=located(ids,symbols),
            write_bearing_source_references=[i for i in runtime if files[i]['write_signal_lines'] or files[i]['generic_persistence']],
            transaction_and_retention='Existing Owner transaction, retention and restore domain; see OWNERSHIP.md. No new tenant-schema route or purge is authorized.')
        row['data_class']='OWNER_SCOPED_SHARED_DATA'
        if row['schema']=='public' and row['name'] in ('DataProtectionKeys','CrmOidcGrant','CrmOidcLogout'):
            row['data_class']='SHARED_SECURITY; OrganizationId/GrantId are not global business definitions'
        elif row['schema']=='public' and row['name'] in ('Sys_Menus','Sys_DictTypes','Sys_DictDatas'):
            row['data_class']='SHARED_DEFINITION; exact current models, not a rule for all Sys_* tables'
        elif row['name'].startswith('__EFMigrationsHistory') or row['kind']=='S':
            row['data_class']='SHARED_STORAGE_CONTROL'
        elif row['schema']=='public' and row['name']=='Sys_Tenants':
            row['data_class']='MAIN_TENANT_CONTROL'
        elif row['schema']=='public' and row['name'] in ('T_DocSequence','T_MesSequence'):
            row['data_class']='SHARED_NUMBER_DOMAIN; retain existing global allocation keys'
        elif row['schema']=='public' and row['name'] in ('Space_Asset','Space_AssetVersion','Space_RackGenerationProfile','Space_RackGenerationProfileVersion'):
            row['data_class']='MIXED_SCOPE; Scope and OwnerTenantId remain authoritative'
        if row['schema']=='cp6_rma_application_v1' and row['name'] in ('heads','versions'):
            row['commissioned_overlay']='RmaTenantStorageV1.CoreCommission.cs disables legacy writers when the tenant layout is commissioned; base physical tables are not deleted.'
        row['unique_domains_without_tenant_column']=[i['name'] for i in row['indexes'] if i['unique'] and
            not any(c.strip('"').lower() in ('tenantid','tenant_id') for c in i.get('key_columns',[]))]
        relation_rows.append(row)

    # PostgreSQL-generated identity/serial sequences inherit their exact owning-column source.
    relation_by_key={(r['schema'],r['name']):r for r in relation_rows}
    for row in relation_rows:
        if row['kind']=='S' and not row['source_references']:
            for dep in row.get('sequence_dependents',[]):
                table=relation_by_key.get((dep['schema'],dep['table']))
                if table and table['source_references']:
                    row.setdefault('generated_by_column',[]).append(dep)
                    row['source_references']+=table['source_references']
                    row['write_bearing_source_references']+=table['write_bearing_source_references']

    routines = []
    for routine in catalog['routines']:
        row = {k:v for k,v in routine.items() if k!='definition'}
        ids = refs([row['name']],row['schema'])
        # Function bodies are retained in the local raw capture; exact definitions remain in source.
        # SQL/plpgsql bodies may use dynamic identifiers. This is a conservative dependency index.
        body_tokens = set(TOKEN.findall(routine['definition']))
        row.update(record='routine',placement='RETAIN_CURRENT_SHARED',owner_group=row['schema'],
            source_references=located(ids,[row['name']]),
            referenced_catalog_relations=[r['schema']+'.'+r['name'] for r in catalog['relations']
                if r['name'] in body_tokens and (r['schema'] in body_tokens or r['schema']==row['schema'])],
            dynamic_sql=bool(re.search(r'\bEXECUTE\b',routine['definition'],re.I)),
            writer=bool(WRITE.search(routine['definition'])))
        routines.append(row)

    # Explicit database-side writes complement CLR references (trigger-maintained projections,
    # lineage registries and derived bindings have no independent application INSERT).
    sql_identifier=r'("[^"]+"|[A-Za-z_][A-Za-z_0-9]*)'
    dml=re.compile(r'\b(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM|TRUNCATE(?:\s+TABLE)?|COPY)\s+'
        +sql_identifier+r'\s*\.\s*'+sql_identifier,re.I)
    for routine in catalog['routines']:
        for match in dml.finditer(routine['definition']):
            key=(match[1].strip('"'),match[2].strip('"'))
            if key in relation_by_key:
                writer=routine['schema']+'.'+routine['name']+'('+routine['arguments']+')'
                writers=relation_by_key[key].setdefault('native_writer_routines',[])
                if writer not in writers: writers.append(writer)

    # Reviewed closed profile mapping; never an arbitrary schema selected by an HTTP caller.
    for row in relation_rows:
        if row['schema'] in ('cp6_document_review_v1','cp6_iteration_review_v1',
                'cp6_location_rule_review_v1','cp6_requirement_set_review_v1') and row['name'] in (
                'delivery','delivery_attempts','delivery_results'):
            row['closed_profile_writer']={
                'mapping':'CP6.Core/Services/Oa/ApprovalBusinessProfileV2.cs',
                'writer':'CP6.Core/Services/Wms/ToolApprovalDeliveryV1.cs',
                'method':'SubmissionSql/SubmissionText map the closed business Owner to this exact shared schema.'}
        if row['schema'] in ('crm_identity','crm_identity_priority','erp_integration','cp6_stock_outbox_v1') and row['name'].startswith('Cp6_'):
            row['package_writer']={'package':'CP6.Platform.EntityFramework 0.10.4',
                'host_scope':row['schema'],'contract':'Existing host-owned queue; retain global native message keys and original caller transaction.',
                'adoption':'docs/development/evidence/W526-01/DELIVERY.md',
                'host_sources':[k for k,v in files.items() if
                    ('StockOutbox' in v['path'] if row['schema']=='cp6_stock_outbox_v1' else
                     '/Services/ErpIntegration/' in v['path'] if row['schema']=='erp_integration' else '/Services/CrmIdentity/' in v['path'])]}
        if (row['schema'],row['name'])==('cp6_warehouse_draft_v1','consumer_fences'):
            row['writer_boundary']={'kind':'MIGRATION_SEEDED_CONTROL_REGISTRY',
                'source':'CP6.Persistence.PostgreSql/Migrations/Core/20261030008000_WarehouseDraftV1.cs',
                'line':129,'runtime':'Guard functions read the table/column registry; no independent business writer or tenant schema route.'}

    schema_rows = []
    for schema in catalog['schemas']:
        name=schema['name']
        ids=refs([name]) if name!='public' else []
        schema_rows.append({**schema,'record':'schema','placement':'RETAIN_CURRENT_SHARED',
            'relations':sum(r['schema']==name for r in relation_rows),
            'routines':sum(r['schema']==name for r in routines),
            'source_references':located(ids,[name]) if ids else [],
            'application_sources':[i for i in ids if files[i]['kind']=='application'],
            'installation_sources':[i for i in ids if files[i]['kind'] in ('migration','installer')]})

    # The four lineages call the same SQL generator with their own schema and audited table list.
    for row in [*relation_rows,*routines]:
        if row['schema'].startswith('cp6_storage_') and row['name'] in ('rowversion_v1','set_rowversion_v1'):
            generator=[k for k,v in files.items() if v['path']=='CP6.Persistence.PostgreSql/PostgreSqlTokenV1.cs']
            row['source_references']+=located(generator,[row['name']])
            row['generated_by']='PostgreSqlTokenV1.Install; schema and table list supplied by that lineage migration.'

    summary={'source_baseline':args.source,'adopted_binary_source':args.binary_source,
        'server_version':catalog['server_version'],
        'schemas':len(schema_rows),'relation_kinds':dict(Counter(r['kind'] for r in relation_rows)),
        'routines':len(routines),'triggers':sum(len(r['triggers']) for r in relation_rows),
        'indexes':sum(len(r['indexes']) for r in relation_rows),
        'unique_indexes':sum(sum(i['unique'] for i in r['indexes']) for r in relation_rows),
        'foreign_keys':sum(sum(c['kind']=='f' for c in r['constraints']) for r in relation_rows),
        'ef_tables':len(model_by_table),'source_files':len(files),
        'discovered_context_types':model_capture['discovered_context_types'],
        'extracted_context_types':[m['context'] for m in models],
        'unhandled_context_types':sorted(set(model_capture['discovered_context_types'])-{m['context'] for m in models}),
        'optional_ddl_templates':len(optional),'model_catalog_mismatches':structural_mismatches,
        'equivalent_index_names':equivalent_index_names,
        'tenant_model_profiles':[m['profile'] for m in tenant_models],
        'native_canonicalized_ef_predicates':len(predicates),
        'relations_without_direct_source_token':[r['schema']+'.'+r['name'] for r in relation_rows if not r['source_references']],
        'routines_without_direct_source_token':[r['schema']+'.'+r['name'] for r in routines if not r['source_references']],
        'scope':'Actual cold Main catalog plus separately identified optional installation source templates; no production inspection.',
        'source_index_semantics':'Conservative token and writer-bearing-file references, not an exhaustive semantic call graph or per-entry authorization proof.'}
    out.mkdir(parents=True,exist_ok=True)
    (out/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    write_jsonl(out/'objects.jsonl',[{'record':'metadata','format':'cp6-storage-inventory-v1',
        'null_semantics':'Omitted fields represent catalog NULL or empty collections; false and zero are retained.',
        'fixture_owner':'LOCAL_MIGRATOR replaces the local test administrator; it is not a business Owner.',
        'excluded_schemas':['pg_*','information_schema','w51_guard','fixture'],
        'extensions':catalog['extensions'],'types':catalog['types'],'default_privileges':catalog['default_privileges']},
        *schema_rows,*relation_rows,*routines])
    write_jsonl(out/'sources.jsonl',files.values())
    write_jsonl(out/'optional-installations.jsonl',optional)
    write_jsonl(out/'tenant-models.jsonl',tenant_models)
    print(json.dumps({k:v for k,v in summary.items() if k not in ('relations_without_direct_source_token','routines_without_direct_source_token','model_catalog_mismatches')},ensure_ascii=False))
    print('Unmatched source references:',len(summary['relations_without_direct_source_token']),len(summary['routines_without_direct_source_token']))
    print('EF/catalog differences:',len(structural_mismatches))
    if structural_mismatches or summary['unhandled_context_types']:
        raise SystemExit('Inventory requires review: native model differences or an unhandled context type remain.')


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ('root','catalog','models','source','binary-source','output'):
        parser.add_argument('--'+name,required=True)
    parser.add_argument('--predicates',help='Optional PostgreSQL-canonicalized model partial-index predicates.')
    build(parser.parse_args())
