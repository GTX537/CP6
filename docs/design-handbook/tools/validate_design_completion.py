"""Validate design-revision traceability and bounded asset edits, not CP6 runtime.

Never executes archived SQL/code and never modifies originals or generated copies.
"""
import argparse
import copy
import hashlib
import json
import re
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path


def sha(data):
    return hashlib.sha256(data).hexdigest()


def pointer(value, path):
    if not path:
        return value
    for token in path.removeprefix('/').split('/'):
        key = token.replace('~1', '/').replace('~0', '~')
        value = value[int(key)] if isinstance(value, list) else value[key]
    return value


def apply_operations(source, operations):
    """Apply explicit expected-value edits in memory, never to an archived file."""
    current = source.decode('utf-8-sig')
    document = None
    for op in operations:
        if op['op'] == 'replace_text':
            if document is not None:
                current = json.dumps(document, ensure_ascii=False, indent=2) + '\n'
                document = None
            count = current.count(op['before'])
            if count != op['expected_count']:
                raise ValueError(f'text occurrence count {count} != {op["expected_count"]}')
            current = current.replace(op['before'], op['after'])
        elif op['op'] in ('replace_json', 'remove_json', 'add_json'):
            if document is None:
                document = json.loads(current)
            parent_path, _, token = op['path'].rpartition('/')
            parent = pointer(document, parent_path)
            key = token.replace('~1', '/').replace('~0', '~')
            if isinstance(parent, list):
                key = int(key)
            if op['op'] == 'add_json':
                if isinstance(parent, list) or key in parent:
                    raise ValueError('add_json requires absent object key')
                parent[key] = copy.deepcopy(op['after'])
            else:
                if parent[key] != op['before']:
                    raise ValueError(f'preimage differs: {op["path"]}')
                if op['op'] == 'remove_json':
                    del parent[key]
                else:
                    parent[key] = copy.deepcopy(op['after'])
        else:
            raise ValueError(f'unknown patch operation: {op["op"]}')
    return (json.dumps(document, ensure_ascii=False, indent=2) + '\n' if document is not None else current).encode('utf-8')


def normalized_patch(patch):
    """Normalize the literal-patch design format without changing its source asset."""
    if 'operations' in patch:
        return patch
    if patch.get('mode') == 'LITERAL_REPLACE_DATA_ONLY':
        return {**patch, 'source_finding_ids': [patch['finding']], 'operations': [{'op':'replace_text', 'before':patch['before'], 'after':patch['after'], 'expected_count':patch['expected_occurrences']}]}
    raise ValueError('Unsupported bounded patch format')


def walk(value, path=''):
    yield path, value
    children = enumerate(value) if isinstance(value, list) else value.items() if isinstance(value, dict) else []
    for key, child in children:
        yield from walk(child, path + '/' + str(key).replace('~', '~0').replace('/', '~1'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--handbook', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    root = args.handbook.resolve()
    errors, checks, patch_results = [], [], []
    files = {}

    def read(path):
        path = Path(path)
        if path not in files:
            data = path.read_bytes()
            files[path] = (data, sha(data))
        return files[path]

    def record(name, action):
        try:
            detail = action()
            checks.append({'check': name, 'result': 'PASS', 'detail': detail})
        except Exception as exc:
            errors.append({'check': name, 'error': str(exc)})

    register = json.loads((root / 'remediation/00-disposition-register.json').read_text(encoding='utf-8'))
    sources = []
    for src in register['source_registers']:
        def source_check(src=src):
            data, digest = read(root / src['path'])
            assert digest == src['sha256'], 'source register SHA changed'
            findings = json.loads(data.decode('utf-8-sig'))['findings']
            sources.extend(f.get('finding_id', f.get('id')) for f in findings)
            assert len(findings) == src['count']
            return len(findings)
        record('source-register:' + src['path'], source_check)

    issues = register['issues']
    mapped = [x for issue in issues for x in issue['source_finding_ids']]

    def coverage_check():
        assert Counter(mapped) == Counter(sources), 'missing/duplicate source dispositions'
        assert len(sources) == register['source_finding_count']
        assert len({i['issue_id'] for i in issues}) == len(issues), 'duplicate canonical issue'
        coverage = json.loads((root / 'remediation/10-target-coverage.json').read_text(encoding='utf-8'))['targets']
        expected = {i['target_id'] for i in json.loads((root / 'indexes/required-inputs.json').read_text(encoding='utf-8'))['targets']}
        assert {i['target_id'] for i in coverage} == expected and len(coverage) == 125
        assert all((root / t['source_module']).is_file() for t in coverage)
        return {'source_findings': len(sources), 'canonical_issues': len(issues), 'targets': len(coverage)}
    record('complete-traceability', coverage_check)

    def acceptance_check():
        catalogue=json.loads((root/'remediation/08-acceptance-catalog.json').read_text(encoding='utf-8'))
        cases={c['id']:c for c in catalogue['cases']}
        assert len(cases)==len(catalogue['cases'])==catalogue['case_count']
        expected={id for i in issues for id in i['acceptance_ids']}
        assert set(cases)==expected, 'acceptance references differ'
        for c in cases.values():
            assert c['input'] and c['expected'] and c['expected_writes']
            if c['verification_level'].startswith('REQUIRED_RUNTIME'):assert c['result']=='NOT_RUN'
        assert register['status']=='DESIGN_REVISION_COMPLETE_RUNTIME_NOT_RUN'
        return {'cases':len(cases),'business_executions':0}
    record('acceptance-catalog-and-runtime-status',acceptance_check)

    def asset_check():
        manifest=json.loads((root/'remediation/assets/manifest.json').read_text(encoding='utf-8'))
        for asset in manifest['assets']:
            data,digest=read(root/asset['path'])
            assert digest==asset['sha256'] and len(data)==asset['bytes'],asset['path']
        composed=json.loads((root/'remediation/assets/composed-derivatives.json').read_text(encoding='utf-8'))
        for source in composed['sources']:
            assert read(source['source_path'])[1]==source['source_sha256']
            data,digest=read(source['local_derived_path'])
            assert digest==source['derived_sha256'] and len(data)==source['derived_bytes']
        codec=json.loads((root/'remediation/assets/commercial-fixtures/codec-compile-validation.json').read_text(encoding='utf-8'))
        for proposal in codec['proposals']:
            actual=next(s for s in composed['sources'] if s['source_sha256']==proposal['original_source_sha256'])
            assert actual['derived_sha256']==proposal['proposed_sha256'], 'compile evidence does not match final codec'
            result=codec['results'][proposal['language']]
            assert result['exit_code']==0 and read(result['log'])[1]==result['log_sha256']
        atp=json.loads((root/'remediation/assets/core-types/atp-stableid-validation.json').read_text(encoding='utf-8'))
        for proof in atp['sources']:
            actual=next(s for s in composed['sources'] if s['source_sha256']==proof['source_sha256'])
            assert actual['derived_sha256']==proof['derived_sha256'], 'ATP lexical/compile evidence differs from composed successor'
        for proof in atp['checks']:
            assert proof['exit_code']==0 and read(proof['log'])[1]==proof['log_sha256']
        return {'assets':len(manifest['assets']),'composed_originals':len(composed['sources'])}
    record('asset-manifest-and-composed-derivatives',asset_check)

    def materialized_recipe_check():
        ops=json.loads((root/'remediation/assets/operations-fixtures/manifest.json').read_text(encoding='utf-8'))
        for src in ops['sources']:
            assert read(src['source_path'])[1]==src['sha256']
        for product in ops['products']:
            data,digest=read(product['path'])
            assert digest==product['sha256'] and len(data)==product['bytes']
        recipe=json.loads((root/'remediation/assets/planning-fixtures/generation-recipe.json').read_text(encoding='utf-8'))
        for path,src in recipe['sources'].items():assert read(path)[1]==src['sha256']
        planned=[recipe['package']]+recipe['other_materialized_outputs']
        for product in planned:
            data,digest=read(Path('D:/CP6-archives/design-revision-20261011-checks/planning')/product['filename'])
            assert digest==product['sha256'] and len(data)==product['bytes']
        return {'operations_products':len(ops['products']),'planning_products':len(planned),'check':'Exact materialized bytes and original-source pins only; generator semantic results reused from recorded reports'}
    record('large-recipes-materialized-byte-integrity',materialized_recipe_check)

    for issue in issues:
        def issue_check(issue=issue):
            assert issue['classification'] in {'DESIGN_GAP', 'ARTIFACT_CONFLICT', 'ADOPTION_PENDING', 'INTENTIONAL_DIFFERENCE', 'SUPERSEDED', 'DOCUMENT_STALE'}, 'not yet classified'
            assert issue['decision'] and issue['outputs'] and issue['acceptance_ids'], 'decision/output/acceptance missing'
            assert issue['supersedes_scope'] and issue['preserved_constraints'] and issue['dependencies']
            assert issue['actual_adoption_status']=='UNPROVEN'
            assert issue['design_status'] in {'DESIGN_RESOLVED', 'BOUNDARY_RECORDED', 'SUPERSESSION_RECORDED'}, 'design not closed'
            assert issue['verification_status'] != 'PRODUCTION_PASS', 'invalid design-only production claim'
            for output in issue['outputs']:
                assert (root / output).is_file(), 'output not present: ' + output
            for evidence in issue['evidence']:
                data, digest = read(evidence['source_path'])
                assert digest == evidence['sha256'], 'original source SHA mismatch'
                lines = data.decode('utf-8-sig').splitlines()
                line = evidence.get('line')
                if isinstance(line, int):
                    assert 1 <= line <= len(lines), 'source line out of bounds'
                    assert evidence.get('end_line',line)<=len(lines), 'source range out of bounds'
                if 'lines' in evidence:
                    assert all(isinstance(n,int) and 1<=n<=len(lines) for n in evidence['lines'])
                if 'json_pointer' in evidence:
                    pointer(json.loads(data.decode('utf-8-sig')),evidence['json_pointer'])
            return {'sources': len(issue['evidence']), 'classification': issue['classification']}
        record('disposition:' + issue['issue_id'], issue_check)

    for bundle in sorted((root / 'remediation/assets').rglob('*patches.json')):
        spec = json.loads(bundle.read_text(encoding='utf-8'))
        for raw_patch in spec.get('patches', []):
            patch = normalized_patch(raw_patch)
            def patch_check(patch=patch):
                before, digest = read(patch['source_path'])
                assert digest == patch['source_sha256'], 'patch source SHA mismatch'
                after = apply_operations(before, patch['operations'])
                assert before != after, 'patch does not change source'
                if 'expected_derived_sha256' in patch:
                    assert sha(after) == patch['expected_derived_sha256'], 'derived output SHA differs'
                if 'expected_derived_bytes' in patch:
                    assert len(after) == patch['expected_derived_bytes'], 'derived output byte length differs'
                row = {'source': patch['source_path'], 'source_sha256': digest, 'derived_sha256': sha(after), 'operations': len(patch['operations']), 'finding_ids': patch['source_finding_ids'], 'result': 'PASS'}
                if Path(patch['source_path']).suffix == '.json':
                    doc = json.loads(after)
                    if isinstance(doc, dict) and 'openapi' in doc:
                        refs = [v['$ref'] for _, v in walk(doc) if isinstance(v, dict) and str(v.get('$ref', '')).startswith('#/')]
                        for ref in refs:
                            pointer(doc, ref[1:])
                        row['local_refs_resolved'] = len(refs)
                patch_results.append(row)
                return row
            record('bounded-patch:' + bundle.relative_to(root).as_posix() + ':' + Path(patch['source_path']).name, patch_check)

    reused=[]
    for p in sorted((root/'remediation/assets').rglob('*validation*.json')):
        value=json.loads(p.read_text(encoding='utf-8'))
        reused.append({'path':p.relative_to(root).as_posix(),'sha256':sha(p.read_bytes()),'result':value.get('result',value.get('status','SEE_ORIGINAL_REPORT')),'scope':'Reused exact recorded local static/compile check; not re-executed by this aggregate validator'})
    report = {'as_of_utc': datetime.now(timezone.utc).isoformat(), 'scope': 'Design traceability, source integrity, exact bounded patch preconditions and declared structural checks only. No archived programs, business tests, database or production execution.', 'command':'python docs/design-handbook/tools/validate_design_completion.py --handbook docs/design-handbook --output docs/design-handbook/evidence/design-completion-validation.json', 'checks': checks, 'errors': errors, 'patches': patch_results, 'source_files_checked': len(files), 'reused_local_results':reused,'business_tests_executed': False, 'runtime_acceptance': 'NOT_RUN', 'result': 'PASS' if not errors else 'FAIL'}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'result': report['result'], 'checks_passed': len(checks), 'errors': len(errors), 'patches': len(patch_results), 'first_errors': errors[:5]}, ensure_ascii=False))
    return 1 if errors else 0


if __name__ == '__main__':
    raise SystemExit(main())
