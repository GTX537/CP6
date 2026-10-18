"""Local publish receipt and fail-closed closure verification (not release authority).

The receipt is created only after dotnet publish. Its SHA-256 must be retained by
the caller, separately from the output being checked. There is no 'accept current
directory' command. Formal Platform expectations come from pinned nupkg bytes.
"""
import argparse
import io
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import zipfile
import xml.etree.ElementTree as ET

import pg_resource as pg
from verify_host import ROOT, DOTNET, PACKAGE_IDS, digest, write

PUBLICATION = 'docs/development/evidence/authorized-adoption-release-20261015/publication/formal-package-publication.v1.json'
ENTRY = 'CP6.WebApi.dll'
DEPS = 'CP6.WebApi.deps.json'
RUNTIME = 'CP6.WebApi.runtimeconfig.json'
SCHEMA = 'CP6-LOCAL-PUBLISH/1'


def git(*args):
    return subprocess.check_output(['git', '-c', 'core.longpaths=true', *args], cwd=ROOT, creationflags=pg.NO_WINDOW)


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


def source_export(source, destination):
    """Export the supported WebApi build graph from Git objects, never the checkout.

    Deliberately reject custom MSBuild imports/tasks/dynamic paths: they require a
    separately reviewed input boundary. SDK and locked NuGet assets remain tools,
    not files claimed to be part of the Main source commit.
    """
    pg.require(re.fullmatch('[0-9a-f]{40}', source), 'Full source SHA required')
    pg.require(git('rev-parse', source + '^{commit}').decode().strip() == source, 'Source must be a commit')
    tree = {}
    for record in git('ls-tree', '-rz', source).split(b'\0'):
        if record:
            metadata, name = record.split(b'\t', 1)
            mode, kind, oid = metadata.decode().split()
            tree[name.decode('utf-8')] = (mode, kind, oid)
    def read(name):
        pg.require(name in tree and tree[name][0] in ('100644', '100755'), 'Missing/plain source file required: ' + name)
        return git('cat-file', 'blob', tree[name][2])
    def relative(base, value):
        pg.require(value and not any(c in value for c in '$@%;:') and not value.startswith(('/', '\\')),
                   'Dynamic or external build input is unsupported')
        parts = list(PurePosixPath(base).parent.parts)
        for part in value.replace('\\', '/').split('/'):
            if part == '..':
                pg.require(parts, 'Build input escapes committed source')
                parts.pop()
            elif part not in ('', '.'):
                parts.append(part)
        return '/'.join(parts)
    projects, prefixes = set(), set()
    allowed_properties = {'TargetFramework', 'Nullable', 'ImplicitUsings', 'RestorePackagesWithLockFile'}
    def inspect(name, project=False):
        xml = ET.fromstring(read(name))
        pg.require(xml.tag == 'Project' and set(xml.attrib) <= {'Sdk'}, 'Unsupported build definition')
        if project:
            pg.require(xml.get('Sdk') in ('Microsoft.NET.Sdk', 'Microsoft.NET.Sdk.Web'), 'Unsupported SDK')
        else:
            pg.require(not xml.attrib, 'Directory props cannot select SDK')
        for group in xml:
            pg.require(group.tag in ('PropertyGroup', 'ItemGroup') and set(group.attrib) <= {'Condition'},
                       'Custom build imports/tasks are unsupported')
            condition = group.get('Condition', '')
            comparison = r"'\$\(MSBuildProjectName\)'\s*==\s*'[A-Za-z0-9_.-]+'"
            pg.require(not condition or re.fullmatch(comparison + r'(?:\s+Or\s+' + comparison + r')*', condition),
                       'External/dynamic build condition unsupported')
            pg.require(project or group.tag == 'PropertyGroup', 'Directory props items unsupported')
            for item in group:
                if group.tag == 'PropertyGroup':
                    pg.require(item.tag in allowed_properties and not item.attrib and len(item) == 0
                               and not any(c in (item.text or '') for c in '$@%;'), 'Unsupported build property')
                else:
                    pg.require(item.tag in ('PackageReference', 'FrameworkReference', 'InternalsVisibleTo', 'ProjectReference', 'Content'),
                               'Unsupported build item')
                    attributes = {'Include'}
                    metadata = set()
                    if item.tag == 'PackageReference':
                        attributes.add('Version')
                        metadata = {'IncludeAssets', 'PrivateAssets'}
                    elif item.tag == 'Content':
                        attributes |= {'Link', 'CopyToOutputDirectory', 'CopyToPublishDirectory'}
                    pg.require(set(item.attrib) <= attributes and all(child.tag in metadata and not child.attrib for child in item),
                               'Unsupported build item metadata')
                    # No per-project property injection or external task/import metadata.
                    pg.require(not any('$' in value or '@' in value for value in item.attrib.values())
                               and all(len(child) == 0 and not any(c in (child.text or '') for c in '$@') for child in item),
                               'Dynamic build item unsupported')
                    if item.tag == 'ProjectReference':
                        target = relative(name, item.get('Include'))
                        pg.require(target.endswith('.csproj') and '*' not in target, 'Unsupported project reference')
                        visit(target)
                    elif item.tag == 'Content':
                        target = relative(name, item.get('Include'))
                        pg.require(target.endswith('/**/*') and '*' not in target[:-5], 'Unsupported content glob')
                        prefixes.add(target[:-4])
    def visit(name):
        if name not in projects:
            projects.add(name)
            prefixes.add(str(PurePosixPath(name).parent) + '/')
            inspect(name, project=True)
    visit('CP6.WebApi/CP6.WebApi.csproj')
    pg.require(not {'Directory.Build.targets', 'Directory.Packages.props'} & tree.keys(),
               'Additional root build imports unsupported')
    configs = [name for name in tree if name.casefold() == 'nuget.config']
    pg.require(len(configs) == 1, 'Exactly one committed root NuGet config required')
    roots = {configs[0]}
    read(configs[0])
    if 'Directory.Build.props' in tree:
        inspect('Directory.Build.props')
        roots.add('Directory.Build.props')
    selected = {name: entry for name, entry in tree.items() if name in roots or any(name.startswith(p) for p in prefixes)}
    for name, (mode, kind, oid) in selected.items():
        safe_relative(name)
        pg.require(mode in ('100644', '100755') and kind == 'blob', 'Source links/submodules unsupported')
        pg.require(not {'bin', 'obj'} & {part.lower() for part in PurePosixPath(name).parts}, 'Committed build outputs unsupported')
        if name.lower().endswith(('.props', '.targets', '.csproj', '.rsp', '.user')):
            pg.require(name in projects or name == 'Directory.Build.props', 'Additional build imports unsupported: ' + name)
    destination = Path(destination).absolute()
    pg.require(not destination.exists() and destination.resolve() == destination, 'Source export must be fresh and unredirected')
    destination.mkdir()
    # archive output is checked against object IDs (including export-ignore/subst).
    archive = git('-c', 'core.autocrlf=false', 'archive', '--format=zip', source, '--', *sorted(roots | {p.rstrip('/') for p in prefixes}))
    files = {}
    with zipfile.ZipFile(io.BytesIO(archive)) as z:
        for member in z.infolist():
            if member.is_dir():
                continue
            name = member.filename
            safe_relative(name)
            pg.require(name in selected and name not in files, 'Unexpected exported file')
            data = z.read(member)
            oid = hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest()
            pg.require(oid == selected[name][2], 'Exported bytes differ from Git object')
            target = destination / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            files[name] = digest(data)
    pg.require(set(files) == set(selected), 'Incomplete source export')
    return files


def build_context(snapshot, cache):
    # Pin local .NET8 SDK selection; no ancestor global.json or user MSBuild hooks.
    versions = [p.name for p in (DOTNET.parent / 'sdk').iterdir() if re.fullmatch(r'8\.0\.\d+', p.name)]
    pg.require(versions, 'Local .NET8 SDK required')
    sdk = max(versions, key=lambda v: tuple(map(int, v.split('.'))))
    write(snapshot / 'global.json', {'sdk': {'version': sdk, 'rollForward': 'disable', 'allowPrerelease': False}})
    home = snapshot.parent / (snapshot.name + '.home')
    home.mkdir()
    allowed = {'SYSTEMROOT', 'WINDIR', 'COMSPEC', 'PATH', 'PATHEXT', 'TEMP', 'TMP',
               'USERPROFILE', 'APPDATA', 'LOCALAPPDATA', 'PROGRAMFILES', 'PROGRAMFILES(X86)', 'PROGRAMDATA'}
    env = {k: v for k, v in os.environ.items() if k.upper() in allowed}
    env.update(NUGET_PACKAGES=str(Path(cache).resolve()), DOTNET_NOLOGO='1', DOTNET_CLI_HOME=str(home),
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_MULTILEVEL_LOOKUP='0')
    flags = ['-noAutoResponse', '-p:MSBuildEnableWorkloadResolver=false', '-p:ImportDirectoryBuildTargets=false',
             '-p:ImportDirectoryPackagesProps=false', '-p:MSBuildUserExtensionsPath=' + str(home),
             '-p:DirectoryBuildPropsPath=' + str(snapshot / 'Directory.Build.props')]
    if not (snapshot / 'Directory.Build.props').exists():
        flags.append('-p:ImportDirectoryBuildProps=false')
    return env, flags, sdk


def publish(c, directory, source, cache):
    root = pg.validate_paths(c)
    directory = Path(directory).absolute()
    pg.require(directory.parent == root and directory.resolve() == directory and not directory.exists(),
               'Publish requires a fresh direct child of the owned private root')
    expected, provenance = formal_expectations(source, cache)
    directory.mkdir()
    receipt_path = root / (directory.name + '.receipt.json')
    pg.require(not receipt_path.exists(), 'Publish receipt already exists')
    snapshot = root / (directory.name + '.source')
    source_files = source_export(source, snapshot)
    env, flags, sdk = build_context(snapshot, cache)
    config = next(p for p in snapshot.iterdir() if p.name.casefold() == 'nuget.config')
    commands = [
        [str(DOTNET), 'restore', 'CP6.WebApi/CP6.WebApi.csproj', '--locked-mode', '--configfile', str(config), *flags],
        [str(DOTNET), 'publish', 'CP6.WebApi/CP6.WebApi.csproj', '--no-restore', '-c', 'Release',
         '-o', str(directory), '-p:UseAppHost=false', '-p:SourceRevisionId=' + source,
         '-warnaserror:MSB3277', *flags],
    ]
    logs = []
    for label, command in zip(('restore', 'publish'), commands):
        logfile = root / (directory.name + '.' + label + '.log')
        with logfile.open('wb') as output:
            result = subprocess.run(command, cwd=snapshot, env=env, stdout=output,
                                    stderr=subprocess.STDOUT, creationflags=pg.NO_WINDOW)
        logs.append({'command': command, 'workingDirectory': str(snapshot), 'log': str(logfile), 'sha256': digest(logfile.read_bytes()),
                     'exitCode': result.returncode})
        write(root / (directory.name + '.build.json'), {'sourceSha': source, 'steps': logs})
        pg.require(result.returncode == 0, label + ' failed; private build log retained')
    pg.require(all(digest((snapshot / name).read_bytes()) == sha for name, sha in source_files.items()),
               'Committed export changed during build')
    files = inventory(directory)
    required = closure(directory)
    for module in expected:
        pg.require(files[module['PackageId'] + '.dll'] == module['AssemblySha256'],
                   'Published Platform DLL differs from formal package')
    receipt = {'schema': SCHEMA, 'sourceSha': source, 'entry': ENTRY,
               'publishDirectory': str(directory), 'publishExitCode': 0, 'build': logs,
               'sourceExport': {'directory': str(snapshot), 'fileCount': len(source_files), 'sdk': sdk,
                                'filesSha256': digest(json.dumps(source_files, sort_keys=True).encode())},
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
