"""Expose already indexed non-text/package members without executing any payload."""
import argparse
import hashlib
import json
import re
import tempfile
import zipfile
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--indexes', required=True, type=Path)
p.add_argument('--cache', required=True, type=Path)
a = p.parse_args()
graphpath = a.indexes / 'container-members.json'
mappath = a.indexes / 'materialized-content-paths.json'
graph = json.loads(graphpath.read_text(encoding='utf-8'))
known = json.loads(mappath.read_text(encoding='utf-8'))
new, reused, checked_bytes = 0, 0, 0
for node in graph['containers']:
    pending = [m for m in node['members'] if m.get('sha256') and not m.get('readable_path')]
    if not pending:
        continue
    with zipfile.ZipFile(node['readable_path']) as z:
        infos = z.infolist()
        for m in pending:
            sha = m['sha256']
            if sha in known:
                m['readable_path'] = known[sha]; reused += 1
                continue
            info = infos[m['ordinal']]
            assert info.filename == m['member'] and info.file_size == m['bytes']
            suffix = Path(info.filename).suffix.lower()
            if not re.fullmatch(r'\.[a-z0-9]{1,12}', suffix):
                suffix = '.raw'
            path = a.cache / (sha + suffix)
            h, size = hashlib.sha256(), 0
            with tempfile.TemporaryFile(dir=a.cache) as temp:
                with z.open(info) as stream:
                    for b in iter(lambda: stream.read(1024 * 1024), b''):
                        h.update(b); size += len(b); temp.write(b)
                assert h.hexdigest() == sha and size == m['bytes']
                if path.exists():
                    assert hashlib.sha256(path.read_bytes()).hexdigest() == sha
                else:
                    import shutil
                    temp.seek(0)
                    with path.open('xb') as dst:
                        shutil.copyfileobj(temp, dst)
            checked_bytes += size; new += 1
            known[sha] = path.as_posix(); m['readable_path'] = path.as_posix()
for dest, obj in ((graphpath, graph), (mappath, known)):
    temp = dest.with_suffix('.next.json')
    temp.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    temp.replace(dest)
summary = {'new_materialized_members': new, 'reused_exact_objects': reused, 'fresh_member_bytes_verified': checked_bytes, 'not_semantic_reading': True}
(a.indexes / 'remaining-member-materialization.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
print(json.dumps(summary))
