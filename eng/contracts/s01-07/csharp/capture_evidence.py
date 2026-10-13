"""Summarize the already executed C# TRX gate; does not run or infer tests."""
import hashlib
import json
import pathlib
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT=pathlib.Path(__file__).resolve().parents[4]
OUT=ROOT/'docs/development/evidence/S01-07/csharp'
TASK=ROOT/'eng/contracts/s01-07/csharp'
REPORT=OUT/'quality-contracts-format-final.trx'
N={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
report=ET.parse(REPORT).getroot()
results=report.findall('t:Results/t:UnitTestResult',N)
fixed=[r for r in results if 'Fixed_NCR_NULL_vector_' in r.attrib['testName']]
ids=[re.search(r'NCR-NULL-\d{3}',r.attrib['testName']).group() for r in fixed]
assert sorted(ids)==[f'NCR-NULL-{i:03}' for i in range(1,21)]
assert all(r.attrib['outcome']=='Passed' for r in fixed)
fixture=json.loads((TASK/'full-object-fixtures.json').read_text(encoding='utf-8'))
manifest=json.loads((TASK/'generation-manifest.json').read_text(encoding='utf-8'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
source_files=sorted((ROOT/'CP6.Entity/Contracts/Quality').glob('*.cs'))+[
    ROOT/'eng/contracts/exact-values-tests/QualityContractTests.cs',
    *sorted((ROOT/'CP6.Entity/Contracts/Wire').glob('*.cs')),
]
dependencies=[ROOT/p for p in ['Directory.Build.props','CP6.Entity/CP6.Entity.csproj','CP6.Entity/packages.lock.json','eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj']]
record={
    'schema':'S01-07-CSHARP-VERIFICATION-1',
    'scope':'Compiled native NCR contract codec only; no Owner, storage, event publication or production acceptance.',
    'sourceHead':subprocess.check_output(['git','-c','core.longpaths=true','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
    'sourceState':'Task source changes uncommitted at execution; exact tested code/dependency file hashes follow. Parent coordinates review/commit/merge.',
    'sourceInputBaseline':'e6ccc89b3e62c7ed2c3d4739757b6ae9f4490c47',
    'command':'dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~QualityContractTests --logger "trx;LogFileName=quality-contracts-format-final.trx" --results-directory docs/development/evidence/S01-07/csharp --logger "console;verbosity=minimal"',
    'times':report.find('t:Times',N).attrib,
    'counters':report.find('t:ResultSummary/t:Counters',N).attrib,
    'runtime':'.NET 8.0.29','targetFramework':'net8.0','sdk':'10.0.302','os':'Windows 10.0.26200 win-x64',
    'fixedVectorResults':[{'id':i,'outcome':r.attrib['outcome'],'scope':'Frozen field assertion executed over each applicable complete native fixture'} for i,r in sorted(zip(ids,fixed))],
    'fullObjectFixtures':{'total':len(fixture['cases']),'positive':sum(c['accepted'] for c in fixture['cases']),'negative':sum(not c['accepted'] for c in fixture['cases']),'fileSha256':sha(TASK/'full-object-fixtures.json'),'provenance':'Constructed from pinned schemas; not actual Owner data or business acceptance'},
    'nativeClosure':{'explicitRoots':len(manifest['roots']),'compiledDeclarations':manifest['declarationCount'],'schemaNodes':manifest['schemaCount'],'sourceManifest':'eng/contracts/s01-07/csharp/generation-manifest.json','fieldInventory':'eng/contracts/s01-07/csharp/native-field-inventory.json'},
    'codeHashes':{p.relative_to(ROOT).as_posix():sha(p) for p in source_files},
    'dependencyHashes':{p.relative_to(ROOT).as_posix():sha(p) for p in dependencies},
    'generation':{'command':'python eng/contracts/s01-07/csharp/generate.py --check','result':'PASS: 92 declarations, 102 schemas, 8 generated files'},
    'tdd':[
        {'log':'red-declaration-boundary.log','discovered':2,'failed':2,'passed':0,'reason':'Real default DTO boundary incorrectly accepted unknown sourceKind and foreign owner field.'},
        {'log':'red-full-native.log','discovered':631,'failed':276,'passed':355,'reason':'Declaration-only baseline lacked native scalar/closed object validation and complete union codecs.'},
        {'log':'red-writer-identity.log','discovered':2,'failed':2,'passed':0,'reason':'Writer allowed concrete branch to become a sibling tuple and replaced unpaired UTF-16.'},
        {'log':'red-regex-dialect.log','discovered':5,'failed':1,'passed':4,'reason':'Raw .NET ECMAScript accepted final LF unlike actual Node native regex semantics.'},
        {'log':'red-rfc3339-format.log','discovered':12,'failed':6,'passed':6,'reason':'DateTime year restrictions and June/December-only positions narrowed RFC3339 format input.'}
    ],
    'fixtureClarification':'Main Decimal uses canonical no-meaningless-trailing-zero syntax; preservation of 1.00000000 is tested on SourceQA ObservationInput where original Qty permits it. Frozen vector bytes/expected values unchanged.',
    'dateTimeScope':{'reference':'https://www.rfc-editor.org/rfc/rfc3339#section-5.7','verified':'Calendar including year0000, exact original fractions/offsets, possible UTC month-end second60 positions','notClaimed':'IERS announcement or actual occurrence of a synthetic leap-second date'},
    'notRun':['Whole source declaration surface outside the explicit adopted closure','QA Owner currentness/authorization/cross-object semantic checks','QualityInspectionService and DefectRecordService wiring','Database/schema migrations','Owner business acceptance and production deployment','GitHub Actions'],
    'actions':{'triggerChanges':[],'cancelledRuns':[],'remoteRuns':[]},
    'artifacts':{p.name:sha(p) for p in sorted(OUT.iterdir()) if p.is_file() and p.name not in ['verification.json','README.md']},
}
assert int(record['counters']['total'])==len(results)==657
assert int(record['counters']['passed'])==657 and int(record['counters']['failed'])==0
(OUT/'verification.json').write_text(json.dumps(record,indent=2,ensure_ascii=False)+'\n',encoding='utf-8',newline='\n')
print(json.dumps({'total':len(results),'passed':657,'fixedNcrIds':len(ids),'compiledDeclarations':manifest['declarationCount']}))
