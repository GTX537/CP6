# -*- coding: utf-8 -*-
import hashlib, json, subprocess
from pathlib import Path

root=Path(r'D:\CP6\tmp\worktrees\db-compat-wp4-20261003')
manifests=[root/'docs/audits/database-compatibility/wp4-native/manifest.json',root/'docs/audits/2026-10-03-bug-147-snapshot-batch/native/post-merge/manifest.json',root/'docs/audits/database-compatibility/wp3-native/post-merge/manifest.json']
records=[]
for path in manifests:
    manifest=json.loads(path.read_text(encoding='utf-8-sig'))
    entries=manifest.get('Files',manifest.get('Entries'))
    assert entries
    for entry in entries:
        relative=entry.get('RelativePath',entry.get('File',entry.get('Path')))
        file=(path.parent/relative).resolve()
        assert str(file).startswith(str(path.parent.resolve())+'\\')
        actual=hashlib.sha256(file.read_bytes()).hexdigest().upper()
        assert actual==entry['Sha256'], str(file)
        records.append((file.relative_to(root).as_posix(),actual))
    records.append((path.relative_to(root).as_posix(),hashlib.sha256(path.read_bytes()).hexdigest().upper()))
paths=[entry[0] for entry in records]
assert len(paths)==len(set(paths))
for i in range(0,len(paths),25):
    subprocess.check_call(['git','add','-f','--']+paths[i:i+25],cwd=str(root))
for path,expected in records:
    blob=subprocess.check_output(['git','cat-file','blob',':'+path],cwd=str(root))
    assert hashlib.sha256(blob).hexdigest().upper()==expected,path
report={'Task':'DB-COMPAT-01-WP4','OriginalEvidenceFiles':len(records)-len(manifests),'Manifests':len(manifests),'AllManifestDiskHashesMatched':True,'AllGitIndexBytesMatched':True,'Scope':'WP4 originals plus carried WP3 and BUG147 postmerge evidence; force-add limited to the exact hash-verified manifest files.'}
Path(r'D:\CP6\tmp\wp4-staged-evidence-byte-check.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report,indent=2))
