import contextlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest


MODULE = Path(__file__).with_name('legacy_inventory.py')


class LegacyInventoryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        spec = importlib.util.spec_from_file_location('legacy_inventory', str(MODULE))
        cls.tool = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.tool)

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / 'repo'
        self.root.mkdir()
        self.git('init', '-q')
        self.git('config', 'user.name', 'Inventory Test')
        self.git('config', 'user.email', 'inventory-test@example.invalid')
        self.git('config', 'core.autocrlf', 'false')
        for name in ('CP6.Core', 'CP6.Entity', 'CP6.WebApi'):
            self.write(name+'/'+name+'.csproj', '<Project Sdk="Microsoft.NET.Sdk"/>')

    def git(self, *args, data=None):
        return subprocess.check_output(['git', '-C', str(self.root), *args], input=data,
                                       stderr=subprocess.PIPE).decode().strip()

    def write(self, name, text):
        p = self.root/name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(text if isinstance(text, bytes) else text.encode('utf-8'))

    def commit(self):
        self.git('add', '--', '.')
        self.git('commit', '-qm', 'isolated scanner fixture')
        return self.git('rev-parse', 'HEAD')

    def scan(self):
        return self.tool.build_inventory(self.root, self.commit())

    def test_catalog_covers_exact_twenty_tables_even_when_models_removed(self):
        r = self.scan()
        self.assertEqual(20, len(r['tables']))
        self.assertEqual('Crm_Account', r['tables'][0]['table'])
        self.assertEqual([], r['references'])
        self.assertFalse(r['developmentAcceptanceAssessed'])
        self.assertFalse(r['databaseWriteAbsenceVerified'])

    def test_frozen_revision_ignores_dirty_and_untracked_sources(self):
        self.write('CP6.Core/Example.cs', 'CrmLead oldEntity;')
        rev = self.commit()
        self.write('CP6.Core/Example.cs', 'removed in uncommitted work')
        self.write('CP6.Core/secret.Local.json', '{"password":"do-not-report","x":"Crm_Account"}')
        r = self.tool.build_inventory(self.root, rev)
        self.assertEqual(['CP6.Core/Example.cs'], [x['path'] for x in r['references']])
        self.assertNotIn('do-not-report', json.dumps(r))
        self.assertEqual(rev, r['sourceCommit'])

    def test_preserves_new_identity_and_erp_contracts_with_similar_names(self):
        self.write('CP6.Core/Bridge.cs', 'CrmIdentitySnapshot; CrmAccountId; CrmOpportunityId; CrmServiceTokenRecord;')
        self.assertEqual([], self.scan()['references'])

    def test_matches_exact_entity_dbset_table_enum_and_namespace(self):
        self.write('CP6.Core/Example.cs', 'CrmLead item;\nctx.CrmLeads;\n[Crm_Lead]\nCrmLeadStatus.New;\nusing CP6.Entity.DomainModels.Crm;')
        r = self.scan()
        self.assertEqual([1, 2, 3, 4, 5], [m['line'] for m in r['references'][0]['matches']])
        self.assertTrue(r['runtimeReferencesRemain'])

    def test_reports_no_source_line_or_surrounding_secret(self):
        self.write('CP6.Core/Example.cs', 'CrmLead item; // password=private-test-value')
        r = self.scan()
        self.assertNotIn('private-test-value', json.dumps(r))
        self.assertEqual({'line': 1, 'symbol': 'CrmLead'}, r['references'][0]['matches'][0])

    def test_snapshot_blocks_but_historical_migration_is_retained(self):
        self.write('CP6.Core/Migrations/202601010001_Legacy.cs', 'Crm_Lead')
        self.write('CP6.Core/Migrations/CP6ContextModelSnapshot.cs', 'CrmLead')
        r = self.scan()
        by_path = {x['path']: x for x in r['references']}
        self.assertEqual('migration-history', by_path['CP6.Core/Migrations/202601010001_Legacy.cs']['category'])
        self.assertEqual('model-snapshot', by_path['CP6.Core/Migrations/CP6ContextModelSnapshot.cs']['category'])
        self.assertEqual(1, r['summary']['blockingFiles'])

    def test_tests_and_source_protection_tools_are_not_runtime(self):
        self.write('CP6.Tests/LegacyTests.cs', 'CrmLead')
        self.write('tools/CP6.Crm.SourceFence/Program.cs', 'Crm_Lead')
        self.write('eng/crm/source-fence-tests/Test.cs', 'CrmLead')
        r = self.scan()
        self.assertEqual({'tests', 'operations'}, {x['category'] for x in r['references']})
        self.assertFalse(r['runtimeReferencesRemain'])
        self.assertFalse(r['developmentAcceptanceAssessed'])

    def test_unknown_code_root_with_legacy_reference_requires_review(self):
        self.write('new-service/Job.cs', 'CrmLead')
        r = self.scan()
        self.assertEqual('unclassified', r['references'][0]['category'])
        self.assertEqual(1, r['summary']['blockingFiles'])

    def test_frontend_runtime_is_scanned(self):
        self.write('cp6.web/src/views/Example.vue', '<script>const type="Crm_Lead";</script>')
        self.assertEqual('runtime', self.scan()['references'][0]['category'])

    def test_deterministic_report_and_source_inventory_digest(self):
        self.write('CP6.Core/Example.cs', 'CrmLead')
        rev = self.commit()
        a = self.tool.build_inventory(self.root, rev)
        b = self.tool.build_inventory(self.root, rev)
        self.assertEqual(a, b)
        self.assertEqual(64, len(a['scannedFileSetSha256']))
        self.assertNotIn(str(self.root), json.dumps(a))

    def test_rejects_abbreviated_or_symbolic_revision(self):
        self.commit()
        for rev in ('HEAD', 'main', '--help', 'abc1234'):
            with self.subTest(revision=rev), self.assertRaises(self.tool.InventoryError):
                self.tool.build_inventory(self.root, rev)

    def test_rejects_invalid_utf8_in_scanned_source(self):
        self.write('CP6.Core/Example.cs', b'CrmLead\xff')
        with self.assertRaises(self.tool.InventoryError):
            self.scan()

    def test_rejects_symlink_in_source_scope(self):
        blob = self.git('hash-object', '-w', '--stdin', data=b'../../private.txt')
        self.git('update-index', '--add', '--cacheinfo', '120000,'+blob+',CP6.Core/linked.cs')
        self.git('add', '--', '*.csproj', 'CP6.Entity', 'CP6.WebApi')
        self.git('commit', '-qm', 'symlink fixture')
        with self.assertRaises(self.tool.InventoryError):
            self.tool.build_inventory(self.root, self.git('rev-parse', 'HEAD'))

    def test_rejects_submodule_in_inventory_tree(self):
        rev = self.commit()
        self.git('update-index', '--add', '--cacheinfo', '160000,'+rev+',nested-service')
        self.git('commit', '-qm', 'gitlink fixture')
        with self.assertRaises(self.tool.InventoryError):
            self.tool.build_inventory(self.root, self.git('rev-parse', 'HEAD'))

    def test_rejects_incomplete_repository(self):
        (self.root/'CP6.Core/CP6.Core.csproj').unlink()
        with self.assertRaises(self.tool.InventoryError):
            self.scan()

    def test_cli_reports_pending_references_and_does_not_overwrite(self):
        self.write('CP6.Core/Example.cs', 'CrmLead')
        rev = self.commit()
        out = Path(self.temp.name)/'report.json'
        args = ['--root', str(self.root), '--revision', rev, '--output', str(out), '--require-no-runtime-references']
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(2, self.tool.main(args))
            before = out.read_bytes()
            self.assertEqual(1, self.tool.main(args))
        self.assertEqual(before, out.read_bytes())
        self.assertFalse(json.loads(before)['developmentAcceptanceAssessed'])


if __name__ == '__main__':
    unittest.main()
