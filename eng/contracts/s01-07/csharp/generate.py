"""Deterministic, bounded adaptation of pinned NCR C# declarations and native schemas.

Run from any directory: python eng/contracts/s01-07/csharp/generate.py [--check]
No archive/network dependency. This file is the explicit adoption/route allowlist.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import posixpath
import re

ROOT = pathlib.Path(__file__).resolve().parents[4]
TASK = ROOT / 'eng/contracts/s01-07'
INPUT = TASK / 'inputs'
OUTPUT = ROOT / 'CP6.Entity/Contracts/Quality'
MAIN = 'design/NCR-R6-WIRE.json'
CP4 = 'design/references/consumer-cp4-r1/CONSUMER-WIRE.json'
OWNER = 'design/references/consumer-cp4-r1/references/owner-addenda-cp2/CONTRACT-SCHEMAS.json'
SOURCE = 'design/references/consumer-cp4-r1/references/owner-addenda-cp2/source-contracts/SOURCE-QA-SCHEMAS.json'
FIN = 'design/references/finance-r10/CLOSED-WIRE-SCHEMA_R10.json'
IPQC = 'design/references/initial-sources/QA-IPQC-01/SCHEMA_REGISTRY_RECOVERY_R4_SYSTEMIC.json'
WMS = 'design/references/initial-sources/WMS-RMA-01/WMS-RMA-01_CANONICAL-CONTRACT.json'
MES = 'design/NCR-R6-MES-DISPATCH-SHAPE.json'
NS = {'QaNcrMainR6Declarations': 'CP6.Entity.Contracts.Quality.Main',
      'SourceQA': 'CP6.Entity.Contracts.Quality.SourceQA',
      'OwnerWire': 'CP6.Entity.Contracts.Quality.OwnerWire',
      'Consumer': 'CP6.Entity.Contracts.Quality.Consumer'}

# Keep native module identities. Each public route validates the entire object.
ROOTS = {
    'QaNcrMainR6Declarations.Main_InitialSourceOptionQuery': MAIN+'#/$defs/InitialSourceOptionQuery',
    'QaNcrMainR6Declarations.Main_InitialSourceOptionPage': MAIN+'#/$defs/InitialSourceOptionPage',
    'QaNcrMainR6Declarations.Main_MainResult_Variant14_Payload': MAIN+'#/$defs/MainResult/oneOf/14',
    'QaNcrMainR6Declarations.Externalf70281e4_C09FailedProblem': FIN+'#/$defs/C09FailedProblem',
    'QaNcrMainR6Declarations.External6d9d3eee__types_IpqcFinalGuardResult': IPQC+'#/types/IpqcFinalGuardResult',
    'QaNcrMainR6Declarations.Main_Quantity': MAIN+'#/$defs/Quantity',
    'QaNcrMainR6Declarations.Externalf70281e4_Ref': FIN+'#/$defs/Ref',
    'QaNcrMainR6Declarations.Externalf70281e4_ScopeRef': FIN+'#/$defs/ScopeRef',
    'QaNcrMainR6Declarations.External6aa69b80_Ref': WMS+'#/$defs/Ref',
    'QaNcrMainR6Declarations.External6aa69b80_Uom': WMS+'#/$defs/Uom',
    'QaNcrMainR6Declarations.External6d9d3eee__types_OutQualitySource_outputRef': IPQC+'#/types/OutQualitySource/properties/outputRef',
    'QaNcrMainR6Declarations.External28fc80fc_RegisterExecutionBinding_scope': MES+'#/$defs/RegisterExecutionBinding/properties/scope',
    'SourceQA.ObservationInput': SOURCE+'#/$defs/ObservationInput',
    'SourceQA.IpqcFinalGuardResult': SOURCE+'#/$defs/IpqcFinalGuardResult',
    'SourceQA.EnvironmentKey': SOURCE+'#/$defs/EnvironmentKey',
    'SourceQA.QapRef': SOURCE+'#/$defs/QapRef',
    'SourceQA.QapEvidence': SOURCE+'#/$defs/QapEvidence',
    'SourceQA.QapUom': SOURCE+'#/$defs/QapUom',
    'SourceQA.ExecRef': SOURCE+'#/$defs/ExecRef',
    'SourceQA.ExecEvidence': SOURCE+'#/$defs/ExecEvidence',
    'SourceQA.ExecScope': SOURCE+'#/$defs/ExecScope',
    'OwnerWire.Scope': OWNER+'#/$defs/Scope',
    'OwnerWire.NestedEvidence': OWNER+'#/$defs/NestedEvidence',
    'Consumer.ReferenceIdentity': CP4+'#/$defs/ReferenceIdentity',
}

documents = {p.relative_to(INPUT/'native').as_posix(): json.loads(p.read_text(encoding='utf-8'))
             for p in (INPUT/'native').rglob('*.json')}
declarations = {}
for source_id in ('composed-15', 'composed-16'):
    source = (INPUT/'composed'/f'{source_id}.cs.txt').read_text(encoding='utf-8')
    namespaces = list(re.finditer(r'namespace (\w+)', source))
    for m in re.finditer(r'^public (?:sealed|abstract) record (\w+)([^\n]*)', source, re.M):
        namespace = next(n.group(1) for n in reversed(namespaces) if n.start() < m.start())
        end = source.index('\n}', m.end()) + 2 if m.group(2).endswith('{') else m.end()
        declarations[namespace+'.'+m.group(1)] = {
            'text': source[m.start():end], 'source': source_id,
            'line': source[:m.start()].count('\n')+1, 'namespace': namespace, 'name': m.group(1),
        }

def node_at(route):
    file, pointer = route.split('#', 1)
    node = documents[file]
    for key in pointer.lstrip('/').split('/') if pointer else []:
        key = key.replace('~1','/').replace('~0','~')
        node = node[int(key)] if isinstance(node,list) else node[key]
    return node

def ref_route(file, ref):
    path, _, pointer = ref.partition('#')
    target = posixpath.normpath(posixpath.join(posixpath.dirname(file),path)) if path else file
    if target not in documents:
        raise ValueError(f'Unpinned reference {file}: {ref}')
    return target+'#'+pointer

def resolved(route):
    node = node_at(route)
    while '$ref' in node:
        route = ref_route(route.split('#')[0],node['$ref'])
        node = node_at(route)
    return route,node

def nonnull(route):
    route,node = resolved(route)
    for kw in ('oneOf','anyOf'):
        if kw in node:
            nonnulls = [(i,b) for i,b in enumerate(node[kw]) if b.get('type')!='null' and not ('const' in b and b['const'] is None)]
            if len(nonnulls)==1 and len(node[kw])==2:
                return resolved(route+f'/{kw}/{nonnulls[0][0]}')
    return route,node

def qualify(name, namespace):
    return name if '.' in name else namespace+'.'+name

type_routes = {}
unions = {}
wrappers = {}
def adopt(type_name, route):
    route,node = nonnull(route)
    if type_name in type_routes:
        assert type_routes[type_name]==route or node_at(type_routes[type_name])==node, ('conflicting native type route',type_name,type_routes[type_name],route)
        return
    declaration = declarations[type_name]
    type_routes[type_name]=route
    text = declaration['text']; namespace = declaration['namespace']; short = declaration['name']
    if 'public abstract record' in text:
        branches = node.get('oneOf', node.get('anyOf'))
        assert branches, (type_name, route)
        unions[type_name]=[]
        for i,branch in enumerate(branches):
            case = qualify(short+(f'_Variant{i}' if namespace=='QaNcrMainR6Declarations' else f'_Case{i+1}'),namespace)
            branch_route = route+('/oneOf/' if 'oneOf' in node else '/anyOf/')+str(i)
            case_text=declarations[case]['text']
            wrapper = re.search(r'record \w+\(([\w.]+) Value\)',case_text)
            if wrapper:
                payload=qualify(wrapper.group(1),namespace)
                wrappers[case]=payload
                type_routes[case]=branch_route
                adopt(payload,branch_route)
            else:
                adopt(case,branch_route)
            unions[type_name].append(case)
        return
    assert node.get('type')=='object' or 'properties' in node, (type_name,route,node)
    for m in re.finditer(r'\[JsonPropertyName\("([^"\n]+)"\)\] public (?:required )?([^\n]+?) (\w+) (?:\{|=>)', text):
        wire,kind,_ = m.groups(); kind=kind.rstrip('?')
        child_route=route+'/properties/'+wire.replace('~','~0').replace('/','~1')
        if kind.startswith('IReadOnlyList<'):
            kind=kind[len('IReadOnlyList<'):-1]
            child_route=resolved(child_route)[0]+'/items'
        if kind in ['string','long','bool','decimal','double','int','JsonNull','object','Cp6.DesignRevision.NullOnly']:
            continue
        adopt(qualify(kind,namespace),child_route)

for t,r in ROOTS.items(): adopt(t,r)

schemas={}
# Deliberately restricted to predicates in this adoption closure. Expanding routes
# must fail generation until the codec implements and tests newly encountered keywords.
schema_keywords = {'$ref','type','const','enum','required','additionalProperties','properties','items','oneOf','minLength','maxLength','pattern','format','minItems','maxItems','minimum','maximum'}
def normalized(node,file):
    if isinstance(node,bool):return node
    result={}
    for k,v in node.items():
        if k not in schema_keywords:
            assert k.startswith('x-') or k in ['$schema','$id','title','description','$comment','default','examples','deprecated','readOnly','writeOnly'], (file,k)
            continue
        if k=='$ref':
            v=ref_route(file,v);add_schema(v)
        elif k=='type':assert v in ['object','array','string','boolean','integer','null'],(file,k,v)
        elif k=='format':assert v in ['uuid','date-time'],(file,k,v)
        elif k=='pattern':assert '$' not in v or v.count('$')==1 and v.endswith('$') and not v.endswith('\\$'),(file,k,v)
        elif k=='properties':v={n:normalized(s,file) for n,s in v.items()}
        elif k=='oneOf':v=[normalized(s,file) for s in v]
        elif k=='items':v=normalized(v,file)
        elif k=='additionalProperties':assert v is False,(file,k,v)
        result[k]=v
    return result

def add_schema(route):
    if route not in schemas:
        schemas[route]=None
        schemas[route]=normalized(node_at(route),route.split('#')[0])

for route in sorted(set(type_routes.values())):add_schema(route)

def adapted_name(original):
    ns,short=original.rsplit('.',1)
    return NS[ns]+'.'+short

def nullable(route):
    route,node=resolved(route)
    if node.get('type')=='null' or 'const' in node and node['const'] is None:return True
    if 'enum' in node and None in node['enum']:return True
    return any(nullable(route+f'/{kw}/{i}') for kw in ['oneOf'] for i in range(len(node.get(kw,[]))))

def field_inventory():
    inventory=[]
    for original,route in sorted(type_routes.items()):
        route,node=resolved(route)
        fields=[]
        for name,shape in node.get('properties',{}).items():
            pointer=route+'/properties/'+name
            resolved_pointer,resolved_shape=resolved(pointer)
            fields.append({'name':name,'required':name in node.get('required',[]),'nullable':nullable(pointer),
                           'sourcePointer':pointer,'resolvedSourcePointer':resolved_pointer,
                           'shape':shape,'resolvedShape':resolved_shape})
        inventory.append({'originalDeclaration':original,'adaptedDeclaration':adapted_name(original),
                          'sourceModule':route.split('#')[0],'sourcePointer':route,
                          'objectClosed':node.get('additionalProperties') is False if node.get('type')=='object' else None,
                          'unionBranches':[adapted_name(t) for t in unions.get(original,[])],
                          'wireWrapper':False,'fields':fields,
                          'versionAndIdentityFields':[f['name'] for f in fields if re.search(r'version|revision|sequence|epoch|owner|schema|kind|operation|status|code',f['name'],re.I)],
                          'identityMeaning':'Exact source predicates only; CLR name is not a wire discriminator. No Owner/event authority inferred.'})
    return {'schema':'S01-07-NATIVE-FIELD-INVENTORY-1','declarations':inventory}

def content():
    outputs={}
    language=json.loads((INPUT/'protocol/NCR-R6-LANGUAGE-CONTRACTS.json').read_text(encoding='utf-8'))
    union_map={x['type']:x for x in language['csharpUnionMap']}
    for original,cases in unions.items():
        if original.startswith('QaNcrMainR6Declarations.'):
            expected=union_map[original.rsplit('.',1)[1]]
            assert expected['oneOfExact'] and expected['variants']==[c.rsplit('.',1)[1] for c in cases]
    header='// <auto-generated by eng/contracts/s01-07/csharp/generate.py; do not edit>\n#nullable enable\n'
    for ns in sorted({declarations[t]['namespace'] for t in type_routes}):
        chunks=[]
        for t in sorted((t for t in type_routes if declarations[t]['namespace']==ns),key=lambda x:declarations[x]['line']):
            text=declarations[t]['text']
            text=text.replace('Cp6.DesignRevision.NullOnly','CP6.Entity.Contracts.Wire.NullOnly')
            text=re.sub(r'\bJsonNull\b','CP6.Entity.Contracts.Wire.NullOnly',text)
            for old,new in NS.items():text=text.replace(old+'.',new+'.')
            # Closed contracts use the task codec on every public serializer entry, too.
            text='[JsonConverter(typeof(CP6.Entity.Contracts.Quality.NativeContractConverterFactory))]\n'+text
            chunks.append(text)
        outputs[OUTPUT/(ns+'.g.cs')] = header+'using System.Collections.Generic;\nusing System.Text.Json.Serialization;\nnamespace '+NS[ns]+';\n\n'+'\n\n'.join(chunks)+'\n'
    data={
        'routes': {adapted_name(k):v for k,v in sorted(type_routes.items())},
        'schemas':dict(sorted(schemas.items())),
        'unions':{adapted_name(k):[adapted_name(c) for c in v] for k,v in sorted(unions.items())},
        'wrappers':{adapted_name(k):adapted_name(v) for k,v in sorted(wrappers.items())},
    }
    serialized=json.dumps(data,separators=(',',':'),ensure_ascii=True)
    outputs[OUTPUT/'NativeContractCatalog.g.cs']=header+'namespace CP6.Entity.Contracts.Quality;\ninternal static class NativeContractCatalog\n{\n    internal const string Json = """\n'+serialized+'\n""";\n}\n'
    fieldmap=json.loads((INPUT/'composed/composed-17.json').read_text(encoding='utf-8'))
    pointers=['#/$defs/MainResult/oneOf/14/properties/priorHeadRef']+['#/$defs/InitialSourceOptionQuery/properties/'+p for p in ['sourceKind','keyword','cursor']]+['#/$defs/InitialSourceOptionPage/properties/nextCursor']
    fields=[next(f for f in fieldmap['fields'] if f['pointer']==p) for p in pointers]
    outputs[OUTPUT/'NcrFieldMetadata.g.cs']=header+'namespace CP6.Entity.Contracts.Quality;\npublic sealed record NcrFieldMetadata(string Pointer, bool Required, bool Nullable, bool Editable)\n{\n    public static System.Collections.Generic.IReadOnlyList<NcrFieldMetadata> Fields { get; } = System.Array.AsReadOnly(new NcrFieldMetadata[]\n    {\n'+''.join('        new('+json.dumps(f['pointer'])+', '+', '.join(str(f[k]).lower() for k in ['required','nullable','editable'])+'),\n' for f in fields)+'    });\n}\n'
    outputs[TASK/'csharp/native-field-inventory.json']=json.dumps(field_inventory(),indent=2,ensure_ascii=True)+'\n'
    manifest={
        'schema':'S01-07-CSHARP-ADAPTATION-1','command':'python eng/contracts/s01-07/csharp/generate.py --check',
        'sourceHashes':{f:hashlib.sha256((INPUT/'composed'/f).read_bytes()).hexdigest() for f in ['composed-15.cs.txt','composed-16.cs.txt','composed-17.json']},
        'originalSourceHashes':{f:hashlib.sha256((INPUT/'original'/f).read_bytes()).hexdigest() for f in ['composed-15.cs.txt','composed-16.cs.txt','composed-17.json']},
        'compositionPatchPointers':['inputs/pins/bounded-patches.json#/patches/2','inputs/pins/bounded-patches.json#/patches/3','inputs/pins/bounded-patches.json#/patches/4'],
        'languageSourceHashes':{f:hashlib.sha256((INPUT/'protocol'/f).read_bytes()).hexdigest() for f in ['NCR-R6-LANGUAGE-CONTRACTS.json','LANGUAGE-CONTRACTS.md']},
        'patternDialect':{'source':'ECMAScript without multiline; original patterns embedded unchanged','dotnetAdaptation':'Terminal unescaped $ -> \\z because .NET ECMAScript still accepts final LF; actual Node and net8 regression evidence recorded.','schemaRevision':False},
        'nativeSchemaHashes':{f:hashlib.sha256((INPUT/'native'/f).read_bytes()).hexdigest() for f in sorted({r.split('#')[0] for r in schemas})},
        'adaptations':['Selected original declaration blocks by explicit root and complete CLR field/union closure.', 'Qualified original namespaces under CP6.Entity.Contracts.Quality.', 'Bound Cp6.DesignRevision.NullOnly and original JsonNull markers to existing Wire.NullOnly.', 'Attached strict native converter to all emitted declaration types.', 'Embedded exact referenced schema predicates with file-relative refs normalized; ignored annotations only.', 'Five metadata rows copied from composed-17 without guessing nullability.'],
        'roots':ROOTS,'declarationCount':len(type_routes),'schemaCount':len(schemas),
        'declarations':[{ 'original':t,'adapted':adapted_name(t),'source':declarations[t]['source'],'line':declarations[t]['line'],'schema':type_routes[t]} for t in sorted(type_routes)],
        'outputs':{p.relative_to(ROOT).as_posix():hashlib.sha256(s.encode()).hexdigest() for p,s in outputs.items()},
        'limitations':'Bounded contract codec routes only; unselected declarations, semantic/currentness/Owner checks, storage and business acceptance are not implemented or claimed.'
    }
    outputs[TASK/'csharp/generation-manifest.json']=json.dumps(manifest,indent=2,ensure_ascii=False)+'\n'
    return outputs

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--check',action='store_true');args=parser.parse_args()
    outputs=content()
    for file,text in outputs.items():
        if args.check:
            assert file.read_bytes()==text.encode(),f'Generated drift: {file}'
        else:
            file.parent.mkdir(parents=True,exist_ok=True);file.write_bytes(text.encode())
    print(json.dumps({'mode':'check' if args.check else 'generate','declarations':len(type_routes),'schemas':len(schemas),'files':len(outputs)}))

if __name__=='__main__':main()
