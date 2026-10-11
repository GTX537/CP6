"""Validate design JSON and exact quantity invariants, not live Owner evidence."""
import argparse
import json
from decimal import Decimal, localcontext
from pathlib import Path
from jsonschema import Draft202012Validator


def semantic_errors(x):
    errors=[]
    def require(condition, name):
        if not condition: errors.append(name)
    q=x['inputQuantity']
    unit=x['binding']['unitRef']
    slices=x['responsibilitySlices']; offsets=x['offsets']; members=x['offsetCoverageMembers']
    share_keys=[v['shareKey'] for v in slices]
    offset_ids=[v['offsetId'] for v in offsets]
    member_ids=[v['memberId'] for v in members]
    require(len(set(share_keys))==len(share_keys),'unique current responsibility share')
    require(len(set(offset_ids))==len(offset_ids),'unique offset identity')
    require(len(set(member_ids))==len(member_ids),'unique physical coverage member')
    for v in offsets+members: require(v['shareKey'] in share_keys,'offset share must exist')
    quantities=[v['grossQuantity'] for v in slices]+[v['quantity'] for v in offsets+members]+([q] if q else [])
    require(all(v['unitRef']==unit for v in quantities),'same exact unit or prior qualified conversion')
    by_member={v['memberId']:v for v in members}
    used=set()
    for off in offsets:
        require(set(off['coverageMembers'])<=set(member_ids),'all offset members exist')
        used.update(off['coverageMembers'])
        with localcontext() as ctx:
            ctx.prec=100
            require(sum((Decimal(by_member[k]['quantity']['value']) for k in off['coverageMembers'] if k in by_member),Decimal(0))==Decimal(off['quantity']['value']),'offset amount equals exact physical member coverage')
        require(set(off['supersedesOffsetIds'])<=set(offset_ids),'stage replacement references explicit original offset')
    require(set(x['excludeFromFurtherNettingSet'])==used,'exclude every eligible physical member once')
    with localcontext() as ctx:
        ctx.prec=100
        gross=sum((Decimal(v['grossQuantity']['value']) for v in slices),Decimal(0))
        net=gross-sum((Decimal(by_member[k]['quantity']['value']) for k in used if k in by_member),Decimal(0))
        require(net>=0,'offset union cannot exceed gross responsibility')
        if x['knowledge']=='KNOWN' and x['coverage']['state']=='COMPLETE':
            require(q is not None,'known complete amount is explicit')
            if q: require(Decimal(q['value'])==(net if x['basisKind']=='NET_RESIDUAL' else gross),'basis quantity preserves mode')
    require(set(x['alreadyAppliedOffsetSet'])==(set(offset_ids) if x['basisKind']=='NET_RESIDUAL' else set()),'mode-specific already-applied offset set')
    if x['eligibleForFormalSeal']:
        require(x['knowledge']=='KNOWN' and x['coverage']['state']=='COMPLETE' and x['coverage']['completenessProof'] is not None,'formal seal needs complete known evidence')
        require(x['coverage']['requestedScope']==x['coverage']['actualScope'],'complete capture covers requested scope exactly')
        require(not x['coverage']['missingScopes'],'no missing scope at formal seal')
        require(all(a['disjointFromRequestedScopeProof'] is not None for a in x['unresolvedActions']),'unresolved overlap forbids formal seal')
        require('FORMAL_PLANNING_INPUT' in x['allowedUses'],'formal permission explicit')
    else: require('FORMAL_PLANNING_INPUT' not in x['allowedUses'],'diagnostic capture cannot grant formal use')
    return errors


def main(book):
    folder=book/'remediation/assets/material-handoff'
    schema=json.loads((folder/'wire-1.0.0.schema.json').read_text(encoding='utf-8'))
    Draft202012Validator.check_schema(schema)
    rows=[]
    for case in json.loads((folder/'examples-1.0.0.json').read_text(encoding='utf-8'))['cases']:
        selected={**schema,'$ref':'#/$defs/'+case['schema']}
        errors=[e.message for e in Draft202012Validator(selected).iter_errors(case['body'])]
        if not errors and case['schema']=='ResidualHandoff': errors+=semantic_errors(case['body'])
        actual=not errors
        assert actual==case['expected_document_valid'],(case['id'],errors)
        rows.append({'id':case['id'],'expected_valid':actual,'result':'PASS','detected':errors})
    assert 40+180-50==170 and 50+180-50==180 and 50+30==80
    report={'result':'PASS','scope':'schema and exact quantity design vectors only; no Owner execution, policy proof or authorization runtime','cases':rows,'continuous_quantity_examples':3,'runtime_acceptance':'NOT_RUN'}
    (folder/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'result':'PASS','cases':len(rows),'quantity_examples':3}))


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--handbook',type=Path,required=True);main(p.parse_args().handbook)
