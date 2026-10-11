"""Targeted post-review lexical and attachment compile check, no Owner runtime."""
import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path
from jsonschema import Draft202012Validator
from validate_design_completion import apply_operations


def main(book):
    out=book/'remediation/assets/core-types'
    patches=json.loads((out/'bounded-patches.json').read_text(encoding='utf-8'))['patches']
    cache=Path('D:/CP6-archives/design-revision-20261011-checks/atp-stableid-r1');cache.mkdir(parents=True,exist_ok=True)
    derived={};sources=[]
    for suffix in ['PLAN-ATP-WIRE.types.cs','PLAN-ATP-WIRE.types.ts','PLAN-ATP-WIRE.schema.json']:
        p=next(x for x in patches if x['source_path'].endswith(suffix));raw=Path(p['source_path']).read_bytes()
        assert hashlib.sha256(raw).hexdigest()==p['source_sha256']
        data=apply_operations(raw,p['operations']);(cache/suffix).write_bytes(data);derived[suffix]=data
        sources.append({'source_path':p['source_path'],'source_sha256':p['source_sha256'],'derived_sha256':hashlib.sha256(data).hexdigest()})
    cases=json.loads((out/'atp-stableid-vectors.json').read_text(encoding='utf-8'))['cases'];vectors=cache/'vectors.json';vectors.write_text(json.dumps(cases),encoding='utf-8')
    schema=json.loads(derived['PLAN-ATP-WIRE.schema.json'])['$defs']['StableId']
    for case in cases:assert Draft202012Validator(schema).is_valid(case['value'])==case['valid']
    cs_pattern=r'\A[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}\z'
    assert cs_pattern in derived['PLAN-ATP-WIRE.types.cs'].decode()
    assert 'const ID = /'+schema['pattern']+'/u;' in derived['PLAN-ATP-WIRE.types.ts'].decode()
    checks=[]
    def run(name,args):
        r=subprocess.run(args,cwd=cache,capture_output=True,text=True,encoding='utf-8',errors='replace')
        log=cache/(name+'.log');log.write_text(r.stdout+r.stderr,encoding='utf-8')
        checks.append({'check':name,'command':args,'exit_code':r.returncode,'log':log.as_posix(),'log_sha256':hashlib.sha256(log.read_bytes()).hexdigest()})
        assert r.returncode==0,(name,r.stdout,r.stderr)
    (cache/'lexical.ps1').write_text("$cases=Get-Content -Raw -LiteralPath './vectors.json' | ConvertFrom-Json\nforeach($c in $cases){if([regex]::IsMatch($c.value, '\\A[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}\\z') -ne $c.valid){throw 'lexical mismatch'}}\nWrite-Output '10 .NET lexical vectors PASS'\n",encoding='utf-8')
    run('dotnet-regex',['pwsh','-NoProfile','-File',str(cache/'lexical.ps1')])
    js="const fs=require('fs');const p=JSON.parse(fs.readFileSync('PLAN-ATP-WIRE.schema.json','utf8')).$defs.StableId.pattern;const r=new RegExp(p,'u');for(const c of JSON.parse(fs.readFileSync('vectors.json','utf8')))if(r.test(c.value)!==c.valid)throw Error('lexical mismatch');console.log('10 JavaScript lexical vectors PASS');"
    run('javascript-regex',['node','-e',js])
    (cache/'ContractCheck.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>',encoding='utf-8')
    run('csharp-compile',['dotnet','build','ContractCheck.csproj','--nologo','--verbosity','quiet'])
    run('typescript-compile',['node','D:/CP6/cp6.web/node_modules/typescript/bin/tsc','--noEmit','--strict','--target','ES2022','--module','ESNext','--lib','ES2022,DOM','PLAN-ATP-WIRE.types.ts'])
    report={'result':'PASS','scope':'10 precise vectors checked in JSON Schema/.NET/JavaScript; revised full ATP C#/TS attachment compile only','sources':sources,'cases':cases,'checks':checks,'runtime_acceptance':'NOT_RUN','old_compile_evidence':'Earlier ATP C# hash in attachment-compile-validation.json is historical; this new exact hash supersedes that attachment only'}
    (out/'atp-stableid-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');print(json.dumps({'result':'PASS','cases_per_engine':10,'compile_checks':2}))


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--handbook',required=True,type=Path);main(p.parse_args().handbook)
