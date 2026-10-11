"""Preserve old acceptance identities and correct a precisely pinned editorial step."""
import argparse
import hashlib
import json
from pathlib import Path


def main(book):
    archive=Path('D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals')
    def read(prefix):
        p=next(archive.glob(prefix+'*'))
        return p,json.loads(p.read_text(encoding='utf-8-sig'))
    def walk(v):
        yield v
        for c in v.values() if isinstance(v,dict) else v if isinstance(v,list) else []:
            yield from walk(c)
    def save(rel,v):
        (book/rel).write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    p,d=read('ae/aeda22b512c5c620')
    q,old=read('9f/9f8fd4357559f8d2')
    ids=['AC-LEAD-035','AC-LEAD-042']+[f'AC-LEAD-{i:03d}' for i in range(48,53)]
    rows=[]
    for id in ids:
        candidates=[x for x in walk(d) if isinstance(x,dict) and x.get('acId')==id]
        assert len(candidates)==1,(id,len(candidates))
        current=candidates[0]
        historical=next(x for x in walk(old) if isinstance(x,dict) and x.get('acId')==id)
        rows.append({'acceptance_id':id,'historical_record':historical,'current_mapping_record':current,'current_expectation':current.get('currentRule'),'required_positive':'Current exact subject authorization; atomic H02/Manifest and one original terminal receipt where applicable','required_negative':'No hidden candidate fields/count outside current closed schema; no Saga/202/partial conversion under atomic H02','runtime_result':'NOT_RUN'})
    save('remediation/assets/core-types/lead-acceptance-supersession.json',{'revision':'CP6-DESIGN-REVISION-20261011/R1','role':'CURRENT_READING_MAP_NOT_HISTORICAL_REVIEW_OVERRIDE','sources':[{'source_path':x.as_posix(),'sha256':hashlib.sha256(x.read_bytes()).hexdigest()} for x in [p,q]],'preserved':'All original 60 AC identities and historical review outcomes; this map is not an independent whole-five-spec acceptance','records':rows})
    src=Path('D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/EXC_NEW_AC_CATALOGUE.json')
    d=json.loads(src.read_text(encoding='utf-8-sig'))
    before=d['criteria'][86]['steps'][0]
    assert 'readcut22' in before and d['criteria'][86]['inputAndPreconditions']['specificConditions']['fixedReadCut']==113
    save('remediation/assets/fixtures/editorial-patches.json',{'revision':'CP6-DESIGN-REVISION-20261011/R1','patches':[{'source_path':src.as_posix(),'source_sha256':hashlib.sha256(src.read_bytes()).hexdigest(),'source_finding_ids':['PLAN-IF-003'],'decision':'AC087 exact input remains readcut113; only stale prose changes','operations':[{'op':'replace_json','path':'/criteria/86/steps/0','before':before,'after':before.replace('readcut22','readcut113')}]}]})


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--handbook',type=Path,required=True);main(p.parse_args().handbook)
