"""Make lossless location indexes; structural indexing is never marked semantic reading."""
from __future__ import annotations
import argparse
import hashlib
import json
import re
from collections import Counter, defaultdict
from pathlib import Path


def document_structure(path):
    raw = path.read_bytes()
    text = raw.decode('utf-8-sig')
    lines = text.splitlines()
    headings, fences, prose = [], [], []
    active = None
    prose_start = 1
    for n, line in enumerate(lines, 1):
        match = re.match(r'^\s{0,3}(`{3,}|~{3,})([^`]*)$', line)
        if active is None:
            if match:
                if prose_start <= n - 1:
                    prose.append([prose_start, n - 1])
                active = {'from_line': n, 'marker': match[1][0], 'width': len(match[1]), 'language': match[2].strip()}
                continue
            heading = re.match(r'^(#{1,6})\s+(.+)', line)
            if heading:
                headings.append({'line': n, 'level': len(heading[1]), 'heading': heading[2]})
        elif match and match[1][0] == active['marker'] and len(match[1]) >= active['width'] and not match[2].strip():
            active['to_line'] = n
            active['content_lines'] = max(0, n - active['from_line'] - 1)
            fences.append(active)
            active = None
            prose_start = n + 1
    if active is not None:
        active.update(to_line=len(lines), unclosed=True)
        fences.append(active)
    elif prose_start <= len(lines):
        prose.append([prose_start, len(lines)])
    for i, heading in enumerate(headings):
        heading['section_end'] = next((h['line'] - 1 for h in headings[i + 1:] if h['level'] <= heading['level']), len(lines))
    return {'path': path.as_posix(), 'sha256': hashlib.sha256(raw).hexdigest(), 'bytes': len(raw), 'total_lines': len(lines), 'headings': headings, 'fenced_blocks': fences, 'prose_line_ranges': prose, 'prose_lines_including_blank': sum(b - a + 1 for a, b in prose), 'inspection': 'structural_index_not_semantic_read'}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--archive', required=True, type=Path)
    parser.add_argument('--indexes', required=True, type=Path)
    args = parser.parse_args()
    root = args.archive.resolve()
    base = root / '完整成果/docs'
    out = args.indexes
    mandatory = json.loads((base / 'provenance/mandatory-current-artifacts.json').read_text(encoding='utf-8'))
    known = json.loads((out / 'materialized-content-paths.json').read_text(encoding='utf-8'))
    graph = json.loads((out / 'container-members.json').read_text(encoding='utf-8'))
    containers = {x['sha256']: x for x in graph['containers']}
    targets, sources, errors = [], {}, []
    for target in mandatory:
        artifacts = []
        missing = []
        for original in target['required_artifacts']:
            item = dict(original)
            sha = item.get('actual_sha256') or item.get('expected_sha256')
            materialized = known.get(sha)
            exact_original = base / item['archive_path'] if item.get('archive_path') else None
            if exact_original and exact_original.is_file():
                materialized = exact_original.as_posix()
            item['resolved_local_path'] = materialized
            item['resolved_status'] = 'exact_bytes_available' if materialized else 'unlocated'
            expected = item.get('expected_sha256')
            item['resolved_source_identity_path'] = known.get(expected) if expected else materialized
            item['resolved_source_identity_sha256'] = expected or sha
            item['carrier_differs_from_source_identity'] = bool(expected and expected != sha)
            if not materialized:
                missing.append(item['component_id'])
            else:
                source = sources.setdefault(sha, {'sha256': sha, 'path': materialized, 'references': []})
                source['references'].append({'target_id': target['target_id'], 'component_id': item['component_id'], 'role': item['role']})
            if sha in containers:
                item['container_index_sha256'] = sha
                item['direct_member_count'] = len(containers[sha]['members'])
            elif expected in containers:
                item['container_index_sha256'] = expected
                item['direct_member_count'] = len(containers[expected]['members'])
            artifacts.append(item)
        current_missing = [a['component_id'] for a in artifacts if a['resolved_status'] == 'unlocated' and a.get('requirement_layer', '').startswith(('L1_', 'L2_'))]
        targets.append({'target_id': target['target_id'], 'artifacts': artifacts, 'unlocated_components_all_layers': missing, 'unlocated_current_components': current_missing, 'current_L1_L2_material_closure': 'complete' if not current_missing else 'incomplete', 'all_declared_layers_material_closure': 'complete' if not missing else 'incomplete', 'semantic_consolidation': 'in_progress_see_group_reading_ledgers'})
    structures = []
    for sha, source in sources.items():
        path = Path(source['path'])
        if path.suffix.lower() in {'.md', '.txt', '.cs', '.ts', '.vue', '.sql', '.py', '.html', '.patch', '.diff'}:
            try:
                record = document_structure(path)
                assert record['sha256'] == sha, path
                record['references'] = source['references']
                structures.append(record)
            except Exception as exc:
                errors.append({'path': path.as_posix(), 'error_type': type(exc).__name__, 'error': str(exc)})
    summary = {'target_count': len(targets), 'required_reference_count': sum(len(t['artifacts']) for t in targets), 'unique_available_source_sha256': len(sources), 'current_L1_L2_complete_targets': sum(t['current_L1_L2_material_closure'] == 'complete' for t in targets), 'all_declared_layers_complete_targets': sum(t['all_declared_layers_material_closure'] == 'complete' for t in targets), 'unlocated_all_layers': [{'target_id': t['target_id'], 'components': t['unlocated_components_all_layers']} for t in targets if t['unlocated_components_all_layers']], 'text_sources_structurally_indexed': len(structures), 'headings': sum(len(s['headings']) for s in structures), 'fenced_blocks': sum(len(s['fenced_blocks']) for s in structures), 'total_text_lines': sum(s['total_lines'] for s in structures), 'prose_lines_including_blank': sum(s['prose_lines_including_blank'] for s in structures), 'structure_errors': errors, 'interpretation': 'All structural counts include shared/reference/code materials, not only normative prose. L1/L2 current closure is 124/125; L3/L4 history/provenance are separate. Material closure is distinct from semantic consolidation and implementation readiness.'}
    (out / 'required-inputs.json').write_text(json.dumps({'summary': summary, 'targets': targets}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (out / 'required-source-structure.json').write_text(json.dumps({'summary': summary, 'sources': structures}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (out / 'required-source-paths.json').write_text(json.dumps(list(sources.values()), ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(summary, ensure_ascii=False))


if __name__ == '__main__':
    main()
