"""Targeted checker acceptance tests; writes only the explicitly selected scratch dir."""
import argparse
import copy
import json
import pathlib
import shutil
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=pathlib.Path, required=True)
    parser.add_argument('--archive-root', type=pathlib.Path, required=True)
    parser.add_argument('--scratch', type=pathlib.Path, required=True)
    args = parser.parse_args()
    script_dir = pathlib.Path(__file__).resolve().parent
    checker = script_dir / 'verify-wp05-supplement.py'
    proposal = json.loads((script_dir / 'wp05-supplement-proposed.json').read_text(encoding='utf-8'))
    work = args.scratch.resolve()
    work.mkdir(parents=True, exist_ok=True)
    fixture = work / 'root'
    fixture.mkdir(exist_ok=True)
    common = subprocess.check_output(['git', 'rev-parse', '--git-common-dir'], cwd=args.root, text=True).strip()
    common_path = (args.root / common).resolve()
    (fixture / '.git').write_text('gitdir: ' + common_path.as_posix() + '\n', encoding='utf-8')
    bundle = pathlib.Path('docs/development/task-deliveries/WP05-01')
    for name in ('basis-manifest.json', 'README.md', 'sources.json', 'cases.json', 'normative-coverage.json', 'coverage.md', 'adoption.json'):
        dest = fixture / bundle / name
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(args.root / bundle / name, dest)
    outcomes = []

    def run(name, change=None, expect_pass=False, archive=None, text_transform=None, overlap=False):
        value = copy.deepcopy(proposal)
        if change:
            change(value)
        src = work / (name + '.json')
        raw = json.dumps(value, ensure_ascii=False, indent=2) + '\n'
        if text_transform:
            raw = text_transform(raw)
        src.write_text(raw, encoding='utf-8', newline='\n')
        evidence = src if overlap else work / (name + '.result.json')
        before = src.read_bytes()
        proc = subprocess.run([sys.executable, '-X', 'utf8', str(checker), '--root', str(fixture), '--archive-root', str(archive or args.archive_root), '--supplement', str(src), '--out', str(evidence)], capture_output=True, text=True, encoding='utf-8')
        try:
            report = json.loads(proc.stdout)
        except json.JSONDecodeError:
            report = {'result': 'NO_REPORT', 'errors': [proc.stderr.strip()]}
        correct = (proc.returncode == 0 and report.get('result') == 'PASS') if expect_pass else (proc.returncode == 1 and report.get('result') == 'FAIL' and bool(report.get('errors')))
        correct = correct and src.read_bytes() == before
        outcomes.append({'name': name, 'test_result': 'PASS' if correct else 'FAIL', 'expected_checker_result': 'PASS' if expect_pass else 'FAIL', 'checker_exit': proc.returncode, 'checker_result': report.get('result'), 'errors': report.get('errors', [])})
        print(name + ': ' + outcomes[-1]['test_result'])

    run('positive', expect_pass=True)
    run('wrong_cell_text', lambda x: x['rows'][0]['source_refs'][0]['cells'].__setitem__('P10', 'forged'))
    run('missing_cell', lambda x: x['rows'][0]['source_refs'][0]['cells'].__setitem__('ZZ9999', 'forged'))
    run('wrong_excerpt', lambda x: x['rows'][18]['source_refs'][0].__setitem__('excerpt', 'forged'))
    run('wrong_line_range', lambda x: x['rows'][18]['source_refs'][0].__setitem__('line_start', 112))
    run('wrong_source_hash', lambda x: x['rows'][18]['source_refs'][0].__setitem__('sha256', '0' * 64))
    run('git_revision_drift', lambda x: x['rows'][33]['source_refs'][0].__setitem__('git_revision', x['confirmed_main']))
    run('unknown_prior_id', lambda x: x['rows'][0]['prior_basis_ids'].append('invented-old-AC'))
    run('runtime_pass', lambda x: x['rows'][0].__setitem__('runtime_state', 'PASS'))
    run('approval_record', lambda x: x.__setitem__('decision_record', {'approved': True}))
    run('row_approval', lambda x: x['rows'][0].__setitem__('approval_state', 'ADOPTED'))
    run('row_evidence', lambda x: x['rows'][0].__setitem__('execution_evidence', ['fake-pass']))
    run('missing_row', lambda x: x['rows'].pop())
    run('changed_counts', lambda x: x['preserve_existing_counts'].__setitem__('recovered', 222))
    run('duplicate_json_key', text_transform=lambda text: text.replace('"status": "PROPOSED",', '"status": "PROPOSED", "status": "PROPOSED",', 1))
    run('output_overlaps_input', overlap=True)
    run('missing_archive', archive=work / 'nonexistent-archive')
    run('unregistered_source', lambda x: x['rows'][0]['source_refs'][0].__setitem__('path', '../outside.xlsx'))
    old_manifest = fixture / bundle / 'basis-manifest.json'
    original = old_manifest.read_bytes()
    old_manifest.write_bytes(original + b' ')
    run('changed_base_manifest')
    old_manifest.write_bytes(original)
    old_file = fixture / bundle / 'README.md'
    original = old_file.read_bytes()
    old_file.write_bytes(original + b' changed')
    run('changed_old_artifact')
    old_file.write_bytes(original)
    git_path = pathlib.Path(proposal['rows'][33]['source_refs'][0]['path'])
    (fixture / git_path).parent.mkdir(parents=True, exist_ok=True)
    (fixture / git_path).write_text('Current worktree content deliberately differs from pinned Git blob.', encoding='utf-8')
    run('git_pin_ignores_same_name_current_file', expect_pass=True)
    source_registry = json.loads((fixture / bundle / 'sources.json').read_text(encoding='utf-8'))
    registered = next(s for s in source_registry['sources'] if s['path'].endswith('assets/shared-consumers/acceptance.json'))
    source = json.loads(subprocess.check_output(['git', 'cat-file', 'blob', registered['git_revision'] + ':' + registered['path']], cwd=fixture))
    ref = {'path': registered['path'], 'sha256': registered['sha256'], 'git_revision': registered['git_revision'], 'json_pointer': '/cases/0', 'expected_value': source['cases'][0]}
    run('json_pointer_positive', lambda x: x['rows'][0]['source_refs'].append(copy.deepcopy(ref)), expect_pass=True)
    wrong_ref = dict(ref, expected_value={'forged': True})
    run('json_pointer_wrong_value', lambda x: x['rows'][0]['source_refs'].append(copy.deepcopy(wrong_ref)))
    invalid_ref = dict(ref, json_pointer='/cases/-1')
    run('json_pointer_invalid_index', lambda x: x['rows'][0]['source_refs'].append(copy.deepcopy(invalid_ref)))
    failed = [r for r in outcomes if r['test_result'] == 'FAIL']
    summary = {'scope': 'SUPPLEMENT_CHECKER_ONLY; no business tests', 'result': 'FAIL' if failed else 'PASS', 'tests': len(outcomes), 'failed': len(failed), 'cases': outcomes}
    (work / 'test-summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps({'result': summary['result'], 'tests': len(outcomes), 'failed': len(failed)}))
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
