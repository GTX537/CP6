"""Join provenance and exact identities. Classification is not a semantic-read claim."""
from __future__ import annotations
import argparse
import csv
import json
import re
from collections import Counter, defaultdict
from pathlib import Path


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--archive', required=True, type=Path)
    p.add_argument('--indexes', required=True, type=Path)
    a = p.parse_args()
    base = a.archive / '完整成果/docs'
    supplement = a.archive / 'CP6_遗漏原包补充_20261009'
    inv = json.loads((a.indexes / 'all-files.json').read_text(encoding='utf-8'))
    known = json.loads((a.indexes / 'materialized-content-paths.json').read_text(encoding='utf-8'))
    graph = json.loads((a.indexes / 'container-members.json').read_text(encoding='utf-8'))
    required = json.loads((a.indexes / 'required-inputs.json').read_text(encoding='utf-8'))
    catalogue = {}
    for part in sorted((base / 'provenance/catalog-parts').glob('*.json')):
        for r in json.loads(part.read_text(encoding='utf-8'))['objects']:
            catalogue[r['sha256']] = r
    current_refs = defaultdict(list)
    current_containers = defaultdict(set)
    for target in required['targets']:
        for r in target['artifacts']:
            sha = r.get('actual_sha256') or r.get('expected_sha256')
            current_refs[sha].append({'target_id': target['target_id'], 'component_id': r['component_id'], 'layer': r.get('requirement_layer'), 'role': r['role']})
            if r.get('container_index_sha256') and r.get('requirement_layer', '').startswith('L1_'):
                current_containers[r['container_index_sha256']].add(target['target_id'])
    nodes = {n['sha256']: n for n in graph['containers']}
    package_refs = defaultdict(set)
    for sha, targets in current_containers.items():
        todo, seen = [sha], set()
        while todo:
            container_sha = todo.pop()
            if container_sha in seen:
                continue
            seen.add(container_sha)
            for m in nodes.get(container_sha, {}).get('members', []):
                member_sha = m.get('sha256')
                if member_sha:
                    package_refs[member_sha].update(targets)
                    if member_sha in nodes:
                        todo.append(member_sha)
    member_names = defaultdict(set)
    mapped_sources = defaultdict(dict)
    manifest_seen = set()
    for node in graph['containers']:
        for m in node['members']:
            sha = m.get('sha256')
            if sha:
                member_names[sha].add(m['member'])
            if m['member'].endswith('ORIGINAL_MEMBER_MAP.json') and sha not in manifest_seen and m.get('readable_path'):
                manifest_seen.add(sha)
                data = json.loads(Path(m['readable_path']).read_text(encoding='utf-8-sig'))
                for r in data.get('records', []):
                    identity = r.get('source_library_file_id')
                    if identity:
                        key = (r['sha256'], r.get('original_member_path'))
                        mapped_sources[identity][key] = r
    status_path = sorted(supplement.glob('supplement_identity_progress_*.json'))[-1]
    status = json.loads(status_path.read_text(encoding='utf-8-sig'))
    records = []
    for source in status['records']:
        r = dict(source)
        sha = r.get('actual_sha256')
        r['local_exact_wrapper_path'] = known.get(sha)
        members = list(mapped_sources.get(r['library_file_id'], {}).values())
        r['member_delivery_reconciliation'] = {'declared_member_identities': len(members), 'locally_resolved_member_identities': sum(x['sha256'] in known for x in members), 'unlocated_members': [{'member': x.get('original_member_path'), 'sha256': x['sha256']} for x in members if x['sha256'] not in known]}
        r['semantic_reading'] = 'not_inferred_from_delivery_status_or_hash'
        records.append(r)
    snapshot_batches = sorted({r.get('delivery_batch') for r in records if r.get('delivery_batch')})
    physical_batches = sorted(p.name for p in supplement.iterdir() if p.is_dir() and re.match(r'^S\d{3}', p.name))
    extra_batches = [s for s in physical_batches if s.split('_', 1)[0] not in snapshot_batches]
    roster = {'status_snapshot_path': status_path.as_posix(), 'status_as_of_utc': status['as_of_utc'], 'source_reported_scope': status['scope'], 'source_reported_status_counts': status['status_counts'], 'archive_complete_source_claim': status['archive_complete'], 'current_global_clean_approval_source_claim': status['current_global_clean_approval'], 'physical_batch_directories': physical_batches, 'batch_directories_not_named_in_status_records': extra_batches, 'meaning': 'Source-reported cloud checks are inherited evidence. Local exact wrappers/member SHA matches are checked independently. A safe selection or reference identity is not an exact original wrapper. These records do not authorize execution/publication.', 'records': records}
    (a.indexes / 'supplement-source-register.json').write_text(json.dumps(roster, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    with (a.indexes / 'supplement-source-register.csv').open('w', encoding='utf-8-sig', newline='') as f:
        writer = csv.DictWriter(f, fieldnames=['library_file_id', 'name', 'status', 'delivery_batch', 'representation', 'actual_sha256', 'local_exact_wrapper_path', 'declared_members', 'resolved_members'])
        writer.writeheader()
        for r in records:
            writer.writerow({**{k: r.get(k) for k in writer.fieldnames if k not in ('declared_members', 'resolved_members')}, 'declared_members': r['member_delivery_reconciliation']['declared_member_identities'], 'resolved_members': r['member_delivery_reconciliation']['locally_resolved_member_identities']})
    dispositions = []
    physical_groups = Counter(r['sha256'] for r in inv['files'])
    for physical in inv['files']:
        sha = physical['sha256']
        meta = catalogue.get(sha, {})
        refs = current_refs.get(sha, [])
        names = sorted(set(meta.get('names', [])) | member_names.get(sha, set()))
        categories = meta.get('categories', [])
        if any((r.get('layer') or '').startswith(('L1_', 'L2_')) for r in refs):
            role = 'current_selected_input'
        elif refs:
            role = 'required_provenance_or_history'
        elif package_refs.get(sha):
            role = 'selected_package_member_role_requires_module_interpretation'
        elif any('transport' in c or 'lossless' in c for c in categories) or physical['extension'] in ('.gz', '.bin'):
            role = 'exact_byte_representation_or_part'
        elif 'CP6_遗漏原包补充_20261009/' in physical['relative_path'] or physical['relative_path'].startswith('增补快照/'):
            role = 'supplement_source_or_delivery_evidence'
        elif physical['physical_category'] in ('transport_or_restore_evidence', 'source_selection_or_navigation', 'recovered_review_or_recovery_provenance'):
            role = physical['physical_category']
        elif categories:
            role = 'preserved_history_reference_or_nonselected_source'
        else:
            role = 'archive_auxiliary_source_not_currently_selected'
        dispositions.append({'relative_path': physical['relative_path'], 'sha256': sha, 'bytes': physical['bytes'], 'physical_occurrences_of_exact_sha': physical_groups[sha], 'organization_role': role, 'source_catalogue_categories': categories, 'target_ids_from_source_catalogue': meta.get('target_ids', []), 'selected_component_refs': refs, 'selected_container_targets_not_normative_claim': sorted(package_refs.get(sha, [])), 'source_names': names, 'retention': 'original_in_place; source indexes link exact bytes; no original deletion or overwrite', 'semantic_reading': 'consult module reading ledger; not inferred from classification'})
    summary = {'physical_files': len(dispositions), 'unique_physical_sha256': len(physical_groups), 'organization_roles': dict(Counter(r['organization_role'] for r in dispositions)), 'unassigned': 0, 'base_catalogued_objects': len(catalogue), 'distinct_member_map_manifests': len(manifest_seen), 'supplement_source_identities': len(records), 'supplement_member_sha_missing_occurrences': sum(len(r['member_delivery_reconciliation']['unlocated_members']) for r in records), 'meaning': 'Complete file disposition does not imply full semantic consolidation. Container membership does not promote history/code/fixture to current normative authority.'}
    (a.indexes / 'all-file-dispositions.json').write_text(json.dumps({'summary': summary, 'files': dispositions}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    with (a.indexes / 'all-file-dispositions.csv').open('w', encoding='utf-8-sig', newline='') as f:
        writer = csv.writer(f); writer.writerow(['relative_path', 'sha256', 'bytes', 'organization_role', 'physical_occurrences', 'catalogue_targets', 'current_container_targets'])
        for r in dispositions:
            writer.writerow([r['relative_path'], r['sha256'], r['bytes'], r['organization_role'], r['physical_occurrences_of_exact_sha'], ';'.join(r['target_ids_from_source_catalogue']), ';'.join(r['selected_container_targets_not_normative_claim'])])
    print(json.dumps(summary, ensure_ascii=False))


if __name__ == '__main__':
    main()
