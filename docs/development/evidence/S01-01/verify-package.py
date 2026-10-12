"""Audit S01 input bytes, references and expected data; no consumer/DB execution."""
import argparse
import collections
import datetime
import decimal
import hashlib
import json
import pathlib
import re
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[4]
BASE = 'df5553fd04ec4fc516602a4e2843650c38ae5006'
PACKAGE = 'eng/contracts/s01/'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=pathlib.Path, required=True)
    args = parser.parse_args()
    errors, physical, pinned = [], [], {}
    docs = {}

    def require(value, message):
        if not value:
            errors.append(message)

    def sha(path):
        return hashlib.sha256(path.read_bytes()).hexdigest()

    def read(path):
        if path not in docs:
            docs[path] = json.loads((ROOT / path).read_text(encoding='utf-8-sig'))
        return docs[path]

    def pointer(doc, ptr):
        for token in ptr.split('/')[1:]:
            key = token.replace('~1', '/').replace('~0', '~')
            doc = doc[int(key)] if isinstance(doc, list) else doc[key]
        return doc

    def refs(value):
        if isinstance(value, dict):
            if 'path' in value and ('sha256' in value or 'json_pointer' in value or 'line' in value):
                path = value['path']
                target = (ROOT / path).resolve()
                require(not pathlib.Path(path).is_absolute() and target.is_relative_to(ROOT), 'Nonportable reference: ' + path)
                if 'sha256' in value:
                    if path not in pinned:
                        if value.get('hash_basis') == 'git_blob_bytes':
                            current = subprocess.check_output(['git', 'hash-object', '--path=' + path, path], cwd=ROOT, text=True).strip()
                            require(current == value['git_blob'], 'Working-tree source differs from blob: ' + path)
                            content = subprocess.check_output(['git', 'cat-file', 'blob', value['git_blob']], cwd=ROOT)
                            pinned[path] = hashlib.sha256(content).hexdigest()
                        else:
                            require(path.startswith(PACKAGE), 'Unspecified legacy pin hash basis: ' + path)
                            pinned[path] = sha(target)
                    actual = pinned[path]
                    require(actual == value['sha256'], 'Local pin mismatch: ' + path)
                if 'json_pointer' in value:
                    pointer(read(path), value['json_pointer'])
                if 'line' in value:
                    require(1 <= value['line'] <= len(target.read_text(encoding='utf-8-sig').splitlines()), 'Line out of range: ' + path)
            for child in value.values():
                refs(child)
        elif isinstance(value, list):
            for child in value:
                refs(child)

    manifest = read(PACKAGE + 'profile-inputs.json')
    vectors = read(PACKAGE + 'vectors.json')
    require(manifest['package_version'] == vectors['package_version'] == 'CP6-S01-CONTRACT-INPUTS/1.0.0', 'Package version mismatch')
    require(manifest['runtime_acceptance'] == vectors['execution'] == 'NOT_RUN', 'Unexpected runtime result')
    refs(manifest)
    refs(vectors)
    sources = {s['id']: s for s in manifest['sources']}
    require(len(sources) == len(manifest['sources']), 'Duplicate source id')
    source_docs = {}
    for sid, s in sources.items():
        selector = s['selector']
        entry = pointer(read(selector['path']), selector['json_pointer'])
        original_path = entry.get('source_path') or entry.get('path') or entry.get('absolute_path')
        original_hash = entry.get('source_sha256') or entry.get('sha256')
        require(s['original_sha256'] == original_hash, 'Original selector mismatch: ' + sid)
        variants = [('original', original_path, original_hash)]
        if s['composed_sha256']:
            require(s['composed_sha256'] == entry['derived_sha256'], 'Composed selector mismatch: ' + sid)
            require(s['patch_operation_count'] == entry['operations'], 'Patch count drift: ' + sid)
            require(s['source_finding_ids'] == entry['source_finding_ids'], 'Finding set drift: ' + sid)
            require([x['path'] for x in s['patch_bundles']] == ['docs/design-handbook/' + p for p in entry['patch_bundles']], 'Patch set drift: ' + sid)
            variants.append(('composed', entry['local_derived_path'], entry['derived_sha256']))
        for variant, path, expected in variants:
            p = pathlib.Path(path)
            actual = sha(p)
            require(actual == expected, f'Physical SHA mismatch: {sid}/{variant}')
            if variant == 'composed':
                require(p.stat().st_size == s['composed_bytes'], 'Composed byte count drift: ' + sid)
            physical.append({'source_id': sid, 'variant': variant, 'expected_sha256': expected, 'actual_sha256': actual})
            if p.suffix == '.json':
                source_docs[(sid, variant)] = json.loads(p.read_text(encoding='utf-8-sig'))

    profiles = {p['id']: p for p in manifest['profiles']}
    require(len(profiles) == len(manifest['profiles']), 'Duplicate profile id')
    require(set(c['profile_id'] for c in vectors['cases']) == set(profiles), 'Profile vector coverage mismatch')
    require(len({c['id'] for c in vectors['cases']}) == len(vectors['cases']), 'Duplicate vector id')
    revision_ids = set()
    for pid, p in profiles.items():
        for selected in p['selected_sources']:
            require(selected['source_id'] in sources, 'Unknown source: ' + pid)
            if selected['variant'] == 'composed':
                require(sources[selected['source_id']]['composed_sha256'] is not None, 'Missing composed source: ' + pid)
        identity = p['schema_or_type_identity']
        if 'definition' in identity:
            original = source_docs[(identity['source_id'], identity['variant'])]
            require(identity['definition'] == pointer(original, identity['json_pointer']), 'Changed native definition: ' + pid)
            require(identity['schema_id'] == original['$id'], 'Changed schema identity: ' + pid)
            if pid.startswith(('fin-credit-', 'fin-inv-')):
                require(bool(p['native_field_bindings']), 'Missing native field bindings: ' + pid)
                for field in p['native_field_bindings']:
                    bound = pointer(original, field)
                    require(bound == identity['definition'] or bound.get('$ref') == '#' + identity['json_pointer'], 'Inexact native field binding: ' + pid + field)
        if pid.startswith('erp-credit-'):
            sid, variant = ('erp-registry-frozen', 'original') if pid.endswith('r8') else ('erp-registry-current', 'composed')
            require(identity['type_registry_sha256'] == sources[sid][variant + '_sha256'], 'ERP registry identity mismatch')
        for case in p['revision_acceptance_refs']:
            require(pointer(read(case['path']), case['json_pointer'])['id'] == case['case_id'], 'Wrong revision case pointer')
            revision_ids.add(case['case_id'])
    require(revision_ids == set(read('docs/development/s01-input-index.json')['runtime_case_ids']), 'Revision case set drift')
    require(set(sources['composed-14']['source_finding_ids']) == {'PLAN-IF-004', 'PLAN-IF-005'}, 'ATP composed patches incomplete')
    require(sum(len(p.get('required_null_fields', [])) for p in profiles.values()) == 7, 'NCR seven fields not preserved')
    lead = profiles['crm-lead-cp20']['owner_id_field_schema']
    native_lead = source_docs[(lead['source_id'], 'original')]
    require(lead['definition'] == pointer(native_lead, lead['json_pointer']) and lead['required'], 'LEAD field definition drift')

    original_cases = read('docs/design-handbook/remediation/assets/core-types/numeric-validation.json')['cases']
    for pos, original in enumerate(original_cases):
        c = next(c for c in vectors['cases'] if c['id'] == f'NUM-{pos + 1:03d}')
        require(all(c[k] == original[k] for k in ('input', 'wire_type')), 'Original numeric input drift: ' + c['id'])
        require(c['expected'] == {k: original[k] for k in ('accepted', 'pair') if k in original}, 'Original numeric expectation drift: ' + c['id'])
        require(c['source_case_id'] is None and c['source_ref']['json_pointer'] == f'/cases/{pos}', 'Invented original case identity')

    # Independent expected-data sanity, explicitly not execution of future C#/TS consumers.
    def no_duplicates(pairs):
        obj = {}
        for k, v in pairs:
            if k in obj:
                raise ValueError('duplicate key')
            obj[k] = v
        return obj

    def strict(text):
        return json.loads(text, object_pairs_hook=no_duplicates, parse_int=decimal.Decimal,
                          parse_float=decimal.Decimal, parse_constant=lambda x: (_ for _ in ()).throw(ValueError(x)))

    def semantic(value):
        if isinstance(value, dict):
            return ('object', sorted((k, semantic(v)) for k, v in value.items()))
        if isinstance(value, list):
            return ('array', [semantic(v) for v in value])
        return (type(value).__name__, value)

    checked = collections.Counter()
    for c in vectors['cases']:
        pid, op, value, expected = c['profile_id'], c['operation'], c['input'], c['expected']
        require('result' not in c and 'result' not in expected, 'Historical execution result leaked: ' + c['id'])
        if op == 'lexical':
            definition = profiles[pid]['schema_or_type_identity']['definition']
            accepted = isinstance(value, str) and re.fullmatch(definition['pattern'], value) is not None
            require(accepted == expected['accepted'], 'Native lexical expectation mismatch: ' + c['id'])
            if accepted and expected.get('pair'):
                pair = expected['pair']
                require(decimal.Decimal(value) == decimal.Decimal(pair.get('unscaledValue', pair.get('unscaledDigits'))).scaleb(-pair['scale'], context=decimal.Context(prec=256)), 'Exact pair mismatch: ' + c['id'])
                if 'originalWire' in pair:
                    require(pair['originalWire'] == value, 'Original wire lost: ' + c['id'])
        elif op == 'strict_json_parse':
            try:
                strict(value)
                accepted = True
            except (ValueError, decimal.InvalidOperation):
                accepted = False
            require(accepted == expected['accepted'], 'Strict JSON expected-data mismatch: ' + c['id'])
        elif op in ('required_null_property', 'required_null_field_projection'):
            obj = strict(value['raw_json'])
            accepted = value['required_property'] in obj and obj[value['required_property']] is None
            require(accepted == expected['accepted'], 'Null field expected-data mismatch: ' + c['id'])
        elif op == 'json_semantic_equality':
            equal = semantic(strict(value['left_raw_json'])) == semantic(strict(value['right_raw_json']))
            require(equal == expected['equal'], 'Equality expected-data mismatch: ' + c['id'])
        elif op == 'native_registry_identity':
            identity = profiles[pid]['schema_or_type_identity']
            accepted = value == {'codec_namespace': identity['csharp_namespace'], 'schema_file': 'ERP-CREDIT-01_TYPE-REGISTRY.json', 'schema_sha256': identity['type_registry_sha256']}
            require(accepted == expected['accepted'], 'Registry expected-data mismatch: ' + c['id'])
        elif op == 'native_schema_identity':
            identity = profiles[pid]['schema_or_type_identity']
            accepted = value == {'openapi': identity['openapi'], 'title': identity['info']['title'], 'version': identity['info']['version'], 'schema_root': identity['schema_root'], 'schema_sha256': sources[identity['source_id']]['composed_sha256']}
            require(accepted == expected['accepted'], 'Schema route expected-data mismatch: ' + c['id'])
        elif op == 'original_utf8_retention':
            a, b = value['original_utf8'].encode('utf-8'), value['replay_utf8'].encode('utf-8')
            require((a == b) == expected['bytes_equal'], 'Byte equality mismatch: ' + c['id'])
            ah, bh = hashlib.sha256(a).hexdigest(), hashlib.sha256(b).hexdigest()
            require(ah == expected['original_sha256'] and bh == expected['replay_sha256'] and (ah == bh) == expected['digest_equal'], 'Byte digest mismatch: ' + c['id'])
        elif op not in ('required_owner_id_field_projection', 'native_type_binding'):
            errors.append('Unknown operation: ' + op)
        checked[op] += 1

    before = json.loads(subprocess.check_output(['git', 'show', BASE + ':docs/development/roadmap.json'], cwd=ROOT))
    after = read('docs/development/roadmap.json')
    for key in ('targets', 'revision_acceptances', 'sources', 'baseline_totals', 'implementation_assessment', 'target_task_coverage', 'work_packages'):
        require(after[key] == before[key], 'Unrelated/business ledger changed: ' + key)
    require([t for t in after['implementation_tasks'] if t['id'] != 'S01-01'] == [t for t in before['implementation_tasks'] if t['id'] != 'S01-01'], 'Another task changed')
    links = 0
    for p in [ROOT / (PACKAGE + 'README.md'), ROOT / 'docs/development/evidence/S01-01/README.md']:
        text = re.sub(r'```.*?```', '', p.read_text(encoding='utf-8'), flags=re.S)
        for link in re.findall(r'\[[^\]]+\]\(([^)]+)\)', text):
            if link.startswith(('http:', 'https:', '#')):
                continue
            require((p.parent / link.split('#')[0]).resolve().exists(), 'Missing Markdown link: ' + link)
            links += 1
    result = {'task_id': 'S01-01', 'observed_at': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'result': 'FAIL' if errors else 'PASS', 'scope': 'Input provenance, exact expected data, portable references and unchanged business state only; no consumer implementation executed',
        'baseline_sha': BASE, 'execution_head_sha': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
        'input_files_sha256': {str(p.relative_to(ROOT)).replace('\\', '/'): sha(p) for p in [ROOT / (PACKAGE + 'profile-inputs.json'), ROOT / (PACKAGE + 'vectors.json'), pathlib.Path(__file__)]},
        'source_count': len(sources), 'profile_count': len(profiles), 'vector_count': len(vectors['cases']),
        'physical_hash_checks': physical, 'local_pin_count': len(pinned), 'local_markdown_links': links,
        'static_expected_data_checks': dict(checked), 'manually_reviewed_operations': ['required_owner_id_field_projection', 'native_type_binding'],
        'runtime_case_refs': len(revision_ids), 'business_tests_executed': False, 'consumer_runtime': 'NOT_RUN', 'errors': errors}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in result.items() if k not in ('physical_hash_checks', 'input_files_sha256')}, ensure_ascii=False))
    return bool(errors)


if __name__ == '__main__':
    raise SystemExit(main())
