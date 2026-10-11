"""Hash revision assets and bind every output to explicit issues and original evidence."""
import argparse
import hashlib
import json
from pathlib import Path


def main(book):
    folder=book/'remediation/assets'
    register=json.loads((book/'remediation/00-disposition-register.json').read_text(encoding='utf-8'))
    evidence={};assets=[]
    for issue in register['issues']:
        for e in issue['evidence']:
            evidence.setdefault(e['sha256'],{'source_path':e['source_path'],'sha256':e['sha256']})
    for path in sorted(folder.rglob('*')):
        if not path.is_file() or path==folder/'manifest.json' or '__pycache__' in path.parts or any(x in path.parts for x in ['bin','obj']):continue
        relative=path.relative_to(book).as_posix();category=path.relative_to(folder).parts[0]
        issues=[i for i in register['issues'] if any(o==relative or o.startswith('remediation/assets/'+category+'/') for o in i['outputs'])]
        data=path.read_bytes()
        record={'path':relative,'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data),'version':register['revision'],'source_finding_ids':sorted({x for i in issues for x in i['source_finding_ids']}),'consumers':sorted({x for i in issues for x in i['targets']}),'original_evidence_shas':sorted({e['sha256'] for i in issues for e in i['evidence']}),'compatibility':'Explicit bounded successor only; historical originals/results and accepted native input/authority retained; actual adoption UNPROVEN','role':'NEW_DESIGN_ASSET_OR_STATIC_EVIDENCE_NOT_RUNTIME'}
        if path.name.endswith('patches.json'):
            spec=json.loads(data)
            record['patch_bindings']=[{'source_path':p['source_path'],'source_sha256':p['source_sha256'],'findings':p.get('source_finding_ids',[p.get('finding')]),'pointers':[o.get('path','literal exact preimage') for o in p.get('operations',[])],'derived_sha256':p.get('expected_derived_sha256'),'composed_identity':'remediation/assets/composed-derivatives.json'} for p in spec.get('patches',[])]
        assets.append(record)
    result={'revision':register['revision'],'scope':'Exact new asset bytes; original SHA index and bounded source pointers; no original archive modification or historical acceptance upgrade','original_sources':list(evidence.values()),'asset_count':len(assets),'assets':assets,'composed_derivatives':'remediation/assets/composed-derivatives.json','business_runtime':'NOT_RUN'}
    (folder/'manifest.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'assets':len(assets),'original_sources':len(evidence)}))


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--handbook',type=Path,required=True);main(p.parse_args().handbook)
