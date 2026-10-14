"""Synthetic ZIPs test checker plumbing only; never publication/qualification evidence."""
import base64
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import warnings
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import verify

IDS = ['CP6.Platform.' + x for x in ('Abstractions', 'AspNetCore', 'Contracts',
       'Deployment', 'EntityFramework', 'Messaging', 'Release')]
RUNTIME = [x for x in IDS if not x.endswith(('.Deployment', '.Release'))]
SCHEMA_ENTRY = 'contracts/release/capabilities/v1/capability-matrix.v1.schema.json'
CTX = {'type': 'object', 'additionalProperties': False,
       'required': ['id', 'version', 'profiles', 'scope'], 'properties': {
           'id': {'const': 'ctx-binding-v1'}, 'version': {'const': '1.0.0'},
           'profiles': {'const': ['ctx-v1']},
           'scope': {'const': ['Background', 'Http', 'Message']}}}
SCHEMA = {'$id': 'https://schemas.cp6.dev/release/capabilities/v1/capability-matrix',
          'properties': {'capabilities': {'items': {'oneOf': [CTX]}}}}


class CheckerTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.base = Path(self.tmp.name)
        self.manifest = {
            'schemaVersion': 1, 'mode': 'PREPARATION_ONLY',
            'approval': {'reference': 'SYNTHETIC UNIT TEST; NO REAL APPROVAL',
                'mainRepository': 'https://github.com/GTX537/CP6', 'mainSourceSha': '1' * 40,
                'platformRepository': 'https://github.com/GTX537/CP6.Platform',
                'platformSourceSha': '2' * 40, 'packageVersion': '0.10.99',
                'feedUrl': 'https://synthetic.invalid/index.json',
                'compatibility': 'COMPATIBLE', 'requiredCapabilities': ['ctx-binding-v1'],
                'runtimePackageIds': RUNTIME},
            'observation': {'feedUrl': 'https://synthetic.invalid/index.json',
                'publication': 'UNVERIFIED', 'packages': []}}
        for package_id in IDS:
            path = self.base / (package_id + '.nupkg')
            schemas = {SCHEMA_ENTRY: json.dumps(SCHEMA).encode()} if package_id.endswith('.Release') else {}
            files = {package_id + '.nuspec': self.nuspec(package_id), **schemas}
            self.write_zip(path, files)
            self.manifest['observation']['packages'].append({
                'id': package_id, 'path': str(path), **self.hashes(path),
                'schemas': [{'entry': n, 'sha256': hashlib.sha256(b).hexdigest()} for n, b in schemas.items()]})

    def nuspec(self, package_id, version='0.10.99', sha='2' * 40,
               repository='https://github.com/GTX537/CP6.Platform', dependency='0.10.99'):
        dep = (f'<dependency id="CP6.Platform.Contracts" version="{dependency}" />'
               if package_id != 'CP6.Platform.Contracts' else '')
        return (f'<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">'
                f'<metadata><id>{package_id}</id><version>{version}</version>'
                f'<repository type="git" url="{repository}" commit="{sha}" />'
                f'<dependencies><group targetFramework="net8.0">{dep}</group></dependencies>'
                '</metadata></package>').encode()

    @staticmethod
    def write_zip(path, files):
        with zipfile.ZipFile(path, 'w') as z:
            for name, content in files.items():
                z.writestr(name, content)

    @staticmethod
    def hashes(path):
        b = path.read_bytes()
        return {'sha256': hashlib.sha256(b).hexdigest(),
                'sha512Archive': base64.b64encode(hashlib.sha512(b).digest()).decode()}

    def mutate_archive(self, index=0, update=None, remove=()):
        package = self.manifest['observation']['packages'][index]
        path = Path(package['path'])
        with zipfile.ZipFile(path) as z:
            files = {n: z.read(n) for n in z.namelist() if n not in remove}
        files.update(update or {})
        self.write_zip(path, files)
        package.update(self.hashes(path))

    def result(self):
        return verify.evaluate(self.manifest, self.base)

    def reject(self, code):
        result = self.result()
        self.assertEqual('BLOCKED', result['admission'])
        self.assertIn(code, [f['code'] for f in result['findings']])

    def test_synthetic_identity_match_never_admits(self):
        result = self.result()
        self.assertEqual('MATCHED', result['identityChecks'])
        self.assertEqual('BLOCKED', result['admission'])
        self.assertEqual('BLOCKED', result['states']['trust'])
        for state in ('consumer', 'host', 'runtime', 'business'):
            self.assertEqual('NOT_RUN', result['states'][state])

    def test_hash_mismatch(self):
        self.manifest['observation']['packages'][0]['sha256'] = '0' * 64
        self.reject('HASH_MISMATCH')

    def test_raw_sha512_mismatch(self):
        self.manifest['observation']['packages'][0]['sha512Archive'] = base64.b64encode(b'0' * 64).decode()
        self.reject('HASH_MISMATCH')

    def test_source_mismatch(self):
        self.mutate_archive(update={IDS[0] + '.nuspec': self.nuspec(IDS[0], sha='3' * 40)})
        self.reject('SOURCE_MISMATCH')

    def test_repository_mismatch(self):
        self.mutate_archive(update={IDS[0] + '.nuspec': self.nuspec(IDS[0], repository='https://evil.invalid/repo')})
        self.reject('REPOSITORY_MISMATCH')

    def test_feed_mismatch(self):
        self.manifest['observation']['feedUrl'] = 'https://evil.invalid/index.json'
        self.reject('FEED_MISMATCH')

    def test_package_id_mismatch(self):
        self.mutate_archive(update={IDS[0] + '.nuspec': self.nuspec('Unapproved')})
        self.reject('PACKAGE_ID_MISMATCH')

    def test_mixed_versions(self):
        self.mutate_archive(update={IDS[0] + '.nuspec': self.nuspec(IDS[0], version='0.10.98')})
        self.reject('VERSION_MISMATCH')

    def test_mixed_dependency_version(self):
        self.mutate_archive(update={IDS[0] + '.nuspec': self.nuspec(IDS[0], dependency='0.10.98')})
        self.reject('DEPENDENCY_MISMATCH')

    def test_dependency_range_not_exact_pin(self):
        self.mutate_archive(update={IDS[0] + '.nuspec': self.nuspec(IDS[0], dependency='[0.10.99,1.0.0)')})
        self.reject('DEPENDENCY_MISMATCH')

    def test_exact_dependency_range(self):
        self.mutate_archive(update={IDS[0] + '.nuspec': self.nuspec(IDS[0], dependency='[0.10.99]')})
        self.assertEqual('MATCHED', self.result()['identityChecks'])

    def test_schema_hash_mismatch(self):
        self.manifest['observation']['packages'][-1]['schemas'][0]['sha256'] = '0' * 64
        self.reject('SCHEMA_HASH_MISMATCH')

    def test_unpinned_schema_rejected(self):
        self.mutate_archive(update={'contracts/unknown.schema.json': b'{}'})
        self.reject('SCHEMA_SET_MISMATCH')

    def test_missing_ctx_schema(self):
        self.mutate_archive(index=-1, remove=[SCHEMA_ENTRY])
        self.manifest['observation']['packages'][-1]['schemas'] = []
        self.reject('MISSING_CTX_SCHEMA')

    def test_capability_substring_in_description_is_not_contract(self):
        schema = {'description': 'ctx-binding-v1'}
        content = json.dumps(schema).encode()
        self.mutate_archive(index=-1, update={SCHEMA_ENTRY: content})
        self.manifest['observation']['packages'][-1]['schemas'][0]['sha256'] = hashlib.sha256(content).hexdigest()
        self.reject('CTX_SCHEMA_SHAPE_MISMATCH')

    def test_unknown_compatibility(self):
        self.manifest['approval']['compatibility'] = 'UNKNOWN'
        self.reject('COMPATIBILITY_NOT_QUALIFIED')

    def test_unknown_compatibility_token(self):
        self.manifest['approval']['compatibility'] = 'SeemsFine'
        self.reject('INVALID_INPUT')

    def test_local_candidate_rejected(self):
        self.manifest['observation']['publication'] = 'LOCAL_ONLY'
        self.reject('LOCAL_CANDIDATE_NOT_FORMAL')

    def test_no_formal_publication_self_assertion(self):
        self.manifest['observation']['publication'] = 'FORMALLY_TRUSTED'
        self.reject('INVALID_INPUT')

    def test_exact_seven_producer_set(self):
        self.manifest['observation']['packages'].pop()
        self.reject('INVALID_INPUT')

    def test_duplicate_package(self):
        self.manifest['observation']['packages'][-1] = copy.deepcopy(self.manifest['observation']['packages'][0])
        self.reject('INVALID_INPUT')

    def test_runtime_is_five_not_seven(self):
        self.manifest['approval']['runtimePackageIds'] = IDS
        self.reject('INVALID_INPUT')

    def test_unknown_field(self):
        self.manifest['approval']['trustMe'] = True
        self.reject('INVALID_INPUT')

    def test_boolean_schema_version_rejected(self):
        self.manifest['schemaVersion'] = True
        self.reject('INVALID_INPUT')

    def test_missing_file(self):
        self.manifest['observation']['packages'][0]['path'] = 'missing.nupkg'
        self.reject('ARCHIVE_INVALID')

    def test_path_traversal_archive(self):
        self.mutate_archive(update={'../escape': b'x'})
        self.reject('ARCHIVE_INVALID')

    def test_archive_entry_case_collision(self):
        self.mutate_archive(update={IDS[0].upper() + '.NUSPEC': b'x'})
        self.reject('ARCHIVE_INVALID')

    def test_xml_dtd_rejected(self):
        self.mutate_archive(update={IDS[0] + '.nuspec': b'<!DOCTYPE x [<!ENTITY x "boom">]><package />'})
        self.reject('NUSPEC_INVALID')

    def test_duplicate_xml_identity(self):
        xml = self.nuspec(IDS[0]).replace(b'</metadata>', b'<version>0.10.99</version></metadata>')
        self.mutate_archive(update={IDS[0] + '.nuspec': xml})
        self.reject('NUSPEC_INVALID')

    def test_supported_nuspec_namespaces(self):
        for namespace in ('http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd',
                          'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'):
            with self.subTest(namespace=namespace):
                xml = self.nuspec(IDS[0]).replace(
                    b'http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd', namespace.encode())
                self.mutate_archive(update={IDS[0] + '.nuspec': xml})
                self.assertEqual('MATCHED', self.result()['identityChecks'])

    def test_unknown_nuspec_namespace_rejected(self):
        xml = self.nuspec(IDS[0]).replace(
            b'http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd', b'https://unknown.invalid/nuspec.xsd')
        self.mutate_archive(update={IDS[0] + '.nuspec': xml})
        self.reject('NUSPEC_INVALID')

    def test_mixed_nuspec_namespace_cannot_hide_dependency(self):
        xml = self.nuspec(IDS[0]).replace(
            b'<dependency ', b'<dependency xmlns="https://unknown.invalid/nuspec.xsd" ')
        self.mutate_archive(update={IDS[0] + '.nuspec': xml})
        self.reject('NUSPEC_INVALID')

    def test_duplicate_schema_json_keys(self):
        content = b'{"properties":{},"properties":{}}'
        self.mutate_archive(index=-1, update={SCHEMA_ENTRY: content})
        self.manifest['observation']['packages'][-1]['schemas'][0]['sha256'] = hashlib.sha256(content).hexdigest()
        self.reject('SCHEMA_INVALID')

    def test_unknown_capability_declaration_rejected(self):
        schema = copy.deepcopy(SCHEMA)
        unknown = copy.deepcopy(CTX)
        unknown['properties']['id']['const'] = 'future-unknown-capability'
        schema['properties']['capabilities']['items']['oneOf'].append(unknown)
        content = json.dumps(schema).encode()
        self.mutate_archive(index=-1, update={SCHEMA_ENTRY: content})
        self.manifest['observation']['packages'][-1]['schemas'][0]['sha256'] = hashlib.sha256(content).hexdigest()
        self.reject('CTX_SCHEMA_SHAPE_MISMATCH')

    def test_duplicate_dependency_rejected(self):
        xml = self.nuspec(IDS[0]).replace(b'</group>', b'<dependency id="CP6.Platform.Contracts" version="0.10.99" /></group>')
        self.mutate_archive(update={IDS[0] + '.nuspec': xml})
        self.reject('NUSPEC_INVALID')

    def test_duplicate_archive_entry_rejected(self):
        package = self.manifest['observation']['packages'][0]
        with warnings.catch_warnings():
            warnings.simplefilter('ignore', UserWarning)
            with zipfile.ZipFile(package['path'], 'a') as archive:
                archive.writestr(IDS[0] + '.nuspec', self.nuspec(IDS[0]))
        package.update(self.hashes(Path(package['path'])))
        self.reject('ARCHIVE_INVALID')

    def test_symlink_archive_entry_rejected(self):
        package = self.manifest['observation']['packages'][0]
        entry = zipfile.ZipInfo('link')
        entry.create_system = 3
        entry.external_attr = 0o120777 << 16
        with zipfile.ZipFile(package['path'], 'a') as archive:
            archive.writestr(entry, '../outside')
        package.update(self.hashes(Path(package['path'])))
        self.reject('ARCHIVE_INVALID')

    def test_archive_size_bound(self):
        with patch.object(verify, 'MAX_ARCHIVE', 20):
            self.reject('ARCHIVE_INVALID')

    def test_entry_count_bound(self):
        with patch.object(verify, 'MAX_ENTRIES', 1):
            self.reject('ARCHIVE_INVALID')

    def test_schema_json_nonfinite_rejected(self):
        content = b'{"value": NaN}'
        self.mutate_archive(index=-1, update={SCHEMA_ENTRY: content})
        self.manifest['observation']['packages'][-1]['schemas'][0]['sha256'] = hashlib.sha256(content).hexdigest()
        self.reject('SCHEMA_INVALID')

    def test_malformed_manifest_shapes_rejected(self):
        for bad in (None, [], 'x', 0, True, {'schemaVersion': 1}):
            with self.subTest(bad=bad):
                result = verify.evaluate(bad, self.base)
                self.assertIn('INVALID_INPUT', [f['code'] for f in result['findings']])

    def test_unsafe_archive_windows_paths(self):
        for name in ('C:/file', 'folder\\file', 'AUX.txt', '/absolute', 'a/./b', 'a//b'):
            with self.subTest(name=name):
                with self.assertRaises(verify.Invalid):
                    verify.safe_entry(name)

    def test_cli_valid_synthetic_readonly_and_always_blocked(self):
        inp = self.base / 'synthetic-input.json'
        inp.write_text(json.dumps(self.manifest), encoding='utf-8')
        paths = [inp] + [Path(p['path']) for p in self.manifest['observation']['packages']]
        before = {p: p.read_bytes() for p in paths}
        out = self.base / 'fresh-output'
        process = subprocess.run([sys.executable, str(Path(verify.__file__)), '--input', str(inp),
                                  '--output', str(out)], capture_output=True, text=True)
        self.assertEqual(2, process.returncode)
        report = json.loads((out / 'report.json').read_text())
        self.assertEqual('BLOCKED', report['admission'])
        self.assertEqual('MATCHED', report['identityChecks'])
        self.assertEqual(before, {p: p.read_bytes() for p in paths})

    def test_cli_duplicate_json_rejected_and_existing_output_not_overwritten(self):
        inp = self.base / 'input.json'
        inp.write_text('{"schemaVersion":1,"schemaVersion":1}', encoding='utf-8')
        out = self.base / 'out'
        cli = [sys.executable, str(Path(verify.__file__)), '--input', str(inp), '--output', str(out)]
        first = subprocess.run(cli, capture_output=True, text=True)
        self.assertEqual(2, first.returncode)
        report = out / 'report.json'
        self.assertTrue(report.is_file())
        self.assertIn('INVALID_INPUT', report.read_text())
        before = report.read_bytes()
        second = subprocess.run(cli, capture_output=True, text=True)
        self.assertEqual(2, second.returncode)
        self.assertEqual(before, report.read_bytes())


if __name__ == '__main__':
    unittest.main()
