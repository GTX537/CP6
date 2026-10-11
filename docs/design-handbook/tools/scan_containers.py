"""Hash and index container members without running supplied restoration code."""
from __future__ import annotations
import argparse
import hashlib
import json
import shutil
import tempfile
import zipfile
from pathlib import Path


def sha_file(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--inventory', required=True, type=Path)
    parser.add_argument('--cache', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--resume', action='store_true', help='Reuse the previously verified graph and index only newly resolved ZIPs')
    args = parser.parse_args()
    inv = json.loads(args.inventory.read_text(encoding='utf-8'))
    root = Path(inv['snapshot']['source_root'])
    cache = args.cache.resolve()
    if cache == root or root in cache.parents:
        raise ValueError('Working extraction must be outside the immutable source archive')
    cache.mkdir(parents=True, exist_ok=True)
    args.output.mkdir(parents=True, exist_ok=True)
    previous = json.loads((args.output / 'container-members.json').read_text(encoding='utf-8')) if args.resume else None
    known = json.loads((args.output / 'materialized-content-paths.json').read_text(encoding='utf-8')) if args.resume else {}
    physical = inv['files']
    for r in physical:
        known.setdefault(r['sha256'], r['absolute_path'])
    queues = []
    for r in physical:
        if r['extension'] == '.zip':
            queues.append((Path(r['absolute_path']), r['sha256'], r['relative_path'], 0, r['physical_category']))
    restored = []
    if args.resume:
        restored = previous['restored_collections']
        queues.extend((Path(path), sha, 'resolved_representation/' + Path(path).name, 0, 'resolved_source_container') for sha, path in known.items() if Path(path).suffix == '.zip' and sha not in {n['sha256'] for n in previous['containers']})
    # Each manifest is read as data; source restoration scripts are never invoked.
    for r in physical:
        if args.resume:
            break
        path = Path(r['absolute_path'])
        if not (path.name.endswith('CONTROL.zip') and 'CP6_遗漏原包补充_20261009' in path.parts):
            continue
        with zipfile.ZipFile(path) as z:
            for name in z.namelist():
                if not name.endswith('SOURCE_BYTE_PARTS.json'):
                    continue
                manifest = json.loads(z.read(name))
                for record in manifest['records']:
                    expected = record['original_sha256']
                    out = cache / (expected + '.zip')
                    total = 0
                    h = hashlib.sha256()
                    pieces = []
                    with tempfile.TemporaryFile(dir=cache) as temp:
                        for part in sorted(record['parts'], key=lambda x: x['order']):
                            partpath = (path.parent / part['file_name']).resolve(strict=True)
                            if partpath.parent != path.parent.resolve():
                                raise ValueError('Unexpected part path')
                            assert partpath.stat().st_size == part['bytes']
                            assert sha_file(partpath) == part['sha256']
                            pieces.append(partpath.as_posix())
                            with partpath.open('rb') as source:
                                for block in iter(lambda: source.read(1024 * 1024), b''):
                                    h.update(block); temp.write(block); total += len(block)
                        assert h.hexdigest() == expected and total == record['original_bytes']
                        if out.exists():
                            assert sha_file(out) == expected
                        else:
                            temp.seek(0)
                            with out.open('xb') as sink:
                                shutil.copyfileobj(temp, sink)
                    known[expected] = out.as_posix()
                    restored.append({'control': path.as_posix(), 'manifest_member': name, 'name': record['original_file_name'], 'sha256': expected, 'bytes': total, 'parts': pieces, 'materialized_path': out.as_posix(), 'representation': record.get('representation'), 'is_original_library_archive': record.get('is_original_library_archive'), 'verified': True})
                    queues.append((out, expected, path.parent.name + '/' + record['original_file_name'], 0, 'verified_reassembled_supplement_collection'))
    nodes = {n['sha256']: n for n in previous['containers']} if args.resume else {}
    roots = previous['roots_and_edges'] if args.resume else []
    errors = previous['errors'] if args.resume else []
    recognized = {'.zip', '.gz', '.md', '.txt', '.json', '.csv', '.yaml', '.yml', '.sql', '.cs', '.ts', '.js', '.html', '.htm', '.xlsx', '.xls', '.docx', '.pdf', '.xml', '.patch', '.diff', '.png'}
    cursor = 0
    while cursor < len(queues):
        path, sha, carrier, depth, category = queues[cursor]
        cursor += 1
        roots.append({'sha256': sha, 'carrier': carrier, 'depth': depth, 'path': path.as_posix()})
        if sha in nodes:
            continue
        members = []
        node = {'sha256': sha, 'readable_path': path.as_posix(), 'first_carrier': carrier, 'depth': depth, 'members': members}
        nodes[sha] = node
        try:
            with zipfile.ZipFile(path) as z:
                for ordinal, info in enumerate(z.infolist()):
                    if info.is_dir():
                        continue
                    entry = {'member': info.filename, 'ordinal': ordinal, 'bytes': info.file_size, 'crc32': f'{info.CRC:08x}'}
                    if category == 'transport_or_restore_evidence':
                        entry['inspection'] = 'central_directory_only; physical restored content freshly inventoried; prior restoration verification retained'
                        members.append(entry)
                        continue
                    h = hashlib.sha256()
                    with tempfile.SpooledTemporaryFile(max_size=8 * 1024 * 1024, dir=cache) as temp:
                        with z.open(info) as stream:
                            total = 0
                            for block in iter(lambda: stream.read(1024 * 1024), b''):
                                h.update(block); temp.write(block); total += len(block)
                        assert total == info.file_size
                        member_sha = h.hexdigest()
                        suffix = Path(info.filename).suffix.lower()
                        entry.update(sha256=member_sha, inspection='full_bytes_sha256_crc_not_semantic_read')
                        location = known.get(member_sha)
                        temp.seek(0); magic = temp.read(4); temp.seek(0)
                        is_zip = magic[:2] == b'PK' and suffix in {'.zip', '.bin', '.raw', ''}
                        if location is None and (suffix in recognized or is_zip):
                            effective_suffix = '.zip' if is_zip else suffix
                            saved = cache / (member_sha + effective_suffix)
                            if saved.exists():
                                assert sha_file(saved) == member_sha
                            else:
                                with saved.open('xb') as sink:
                                    shutil.copyfileobj(temp, sink)
                            location = saved.as_posix()
                            known[member_sha] = location
                        entry['readable_path'] = location
                        if is_zip:
                            if depth >= 16:
                                errors.append({'carrier': carrier, 'member': info.filename, 'error': 'recursion_depth_16_requires_followup'})
                            else:
                                queues.append((Path(location), member_sha, carrier + '!' + info.filename, depth + 1, 'nested_source_container'))
                    members.append(entry)
        except Exception as exc:
            errors.append({'carrier': carrier, 'sha256': sha, 'error_type': type(exc).__name__, 'error': str(exc)})
        if len(nodes) % 100 == 0:
            print(json.dumps({'indexed_containers': len(nodes), 'queue_remaining': len(queues) - cursor, 'errors': len(errors)}), flush=True)
    roots = list({(r['sha256'], r['carrier'], r['depth'], r['path']): r for r in roots}.values())
    summary = {'physical_zip_occurrences': sum(r['extension'] == '.zip' for r in physical), 'restored_split_collections': len(restored), 'unique_zip_containers': len(nodes), 'graph_edges': len(roots), 'file_member_occurrences_in_unique_containers': sum(len(n['members']) for n in nodes.values()), 'member_bytes_hashed': sum(m['bytes'] for n in nodes.values() for m in n['members'] if 'sha256' in m), 'unresolved_errors': len(errors), 'meaning': 'Container graph is deduplicated by exact container SHA. Office OpenXML and gzip payloads are separate format tasks. Outer restoration/transport ZIP contents only list central directories; reused base restore evidence is not claimed as freshly rerun.'}
    result = {'summary': summary, 'restored_collections': restored, 'roots_and_edges': roots, 'containers': list(nodes.values()), 'errors': errors}
    (args.output / 'container-members.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (args.output / 'materialized-content-paths.json').write_text(json.dumps(known, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(summary, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
