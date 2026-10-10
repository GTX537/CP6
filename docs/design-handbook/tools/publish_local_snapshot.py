"""Copy a private local handbook snapshot; never overwrite unknown or user-modified files."""
import argparse
import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--source', required=True, type=Path)
p.add_argument('--destination', required=True, type=Path)
a = p.parse_args()
source, dest = a.source.resolve(), a.destination.resolve()
assert source != dest and source not in dest.parents and dest not in source.parents
dest.mkdir(parents=True, exist_ok=True)
manifest_path = dest / 'LOCAL-SNAPSHOT.json'
old = json.loads(manifest_path.read_text(encoding='utf-8')) if manifest_path.exists() else {'files': []}
owned = {r['path']: r['sha256'] for r in old['files']}
files, conflicts = [], []
for path in sorted(source.rglob('*')):
    if not path.is_file() or '__pycache__' in path.parts or path.name.endswith('.next.json'):
        continue
    rel = path.relative_to(source).as_posix()
    before = path.stat(); data = path.read_bytes(); after = path.stat()
    assert (before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns), 'Source file changed during snapshot: ' + rel
    sha = hashlib.sha256(data).hexdigest()
    target = dest / rel
    if target.exists():
        prior_sha = hashlib.sha256(target.read_bytes()).hexdigest()
        if prior_sha != sha and owned.get(rel) != prior_sha:
            conflicts.append(rel)
            continue
    files.append({'path': rel, 'sha256': sha, 'bytes': len(data)})
if conflicts:
    raise RuntimeError('User/unknown destination changes retained; no copy performed: ' + ', '.join(conflicts))
# Preparation is complete before the first copy. No file is deleted from either tree.
for r in files:
    src, dst = source / r['path'], dest / r['path']
    data = src.read_bytes()
    assert hashlib.sha256(data).hexdigest() == r['sha256'], 'Source changed since preparation: ' + r['path']
    dst.parent.mkdir(parents=True, exist_ok=True)
    if not dst.exists() or hashlib.sha256(dst.read_bytes()).hexdigest() != r['sha256']:
        temp = dst.with_name(dst.name + '.snapshot-next')
        temp.write_bytes(data); temp.replace(dst)
    assert hashlib.sha256(dst.read_bytes()).hexdigest() == r['sha256']
navigation = json.loads((source / 'indexes/module-navigation.json').read_text(encoding='utf-8'))['summary']
available_complete = navigation.get('available_current_materials_consolidated', False)
manifest = {'kind': 'private_local_design_delivery_with_declared_gaps' if available_complete else 'private_local_in_progress_snapshot', 'created_at_utc': datetime.now(timezone.utc).isoformat(), 'source': source.as_posix(), 'destination': dest.as_posix(), 'available_current_materials_consolidated': available_complete, 'all_required_materials_present': False, 'files': files, 'retained_previous_files_not_in_current_source': sorted(set(owned) - {r['path'] for r in files}), 'verification': 'Each copied file SHA-256 checked against prepared source. Semantic scope comes from the separate reading ledgers; missing originals and runtime acceptance remain separate.'}
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
(dest / 'DELIVERY-SHA256SUMS.txt').write_text(''.join(f'{r["sha256"]}  {r["path"]}\n' for r in files), encoding='utf-8')
print(json.dumps({'copied_or_confirmed': len(files), 'bytes': sum(r['bytes'] for r in files), 'destination': dest.as_posix(), 'kind': manifest['kind']}, ensure_ascii=False))
