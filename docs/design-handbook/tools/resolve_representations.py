"""Resolve declared byte/gzip carriers as data, verifying every decoded identity."""
from __future__ import annotations
import argparse
import gzip
import hashlib
import json
import shutil
import tempfile
from pathlib import Path


def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for b in iter(lambda: f.read(1024 * 1024), b''):
            h.update(b)
    return h.hexdigest()


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--base', required=True, type=Path)
    p.add_argument('--indexes', required=True, type=Path)
    p.add_argument('--cache', required=True, type=Path)
    a = p.parse_args()
    cache = a.cache.resolve()
    base = a.base.resolve()
    assert cache != base and base not in cache.parents
    cache.mkdir(parents=True, exist_ok=True)
    known_path = a.indexes / 'materialized-content-paths.json'
    known = json.loads(known_path.read_text(encoding='utf-8'))
    evidence_path = a.indexes / 'resolved-representations.json'
    evidence = json.loads(evidence_path.read_text(encoding='utf-8')) if evidence_path.exists() else {'records': [], 'errors': []}
    verified = {r['original_sha256']: r for r in evidence['records']}

    def save():
        # Siblings are replaced only after complete serialization, so readers never see a partial map.
        for dest, obj in ((known_path, known), (evidence_path, evidence)):
            temp = dest.with_suffix('.next.json')
            temp.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
            temp.replace(dest)

    def locate(sha, rel=None):
        if sha in known:
            return Path(known[sha])
        if rel:
            path = (base / rel).resolve()
            assert base in path.parents
            if path.is_file():
                return path
        raise FileNotFoundError(sha)

    def recover(r, kind):
        sha, size = r['original_sha256'], r['original_bytes']
        name = r.get('original_name') or r['original_member_path']
        if sha in verified and Path(verified[sha]['materialized_path']).is_file():
            return
        pieces = r.get('parts') if kind in ('byte_parts', 'gzip_parts') else [r]
        ordered = sorted(pieces, key=lambda x: x.get('index', 0))
        if len(ordered) > 1:
            assert [x['index'] for x in ordered] == list(range(1, len(ordered) + 1))
        out = cache / (sha + Path(name).suffix.lower())
        total, full_hash, inputs = 0, hashlib.sha256(), []
        with tempfile.TemporaryFile(dir=cache) as tmp:
            for piece in ordered:
                psha = piece['sha256'] if kind == 'byte_parts' else piece['gzip_sha256']
                expected_size = piece['bytes'] if kind == 'byte_parts' else piece['gzip_bytes']
                src = locate(psha, piece.get('archive_path'))
                assert src.stat().st_size == expected_size and digest(src) == psha
                inputs.append({'sha256': psha, 'path': src.as_posix()})
                opener = src.open('rb') if kind == 'byte_parts' else gzip.open(src, 'rb')
                chunk_hash, chunk_size = hashlib.sha256(), 0
                with opener as stream:
                    for b in iter(lambda: stream.read(1024 * 1024), b''):
                        full_hash.update(b); chunk_hash.update(b)
                        total += len(b); chunk_size += len(b); tmp.write(b)
                if kind != 'byte_parts':
                    assert chunk_size == piece['original_bytes'] and chunk_hash.hexdigest() == piece['original_sha256']
            assert total == size and full_hash.hexdigest() == sha
            if out.exists():
                assert out.stat().st_size == size and digest(out) == sha
            else:
                tmp.seek(0)
                with out.open('xb') as dst:
                    shutil.copyfileobj(tmp, dst)
        known[sha] = out.as_posix()
        rec = {'representation': kind, 'original_name': name, 'original_sha256': sha, 'original_bytes': size, 'materialized_path': out.as_posix(), 'source_inputs': inputs, 'verification': 'full compressed/carrier and decoded bytes; not semantic reading'}
        evidence['records'].append(rec); verified[sha] = rec
        save()
        print(json.dumps({'restored': name, 'bytes': size, 'count': len(verified)}, ensure_ascii=False), flush=True)

    binaries = json.loads((base / 'provenance/binary-transport-representations.json').read_text(encoding='utf-8'))['records']
    # Unblock current CRM wire attachments before handling the historical large payloads.
    binaries.sort(key=lambda r: (r['original_sha256'] != '4578f839e4094db347c28b66e97aa7f54bd1623b5ba997b45d59bbae5ceb26bb', not r['original_name'].endswith('.zip')))
    for r in binaries:
        try:
            recover(r, 'byte_parts')
        except Exception as e:
            evidence['errors'].append({'stage': 'byte_parts', 'sha256': r['original_sha256'], 'error': repr(e)})
            save()
    lossless = json.loads((base / 'provenance/lossless-representations.json').read_text(encoding='utf-8'))
    for r in lossless:
        try:
            recover(r, r.get('representation_kind', 'gzip'))
        except Exception as e:
            evidence['errors'].append({'stage': 'gzip', 'sha256': r['original_sha256'], 'error': repr(e)})
            save()
    supplement_descriptors = {}
    graph = json.loads((a.indexes / 'container-members.json').read_text(encoding='utf-8'))
    for node in graph['containers']:
        for member in node['members']:
            if member['member'].endswith('ORIGINAL_MEMBER_MAP.json') and member.get('readable_path'):
                manifest = json.loads(Path(member['readable_path']).read_text(encoding='utf-8-sig'))
                for r in manifest.get('records', []):
                    if r.get('representation') == 'lossless_original_bytes_via_descriptor_and_ordered_gzip_parts':
                        supplement_descriptors[r['descriptor_sha256']] = r
    for descriptor_sha, member in supplement_descriptors.items():
        try:
            path = locate(descriptor_sha)
            assert digest(path) == descriptor_sha
            desc = json.loads(path.read_text(encoding='utf-8-sig'))
            assert desc['original_sha256'] == member['sha256'] and desc['original_bytes'] == member['bytes']
            parts, offset = [], 0
            for part in sorted(desc['parts'], key=lambda x: x['ordinal']):
                assert part['offset'] == offset and part['ordinal'] == len(parts)
                offset += part['decoded_bytes']
                parts.append({'index': part['ordinal'] + 1, 'gzip_sha256': part['sha256'], 'gzip_bytes': part['bytes'], 'original_sha256': part['decoded_sha256'], 'original_bytes': part['decoded_bytes']})
            assert offset == desc['original_bytes']
            recover({'original_sha256': desc['original_sha256'], 'original_bytes': desc['original_bytes'], 'original_name': desc['original_file_name'], 'parts': parts}, 'gzip_parts')
        except Exception as e:
            evidence['errors'].append({'stage': 'supplement_gzip_parts', 'sha256': member['sha256'], 'error': repr(e)})
            save()
    native = json.loads((base / 'provenance/native-gzip-payload-verification.json').read_text(encoding='utf-8'))
    try:
        recover({'original_sha256': native['uncompressed_payload_sha256'], 'original_bytes': native['uncompressed_payload_bytes'], 'gzip_sha256': native['original_gzip_sha256'], 'gzip_bytes': native['original_gzip_bytes'], 'original_name': 'STAGE59-INITIAL-OWN-APPLIED-BEFORE-FULL-H30-NATIVE-VECTOR.json'}, 'gzip')
    except Exception as e:
        evidence['errors'].append({'stage': 'native_gzip', 'error': repr(e)})
    evidence['summary'] = {'declared_byte_part_objects': len(binaries), 'declared_lossless_objects': len(lossless), 'supplement_gzip_descriptors': len(supplement_descriptors), 'native_gzip_payloads': 1, 'unique_restored_objects': len(verified), 'restored_bytes': sum(r['original_bytes'] for r in verified.values()), 'errors': len(evidence['errors'])}
    save()
    print(json.dumps(evidence['summary']), flush=True)


if __name__ == '__main__':
    main()
