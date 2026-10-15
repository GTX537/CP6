"""Read-only independent candidate/evidence verification; writes only its review report."""
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path('D:/CP6.Platform-worktrees/wp02-26-20261014')
RUN = ROOT/'docs/development/evidence/WP02-26/20261015T001945Z'
OUT = Path(__file__).parent
BASE = 'b28db763f70ab6f164cb7c87d6ce49a6d97267f1'
HEAD = '5ec6e733039bfbb3b52a492a3f96f3604b8c19fb'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def git(*args, cwd=ROOT):
    return subprocess.check_output(['git', '-c', 'core.longpaths=true', *args], cwd=cwd)

def sha(raw):
    return hashlib.sha256(raw).hexdigest()

def read(path):
    return json.loads(path.read_bytes())

def tree(ref):
    return {entry.split('\t', 1)[1]: entry.split()[2] for entry in git('ls-tree', '-r', ref).decode().splitlines()}

assert git('rev-parse', 'HEAD').decode().strip() == HEAD
assert not git('status', '--porcelain').strip()
files = git('diff', '--name-only', BASE, HEAD).decode().splitlines()
old, current = tree(BASE), tree(HEAD)
batch = subprocess.run(['git', '-c', 'core.longpaths=true', 'cat-file', '--batch'], cwd=ROOT,
                       input=('\n'.join(current[p] for p in files)+'\n').encode(), capture_output=True, check=True).stdout
manifest = []
cursor = 0
parsed = {'json': 0, 'trx': 0}
for path in files:
    end = batch.index(b'\n', cursor)
    blob, kind, size = batch[cursor:end].decode().split()
    assert kind == 'blob' and blob == current[path]
    cursor = end+1
    raw = batch[cursor:cursor+int(size)]
    cursor += int(size)+1
    manifest.append(dict(path=path, git_blob=blob, sha256=sha(raw), bytes=len(raw)))
    if path.endswith('.json'):
        json.loads(raw)
        parsed['json'] += 1
    if path.endswith('.trx'):
        ET.fromstring(raw)
        parsed['trx'] += 1
assert cursor == len(batch)
legacy = [p for p in old if p.startswith(('src/', 'contracts/observability/slo-evidence/v1/', '.github/workflows/'))]
for path in legacy:
    assert old[path] == current[path], path
for path in ['Directory.Build.props', 'Directory.Packages.props']:
    assert old[path] == current[path]

gates = read(RUN/'verified-gates-final.json')
tests, summaries = {}, []
for report in gates['reports']:
    suite = report['suite']
    directory = RUN/'final-01'/suite
    before, after = read(directory/'source-before.json'), read(directory/'source-after.json')
    project = {'unit': 'CP6.Platform.WP0226.Tests', 'pg': 'CP6.Platform.WP0226.PostgreSqlTests', 'architecture': 'CP6.Platform.ArchitectureTests'}[suite]
    architecture_files = ['eng/verify.ps1', 'eng/pack-release.ps1', '.github/workflows/publish-alpha.yml', 'README.md', 'VERSION', 'docs/P10-RELEASE-GOVERNANCE.md']

    def relevant(path):
        project_file = path.startswith('tests/'+project+'/') and (not path.endswith('.py') or path.endswith('/pg_resource.py'))
        return (path.startswith('src/') or project_file or path in ['tests/CP6.Platform.UnitTests/TestingUtilityTests.cs', 'tests/CP6.Platform.UnitTests/SloEvidenceContractTests.cs']
                or '/' not in path or path.startswith('contracts/observability/slo-evidence/v1/') or path.endswith('/inputs/vectors.json')
                or (suite == 'architecture' and (path in architecture_files or path.startswith(('docs/P08-', 'docs/runbooks/P08-')))))

    before_inputs = {f['path']: f['sha256'] for f in before['source_files'] if relevant(f['path'])}
    after_inputs = {f['path']: f['sha256'] for f in after['source_files'] if relevant(f['path'])}
    assert before_inputs == after_inputs
    for path, digest in after_inputs.items():
        assert sha((ROOT/path).read_bytes()) == digest, path
    dependencies = []
    for item in after['resolved_dependencies']:
        if item['path'].startswith('src/') or item['path'].startswith('tests/'+project+'/'):
            assert sha((ROOT/item['path']).read_bytes()) == item['sha256'], item['path']
            dependencies.append(item)
    for path, digest in report['assembly_hashes'].items():
        assert sha((ROOT/path).read_bytes()) == digest, path
    raw = (ROOT/report['trx']).read_bytes()
    assert sha(raw) == report['trx_sha256']
    xml = ET.fromstring(raw)
    results = xml.findall('t:Results/t:UnitTestResult', NS)
    counters = xml.find('t:ResultSummary/t:Counters', NS).attrib
    assert len(results) == int(counters['total']) == int(counters['passed']) == int(counters['executed'])
    assert report['execution'] == read(directory/'execution.json') and report['execution']['exit_code'] == 0
    assert all(r.attrib['outcome'] == 'Passed' for r in results)
    tests[report['trx']] = {r.attrib['testName']: r.attrib['outcome'] for r in results}
    summaries.append(dict(suite=suite, total=len(results), passed=len(results), exit_code=0, execution=report['execution'],
                          trx=report['trx'], trx_sha256=sha(raw), relevant_source_files=len(after_inputs),
                          resolved_dependencies=dependencies, assembly_hashes=report['assembly_hashes'],
                          verified_current_bytes=True, reused_not_rerun=True))

catalog = read(RUN/'inputs/catalog.json')
originals = {case['id']: case for target in catalog for case in target['cases']}
matrix = read(RUN/'acceptance-final.json')
rows = matrix['rows']
assert len(rows) == len(originals) == 56 and len({r['id'] for r in rows}) == 56
assert matrix['original_business_acceptance'] == 'NOT_RUN'
proof_count = 0
for row in rows:
    assert row['original_business_acceptance'] == 'NOT_RUN'
    assert all(row[key] == value for key, value in originals[row['id']].items())
    for proof in row['local_evidence']:
        assert sha((ROOT/proof['trx']).read_bytes()) == proof['sha256']
        assert tests[proof['trx']][proof['test_name']] == proof['outcome'] == 'Passed'
        proof_count += 1
execution_matrix = read(RUN/'execution-matrix-final.json')
assert len(execution_matrix['all_actual_local_unit_and_pg_scenarios']) == 114
for row in execution_matrix['rows']:
    if row['id'] == 'R15':
        assert sha((ROOT/row['path']).read_bytes()) == row['sha256']
        assert row['original_count'] == 56 and row['original_business_acceptance'] == 'NOT_RUN'
    else:
        for proof in row['evidence']:
            assert sha((ROOT/proof['trx']).read_bytes()) == proof['sha256']
            assert tests[proof['trx']][proof['test']] == proof['outcome'] == 'Passed'
collection = read(RUN/'final-01/pg-resource/collection.json')
for item in collection['files']:
    raw = (RUN/'final-01/pg-resource'/item['path']).read_bytes()
    assert len(raw) == item['bytes'] and sha(raw) == item['sha256']
pins = read(RUN/'input-pins.json')
for pin in pins:
    raw = (RUN/'inputs'/pin['copy']).read_bytes()
    assert sha(raw) == pin['sha256']
    if 'commit' in pin:
        assert git('show', pin['commit']+':'+pin['path'], cwd='D:/CP6') == raw, pin['copy']
index = read(RUN/'index-audit-final.json')
candidate_manifest = {f['path']: f for f in manifest}
for item in index['indexed_byte_manifest']:
    assert candidate_manifest[item['path']] == item

report = dict(task='WP02-26', reviewed_base=BASE, reviewed_head=HEAD, verified_at=datetime.now(timezone.utc).isoformat(),
              status='PASS', changed_files=len(files), changed_source_and_test_files=sum(p.startswith(('src/', 'tests/')) for p in files),
              parsed_evidence=parsed, git_manifest=manifest, unchanged_legacy_and_workflow_files=len(legacy), reused_gates=summaries,
              acceptance=dict(original_count=56, original_fields_preserved=True, business_NOT_RUN=56,
                              local_pass=sum(row['local_result'] == 'LOCAL_PASS' for row in rows), verified_proof_links=proof_count,
                              execution_rows=len(execution_matrix['rows']), local_scenarios=114),
              resource_collection=dict(files=len(collection['files']), owner=collection['owner'], system_identifier=collection['system_identifier'],
                                       database_oid=collection['database_oid'], evidence_hashes_verified=True, clean_shutdown=collection['clean_shutdown'],
                                       note='Recorded state only; reviewer did not start, connect to, or change resources.'),
              pinned_inputs_verified=len(pins), index_manifest_verified=len(index['indexed_byte_manifest']),
              scope='Read-only candidate/evidence/source/dependency/assembly verification; no test/build/DB/Actions rerun.',
              audit_history=[dict(result='SCRIPT_ERROR', error="KeyError: evidence", reason='Initial inline audit assumed all execution matrix rows are TRX proof arrays; R15 is an artifact proof. No product failure.'),
                             dict(result='PASS', correction='Validate R15 artifact hash and remaining rows exact TRX names and hashes.')])
(OUT/'WP02-26-independent-provenance.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps({key: value for key, value in report.items() if key not in ['git_manifest', 'reused_gates']}, indent=2))
print(json.dumps([dict(suite=row['suite'], passed=row['passed'], source_files=row['relevant_source_files'], dependencies=len(row['resolved_dependencies']), assemblies=len(row['assembly_hashes'])) for row in summaries]))
