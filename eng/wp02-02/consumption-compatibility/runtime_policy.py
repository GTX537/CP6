"""Task-only runtime identity policy; never authorizes package trust or Main adoption."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import uuid
from urllib.parse import urlparse
from snapshot import logical, native, no_links, owner_root

TASK = Path(__file__).resolve().parent


def output_path(root, output):
    root = owner_root(TASK.parents[2], Path(root).name, Path(root), create=False)
    output = logical(Path(output).absolute())
    no_links(output)
    if not output.parent.resolve(strict=True).is_relative_to(root) or native(output).exists():
        raise ValueError('Output must be a new file within the exact task owner root')
    return output

MAIN = '1129953156b7775152a74ad2f51cb95ef430ee56'
PLATFORM = 'b28db763f70ab6f164cb7c87d6ce49a6d97267f1'
REQUIRED = ('CP6.Platform.Abstractions', 'CP6.Platform.AspNetCore',
            'CP6.Platform.Contracts', 'CP6.Platform.EntityFramework',
            'CP6.Platform.Messaging', 'System.Diagnostics.DiagnosticSource', 'OpenTelemetry', 'OpenTelemetry.Api')


def validate(expected, observed, root):
    result = {'result': 'REJECTED', 'findings': [], 'formalPackageTrust': 'BLOCKED',
              'mainConsumption': 'NOT_RUN', 'scope': 'PREPARATION_ONLY'}
    def require(condition, reason):
        if not condition:
            raise ValueError(reason)

    def contained(path):
        path = Path(path)
        require(path.is_absolute(), 'Assembly/evidence path must be absolute')
        no_links(path)
        require(native(path).is_file(), 'Missing dependency/evidence: ' + str(path))
        resolved = logical(native(path).resolve(strict=True))
        require(resolved.is_relative_to(root), 'Cross-owner path rejected: ' + str(path))
        return resolved

    def verified_file(row):
        path = contained(row['path'])
        require(re.fullmatch('[0-9a-f]{64}', row['sha256']) is not None, 'Invalid expected hash')
        require(hashlib.sha256(native(path).read_bytes()).hexdigest() == row['sha256'], 'Build bytes changed: ' + str(path))
        return path

    try:
        root = Path(root)
        require(root.is_absolute(), 'Owner root must be absolute')
        root = owner_root(TASK.parents[2], root.name, root, create=False)
        require(re.fullmatch('[0-9a-f]{32}', root.name) is not None, 'Invalid owner root')
        require(expected['owner'] == observed['owner'] == root.name, 'Cross-snapshot owner mismatch')
        require(expected['mainSource'] == MAIN and expected['platformSource'] == PLATFORM, 'Wrong fixed source')
        require(expected['compatibility'] == 'SPECIFIED_PATHS_VERIFIED', 'Unknown/unqualified compatibility')
        require(set(expected['builds']) == {'WebApi', 'DatabaseCompatibility.Probe', 'Probe'}, 'Missing project build')
        require(all(type(row[k]) is int and row[k] == 0 for row in expected['builds'].values()
                    for k in ('restoreExitCode', 'buildExitCode')), 'Actual dependency build not qualified')
        require(bool(expected['evidence']), 'Missing pre-runtime build evidence')
        for row in expected['evidence']:
            verified_file(row)
        require(observed['status'] == 'PASS' and observed['failures'] == [], 'Runtime did not pass')
        require(observed['mainConsumption'] == 'NOT_RUN' and observed['formalPackageTrust'] == 'BLOCKED'
                and observed['evidenceKind'] == 'SYNTHETIC_AUTHORITY', 'Unsupported adoption/trust claim')
        names = [row['name'] for row in expected['assemblies']]
        require(len(names) == len(set(names)) and set(REQUIRED) <= set(names), 'Missing/duplicate expected assembly')
        by_name = {row['name']: row for row in expected['assemblies']}
        observed_names = [row['name'] for row in observed['assemblies']]
        require(len(observed_names) == len(set(observed_names)) and set(REQUIRED) <= set(observed_names),
                'Missing dependency or duplicate loaded assembly')
        for row in observed['assemblies']:
            require(row['name'] in by_name, 'Unapproved loaded assembly: ' + row['name'])
            approved = by_name[row['name']]
            require(row['version'] == approved['version'], 'Wrong assembly version: ' + row['name'])
            require(row['sha256'] == approved['sha256'], 'Wrong assembly hash: ' + row['name'])
            require(contained(row['path']) == verified_file(approved), 'Wrong loaded assembly source: ' + row['name'])
            require(uuid.UUID(row['mvid']).int != 0, 'Invalid observed MVID')
            require(row['mvid'] == approved['mvid'], 'Wrong observed MVID: ' + row['name'])
            if row['name'].startswith('CP6.Platform.'):
                require(bool(row['observedTypes']), 'Platform type not actually observed')
        assertions = observed['assertions']
        require(bool(expected['requiredAssertions']) and len(set(expected['requiredAssertions'])) == len(expected['requiredAssertions']),
                'Expected assertions empty/duplicate')
        require(all(row['passed'] is True for row in assertions), 'Runtime assertion failure')
        actual = [row['name'] for row in assertions]
        require(len(actual) == len(set(actual)) and set(expected['requiredAssertions']) <= set(actual), 'Missing/duplicate runtime assertions')
        http = observed['http']
        address = urlparse(http['address'])
        require(address.scheme == 'http' and address.hostname == '127.0.0.1' and address.port is not None
                and address.port > 0 and address.username is None and address.password is None, 'Not an owned loopback HTTP listener')
        require(bool(http['requests']) and all(row['passed'] is True and row['expectedStatus'] == row['actualStatus']
                for row in http['requests']), 'HTTP missing/failed')
        telemetry = observed['telemetry']
        require(telemetry['flushSucceeded'] is True and telemetry['droppedCount'] == 0
                and telemetry['propagationIntact'] is True, 'SDK flush/drop/propagation failure')
        require({'Trace', 'Metric', 'Log'} <= {row['signal'] for row in telemetry['records']}, 'Missing actual SDK signal')
        require(all(observed['cleanup'][key] is True for key in
                    ('stopSucceeded', 'disposeSucceeded', 'listenerClosed', 'propagatorsRestored')), 'Owned host cleanup failed')
        result['result'] = 'MATCHED_SPECIFIED_PATHS'
    except (KeyError, TypeError, ValueError, OSError, AttributeError) as error:
        result['findings'].append(str(error))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--expected', required=True, type=Path)
    parser.add_argument('--runtime', required=True, type=Path)
    parser.add_argument('--owner-root', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    # Existing owner directory only; an explicit exclusive output never overwrites evidence.
    root = owner_root(TASK.parents[2], args.owner_root.name, args.owner_root, create=False)
    output = output_path(root, args.output)
    documents = []
    for path in (args.expected, args.runtime):
        no_links(path)
        if not logical(native(path).resolve(strict=True)).is_relative_to(root):
            parser.error('Input reports must belong to the same task owner')
        data = native(path).read_bytes()
        if len(data) > 64 * 1024 * 1024:
            parser.error('Report exceeds 64 MiB bound')
        def unique(pairs):
            value = {}
            for key, item in pairs:
                if key in value:
                    raise ValueError('Duplicate JSON key: ' + key)
                value[key] = item
            return value
        documents.append(json.loads(data, object_pairs_hook=unique))
    result = validate(*documents, root)
    result['inputs'] = {str(p): hashlib.sha256(native(p).read_bytes()).hexdigest() for p in (args.expected, args.runtime)}
    result['policySha256'] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    with native(output).open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(result, stream, indent=2)
        stream.write('\n')
    print(json.dumps(result))
    return 0 if result['result'] == 'MATCHED_SPECIFIED_PATHS' else 2


if __name__ == '__main__':
    sys.exit(main())
