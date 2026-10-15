"""Record exact local commands, times, source bytes and outputs; no Actions."""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[5]
focused = '--focused' in sys.argv[1:]
out = Path(__file__).parent / 'runs' / ('verified-' + dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%SZ'))
out.mkdir(parents=True, exist_ok=False)
sources = out / 'sources'
sources.mkdir()
files = ['acceptance_basis.py', 'test_acceptance_basis.py', 'manage_roadmap.py', 'test_manage_roadmap.py']
source_hashes = {}
for name in files:
    raw = (root / 'docs/development/tools' / name).read_bytes()
    (sources / name).write_bytes(raw)
    source_hashes[name] = hashlib.sha256(raw).hexdigest()
unchanged = ['docs/development/roadmap.json', 'docs/development/task-definitions.json',
             'docs/development/task-deliveries/WP05-01/adoption.json',
             'docs/development/evidence/WP01-01/preparation/obligation-matrix.json',
             'docs/development/evidence/WP01-01/decision-closure/manifest.json']
before = {name: hashlib.sha256((root/name).read_bytes()).hexdigest() for name in unchanged}
metadata = {'started_at': dt.datetime.now(dt.timezone.utc).isoformat(), 'cwd': str(root),
            'recorder_sha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
            'applicability': 'FINAL_IMPLEMENTATION_CANDIDATE',
            'source_head': subprocess.check_output(['git', '-c', 'core.longpaths=true', 'rev-parse', 'HEAD'], cwd=root).decode().strip(),
            'source_snapshot_representation': 'RAW_FILE_BYTES', 'source_hashes': source_hashes,
            'unchanged_before': before,
            'trust_policy_configured': bool(os.environ.get('CP6_ACCEPTANCE_TRUST_POLICY')),
            'business_tests_executed': False, 'runs': []}
commands = [('adapter-tests', [sys.executable, '-B', '-Xutf8', '-m', 'unittest', 'discover', '-s', 'docs/development/tools', '-p', 'test_acceptance_basis.py', '-v']),
            ('manager-tests', [sys.executable, '-B', '-Xutf8', '-m', 'unittest', 'discover', '-s', 'docs/development/tools', '-p', 'test_manage_roadmap.py', '-v']),
            ('manager-readonly', [sys.executable, '-B', '-Xutf8', 'docs/development/tools/manage_roadmap.py', '--basis-report', str(out/'ledger-basis-report.json')]),
            ('candidate-readonly', [sys.executable, '-B', '-Xutf8', str(Path(__file__).parent/'inspect_candidate.py'), '--root', str(root), '--archive-root', 'D:/CP6']),
            ('diff-check', ['git', '-c', 'core.longpaths=true', 'diff', '--check'])]
if focused:
    commands[1][1].extend(['-k', 'FormalAcceptanceRouteTests'])
    metadata['manager_reuse'] = {
        'source': 'runs/verified-20261015T055559Z/manager-tests.stderr.txt',
        'source_run_metadata': 'runs/verified-20261015T055559Z/run.json',
        'scope': '62 pre-existing manager tests passed within the actual 70-test execution at that snapshot; not re-executed or relabelled as final-source execution.',
        'reason': 'Last corrections are independent adapter identity-history verification and preserving existing recovered rows while inventory remains NOT_INVENTORIED. The affected nine formal-route tests, including the unmodified real ledger, are executed now; other original checks and test inputs are unchanged.'}
for name, command in commands:
    started = dt.datetime.now(dt.timezone.utc).isoformat()
    with (out/(name+'.stdout.txt')).open('wb') as stdout, (out/(name+'.stderr.txt')).open('wb') as stderr:
        completed = subprocess.run(command, cwd=root, stdout=stdout, stderr=stderr)
    metadata['runs'].append({'name': name, 'command': command, 'started_at': started,
                             'ended_at': dt.datetime.now(dt.timezone.utc).isoformat(), 'exit_code': completed.returncode,
                             'stdout_sha256': hashlib.sha256((out/(name+'.stdout.txt')).read_bytes()).hexdigest(),
                             'stderr_sha256': hashlib.sha256((out/(name+'.stderr.txt')).read_bytes()).hexdigest()})
    (out/'run.json').write_text(json.dumps(metadata, indent=2)+'\n', encoding='utf-8')
    print(name, completed.returncode, flush=True)
metadata['unchanged_after'] = {name: hashlib.sha256((root/name).read_bytes()).hexdigest() for name in unchanged}
metadata['unchanged_inputs_match'] = metadata['unchanged_before'] == metadata['unchanged_after']
metadata['ended_at'] = dt.datetime.now(dt.timezone.utc).isoformat()
(out/'run.json').write_text(json.dumps(metadata, indent=2)+'\n', encoding='utf-8')
raise SystemExit(any(r['exit_code'] for r in metadata['runs']) or not metadata['unchanged_inputs_match'])
