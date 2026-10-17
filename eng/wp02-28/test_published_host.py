"""Behavior regressions for local tooling; fixtures are not runtime evidence."""
import copy
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
