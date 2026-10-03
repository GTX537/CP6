# -*- coding: utf-8 -*-
import hashlib,json,re,subprocess
from pathlib import Path
root=Path(r'D:\CP6\tmp\worktrees\db-compat-wp4-20261003')
review=Path(r'D:\CP6\tmp\wp4-task-code-review.md')
source=review.read_text(encoding='utf-8-sig')
entries=re.findall(r'^\| `([^`]+)` \| `([A-F0-9]{64})` \|$',source,re.M)
assert len(entries)==52
extra=('eng/crm/erp-integration-tests/ErpReplaySerializationRelationalTests.cs','9CC92AA32F479C793809AFF49F5F77C9744BE5E40119D65B0557F75A81F3B431')
assert extra[0] in source and extra[1] in source
entries.append(extra)
for path,expected in entries:
    assert hashlib.sha256((root/path).read_bytes()).hexdigest().upper()==expected,path
changed=subprocess.check_output(['git','diff','origin/main','--name-only'],cwd=str(root)).decode('utf-8').splitlines()
code=[p for p in changed if not p.startswith('docs/') and (p.endswith('.cs') or p.endswith('.csproj') or p.endswith('packages.lock.json'))]
reviewed={p for p,h in entries}
assert set(code)<=reviewed,repr(set(code)-reviewed)
assert not subprocess.check_output(['git','diff','--name-only'],cwd=str(root)).strip(),'Unstaged inputs remain.'
lock=hashlib.sha256(Path(r'D:\CP6\CP6.Space.IntegrationTests\packages.lock.json').read_bytes()).hexdigest().upper()
assert lock=='71D6B6B9EC4446E92D25842310633AA9E49E994EC6211A353B5140251B1870A5'
result={'Task':'DB-COMPAT-01-WP4','Base':'974e57c0650279565330a67c844355ba3e1b563d','ReviewedFileHashesMatched':len(entries),'ChangedCodeAndLockFiles':len(code),'AllChangedCodeCoveredByReview':True,'NoUnstagedInputs':True,'RootPreexistingLockSha256':lock,'ReviewSha256':hashlib.sha256(review.read_bytes()).hexdigest().upper(),'Scope':'Final local source/input binding; not remote delivery, full application startup or production acceptance.'}
Path(r'D:\CP6\tmp\wp4-final-input-review.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
print(json.dumps(result,indent=2))
