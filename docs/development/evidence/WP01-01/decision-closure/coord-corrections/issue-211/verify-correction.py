"""Read-only, reproducible structural check for the Issue 211 design correction."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--archive-root', type=Path, required=True)
    args = parser.parse_args()
    root = args.root.resolve()
    relative = Path('docs/development/evidence/WP01-01/decision-closure')
    out = root / relative
    here = out / 'coord-corrections/issue-211'
    oldroot = here / 'superseded'
    load = lambda p: json.loads(p.read_bytes())
    digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
    before = load(oldroot / relative / 'postgresql-mapping.json')
    current = load(out / 'postgresql-mapping.json')
    old = {e['entity']: e for e in before['entities']}
    tables = {e['entity']: e for e in current['entities']}
    affected = {'Project', 'Iteration', 'IterationInput', 'CandidateRef',
                'ReviewRound', 'ExternalRequestReceipt', 'OwnerKey'}
    assert {n for n in tables if tables[n] != old[n]} == affected
    assert {k: v for k, v in before.items() if k != 'entities'} == {
        k: v for k, v in current.items() if k != 'entities'}
    checks = 2
    sources = {}

    def pins(value):
        nonlocal checks
        if isinstance(value, dict):
            if all(k in value for k in ('path', 'sha256', 'bytes')):
                key = (value.get('repository', 'Main'), value['path'])
                if key not in sources:
                    base = args.archive_root if key[0] == 'Archive' else root
                    data = (base / key[1]).read_bytes()
                    sources[key] = data
                data = sources[key]
                assert hashlib.sha256(data).hexdigest() == value['sha256']
                assert len(data) == value['bytes']
                if 'excerpt' in value:
                    lines = data.decode('utf-8-sig').splitlines()
                    assert '\n'.join(lines[value['line_start']-1:value['line_end']]) == value['excerpt']
                checks += 1
            for v in value.values():
                pins(v)
        elif isinstance(value, list):
            for v in value:
                pins(v)

    for name, table in tables.items():
        cols = {c['name']: c for c in table['columns']}
        assert len(cols) == len(table['columns'])
        assert all(not cols[c]['nullable'] for c in table['primary_key']['columns'])
        for item in [table['primary_key'], *table['unique_constraints'],
                     *table['indexes'], *table['checks']]:
            assert set(item['columns'] + item.get('predicate_columns', [])) <= set(cols)
            checks += 1
        for f in table.get('fields', []):
            assert set(f['physical_columns']) <= set(cols)
            checks += 1
        for f in table['foreign_keys']:
            target = tables[f['target_entity']]
            targetcols = {c['name']: c for c in target['columns']}
            keys = [target['primary_key']['columns']] + [
                u['columns'] for u in target['unique_constraints'] if not u.get('predicate')]
            assert f['target_columns'] in keys
            assert len(f['columns']) == len(f['target_columns'])
            assert all(cols[a]['postgresql'] == targetcols[b]['postgresql']
                       for a, b in zip(f['columns'], f['target_columns']))
            assert 'tenant_id' in f['columns'] and 'tenant_id' in f['target_columns']
            checks += 1
        if name in affected:
            pins(table)

    reused = []
    for filename in ('tests-final.run.json', 'manage-roadmap.run.json'):
        record = load(out / filename)
        assert record['exit_code'] == 0
        for path, expected in record['source_hashes'].items():
            assert digest(root / path) == expected, path
            checks += 1
        for key in ('stdout', 'stderr'):
            ref = record[key]
            data = (root / ref['path']).read_bytes()
            assert len(data) == ref['bytes'] and hashlib.sha256(data).hexdigest() == ref['sha256']
            checks += 1
        reused.append({'record': filename, 'started_at': record['started_at'],
                       'scope': 'Original exact input/log hashes; reused, not rerun'})
    # The 54 gate tests additionally read the immutable matrix and roadmap target IDs.
    for path in ('docs/development/evidence/WP01-01/preparation/obligation-matrix.json',
                 'docs/development/roadmap.json'):
        original = json.loads(subprocess.check_output(
            ['git', '-c', 'core.longpaths=true', 'show',
             '6d09479f2f44eb604ef69993b370f2f7cacde980:' + path], cwd=root))
        value = load(root / path)
        if path.endswith('roadmap.json'):
            assert [t['id'] for t in value['targets']] == [t['id'] for t in original['targets']]
        else:
            assert value == original
        checks += 1
    for ref in load(here / 'superseded-index.json')['files']:
        data = (root / ref['snapshot_path']).read_bytes()
        assert len(data) == ref['bytes'] and hashlib.sha256(data).hexdigest() == ref['sha256']
        checks += 1
    print(json.dumps({'result': 'PASS', 'checks': checks,
        'changed_entities': sorted(affected), 'unchanged_entities': len(tables) - len(affected),
        'source_files_verified': len(sources), 'reused_runs': reused,
        'analogous_identity_entities_inspected_unchanged': [
            'Candidate', 'EngineeringTask', 'ReviewIssue', 'Evaluation', 'AuditClosureAmendment'],
        'scope': 'Static candidate/schema/source/hash checks only; no DB, runtime, adoption or completion'},
        ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
