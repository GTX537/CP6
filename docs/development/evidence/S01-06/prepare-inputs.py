"""Read-only source verification and exact StableId fragment extraction; never runs archive code/SQL."""
import hashlib
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[4]
OUT = Path(__file__).resolve().parent
FIXED = 'd7a210256783e45291a5e724ea068ed4fc165ecc'


def blob(path):
    return subprocess.check_output(['git', '-c', 'core.longpaths=true', '-C', str(ROOT), 'cat-file', 'blob', FIXED + ':' + path])


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def write(path, value):
    path.write_bytes((json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))


def main():
    manifest_bytes = blob('eng/contracts/s01/profile-inputs.json')
    vectors_bytes = blob('eng/contracts/s01/vectors.json')
    assert digest(manifest_bytes) == '2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e'
    assert digest(vectors_bytes) == 'b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01'
    package = json.loads(manifest_bytes)
    profile = next(p for p in package['profiles'] if p['id'] == 'atp-stableid-r1')
    sources = []
    originals = {}
    composed = {}
    for selected in profile['selected_sources']:
        source = next(s for s in package['sources'] if s['id'] == selected['source_id'])
        selector = source['selector']
        index = blob(selector['path'])
        assert digest(index) == selector['sha256']
        node = json.loads(index)
        for part in selector['json_pointer'].split('/')[1:]:
            node = node[int(part)] if isinstance(node, list) else node[part.replace('~1', '/').replace('~0', '~')]
        original = Path(node['source_path']).read_bytes()
        derived = Path(node['local_derived_path']).read_bytes()
        assert digest(original) == source['original_sha256']
        assert digest(derived) == source['composed_sha256']
        originals[source['id']] = original
        composed[source['id']] = derived
        bundles = []
        for pin in source['patch_bundles']:
            raw = blob(pin['path'])
            assert digest(raw) == pin['sha256']
            patches = json.loads(raw)['patches']
            applicable = [(index, patch) for index, patch in enumerate(patches)
                          if patch.get('source_sha256') == source['original_sha256']]
            assert applicable, source['id']
            bundles.append(dict(pin=pin, applicable_pointers=['/patches/' + str(i) for i, _ in applicable],
                                findings=source['source_finding_ids']))
        sources.append(dict(source_id=source['id'], original_sha256=digest(original), composed_sha256=digest(derived),
                            original_bytes=len(original), composed_bytes=len(derived), selector=selector,
                            patch_bundles=bundles, scope='Verified source input only; only StableId lexical subset is implemented here'))

    full_schema = json.loads(composed['composed-24'])
    definition = full_schema['$defs']['StableId']
    assert definition == profile['schema_or_type_identity']['definition']
    assert full_schema['$id'] == profile['schema_or_type_identity']['schema_id']
    fragment = {'$schema': full_schema['$schema'], '$id': 'urn:cp6:plan-atp:stable-id:20261011-r1',
                '$comment': 'ATP-STABLEID-LEXICAL/20261011-R1. Scalar fragment extracted from ' + full_schema['$id'] +
                '#/$defs/StableId; full source composed SHA256=' + digest(composed['composed-24']) +
                '. This fragment does not validate the full ATP wire contract.', **definition}
    write(ROOT / 'eng/contracts/s01/atp-stable-id.schema.json', fragment)

    ddl = composed['composed-14'].decode('utf-8-sig')
    old_ddl = originals['composed-14'].decode('utf-8-sig')
    columns = []
    table = None
    for line_number, line in enumerate(ddl.splitlines(), 1):
        match = re.match(r'CREATE TABLE (\S+) \(', line)
        if match: table = match.group(1)
        if re.search(r'^  \w+ varchar\(128\)', line):
            columns.append(dict(table=table, column=line.strip().split()[0], line=line_number,
                                nullable='NOT NULL' not in line, exact_definition=line.strip()))
    assert len(columns) == 5
    assert old_ddl.count('varchar(16)') == 5
    assert {(c['table'], c['column']) for c in columns} == {
        ('plan_atp.evaluation', 'result_uom'), ('plan_atp.segment', 'uom'),
        ('plan_atp.basis', 'native_uom'), ('plan_atp.basis', 'result_uom'),
        ('plan_atp.reservation_ledger', 'uom')}
    # Both registered bundles must be applied to the original DDL in memory, in order.
    # The resulting hash proves that PLAN-IF-005 is not accidentally dropped from the length-only input.
    ddl_source = next(s for s in package['sources'] if s['id'] == 'composed-14')
    combined = old_ddl
    ddl_operations = []
    for pin in ddl_source['patch_bundles']:
        for index, patch in enumerate(json.loads(blob(pin['path']))['patches']):
            if patch.get('source_sha256') != ddl_source['original_sha256']: continue
            for operation_index, operation in enumerate(patch.get('operations', [patch])):
                count = operation.get('expected_count', operation.get('expected_occurrences'))
                assert old_ddl.count(operation['before']) == count
                assert combined.count(operation['before']) == count
                combined = combined.replace(operation['before'], operation['after'])
                ddl_operations.append(dict(bundle=pin['path'], pointer=f'/patches/{index}', operation_index=operation_index,
                                           finding=patch.get('finding', patch.get('source_finding_ids')),
                                           expected_count=count, before_sha256=digest(operation['before'].encode()),
                                           after_sha256=digest(operation['after'].encode())))
    assert digest(combined.encode('utf-8')) == ddl_source['composed_sha256']
    assert len(ddl_operations) == 9  # four length operations (one matches two columns) + five recovery lock operations

    write(OUT / 'source-provenance.json', dict(source_repository='GTX537/CP6', fixed_commit=FIXED,
          package_version=package['package_version'], manifest_sha256=digest(manifest_bytes), vectors_sha256=digest(vectors_bytes),
          profile=profile['id'], profile_version=profile['schema_or_type_identity']['profile_version'], sources=sources,
          schema_fragment=dict(path='eng/contracts/s01/atp-stable-id.schema.json', definition=definition,
                               source_id=full_schema['$id'], pointer='/$defs/StableId',
                               sha256=digest((ROOT / 'eng/contracts/s01/atp-stable-id.schema.json').read_bytes())),
          postgresql=dict(source_composed_sha256=digest(composed['composed-14']), five_columns=columns,
                          recomposed_full_patch_set_matches=True, patch_operations=ddl_operations,
                          plan_if_005='Five exact recovery/replay lock-order/binding replacements in addition to lexical column changes; full input retained for S01-10.',
                          execution='NOT_RUN', migration='NOT_RUN'),
          owner='Existing ISupplyService is decimal scalar MRP supply; new ATP R11 Owner not adopted by this task.'))
    print('PASS: four original/composed pins and complete DDL patch set verified; local scalar Schema extracted.')


if __name__ == '__main__': main()
