"""External archive version/binding regressions; native rehearsal is recorded in W676-01."""
import copy
import json
import unittest
from uuid import UUID

import archive
import storage_model_v2 as model


class StorageModelArchiveTests(unittest.TestCase):
    def setUp(self):
        self.tenant = UUID("565d8f3c-75a5-4e2a-bef1-e410c3fd4a10")
        self.installation = UUID("fa932b22-14ce-457d-afc6-95a2fbdb358b")
        self.operation = UUID("c5eaa44b-7ee1-4ad9-8658-ac409fbf9ee6")
        self.target = archive.PhysicalTarget.from_mapping(dict(
            Server="127.0.0.1", Port=55432, Database="model_v2_fixture", DatabaseOid=16386,
            SystemIdentifier="7706117870886737900"))
        self.result = dict(Outcome="READY", InstallationId=str(self.installation), OperationId=str(self.operation),
            TenantId=str(self.tenant), Schema="t_" + self.tenant.hex, Profile=model.MODEL,
            Lineage=archive.LINEAGE, MigrationId=model.MIGRATION, SharedMigration=model.SHARED,
            ScriptSha256="a" * 64, DatabaseOid=self.target.database_oid,
            SystemIdentifier=self.target.system_identifier, CompletedAt="2026-11-16T00:00:00+00:00")
        self.descriptor = dict(model=model.MODEL, migrationId=model.MIGRATION, sharedMigration=model.SHARED,
            original=dict(operationId=str(self.operation), scriptSha256="a" * 64,
                          resultJson=json.dumps(self.result, indent=2)))

    def test_preserves_original_receipt_text(self):
        before = copy.deepcopy(self.descriptor)
        model.verify(self.descriptor, self.tenant, self.installation, self.target)
        self.assertEqual(before, self.descriptor)

    def test_rejects_foreign_or_changed_original_binding(self):
        replacements = dict(TenantId=str(self.installation), InstallationId=str(self.tenant),
            OperationId=str(self.tenant), Schema="t_" + self.installation.hex, Profile=archive.PROFILE,
            MigrationId=archive.MIGRATION, SharedMigration="unknown", DatabaseOid=16387,
            SystemIdentifier="7706117870886737901", ScriptSha256="b" * 64, Outcome="PREPARED")
        for field, value in replacements.items():
            with self.subTest(field=field):
                changed = copy.deepcopy(self.descriptor)
                changed["original"]["resultJson"] = json.dumps({**self.result, field: value})
                with self.assertRaisesRegex(archive.ArchiveError, "original-binding-rejected"):
                    model.verify(changed, self.tenant, self.installation, self.target)

    def test_unknown_model_and_malformed_original_fail_at_ingestion(self):
        for change in ({"model": "rma-application-core-tenant-v99"}, {"original": None},
                       {"original": {**self.descriptor["original"], "scriptSha256": "invalid"}}):
            with self.subTest(change=change), self.assertRaises(archive.ArchiveError):
                model.verify({**self.descriptor, **change}, self.tenant, self.installation, self.target)

    def test_model_v2_cannot_export_without_complete_companions(self):
        # Must fail before examining paths, credentials or opening a database connection.
        with self.assertRaisesRegex(archive.ArchiveError, "needs-complete-file-content-history-archive"):
            archive.export_bundle(None, None, None, None, None, None, include_rma_storage_model_v2=True)


if __name__ == "__main__":
    unittest.main()
