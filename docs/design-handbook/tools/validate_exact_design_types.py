"""Check original numeric languages against the new lossless storage design."""
import argparse
import hashlib
import json
import re
from pathlib import Path


def canonical_pair(text):
    sign=-1 if text.startswith('-') else 1
    whole,sep,fraction=text.lstrip('-').partition('.')
    coefficient=sign*int(whole+fraction)
    scale=len(fraction)
    if coefficient==0: return 0,0
    while scale and coefficient%10==0: coefficient//=10;scale-=1
    return coefficient,scale


def encode_pair(coefficient,scale):
    sign='-' if coefficient<0 else ''
    digits=str(abs(coefficient)).zfill(scale+1)
    return sign+(digits[:-scale]+'.'+digits[-scale:] if scale else digits)


def main(book,archive,cache):
    original=archive/'完整成果/docs/originals'
    c=next(original.glob('f7/f768bf*')); inv=next(original.glob('3a/3a61c4*')); dictionary=next(original.glob('8d/8d2b80*'))
    defs=json.loads(c.read_text(encoding='utf-8-sig'))['$defs']
    invdefs=json.loads(inv.read_text(encoding='utf-8-sig'))['$defs']
    tests=[]
    values=[('signedDecimal','9'*38,True),('signedDecimal','-'+'9'*38,True),('signedDecimal','9'*30+'.12345678',True),('signedDecimal','9'*31+'.12345678',False),('signedDecimal','0.00000001',True),('signedDecimal','0',True),('signedDecimal','-0',False),('signedDecimal','1.0',False),('positiveDecimal','0',False),('signedDecimal','1e3',False),('nonNegativeDecimal','-1',False)]
    for kind,value,expected in values:
        valid=bool(re.fullmatch(defs[kind]['pattern'],value))
        assert valid==expected,(kind,value)
        pair=None
        if valid:
            coefficient,scale=canonical_pair(value)
            assert abs(coefficient)<10**38 and 0<=scale<=8
            assert encode_pair(coefficient,scale)==value
            pair={'unscaledValue':str(coefficient),'scale':scale}
        tests.append({'wire_type':kind,'input':value,'accepted':valid,'pair':pair,'result':'PASS'})
    for kind,value,expected in [('Decimal18','9'*90+'.'+'1'*18,True),('Qty12','-'+'9'*90+'.'+'1'*12,True),('Qty12','-0.000',True),('Decimal18','1.'+'1'*19,False),('Qty12','1e12',False)]:
        valid=bool(re.fullmatch(invdefs[kind]['pattern'],value));assert valid==expected
        pair=None
        if valid:
            n,s=canonical_pair(value)
            # Preserve exact source bytes separately: -0.000 is legal in native INV.
            pair={'unscaledDigits':str(n),'scale':s,'originalWire':value}
            n2,s2=canonical_pair(encode_pair(n,s));assert (n,s)==(n2,s2)
        tests.append({'wire_type':kind,'input':value,'accepted':valid,'pair':pair,'result':'PASS'})
    # Exact arithmetic intermediate can exceed bounded output domain.
    large=int('9'*38);assert (large*10)//10==large and large+1==10**38
    uom=json.loads((cache/'root-planning-cache/atp-r11/PLAN-ATP-WIRE.schema.json').read_text(encoding='utf-8-sig'))['$defs']['StableId']
    for value,expected in [('U'*16,True),('U'*17,True),('U'*128,True),('U'*129,False),('',False),('EA ',False),('公斤',False)]:
        valid=bool(re.fullmatch(uom['pattern'],value));assert valid==expected,(value,uom)
        tests.append({'wire_type':'ATP.StableId','input':value,'accepted':valid,'result':'PASS'})
    data=json.loads(dictionary.read_text(encoding='utf-8-sig'));mappings=[]
    for ti,table in enumerate(data['Tables']):
        for ci,col in enumerate(table['Columns']):
            m=re.fullmatch(r'decimal\((\d+),(\d+)\)',col['Type'])
            if not m: continue
            scale=int(m[2]);mappings.append({'source_pointer':f'/Tables/{ti}/Columns/{ci}', 'table':table['Name'],'logical_column':col['Name'],'old_storage_type':col['Type'],'nullable':col['Nullable'],'new_columns':[{'name':col['Name']+'UnscaledDigits','sql_server_type':'varchar(max)','postgres_type':'text','encoding':'canonical signed integer ASCII; exact BigInteger; no exponent/leading plus/negative zero'},{'name':col['Name']+'Scale','type':'smallint','range':[0,scale]}], 'coupling':'both null iff logical value null; otherwise both non-null; original native wire remains separately preserved','conversion':'validate exact native field schema first; no rounding, truncation or cast through finite decimal; wire range not enlarged by storage representation'})
    out=book/'remediation/assets/core-types';out.mkdir(parents=True,exist_ok=True)
    sources=[{'path':p.as_posix(),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in [c,inv,dictionary]]
    mapping={'revision':'CP6-EXACT-NUMERIC-STORAGE/1','source_integrity':sources,'credit':{'wire':'original signed/nonnegative/positiveDecimal selected per field','sql_server_columns':{'UnscaledValue':'decimal(38,0)','Scale':'smallint CHECK 0..8'},'canonical_rules':'zero -> (0,0); Scale>0 forbids UnscaledValue divisible by10; validate reconstructed wire against original exact field pattern','dotnet':'BigInteger + scale; never System.Decimal intermediary','typescript':'string/bigint pair; never Number intermediary'},'inventory_mappings':mappings,'inventory_rational':'unbounded signed numerator/positive denominator canonical integer text, reduced gcd, exact BigInteger; division only at original policy rounding point','indexing':'do not index or CAST arbitrary digits as numeric; identity uses original stable IDs; all arithmetic through validated exact codec inside original transaction/lock boundary','legacy_rollout':'append new representation and compare full exact old original; values formerly representable must round-trip, original receipt bytes unchanged; switch all writers together after verified backfill; migration NOT_RUN','business_runtime':'NOT_RUN'}
    (out/'numeric-storage-1.0.0.json').write_text(json.dumps(mapping,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    report={'result':'PASS','scope':'original regex membership, exact decimal round trip and design storage mappings; no database or application execution','cases':tests,'inventory_columns_mapped':len(mappings),'runtime_acceptance':'NOT_RUN'}
    (out/'numeric-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'result':'PASS','cases':len(tests),'columns':len(mappings)}))


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--handbook',type=Path,required=True);p.add_argument('--archive',type=Path,required=True);p.add_argument('--cache',type=Path,required=True);a=p.parse_args();main(a.handbook,a.archive,a.cache)
