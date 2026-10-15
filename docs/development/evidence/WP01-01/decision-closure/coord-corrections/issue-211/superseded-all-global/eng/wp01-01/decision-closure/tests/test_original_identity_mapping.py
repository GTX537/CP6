"""Static regression for Issue 211; synthetic rows exercise declared constraints.

This interprets the candidate's identity tuple and uniqueness declarations only.
It does not implement a database registry or prove runtime isolation/locking.
"""
import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[4]
MAPPING = ROOT / 'docs/development/evidence/WP01-01/decision-closure/postgresql-mapping.json'
ENTITIES = ('Project', 'Iteration', 'IterationInput', 'CandidateRef',
            'ReviewRound', 'ExternalRequestReceipt')


class OriginalIdentityMappingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.mapping = json.loads(MAPPING.read_bytes())
        cls.tables = {e['entity']: e for e in cls.mapping['entities']}

    def identity(self, name):
        table = self.tables[name]
        self.assertIn('original_identity', table, name + ' has no original Id authority binding')
        return table, table['original_identity']

    def declared_tuple(self, name, raw, tenant):
        _, identity = self.identity(name)
        values = {'domain_key': identity['registry_domain'].encode('utf-8'),
                  'schema_bytes': b'synthetic-fixed-original-schema',
                  'scope_bytes': bytes.fromhex(identity['scope_bytes_hex']),
                  'identity_bytes': raw, 'tenant_id': tenant.encode('utf-8')}
        return tuple(values[c] for c in identity['interning']['tuple_columns'])

    def test_all_six_original_ids_are_required_bytes_with_required_authority(self):
        for name in ENTITIES:
            with self.subTest(entity=name):
                table, identity = self.identity(name)
                cols = {c['name']: c for c in table['columns']}
                self.assertEqual('Id', identity['source_field'])
                self.assertEqual('bytea', cols[identity['raw_column']]['postgresql'])
                self.assertIs(cols[identity['raw_column']]['nullable'], False)
                self.assertIs(cols[identity['key_column']]['nullable'], False)
                self.assertEqual('ORIGINAL_GLOBAL_IDENTITY', identity['scope'])
                self.assertEqual('EXACT_ORIGINAL_BYTES', identity['raw_binding'])

    def test_global_original_identity_is_independent_of_existing_business_keys(self):
        for name in ENTITIES:
            with self.subTest(entity=name):
                table, identity = self.identity(name)
                key = identity['key_column']
                unique = [u['columns'] for u in table['unique_constraints']]
                self.assertIn([key], unique, 'Original identity cannot be tenant/parent scoped')
                self.assertTrue(any(u['name'] == name.lower() + '_business'
                                    and key not in u['columns'] for u in table['unique_constraints']))
                self.assertTrue(any(f['columns'] == ['tenant_id', key]
                                    and f['target_entity'] == 'OwnerKey'
                                    for f in table['foreign_keys']))

    def test_same_original_id_conflicts_despite_changed_business_number_and_tenant(self):
        for name in ENTITIES:
            with self.subTest(entity=name):
                table, identity = self.identity(name)
                original = self.declared_tuple(name, b'original-id', 'tenant-A')
                other_tenant = self.declared_tuple(name, b'original-id', 'tenant-B')
                self.assertEqual(original, other_tenant, 'Tenant cannot split global interning')
                key = identity['key_column']
                columns = {c['name'] for c in table['columns']}
                rows = [{c: (number, c) for c in columns} for number in (1, 2)]
                for row in rows:
                    row[key] = 'one-interned-key'
                keys = [table['primary_key']['columns']] + [u['columns'] for u in table['unique_constraints']]
                collisions = [k for k in keys if len({tuple(r[c] for c in k) for r in rows}) < 2]
                self.assertIn([key], collisions)
                self.assertEqual('SAFE_IDENTITY_CONFLICT_NO_NEW_UUID',
                                 identity['interning']['same_tuple_other_tenant'])

    def test_hash_collision_compares_all_original_tuple_bytes(self):
        for name in ENTITIES:
            with self.subTest(entity=name):
                _, identity = self.identity(name)
                policy = identity['interning']
                self.assertEqual(['domain_key', 'schema_bytes', 'scope_bytes', 'identity_bytes'],
                                 policy['tuple_columns'])
                self.assertEqual('FULL_TUPLE_BYTES', policy['equality'])
                self.assertEqual('COMPARE_FULL_TUPLE_THEN_ALLOCATE_DISTINCT_UUID',
                                 policy['different_tuple_same_bucket'])
                self.assertIs(policy['bucket_digest_unique'], False)
                # A forced equal bucket must not make distinct exact IDs equal.
                bucket = {self.declared_tuple(name, b'A\x00B', 'tenant-A'): 'key-1'}
                other = self.declared_tuple(name, b'AB', 'tenant-A')
                self.assertNotIn(other, bucket)
                self.assertEqual('RETURN_EXISTING_UUID', policy['same_tuple_same_tenant'])
                self.assertEqual('ONE_OWNER_TRANSACTION_BUCKET_LOCK_COMPARE_INSERT', policy['atomicity'])

    def test_original_bytes_and_owner_domain_are_not_normalized(self):
        domains = set()
        for name in ENTITIES:
            with self.subTest(entity=name):
                _, identity = self.identity(name)
                domains.add(identity['registry_domain'])
                self.assertEqual('', identity['scope_bytes_hex'])
                self.assertEqual('PINNED_ORIGINAL_OWNER_SCHEMA_NOT_CALLER_SELECTED', identity['schema_binding'])
                values = [b'A', b'a', b'A ', b'A\x00', '\u00e9'.encode(), 'e\u0301'.encode()]
                self.assertEqual(len(values), len({self.declared_tuple(name, raw, 'tenant-A') for raw in values}))
                source = identity['source_pin']
                self.assertEqual(source['line_start'], source['line_end'])
                self.assertIn('可空N', source['excerpt'])
        self.assertEqual(len(ENTITIES), len(domains))

    def test_raw_id_cannot_disagree_with_registry_authority(self):
        for name in ENTITIES:
            with self.subTest(entity=name):
                table, identity = self.identity(name)
                check = next((c for c in table['checks'] if c['name'] == 'original_id_authority_binding'), None)
                self.assertIsNotNone(check, 'Required raw bytes must be tied to the selected authority key')
                self.assertEqual(['tenant_id', identity['raw_column'], identity['key_column']], check['columns'])
                self.assertEqual('OWNER_GLOBAL_INTERN_AND_EXACT_RAW_BINDING_BEFORE_COMMIT', check['enforcement'])
                self.assertEqual('EXACT_RAW_SCHEMA_DOMAIN_AND_IMMUTABLE_TENANT', check['comparison'])


if __name__ == '__main__':
    unittest.main()
