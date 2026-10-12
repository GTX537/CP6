"""Read-only S01-01 preparation audit; never executes archived code or SQL."""

import argparse
import datetime
import hashlib
import json
import pathlib
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=pathlib.Path, default=pathlib.Path(__file__).resolve().parents[4])
    parser.add_argument('--out', type=pathlib.Path, required=True)
    args = parser.parse_args()
    root = args.root.resolve()
    errors = []
    checks = []

    def read(path):
        return json.loads((root / path).read_text(encoding='utf-8-sig'))

    def check_file(path, expected, identity):
        try:
            data = path.read_bytes()
            actual = hashlib.sha256(data).hexdigest()
            passed = actual == expected
            checks.append(dict(identity=identity, expected_sha256=expected,
                               actual_sha256=actual, bytes=len(data),
                               result='PASS' if passed else 'FAIL'))
            if not passed:
                errors.append(f'SHA-256 mismatch: {identity}')
        except OSError as exc:
            errors.append(f'Cannot read {identity}: {type(exc).__name__}')
            checks.append(dict(identity=identity, expected_sha256=expected, result='MISSING'))

    index_path = 'docs/development/s01-input-index.json'
    index = read(index_path)
    manifest = read('docs/design-handbook/remediation/assets/composed-derivatives.json')
    for pos, entry in enumerate(index['sources']):
        identity = f'{index_path}#/sources/{pos}'
        if entry not in manifest['sources']:
            errors.append(f'Selected entry differs from composed manifest: {identity}')
        for path_key, hash_key in [('source_path', 'source_sha256'), ('local_derived_path', 'derived_sha256')]:
            check_file(pathlib.Path(entry[path_key]), entry[hash_key], identity + '/' + path_key)

    for entry in index['local_asset_pins']:
        check_file(root / entry['path'], entry['sha256'], entry['path'])

    ddl = [e for e in index['sources'] if pathlib.Path(e['source_path']).name == 'PLAN-ATP-POSTGRESQL-DDL.sql']
    if len(ddl) != 1 or set(ddl[0]['source_finding_ids']) != {'PLAN-IF-004', 'PLAN-IF-005'}:
        errors.append('ATP PostgreSQL DDL must retain PLAN-IF-004 and PLAN-IF-005 together')

    numeric = read('docs/design-handbook/remediation/assets/core-types/numeric-validation.json')
    if len(numeric['cases']) != 23:
        errors.append('Expected the original 23 numeric/unit source cases')
    catalog = read('docs/design-handbook/remediation/08-acceptance-catalog.json')
    runtime_ids = {c['id'] for c in catalog['cases'] if c['verification_level'].startswith('REQUIRED_RUNTIME')}
    if set(index['runtime_case_ids']) - runtime_ids:
        errors.append('Source selector contains an unknown runtime acceptance identity')

    ledger = read('docs/development/roadmap.json')
    task = next(t for t in ledger['implementation_tasks'] if t['id'] == 'S01-01')
    report = {
        'task_id': 'S01-01',
        'scope': 'PREPARATION_ONLY: source selector, original/composed byte hashes, local pins and acceptance identity references; no implementation or business acceptance',
        'observed_at': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'source_sha': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip(),
        'python_version': sys.version.split()[0],
        'result': 'FAIL' if errors else 'PASS',
        'selected_source_count': len(index['sources']),
        'physical_file_checks': checks,
        'numeric_source_case_count': len(numeric['cases']),
        'selected_runtime_case_count': len(index['runtime_case_ids']),
        'entry_snapshot': {k: task[k] for k in ('state', 'entry_gate', 'assignment', 'dependencies', 'exclusive_resources')},
        'entry_snapshot_note': 'Observed registration only. This audit cannot confirm a coordination window or claim the task.',
        'business_tests_executed': False,
        'runtime_acceptance': 'NOT_RUN',
        'errors': errors,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: report[k] for k in ('task_id', 'result', 'selected_source_count', 'numeric_source_case_count', 'selected_runtime_case_count', 'errors')}, ensure_ascii=False))
    return bool(errors)


if __name__ == '__main__':
    raise SystemExit(main())
