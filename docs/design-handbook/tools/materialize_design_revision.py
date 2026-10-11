"""Compose all bounded corrections into separate local derivatives; never edit sources."""
import argparse
import hashlib
import json
from collections import defaultdict
from pathlib import Path
from validate_design_completion import apply_operations, normalized_patch


def materialize(book,output):
    book=book.resolve();output=output.resolve()
    groups=defaultdict(list)
    for bundle in sorted((book/'remediation/assets').rglob('*patches.json')):
        spec=json.loads(bundle.read_text(encoding='utf-8'))
        for raw in spec.get('patches',[]):
            patch=normalized_patch(raw)
            source=Path(patch['source_path']).resolve()
            if source==output or source.is_relative_to(output):
                raise ValueError('Output directory contains an immutable source')
            groups[source].append((bundle.relative_to(book).as_posix(),patch))
    rows=[]
    for source,bundles in groups.items():
        before=source.read_bytes();source_sha=hashlib.sha256(before).hexdigest();ops=[]
        for bundle,p in bundles:
            assert source_sha==p['source_sha256'],(source,'source SHA differs')
            # Verify each separately promised derivative, then compose non-overlapping edits.
            single=apply_operations(before,p['operations'])
            if 'expected_derived_sha256' in p:assert hashlib.sha256(single).hexdigest()==p['expected_derived_sha256'],source
            if 'expected_derived_bytes' in p:assert len(single)==p['expected_derived_bytes'],source
            ops.extend(p['operations'])
        after=apply_operations(before,ops);newsha=hashlib.sha256(after).hexdigest()
        dest=output/source_sha/source.name
        assert dest.resolve().is_relative_to(output) and dest.resolve()!=source
        dest.parent.mkdir(parents=True,exist_ok=True)
        if dest.exists() and dest.read_bytes()!=after:
            # Keep earlier revision output under its own SHA rather than overwrite it.
            dest=output/source_sha/newsha/source.name;dest.parent.mkdir(parents=True,exist_ok=True)
        if dest.exists():assert dest.read_bytes()==after
        else:dest.write_bytes(after)
        rows.append({'source_path':source.as_posix(),'source_sha256':source_sha,'derived_sha256':newsha,'derived_bytes':len(after),'local_derived_path':dest.as_posix(),'patch_bundles':sorted(set(b for b,p in bundles)),'source_finding_ids':sorted(set(i for b,p in bundles for i in p['source_finding_ids'])),'operations':len(ops)})
    report={'revision':'CP6-DESIGN-REVISION-20261011/R1','role':'COMPOSED_LOCAL_DESIGN_DERIVATIVES_NOT_OWNER_RUNTIME','result':'PASS','sources':rows,'originals_modified':False,'runtime_acceptance':'NOT_RUN'}
    (book/'remediation/assets/composed-derivatives.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'result':'PASS','source_files':len(rows),'operations':sum(r['operations'] for r in rows)}))


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--handbook',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();materialize(a.handbook,a.output)
