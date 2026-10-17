"""Local publish receipt and fail-closed closure verification (not release authority).

The receipt is created only after dotnet publish. Its SHA-256 must be retained by
the caller, separately from the output being checked. There is no 'accept current
directory' command. Formal Platform expectations come from pinned nupkg bytes.
"""
import argparse
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import zipfile

import pg_resource as pg
from verify_host import ROOT, DOTNET, PACKAGE_IDS, digest, write

PUBLICATION = 'docs/development/evidence/authorized-adoption-release-20261015/publication/formal-package-publication.v1.json'
ENTRY = 'CP6.WebApi.dll'
DEPS = 'CP6.WebApi.deps.json'
RUNTIME = 'CP6.WebApi.runtimeconfig.json'
SCHEMA = 'CP6-LOCAL-PUBLISH/1'


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, creationflags=pg.NO_WINDOW)


def safe_relative(value):
    pg.require(isinstance(value, str) and value and '\\' not in value and ':' not in value
               and not value.startswith('/') and all(p not in ('', '.', '..') for p in value.split('/')),
               'Invalid artifact relative path')
    return PurePosixPath(value)


def inventory(directory):
    directory = Path(directory).absolute()
    pg.require(directory.resolve() == directory and directory.is_dir(), 'Publish directory missing or redirected')
    files = {}
    for path in sorted(directory.rglob('*')):
        pg.require(path.resolve() == path and not path.is_symlink() and not path.is_junction(),
                   'Redirected publish member')
        if path.is_file():
            files[path.relative_to(directory).as_posix()] = digest(path.read_bytes())
    pg.require(files, 'Empty publish output')
    return files


def closure(directory):
    """Framework-dependent, non-RID publish: runtimeTargets retain RID subpaths."""
    directory = Path(directory)
    for name in (ENTRY, DEPS, RUNTIME, 'appsettings.json'):
        pg.require((directory / name).is_file(), 'Required publish file missing: ' + name)
    deps = json.loads((directory / DEPS).read_bytes())
    target = deps['targets'][deps['runtimeTarget']['name']]
    pg.require('/' not in deps['runtimeTarget']['name'], 'Only portable framework-dependent publish is supported')
    required = {ENTRY, DEPS, RUNTIME, 'appsettings.json'}
    owners = {}
    for library, assets in target.items():
        pg.require(library in deps['libraries'], 'Unregistered deps library')
        for group in ('runtime', 'native', 'resources', 'runtimeTargets'):
            for asset, details in assets.get(group, {}).items():
                path = safe_relative(asset)
                if path.name == '_._':
                    continue
                if group == 'runtimeTargets':
                    pg.require(details.get('assetType') in ('runtime', 'native'), 'Unsupported runtime target')
                    relative = path.as_posix()
                elif group == 'resources':
                    locale = safe_relative(details['locale'])
                    pg.require(len(locale.parts) == 1, 'Invalid resource locale')
                    relative = str(locale / path.name)
                else:
                    relative = path.name
                pg.require(relative not in owners or owners[relative] == library, 'Ambiguous runtime asset owner')
                owners[relative] = library
                required.add(relative)
    for name in required:
        pg.require((directory / name).is_file(), 'Dependency file missing: ' + name)
    for name in PACKAGE_IDS:
        pg.require(owners.get(name + '.dll') == name + '/0.10.3'
                   and deps['libraries'][name + '/0.10.3']['type'] == 'package',
                   'Platform dependency declaration mismatch')
    runtime = json.loads((directory / RUNTIME).read_bytes())['runtimeOptions']
    pg.require(runtime['tfm'] == 'net8.0' and runtime.get('frameworks')
               and not runtime.get('additionalProbingPaths'), 'Unexpected runtime configuration')
    return sorted(required)


def formal_expectations(source, cache):
    pg.require(re.fullmatch('[0-9a-f]{40}', source), 'Full source SHA required')
    record = git('show', source + ':' + PUBLICATION)
    packages = json.loads(record)['packages']
    expected, provenance = [], []
    for name in PACKAGE_IDS:
        matches = [p for p in packages if p['packageId'] == name and p['version'] == '0.10.3']
        pg.require(len(matches) == 1, 'Formal package record missing or ambiguous')
        published = matches[0]
        archive = Path(cache) / name.lower() / '0.10.3' / (name.lower() + '.0.10.3.nupkg')
        raw = archive.read_bytes()
        pg.require(digest(raw) == published['publishedPackageSha256'], 'Archive differs from formal publication')
        with zipfile.ZipFile(io.BytesIO(raw)) as z:
            assembly = z.read('lib/net8.0/' + name + '.dll')
        expected.append({'PackageId': name, 'PackageVersion': '0.10.3',
                         'SourceSha': published['sourceGitSha'], 'AssemblySha256': digest(assembly)})
        provenance.append({'packageId': name, 'archiveSha256': digest(raw), 'feedIdentity': published['feedIdentity']})
    return expected, {'recordPath': PUBLICATION, 'recordSha256': digest(record), 'packages': provenance}


def verify(directory, receipt_path, receipt_sha256, source, cache):
    raw = Path(receipt_path).read_bytes()
    pg.require(re.fullmatch('[0-9a-f]{64}', receipt_sha256) and digest(raw) == receipt_sha256,
               'Publish receipt pin mismatch')
    receipt = json.loads(raw)
    pg.require(receipt['schema'] == SCHEMA and receipt['sourceSha'] == source
               and receipt['entry'] == ENTRY and receipt['publishExitCode'] == 0,
               'Publish source/entry/receipt mismatch')
    pg.require(Path(directory).absolute() == Path(receipt['publishDirectory']), 'Publish path mismatch')
    pg.require(inventory(directory) == receipt['files'], 'Publish closure hash/inventory mismatch')
    pg.require(closure(directory) == receipt['requiredFiles'], 'Publish dependency closure mismatch')
    expected, provenance = formal_expectations(source, cache)
    pg.require(receipt['expectedModules'] == expected and receipt['formalPackages'] == provenance,
               'Independent package expectation mismatch')
    for module in expected:
        pg.require(receipt['files'][module['PackageId'] + '.dll'] == module['AssemblySha256'],
                   'Published Platform DLL differs from formal package')
    return receipt


def clean_source(source):
    pg.require(git('rev-parse', 'HEAD').decode().strip() == source, 'Source SHA is not checked-out HEAD')
    # Documentation checkout absences and this verifier do not enter WebApi build.
    paths = ['.', ':!docs', ':!eng/wp02-28']
    pg.require(not git('diff', '--name-only', 'HEAD', '--', *paths).strip(), 'Build source has tracked changes')
    pg.require(not git('ls-files', '--others', '--exclude-standard', '--', *paths).strip(),
               'Untracked build inputs are not source-bound')
    # Local/Development JSON is implicitly published by the Web SDK, even if gitignored.
    pg.require(not list((ROOT / 'CP6.WebApi').glob('appsettings.Local*.json')), 'Private source configuration present')


def publish(c, directory, source, cache):
    clean_source(source)
    root = pg.validate_paths(c)
    directory = Path(directory).absolute()
    pg.require(directory.parent == root and directory.resolve() == directory and not directory.exists(),
               'Publish requires a fresh direct child of the owned private root')
    expected, provenance = formal_expectations(source, cache)
    directory.mkdir()
    receipt_path = root / (directory.name + '.receipt.json')
    pg.require(not receipt_path.exists(), 'Publish receipt already exists')
    env = dict(os.environ, NUGET_PACKAGES=str(Path(cache).resolve()), DOTNET_NOLOGO='1')
    commands = [
        [str(DOTNET), 'restore', 'CP6.WebApi/CP6.WebApi.csproj', '--locked-mode'],
        [str(DOTNET), 'publish', 'CP6.WebApi/CP6.WebApi.csproj', '--no-restore', '-c', 'Release',
         '-o', str(directory), '-p:UseAppHost=false', '-p:SourceRevisionId=' + source,
         '-warnaserror:MSB3277'],
    ]
    logs = []
    for label, command in zip(('restore', 'publish'), commands):
        logfile = root / (directory.name + '.' + label + '.log')
        with logfile.open('wb') as output:
            result = subprocess.run(command, cwd=ROOT, env=env, stdout=output,
                                    stderr=subprocess.STDOUT, creationflags=pg.NO_WINDOW)
        logs.append({'command': command, 'log': str(logfile), 'sha256': digest(logfile.read_bytes()),
                     'exitCode': result.returncode})
        write(root / (directory.name + '.build.json'), {'sourceSha': source, 'steps': logs})
        pg.require(result.returncode == 0, label + ' failed; private build log retained')
    clean_source(source)
    files = inventory(directory)
    required = closure(directory)
    for module in expected:
        pg.require(files[module['PackageId'] + '.dll'] == module['AssemblySha256'],
                   'Published Platform DLL differs from formal package')
    receipt = {'schema': SCHEMA, 'sourceSha': source, 'entry': ENTRY,
               'publishDirectory': str(directory), 'publishExitCode': 0, 'build': logs,
               'files': files, 'requiredFiles': required, 'expectedModules': expected,
               'formalPackages': provenance, 'scope': 'local diagnostic publish; no production release authority'}
    write(receipt_path, receipt)
    pin = digest(receipt_path.read_bytes())
    verify(directory, receipt_path, pin, source, cache)
    print(json.dumps({'receipt': str(receipt_path), 'sha256': pin, 'sourceSha': source, 'fileCount': len(files)}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--owner', required=True)
    parser.add_argument('--settings', required=True)
    parser.add_argument('--publish-directory', required=True)
    parser.add_argument('--source-sha', required=True)
    parser.add_argument('--package-cache', required=True)
    args = parser.parse_args()
    c = pg.load_settings(args.settings)
    pg.require(c['Owner'] == args.owner, 'Explicit owner mismatch')
    publish(c, args.publish_directory, args.source_sha, args.package_cache)


if __name__ == '__main__':
    main()
