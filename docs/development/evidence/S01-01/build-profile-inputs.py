"""Materialize S01-01 provenance only; read archived data, never run its code."""
import hashlib
import json
import pathlib
import re
import subprocess
from functools import lru_cache

ROOT = pathlib.Path(__file__).resolve().parents[4]
BOOK = 'docs/design-handbook/remediation/'
INDEX = 'docs/development/s01-input-index.json'
VERSION = 'CP6-S01-CONTRACT-INPUTS/1.0.0'


def read(path):
    return json.loads((ROOT / path).read_text(encoding='utf-8-sig'))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


@lru_cache(maxsize=None)
def source_pin(path):
    blob = subprocess.check_output(['git', 'rev-parse', 'HEAD:' + path], cwd=ROOT, text=True).strip()
    current = subprocess.check_output(['git', 'hash-object', '--path=' + path, path], cwd=ROOT, text=True).strip()
    assert current == blob, 'Committed source changed: ' + path
    content = subprocess.check_output(['git', 'cat-file', 'blob', blob], cwd=ROOT)
    return {'path': path, 'sha256': hashlib.sha256(content).hexdigest(), 'git_blob': blob, 'hash_basis': 'git_blob_bytes'}


def pin(path, pointer=None):
    result = dict(source_pin(path))
    if pointer is not None:
        result['json_pointer'] = pointer
    return result


def bindings(value, ref, pointer='', definition=None):
    result = []
    if isinstance(value, dict):
        if value.get('$ref') == ref or (definition is not None and value == definition and '#' + pointer != ref):
            result.append(pointer)
        for key, child in value.items():
            result += bindings(child, ref, pointer + '/' + key.replace('~', '~0').replace('/', '~1'), definition)
    elif isinstance(value, list):
        for key, child in enumerate(value):
            result += bindings(child, ref, pointer + '/' + str(key), definition)
    return result


def build():
    index = read(INDEX)
    composed = read(BOOK + 'assets/composed-derivatives.json')
    sources = []
    for pos, entry in enumerate(index['sources']):
        assert entry in composed['sources'], entry['source_path']
        assert digest(pathlib.Path(entry['source_path'])) == entry['source_sha256']
        assert digest(pathlib.Path(entry['local_derived_path'])) == entry['derived_sha256']
        sources.append({
            'id': f'composed-{pos:02d}',
            'filename': pathlib.Path(entry['source_path']).name,
            'selector': pin(INDEX, f'/sources/{pos}'),
            'original_sha256': entry['source_sha256'],
            'composed_sha256': entry['derived_sha256'],
            'composed_bytes': entry['derived_bytes'],
            'patch_bundles': [pin('docs/design-handbook/' + p) for p in entry['patch_bundles']],
            'source_finding_ids': entry['source_finding_ids'],
            'patch_operation_count': entry['operations'],
        })

    storage_path = BOOK + 'assets/core-types/numeric-storage-1.0.0.json'
    numeric = read(storage_path)
    numeric_schemas = []
    for pos, entry in enumerate(numeric['source_integrity']):
        original = pathlib.Path(entry['path'])
        assert digest(original) == entry['sha256']
        sources.append({
            'id': f'numeric-{pos}', 'filename': original.name,
            'selector': pin(storage_path, f'/source_integrity/{pos}'),
            'original_sha256': entry['sha256'], 'composed_sha256': None,
            'patch_bundles': [], 'source_finding_ids': ['OPS-IF-002' if pos == 0 else 'OPS-IF-030'],
            'note': 'Unmodified native schema/data dictionary; the R1 storage decision is a separate local asset.',
        })
        numeric_schemas.append(json.loads(original.read_text(encoding='utf-8-sig')))

    # Registry versions are separate actual byte sources, not merely comments in codecs.
    registry_pos = next(i for i, s in enumerate(composed['sources']) if s['source_sha256'] == 'c69762861155ed95af608a9d4867e900f8287eccdad3c38bd9c4c6b50f2ee857')
    registry = composed['sources'][registry_pos]
    assert digest(pathlib.Path(registry['source_path'])) == registry['source_sha256']
    assert digest(pathlib.Path(registry['local_derived_path'])) == registry['derived_sha256']
    sources.append({'id': 'erp-registry-current', 'filename': pathlib.Path(registry['source_path']).name,
        'selector': pin(BOOK + 'assets/composed-derivatives.json', f'/sources/{registry_pos}'),
        'original_sha256': registry['source_sha256'], 'composed_sha256': registry['derived_sha256'],
        'composed_bytes': registry['derived_bytes'], 'patch_bundles': [pin('docs/design-handbook/' + p) for p in registry['patch_bundles']],
        'source_finding_ids': registry['source_finding_ids'], 'patch_operation_count': registry['operations']})
    files_path = 'docs/design-handbook/indexes/all-files.json'
    files = read(files_path)['files']
    frozen_pos = next(i for i, s in enumerate(files) if s['sha256'] == '62effa1e0542784a5fa936d11961465db4da522ed4d28fc9c9bb5351cf40b6f1')
    frozen = files[frozen_pos]
    assert digest(pathlib.Path(frozen['absolute_path'])) == frozen['sha256']
    sources.append({'id': 'erp-registry-frozen', 'filename': pathlib.Path(frozen['absolute_path']).name,
        'selector': pin(files_path, f'/files/{frozen_pos}'), 'original_sha256': frozen['sha256'], 'composed_sha256': None,
        'patch_bundles': [], 'source_finding_ids': ['COM-CREDIT-FROZEN-R8-CODEC-CURRENT-ADAPTATION']})
    lead_path = BOOK + '00-disposition-register.json'
    lead = read(lead_path)['issues'][25]['evidence'][1]
    assert digest(pathlib.Path(lead['source_path'])) == lead['sha256']
    lead_schema = json.loads(pathlib.Path(lead['source_path']).read_text(encoding='utf-8-sig'))
    sources.append({'id': 'lead-query-schema', 'filename': pathlib.Path(lead['source_path']).name,
        'selector': pin(lead_path, '/issues/25/evidence/1'), 'original_sha256': lead['sha256'], 'composed_sha256': None,
        'patch_bundles': [], 'source_finding_ids': ['COM-LEAD-GENERATED-CS-NULLABLE']})

    original_catalog = 'docs/development/assessments/20261011/original-acceptance-catalog.json'
    originals = read(original_catalog)
    acceptance_catalog = BOOK + '08-acceptance-catalog.json'
    revised = read(acceptance_catalog)
    profiles = []

    def profile(pid, owner, repo, source_ids, identity, tasks, findings, note):
        selected = [{'source_id': sid, 'variant': 'composed' if next(s for s in sources if s['id'] == sid)['composed_sha256'] else 'original'} for sid in source_ids]
        result = {'id': pid, 'owner': {'target_id': owner, 'repository': repo,
                                     'authority': 'Original native business Owner; this package grants no write authority.' if owner else 'Shared contract utility; no business Owner or write authority.'},
                  'target_consumers': tasks, 'selected_sources': selected,
                  'schema_or_type_identity': identity, 'scope': note,
                  'adoption_state': 'NOT_RUN', 'original_acceptance_refs': [], 'revision_acceptance_refs': []}
        if owner:
            target_pos = next(i for i, t in enumerate(originals['targets']) if t['id'] == owner)
            target = originals['targets'][target_pos]
            result['original_acceptance_refs'] = [pin(original_catalog, f'/targets/{target_pos}'),
                pin(target['definition_ref']['path'], target['definition_ref']['json_pointer'])]
            required_path = 'docs/design-handbook/indexes/required-inputs.json'
            required_pos = next(i for i, t in enumerate(read(required_path)['targets']) if t['target_id'] == owner)
            result['required_combination_ref'] = pin(required_path, f'/targets/{required_pos}')
        for pos, case in enumerate(revised['cases']):
            if case['id'] in index['runtime_case_ids'] and set(case['source_finding_ids']) & set(findings):
                result['revision_acceptance_refs'].append({**pin(acceptance_catalog, f'/cases/{pos}'),
                    'case_id': case['id'], 'coverage': 'CONTRACT_SUBASSERTIONS_ONLY',
                    'remaining': 'Real consumers, permissions, persistence and complete business sequence require later tasks; whole case remains NOT_RUN.'})
        profiles.append(result)
        return result

    for wire, suffix in [('signedDecimal', 'signed'), ('nonNegativeDecimal', 'nonnegative'), ('positiveDecimal', 'positive')]:
        schema = numeric_schemas[0]
        p = profile('fin-credit-' + suffix, 'FIN-CREDIT-01', 'Main', ['numeric-0'],
            {'source_id': 'numeric-0', 'variant': 'original', 'schema_id': schema['$id'], 'json_pointer': '/$defs/' + wire,
             'definition': schema['$defs'][wire]},
            ['Main:S01-03', 'Main:S01-05', 'Main:S01-10'], ['OPS-IF-002'],
            '38 total digits / up to 8 fractional digits, native sign profile per exact field. R1 unscaled integer plus scale; no decimal/double intermediary.')
        p['native_field_bindings'] = bindings(schema, '#/$defs/' + wire, definition=schema['$defs'][wire])
        assert p['native_field_bindings'], 'No exact field binding: ' + wire
        p['storage_decision_ref'] = pin(storage_path, '/credit')
    for wire, suffix in [('Decimal18', 'decimal18'), ('Qty12', 'qty12')]:
        schema = numeric_schemas[1]
        p = profile('fin-inv-' + suffix, 'FIN-INV-01', 'Main', ['numeric-1', 'numeric-2'],
            {'source_id': 'numeric-1', 'variant': 'original', 'schema_id': schema['$id'], 'json_pointer': '/$defs/' + wire,
             'definition': schema['$defs'][wire]},
            ['Main:S01-04', 'Main:S01-05', 'Main:S01-10'], ['OPS-IF-030'],
            'Unbounded integer digits; native 18/12 fractional boundary. Arithmetic normalization never rewrites original wire, negative zero or digest/replay bytes.')
        p['native_field_bindings'] = bindings(schema, '#/$defs/' + wire, definition=schema['$defs'][wire])
        assert p['native_field_bindings'], 'No exact field binding: ' + wire
        p['storage_decision_ref'] = pin(storage_path, '/inventory_mappings')

    atp = json.loads(pathlib.Path(index['sources'][24]['local_derived_path']).read_text(encoding='utf-8-sig'))
    profile('atp-stableid-r1', 'PLAN-ATP-01', 'Main', ['composed-13', 'composed-14', 'composed-24', 'composed-25'],
        {'source_id': 'composed-24', 'variant': 'composed', 'schema_id': atp['$id'], 'profile_version': 'ATP-STABLEID-LEXICAL/20261011-R1',
         'json_pointer': '/$defs/StableId', 'definition': atp['$defs']['StableId']},
        ['Main:S01-06', 'Main:S01-10'], ['PLAN-IF-004'],
        'Explicit lexical successor. Five PostgreSQL UOM columns use full PLAN-IF-004 + PLAN-IF-005 composed DDL; no migration here.')

    helper = BOOK + 'assets/core-types/DesignRevisionTypes.cs'
    for pid, name in [('strict-json', 'StrictJson (S01-02 implementation pending)'), ('null-only', 'Cp6.DesignRevision.NullOnly'),
                      ('json-semantic-equality', 'Cp6.DesignRevision.JsonSemanticEquality')]:
        p = profile(pid, None, None, [], {'type_name': name, 'support_asset': pin(helper)},
            ['Main:S01-02', 'CRM:S01-08', 'Platform:S01-09'], ['NCR-TYPE-01', 'COM-INQ-GENERATED-CONTRACT-ADOPTION'],
            'Strict duplicate-key boundary and required-null / schema equality helpers only. Semantic equality is never a signing, hashing or replay canonicalizer.')
        p['rule_ref'] = pin('docs/superpowers/plans/2026-10-11-s01-task-breakdown.md')

    for pid, ids, namespace in [('ncr-main-r6', [15, 17], 'QaNcrMainR6Declarations'), ('ncr-cp4', [16], 'SourceQA; OwnerWire; Consumer; LegacyAdapter')]:
        p = profile(pid, 'QA-NCR-01', 'Main', [f'composed-{i:02d}' for i in ids],
            {'source_id': f'composed-{ids[0]:02d}', 'variant': 'composed', 'csharp_namespaces': namespace},
            ['Main:S01-07'], ['NCR-TYPE-01', 'NCR-FIELD-01'],
            'Only pinned generated attachment subset of current NCR combination. Seven required const-null properties across Main/CP4 and five exact nullable metadata corrections; Owner identity unchanged.')
        record = None
        p['required_null_fields'] = []
        for line_no, line in enumerate(pathlib.Path(index['sources'][ids[0]]['local_derived_path']).read_text(encoding='utf-8-sig').splitlines(), 1):
            if 'public sealed record ' in line:
                record = line.split('public sealed record ')[1].split()[0]
            if 'Cp6.DesignRevision.NullOnly' in line:
                p['required_null_fields'].append({'record': record, 'json_field': re.search(r'JsonPropertyName\("([^"]+)"\)', line)[1],
                    'source_id': f'composed-{ids[0]:02d}', 'variant': 'composed', 'line': line_no, 'required': True, 'const': None})
    profile('ipqc-r4', 'QA-IPQC-01', 'Main', ['composed-19'],
        {'source_id': 'composed-19', 'variant': 'composed', 'typescript_types': ['QapEvidence', 'QapUom'],
         'exact_fields': {'QapEvidence': ['owner', 'id', 'version', 'digest'], 'QapUom': ['id', 'version']}},
        ['Main:S01-07'], ['OPS-IF-015'],
        'Native QAP registrationRef and FullCount units. ExecEvidence has ref+digest and cannot be substituted by matching its name.')
    inq = json.loads(pathlib.Path(index['sources'][20]['local_derived_path']).read_text(encoding='utf-8-sig'))
    p = profile('crm-inq-r3-v15', 'CRM-INQ-01', 'CRM', ['composed-20', 'composed-21', 'composed-22', 'composed-23', 'composed-26'],
        {'source_id': 'composed-20', 'variant': 'composed', 'openapi': inq['openapi'], 'info': inq['info'], 'schema_root': '/components/schemas'},
        ['CRM:S01-08', 'Platform:S01-09'], ['COM-INQ-GENERATED-CONTRACT-ADOPTION'],
        'Generated contract subset: references, boolean extensions, structural uniqueItems and code-point length. Full H01, permissions, download proof/session/ETag and terminal-page flows remain untested.')
    catalog_path = 'docs/development/assessments/20261011/catalogs/commercial-finance.json'
    inq_original = next(t for t in read(catalog_path)['targets'] if t['id'] == 'CRM-INQ-01')
    inq_pos = next(i for i, t in enumerate(read(catalog_path)['targets']) if t['id'] == 'CRM-INQ-01')
    p['selected_original_subassertions'] = []
    for case_id, scope in [('AC-INQ-009', 'Field identity across generated contracts; no business fixture execution'),
                           ('AC-INQ-048', 'Strict schema before decode; no real permission projection'),
                           ('AC-INQ-053', 'Unicode code-point length; no full endpoint or UI validation')]:
        pos = next(i for i, c in enumerate(inq_original['cases']) if c['source_case_id'] == case_id)
        c = inq_original['cases'][pos]
        p['selected_original_subassertions'].append({**pin(catalog_path, f'/targets/{inq_pos}/cases/{pos}'),
            'case_id': case_id, 'original_source_sha256': c['source_sha256'], 'original_source_line': c['source_line'], 'scope': scope, 'whole_case_state': 'NOT_RUN'})
    p = profile('crm-lead-cp20', 'CRM-LEAD-01', 'CRM', ['composed-18', 'lead-query-schema'],
        {'source_id': 'composed-18', 'variant': 'composed', 'filename_identity': 'CrmLeadCp20Contracts.cs',
         'actual_csharp_namespace': 'CP6.CRM.CP21.Contracts', 'field': 'ownerId'},
        ['CRM:S01-08'], ['COM-LEAD-GENERATED-CS-NULLABLE'],
        'Filename CP20 is a locator, actual namespace CP21; current required combination is separately pinned. OwnerId UUID/unassigned/null schema remains required; string? does not authorize arbitrary strings.')
    p['owner_id_field_schema'] = {'source_id': 'lead-query-schema', 'variant': 'original',
        'schema_id': lead_schema.get('$id'), 'json_pointer': '/$defs/LeadQueryFilters/properties/ownerId',
        'definition': lead_schema['$defs']['LeadQueryFilters']['properties']['ownerId'],
        'required': 'ownerId' in lead_schema['$defs']['LeadQueryFilters']['required'],
        'scope': 'LeadQueryFilters only; ContactCreateInput.ownerId is independently optional.'}
    for pid, frozen in [('erp-credit-current-v1', False), ('erp-credit-frozen-r8', True)]:
        p = profile(pid, 'ERP-CREDIT-01', 'Main', [f'composed-{i:02d}' for i in (range(13) if not frozen else [10, 11, 12])],
            {'source_id': 'composed-11', 'variant': 'original' if frozen else 'composed',
             'csharp_namespace': 'Cp6.ErpCredit.Generated' if frozen else 'Cp6.ErpCredit.CurrentV1.Generated',
             'type_registry_sha256': '62effa1e0542784a5fa936d11961465db4da522ed4d28fc9c9bb5351cf40b6f1' if frozen else '564706ce0247d84dc8056002d540f17eccdcab7c5b5e4b123c09121ad407fbda'},
            ['Main:S01-11', 'Platform:S01-09'], ['COM-CREDIT-CURRENT-BODY-MONEY-FIELD', 'COM-CREDIT-FROZEN-R8-CODEC-CURRENT-ADAPTATION', 'COM-CREDIT-OPENAPI-FROZEN-REPLAY-MAPPING'],
            'ERP native Money, independent of FIN-CREDIT decimals. Exact registry route, no latest/fallback. Frozen original commands/receipts preserve bytes; insufficient current mapping proof stays READ_ONLY_UNPROVEN/MANUAL_RECONCILIATION.')
        if frozen:
            for s in p['selected_sources']:
                s['variant'] = 'original'
        p['selected_sources'].append({'source_id': 'erp-registry-frozen' if frozen else 'erp-registry-current',
                                      'variant': 'original' if frozen else 'composed'})
        p['registry_identity_ref'] = pin(BOOK + 'assets/commercial-fixtures/bounded-patches.json', '/patches/13/operations/0')

    local_paths = [x['path'] for x in index['local_asset_pins']] + [
        BOOK + '01-version-and-authority.md', BOOK + '06-wire-types-and-fixtures.md',
        BOOK + 'assets/composed-derivatives.json', BOOK + 'assets/core-types/atp-stableid-vectors.json',
        BOOK + 'assets/core-types/bounded-patches.json', BOOK + 'assets/commercial-fixtures/bounded-patches.json',
        BOOK + 'assets/finance-recovery/source-patches.json']
    result = {'package_version': VERSION, 'role': 'PINNED_ADOPTION_INPUTS_NOT_BUSINESS_EXECUTION',
        'design_revision': 'CP6-DESIGN-REVISION-20261011/R1', 'database_target': 'PostgreSQL',
        'source_index': pin(INDEX), 'local_asset_pins': [pin(p) for p in dict.fromkeys(local_paths)],
        'sources': sources, 'profiles': profiles,
        'runtime_acceptance': 'NOT_RUN', 'historical_results': 'REFERENCE_ONLY; never copied into this task execution results',
        'retrieval': 'Repository-relative selectors resolve archival evidence during tooling/adoption only. Repository source pins bind exact git_blob bytes (working-tree filters may change line endings); original/composed archive hashes always bind raw file bytes. No consumer runtime uses machine absolute paths. Obtain exact bytes by SHA before code generation; unavailable/mismatched evidence blocks only its dependent profile.',
        'version_policy': 'Package version identifies this manifest/vector format, not an Owner envelope field. Unknown native version/schema/registry/hash rejects; no fallback or reserialization of original commands.',
        'limits': ['Contract input freeze and expected vectors only; production consumers are not implemented.',
                   'Original required combinations and historical missing evidence retain their own identities and gaps.',
                   'Database migrations, real authorization, Owner atomicity and production deployment are outside S01-01.']}
    path = ROOT / 'eng/contracts/s01/profile-inputs.json'
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes((json.dumps(result, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))
    print(json.dumps({'sources': len(sources), 'profiles': len(profiles), 'package_version': VERSION}))


if __name__ == '__main__':
    build()
