"""Behavior regressions for local tooling; fixtures are not runtime evidence."""
import copy
import json
import os
import subprocess
import sys
from contextlib import ExitStack
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import patch

import published_artifact as artifact
import verify_published_host as host
from verify_host import PACKAGE_IDS, digest, write


class PublishTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.directory = self.root / 'publish'
        self.directory.mkdir()
        self.source = 'a' * 40
        target, libraries = {}, {}
        self.expected = []
        for name in ['CP6.WebApi', *PACKAGE_IDS]:
            version = '0.10.3' if name in PACKAGE_IDS else '1.0.0'
            library = name + '/' + version
            filename = name + '.dll'
            (self.directory / filename).write_bytes(name.encode())
            target[library] = {'runtime': {'lib/net8.0/' + filename: {}}}
            libraries[library] = {'type': 'package' if name in PACKAGE_IDS else 'project'}
            if name in PACKAGE_IDS:
                self.expected.append({'PackageId': name, 'PackageVersion': version, 'SourceSha': 'b' * 40,
                                      'AssemblySha256': digest(name.encode())})
        # Portable publish retains RID-specific native assets and localized resources.
        target['CP6.WebApi/1.0.0']['runtimeTargets'] = {
            'runtimes/win-x64/native/test.dll': {'assetType': 'native', 'rid': 'win-x64'}}
        target['CP6.WebApi/1.0.0']['resources'] = {'lib/net8.0/ja/test.resources.dll': {'locale': 'ja'}}
        for name in ['runtimes/win-x64/native/test.dll', 'ja/test.resources.dll']:
            path = self.directory / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b'test')
        write(self.directory / artifact.DEPS, {'runtimeTarget': {'name': 'net8.0'},
                                             'targets': {'net8.0': target}, 'libraries': libraries})
        write(self.directory / artifact.RUNTIME, {'runtimeOptions': {'tfm': 'net8.0', 'frameworks': [{'name': 'Microsoft.NETCore.App'}]}})
        write(self.directory / 'appsettings.json', {})
        self.receipt = {'schema': artifact.SCHEMA, 'sourceSha': self.source, 'entry': artifact.ENTRY,
                        'publishExitCode': 0, 'publishDirectory': str(self.directory),
                        'files': artifact.inventory(self.directory), 'requiredFiles': artifact.closure(self.directory),
                        'expectedModules': self.expected, 'formalPackages': {'test': 'independent source'}}
        self.receipt_path = self.root / 'receipt.json'
        self.pin = self.save()
        self.formal = patch.object(artifact, 'formal_expectations', return_value=(self.expected, self.receipt['formalPackages']))
        self.formal.start()
        self.addCleanup(self.formal.stop)

    def save(self):
        write(self.receipt_path, self.receipt)
        return digest(self.receipt_path.read_bytes())

    def verify(self, **overrides):
        args = dict(directory=self.directory, receipt_path=self.receipt_path, receipt_sha256=self.pin,
                    source=self.source, cache=self.root)
        args.update(overrides)
        return artifact.verify(**args)

    def test_closure_accepts_complete_output_and_rejects_missing_runtime_assets(self):
        self.verify()
        for relative in (artifact.ENTRY, artifact.RUNTIME, 'runtimes/win-x64/native/test.dll', 'ja/test.resources.dll'):
            with self.subTest(relative=relative):
                path = self.directory / relative
                raw = path.read_bytes()
                path.unlink()
                try:
                    with self.assertRaises(RuntimeError): self.verify()
                    with self.assertRaises(RuntimeError): artifact.closure(self.directory)
                finally:
                    path.write_bytes(raw)

    def test_extra_or_changed_output_is_not_rebaselined(self):
        extra = self.directory / 'unexpected.dll'
        extra.write_bytes(b'extra')
        with self.assertRaisesRegex(RuntimeError, 'inventory'): self.verify()
        extra.unlink()
        (self.directory / artifact.ENTRY).write_bytes(b'wrong host')
        with self.assertRaisesRegex(RuntimeError, 'inventory'): self.verify()

    def test_pin_source_and_exact_path_are_required(self):
        for overrides in ({'receipt_sha256': '0' * 64}, {'source': 'c' * 40}, {'directory': self.root}):
            with self.subTest(overrides=overrides), self.assertRaises(RuntimeError): self.verify(**overrides)
        self.receipt['sourceSha'] = 'c' * 40
        self.save()
        with self.assertRaisesRegex(RuntimeError, 'pin mismatch'): self.verify()

    def test_even_repinned_output_cannot_self_attest_platform_identity(self):
        name = PACKAGE_IDS[0] + '.dll'
        (self.directory / name).write_bytes(b'wrong platform')
        self.receipt['files'] = artifact.inventory(self.directory)
        self.pin = self.save()
        with self.assertRaisesRegex(RuntimeError, 'formal package'): self.verify()

    def test_preflight_denial_never_starts_process_or_writes_database(self):
        args = types.SimpleNamespace(owner='owner', settings='private', publish_directory=str(self.directory),
                                     receipt=str(self.receipt_path), receipt_sha256='0' * 64,
                                     source_sha=self.source, package_cache=str(self.root), negative_checks=False)
        with patch.object(host.pg, 'load_settings', return_value={'Owner': 'owner'}), \
             patch.object(host.pg, 'validate_paths', return_value=self.root), \
             patch.object(host.pg, 'sql') as sql, patch.object(host.subprocess, 'Popen') as process:
            with self.assertRaisesRegex(RuntimeError, 'pin mismatch'): host.run(args)
            process.assert_not_called()
            sql.assert_not_called()


class SourceBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.repo = self.root / 'repo'
        self.repo.mkdir()
        self.project = self.repo / 'CP6.WebApi'
        self.project.mkdir()
        (self.project / 'CP6.WebApi.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>')
        (self.project / 'Program.cs').write_text('// committed')
        (self.project / 'appsettings.json').write_text('{}')
        (self.repo / '.gitignore').write_text('tmp/\nappsettings.Development.json\nbin/\nobj/\n')
        (self.repo / 'NuGet.config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        self.git('init', '-q')
        self.git('config', 'core.autocrlf', 'true')
        self.git('add', '.')
        self.git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-qm', 'fixture')
        self.source = self.git('rev-parse', 'HEAD').decode().strip()
        for relative in ('tmp/Uncommitted.cs', 'appsettings.Development.json', 'obj/stale.targets', 'bin/stale.dll'):
            p = self.project / relative
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text('private-uncommitted-input')
        # The original clean_source accepts these, although SDK defaults include .cs/JSON.
        self.assertEqual(b'', self.git('status', '--porcelain'))

    def git(self, *args):
        return subprocess.check_output(['git', '-c', 'core.longpaths=true', *args], cwd=self.repo, creationflags=artifact.pg.NO_WINDOW)

    def test_publish_uses_committed_export_excluding_ignored_inputs_and_old_outputs(self):
        output = self.root / 'publish'
        observed = []
        real_run = subprocess.run
        def build(command, *, cwd, **kwargs):
            if command[0] == "git":
                return real_run(command, cwd=cwd, **kwargs)
            cwd = Path(cwd)
            observed.append(cwd)
            self.assertEqual('// committed', (cwd / 'CP6.WebApi/Program.cs').read_text())
            for relative in ('tmp/Uncommitted.cs', 'appsettings.Development.json', 'obj/stale.targets', 'bin/stale.dll'):
                self.assertFalse((cwd / 'CP6.WebApi' / relative).exists(), relative)
            if 'publish' in command:
                (output / artifact.ENTRY).write_bytes(b'fixture-only')
            return types.SimpleNamespace(returncode=0)
        with patch.object(artifact, 'ROOT', self.repo), \
             patch.object(artifact.pg, 'validate_paths', return_value=self.root), \
             patch.object(artifact, 'formal_expectations', return_value=([], {})), \
             patch.object(artifact, 'closure', return_value=[artifact.ENTRY]), \
             patch.object(artifact.subprocess, 'run', side_effect=build):
            artifact.publish({}, output, self.source, self.root / 'cache')
        self.assertTrue(observed and all(p != self.repo for p in observed))
        receipt = json.loads((self.root / 'publish.receipt.json').read_bytes())
        self.assertEqual(self.source, receipt['sourceSha'])


    def test_sdk_inputs_exclude_ignored_files_parent_imports_and_inherited_properties(self):
        # Real SDK evaluation, no restore/build, service or database. First prove
        # that the original checkout includes both ignored Compile and Content.
        old_env, old_flags, _ = artifact.build_context(self.repo, self.root / 'cache')
        old = subprocess.run([str(artifact.DOTNET), 'msbuild', 'CP6.WebApi/CP6.WebApi.csproj',
                              '-getItem:Compile,Content', *old_flags], cwd=self.repo, env=old_env,
                             capture_output=True, text=True, encoding='utf-8', creationflags=artifact.pg.NO_WINDOW)
        self.assertEqual(0, old.returncode, old.stdout + old.stderr)
        old_data = json.loads(old.stdout[old.stdout.index('{'):])
        old_paths = {item['Identity'].replace('\\', '/') for group in old_data['Items'].values() for item in group}
        self.assertIn('tmp/Uncommitted.cs', old_paths)
        self.assertIn('appsettings.Development.json', old_paths)
        parent_marker = self.root / 'parent-only.cs'
        parent_marker.write_text('// outside source')
        (self.root / 'Directory.Build.props').write_text('<Project><ItemGroup><Compile Include="' + str(parent_marker) + '" /></ItemGroup></Project>')
        (self.root / 'Directory.Build.targets').write_text('<Project><ItemGroup><Compile Include="' + str(parent_marker) + '" /></ItemGroup></Project>')
        (self.root / 'Directory.Packages.props').write_text('<Project><ItemGroup><Compile Include="' + str(parent_marker) + '" /></ItemGroup></Project>')
        (self.root / 'global.json').write_text('{"sdk":{"version":"0.0.0","rollForward":"disable"}}')
        snapshot = self.root / 'source'
        with patch.object(artifact, 'ROOT', self.repo):
            artifact.source_export(self.source, snapshot)
        with patch.dict(os.environ, {'CustomBeforeMicrosoftCommonTargets': str(self.root / 'Directory.Build.targets'), 'MSBuildSDKsPath': 'invalid-sdk-path'}):
            env, flags, _ = artifact.build_context(snapshot, self.root / 'cache')
        command = [str(artifact.DOTNET), 'msbuild', 'CP6.WebApi/CP6.WebApi.csproj', '-getItem:Compile,Content', *flags]
        result = subprocess.run(command, cwd=snapshot, env=env, capture_output=True, text=True, encoding='utf-8', creationflags=artifact.pg.NO_WINDOW)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        data = json.loads(result.stdout[result.stdout.index('{'):])
        paths = {item['Identity'].replace('\\', '/') for group in data['Items'].values() for item in group}
        self.assertIn('Program.cs', paths)
        self.assertIn('appsettings.json', paths)
        self.assertFalse(any('Uncommitted' in p or 'Development' in p or 'parent-only' in p for p in paths), paths)

    def test_explicit_external_import_or_project_reference_is_rejected_before_build(self):
        for xml in ('<Import Project="../../external.props" />', '<ItemGroup><ProjectReference Include="../../external.csproj" /></ItemGroup>'):
            with self.subTest(xml=xml):
                (self.project / 'CP6.WebApi.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk.Web">' + xml + '</Project>')
                self.git('add', 'CP6.WebApi/CP6.WebApi.csproj')
                self.git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-qm', 'external boundary')
                source = self.git('rev-parse', 'HEAD').decode().strip()
                with patch.object(artifact, 'ROOT', self.repo):
                    with self.assertRaisesRegex(RuntimeError, 'imports/tasks|escapes'):
                        artifact.source_export(source, self.root / 'source')
                self.assertFalse((self.root / 'source').exists())


class OwnedProcessTests(unittest.TestCase):
    def check_identity_failure(self, cleanup_failure=False):
        with tempfile.TemporaryDirectory() as temp, ExitStack() as stack:
            root = Path(temp).resolve()
            directory = root / 'publish'
            directory.mkdir()
            write(directory / 'appsettings.json', {})
            settings = dict(Owner='fixture', RunDirectory=str(root), Runtime='fixture', RuntimePassword='fixture-only', AdminPassword='fixture-only', Database='fixture')
            receipt = dict(files={artifact.ENTRY:'a'*64}, expectedModules=[])
            args = types.SimpleNamespace(owner='fixture', settings='unused', publish_directory=str(directory), receipt='unused', receipt_sha256='a'*64, source_sha='b'*40, package_cache='unused', negative_checks=False)
            real_popen = subprocess.Popen
            children = []
            def start(*args, **kwargs):
                child = real_popen([sys.executable, '-c', 'import time; time.sleep(30)'], **kwargs)
                children.append(child)
                if cleanup_failure:
                    stack.enter_context(patch.object(child, 'terminate', side_effect=PermissionError('injected cleanup failure')))
                return child
            for obj, name, value in ((host.pg, 'load_settings', settings), (host.pg, 'validate_paths', root),
                                     (host.artifact, 'verify', receipt), (host.pg, 'verify', dict(SystemIdentifier='fixture', Database='fixture', Oid=1, Owner='fixture', Port=0)), (host.pg, 'sql', None)):
                stack.enter_context(patch.object(obj, name, return_value=value))
            stack.enter_context(patch.object(host.subprocess, 'Popen', side_effect=start))
            original = LookupError('injected initial identity failure')
            stack.enter_context(patch.object(host, 'process_identity', side_effect=[original, RuntimeError('unexpected second identity query')]))
            try:
                with self.assertRaises(LookupError) as failure:
                    host.run(args)
                self.assertIs(original, failure.exception)
                result = json.loads(next(root.glob('published-host-*/results.json')).read_bytes())
                self.assertEqual('LookupError', result['failureType'])
                self.assertFalse(result['completed'])
                if cleanup_failure:
                    self.assertEqual('PermissionError', result['cleanupFailureType'])
                else:
                    self.assertIsNotNone(children[0].poll())
                    self.assertTrue(result['processStopped'])
            finally:
                stack.close()
                for child in children:
                    if child.poll() is None:
                        child.terminate()
                    child.wait(timeout=5)

    def test_initial_identity_failure_stops_owned_child_and_preserves_failure(self):
        self.check_identity_failure()

    def test_cleanup_failure_does_not_replace_original_identity_failure(self):
        self.check_identity_failure(cleanup_failure=True)


class ReadinessTests(unittest.TestCase):
    def test_loaded_mismatch_or_wrong_process_is_refused_unloaded_remains_unknown(self):
        expected = [{'PackageId': 'Loaded', 'PackageVersion': '0.10.3', 'SourceSha': 'a' * 40, 'AssemblySha256': 'b' * 64},
                    {'PackageId': 'Unloaded', 'PackageVersion': '0.10.3', 'SourceSha': 'a' * 40, 'AssemblySha256': 'c' * 64}]
        native = {'Pid': 42, 'Started': '2026-10-17T00:00:00+00:00'}
        modules = []
        for e in expected:
            modules.append({'packageId': e['PackageId'], 'status': 'Unknown', 'reasonCodes': ['ASSEMBLY_NOT_LOADED'],
                            'expected': {k[0].lower() + k[1:]: v for k, v in e.items() if k != 'PackageId'}, 'observed': None})
        modules[0].update(status='Matched', reasonCodes=[], observed={
            'assemblySha256': 'b' * 64, 'sourceSha': 'a' * 40, 'declaredPackageVersion': '0.10.3',
            'defaultLoadContext': True, 'loadedMvid': 'test-mvid', 'backingMvid': 'test-mvid'})
        body = {'processId': 42, 'processStartedAtUtc': native['Started'], 'modules': modules,
                'identityStatus': 'Unknown', 'reasonCodes': [], 'rollback': {'allowed': False},
                'runtimeQualification': 'Unknown', 'businessAcceptance': 'Unknown', 'trustState': 'Unknown'}
        self.assertEqual({'status': 'Unknown', 'ready': False, 'loadedCount': 1, 'unloadedCount': 1},
                         host.identity_readiness(body, expected, native))
        for mutate in (lambda b: b.update(processId=99),
                       lambda b: b.update(processStartedAtUtc='2026-10-17T00:01:00+00:00'),
                       lambda b: b['modules'][0]['observed'].update(assemblySha256='d' * 64),
                       lambda b: b['modules'][0].update(status='Mismatch'),
                       lambda b: b['modules'][1].update(status='Matched')):
            candidate = copy.deepcopy(body)
            mutate(candidate)
            with self.assertRaises(RuntimeError): host.identity_readiness(candidate, expected, native)


if __name__ == '__main__':
    unittest.main()
