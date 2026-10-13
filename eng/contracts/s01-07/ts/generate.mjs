// Generates only from repository-owned, hash-verified inputs. No archive program is executed.
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs'
import { createHash } from 'node:crypto'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../..')
const require = createRequire(path.join(root, 'cp6.web/package.json'))
const ts = require('typescript')
const base = 'eng/contracts/s01-07/'
const manifest = JSON.parse(readFileSync(path.join(root, base, 'sources.json')))
const hash = data => createHash('sha256').update(data).digest('hex')
function input(relative) {
  const name = base + relative
  const asset = manifest.assets.find(a => a.path === name)
  const bytes = readFileSync(path.join(root, name))
  if (!asset || hash(bytes) !== asset.sha256) throw new Error(`Unpinned input: ${name}`)
  return bytes.toString('utf8')
}
const source = input('inputs/composed/composed-19.ts.txt')
const registry = JSON.parse(input('inputs/native/design/references/initial-sources/QA-IPQC-01/SCHEMA_REGISTRY_RECOVERY_R4_SYSTEMIC.json'))
const canonical = JSON.parse(input('inputs/protocol/CANONICAL_CONTRACT_RECOVERY_R4_SYSTEMIC.json'))
const ast = ts.createSourceFile('composed-19.ts', source, ts.ScriptTarget.Latest, true)
const declarations = new Map(ast.statements.filter(ts.isTypeAliasDeclaration).map(d => [d.name.text, d.type]))
function schema(node) {
  if (ts.isParenthesizedTypeNode(node)) return schema(node.type)
  if (ts.isTypeReferenceNode(node)) {
    const name = node.typeName.getText(ast)
    if (name === 'Readonly') return schema(node.typeArguments[0])
    if (name === 'ReadonlyArray') return { type: 'array', items: schema(node.typeArguments[0]) }
    if (node.typeArguments?.length) throw new Error(`Unsupported generic: ${name}`)
    if (registry.types[name]) return registry.types[name]
    if (!declarations.has(name)) throw new Error(`Unresolved type: ${name}`)
    return schema(declarations.get(name))
  }
  if (ts.isUnionTypeNode(node)) return { oneOf: node.types.map(schema) }
  if (ts.isIntersectionTypeNode(node)) {
    const parts = node.types.map(schema)
    if (!parts.every(s => s.type === 'object')) throw new Error('Non-object intersection')
    const properties = {}, required = []
    for (const part of parts) {
      for (const [name, value] of Object.entries(part.properties)) {
        if (Object.hasOwn(properties, name)) throw new Error(`Duplicate intersection member: ${name}`)
        properties[name] = value
      }
      required.push(...part.required)
    }
    return { type: 'object', properties, required, additionalProperties: false }
  }
  if (ts.isTypeLiteralNode(node)) {
    const properties = {}, required = []
    for (const member of node.members) {
      if (!ts.isPropertySignature(member)) throw new Error('Unsupported member')
      const name = member.name.getText(ast)
      properties[name] = schema(member.type)
      if (!member.questionToken) required.push(name)
    }
    return { type: 'object', properties, required, additionalProperties: false }
  }
  if (ts.isLiteralTypeNode(node)) {
    if (node.literal.kind === ts.SyntaxKind.NullKeyword) return { type: 'null' }
    if (node.literal.kind === ts.SyntaxKind.TrueKeyword) return { const: true }
    if (node.literal.kind === ts.SyntaxKind.FalseKeyword) return { const: false }
    if (ts.isStringLiteral(node.literal)) return { const: node.literal.text }
    if (ts.isNumericLiteral(node.literal)) return { const: Number(node.literal.text) }
  }
  throw new Error(`Unsupported type: ${node.getText(ast)}`)
}
const names = ['EnvironmentKey', 'QapRef', 'QapEvidence', 'QapUom', 'QapRange', 'ExecRef', 'ExecEvidence', 'ExecRange', 'ExecScope', 'ExecQuantity', 'ExecQaSource', 'OutRef', 'OutEvidence', 'OutFlatRef', 'OutRange', 'OutQualitySource', ...Object.values(canonical.payloadTypeRegistry).map(p => p.schemaType), 'R4FrozenPayload', 'R4QapFullCountPlanContent']
const schemas = Object.fromEntries(names.map(n => [n, registry.types[n] ?? schema(declarations.get(n))]))
const keywords = new Set(['type', 'properties', 'required', 'additionalProperties', 'oneOf', 'items', 'minItems', 'maxItems', 'minLength', 'maxLength', 'pattern', 'format', 'const', 'enum', '$id', '$schema'])
function audit(node) {
  for (const key of Object.keys(node)) if (!keywords.has(key)) throw new Error(`Unsupported schema keyword: ${key}`)
  if (node.format && !['uuid', 'date-time'].includes(node.format)) throw new Error(`Unsupported format: ${node.format}`)
  if (node.additionalProperties !== undefined && typeof node.additionalProperties !== 'boolean') throw new Error('Schema-valued additionalProperties unsupported')
  for (const child of Object.values(node.properties ?? {})) audit(child)
  for (const child of node.oneOf ?? []) audit(child)
  if (node.items) audit(node.items)
}
Object.values(schemas).forEach(audit)
const routes = Object.fromEntries(Object.entries(canonical.payloadTypeRegistry).map(([event, p]) => [event, p.schemaType]))
const generated = {
  'cp6.web/src/contracts/quality/ipqc.g.ts': '// Generated by eng/contracts/s01-07/ts/generate.mjs from composed-19; exact source and patches in sources.json.\n' + source.replace(/\r\n/g, '\n'),
  'cp6.web/src/contracts/quality/ipqcSchemas.g.ts': '// Generated; native roots preserve registry predicates. R4 roots adapt exact composed TypeScript declarations.\nimport type * as Native from "./ipqc.g"\nimport type { Schema } from "./schema"\n\nexport interface IpqcContracts {\n' + names.map(n => `  ${n}: Native.${n}\n`).join('') + '}\n\nexport const ipqcSchemas: Readonly<Record<keyof IpqcContracts, Schema>> = ' + JSON.stringify(schemas, null, 2) + '\n\nexport const payloadRoutes = ' + JSON.stringify(routes, null, 2) + ' as const\n',
}
const report = {
  version: 'CP6-S01-07-IPQC-GENERATION/1',
  command: 'node eng/contracts/s01-07/ts/generate.mjs',
  input_manifest: base + 'sources.json',
  declarations: declarations.size,
  roots: names.map(name => ({ name, source: registry.types[name] ? 'native-registry' : 'composed-19', selector: registry.types[name] ? `/types/${name}` : `typescript_declaration:${name}` })),
  payload_routes: routes,
  semantics: 'Native registry constraints and closed members; R4 types use declared fields plus referenced native scalar/Ref predicates. No Owner/auth/currentness/DB checks.',
  outputs: Object.entries(generated).map(([name, body]) => ({ path: name, sha256: hash(body) })),
}
generated[base + 'ts/generation.json'] = JSON.stringify(report, null, 2) + '\n'
for (const [name, body] of Object.entries(generated)) {
  const dest = path.join(root, name)
  if (process.argv.includes('--check')) {
    if (readFileSync(dest, 'utf8') !== body) throw new Error(`Generation drift: ${name}`)
  } else { mkdirSync(path.dirname(dest), { recursive: true }); writeFileSync(dest, body) }
}
console.log(`PASS: ${names.length} native/R4 roots, ${Object.keys(routes).length} event routes, ${declarations.size} source type aliases`)
