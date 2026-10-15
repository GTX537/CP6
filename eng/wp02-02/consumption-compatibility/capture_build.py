"""Capture independent pre-runtime build identities; a local observation, never approval."""
import argparse
import datetime
import hashlib
import json
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
from runtime_policy import MAIN, PLATFORM, REQUIRED
from snapshot import logical, native, no_links, owner_root

TASK = Path(__file__).resolve().parent
ASSERTIONS = [
    'http.single-loopback-listener', 'http.trusted-context', 'http.caller-fields-ignored',
    'http.downstream', 'http.trace-propagation', 'http.baggage-stripped',
    'http.response.positive', 'http.response.unauthenticated', 'http.response.revoked',
    'http.response.unavailable', 'http.denied-handlers-not-run',
    *['context.' + scope + '.' + outcome for scope in ('background', 'message') for outcome in ('trusted', 'restored', 'denied')],
    *['telemetry.' + name for name in ('flush', 'server-traces', 'client-trace', 'metric', 'log', 'privacy', 'local-only')],
    *['cleanup.' + name for name in ('stop', 'dispose', 'listener-closed', 'propagators-restored')],
    'assemblies.required', 'assemblies.owner-contained', 'assemblies.platform-types-observed',
]


def digest(path):
    return hashlib.sha256(native(path).read_bytes()).hexdigest()


def capture(root):
    root = owner_root(TASK.parents[2], root.name, root, create=False)
    no_links(root)
    state = json.loads((root / 'state.json').read_bytes())
    snapshot = json.loads((root / 'snapshot-manifest.json').read_bytes())
    if state['runtime'] != 'NOT_RUN' or state['build'] not in ('PASS', 'FAIL'):
        raise ValueError('Capture must follow a completed build and precede runtime')
    if snapshot['sources']['main']['sha'] != MAIN or snapshot['sources']['platform']['sha'] != PLATFORM:
        raise ValueError('Unexpected producer source')
    if digest(root / 'snapshot-manifest.json') != state['snapshotManifestSha256']:
        raise ValueError('Snapshot manifest changed')
    identities = json.loads((root / 'build-identities.json').read_bytes())
    evidence = []
    paths = set()
    for row in identities['files']:
        p = root / row['path']
        no_links(p)
        if not logical(native(p).resolve(strict=True)).is_relative_to(root) or row['path'] in paths or digest(p) != row['sha256']:
            raise ValueError('Build artifact changed or escaped owner: ' + row['path'])
        paths.add(row['path'])
    commands = {row['label']: row for row in state['commands']}
    builds = {}
    for name, label in [('WebApi', 'main-webapi'), ('DatabaseCompatibility.Probe', 'database-probe-build-only'), ('Probe', 'private-probe')]:
        builds[name] = {stage + 'ExitCode': commands.get(label + '-' + stage, {}).get('exitCode') for stage in ('restore', 'build')}
        for stage in ('restore', 'build'):
            path = root / 'logs' / (label + '-' + stage + '.command.json')
            if path.exists():
                evidence.append({'path': str(path), 'sha256': digest(path)})
    if builds['Probe'] != {'restoreExitCode': 0, 'buildExitCode': 0}:
        raise ValueError('No successful private probe build to inspect')
    for path in [root / 'snapshot-manifest.json', root / 'build-identities.json']:
        evidence.append({'path': str(path), 'sha256': digest(path)})
    command = ['pwsh', '-NoProfile', '-File', str(TASK / 'Inspect-Assemblies.ps1'),
               '-Directory', str(root / 'probe/bin/Release/net8.0')]
    result = subprocess.run(command, capture_output=True)
    if result.returncode:
        raise ValueError('PE metadata inspection failed: ' + result.stderr.decode('utf-8', errors='replace'))
    assemblies = json.loads(result.stdout)
    if not set(REQUIRED) <= {row['name'] for row in assemblies}:
        raise ValueError('Required build DLL missing')
    return {'capturedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(),
            'kind': 'PRE_RUNTIME_LOCAL_BUILD_OBSERVATION_NOT_APPROVAL', 'owner': root.name,
            'mainSource': MAIN, 'platformSource': PLATFORM,
            'compatibility': 'SPECIFIED_PATHS_VERIFIED' if state['build'] == 'PASS' else 'Unknown',
            'builds': builds, 'evidence': evidence, 'assemblies': assemblies, 'requiredAssertions': ASSERTIONS,
            'captureCommand': command, 'captureToolSha256': digest(Path(__file__)),
            'metadataToolSha256': digest(TASK / 'Inspect-Assemblies.ps1'),
            'formalPackageTrust': 'BLOCKED', 'mainConsumption': 'NOT_RUN'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--owner-root', type=Path, required=True)
    args = parser.parse_args()
    result = capture(args.owner_root.absolute())
    path = args.owner_root / 'pre-runtime-identities.json'
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(result, stream, indent=2)
        stream.write('\n')
    print(json.dumps({'output': str(path), 'sha256': digest(path), 'assemblies': len(result['assemblies'])}))


if __name__ == '__main__':
    main()
