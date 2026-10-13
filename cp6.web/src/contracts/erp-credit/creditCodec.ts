/** Bounded full-native ERP-CREDIT codecs. A decoded wire never proves Owner or Store authority. */
export const WMS_SCHEMA = 'ERP-CREDIT-01_WMS-RMA-R3-EXACT.schema.json'
export const FIN_AR_SCHEMA = 'ERP-CREDIT-01_FIN-AR-R2-EXACT.schema.json'
export const REGISTRY_FILE = 'ERP-CREDIT-01_TYPE-REGISTRY.json'
const CURRENT_NS = 'Cp6.ErpCredit.CurrentV1.Generated', FROZEN_NS = 'Cp6.ErpCredit.Generated'
const CURRENT_PROFILE = 'erp-credit-current-v1', FROZEN_PROFILE = 'erp-credit-frozen-r8'
const WMS_HASH = 'b324a9b62d888f268a514de4266ecff70783e80d30c39eb5b82e7fffaf62af3a'
const FIN_AR_HASH = '8a7864b91e2bd19f9102abcb9eea756d7f6df33ad84e7b852f24a9bd669c56ca'
const CURRENT_HASH = '564706ce0247d84dc8056002d540f17eccdcab7c5b5e4b123c09121ad407fbda'
const FROZEN_HASH = '62effa1e0542784a5fa936d11961465db4da522ed4d28fc9c9bb5351cf40b6f1'
export type CreditWireIdentity = Readonly<{ profileId: string, codecNamespace: string, schemaFile: string, schemaSha256: string, registrySha256: string, root: string }>
export type NativeCreditDocument = Readonly<{ identity: CreditWireIdentity, sha256: string, value: unknown, originalUtf8(): Uint8Array }>
export type NativeCreditCodec = Readonly<{ identity: CreditWireIdentity, parse(raw: Uint8Array): Promise<NativeCreditDocument> }>
export class CreditWireError extends Error { constructor(readonly code: string) { super(code) } }
function requireValue(ok: unknown, code: string): asserts ok { if (!ok) throw new CreditWireError(code) }
const own = (value: object, key: string) => Object.prototype.hasOwnProperty.call(value, key)
// Buffer is a Uint8Array subclass whose slice() aliases its input. Own bytes at every boundary.
const copyBytes = (raw: Uint8Array): Uint8Array<ArrayBuffer> => new Uint8Array(raw)
export const sha256 = async (raw: Uint8Array): Promise<string> => [...new Uint8Array(await crypto.subtle.digest('SHA-256', copyBytes(raw)))].map(v => v.toString(16).padStart(2, '0')).join('')
export const acceptsRegistry = (profile: string, ns: string, file: string, hash: string): boolean => file === REGISTRY_FILE && ((profile === CURRENT_PROFILE && ns === CURRENT_NS && hash === CURRENT_HASH) || (profile === FROZEN_PROFILE && ns === FROZEN_NS && hash === FROZEN_HASH))
const roots = Object.freeze({
  wmsCreditOwnerRequest: 'CreditOwnerRequest', wmsCreditOwnerQueryRequest: 'CreditOwnerQueryRequest',
  wmsCreditOwnerQueryResult: 'CreditOwnerQueryResult', wmsCommercialOutcomeRef: 'CommercialOutcomeRef',
  finArCreditAuthorityReadRequest: 'CreditAuthorityReadRequest', finArCreditAuthorityReadResult: 'CreditAuthorityReadResult',
  finArCreditSelectionsQuery: 'CreditSelectionsQuery', finArCreditSelectionPage: 'CreditSelectionPage',
  finArCreditPrepare: 'CreditPrepare', finArPostInput: 'PostInput', finArSafeOperationDetail: 'SafeOperationDetail',
  finArCommandResult: 'CommandResult', terminalCommandReceipt: 'TerminalCommandReceipt', finArObservation: 'FinArObservation',
})
const wmsRoots = new Set(['CreditOwnerRequest', 'CreditOwnerQueryRequest', 'CreditOwnerQueryResult', 'CommercialOutcomeRef', 'Money'])
const arRoots = new Set(['CreditAuthorityReadRequest', 'CreditAuthorityReadResult', 'CreditSelectionsQuery', 'CreditSelectionPage', 'CreditPrepare', 'PostInput', 'SafeOperationDetail', 'CommandResult', 'D8'])
function identityFor(current: boolean, root: string, replay = false): CreditWireIdentity {
  const registry = current ? CURRENT_HASH : FROZEN_HASH
  const file = wmsRoots.has(root) ? WMS_SCHEMA : arRoots.has(root) ? FIN_AR_SCHEMA : ['TerminalCommandReceipt', 'FinArObservation'].includes(root) || (replay && ['EC_C01_COMMITTED_CommercialDraftView', 'EC_C02_COMMITTED_CommercialDraftView'].includes(root)) ? REGISTRY_FILE : undefined
  requireValue(file, 'UNPUBLISHED_NATIVE_ROOT')
  return Object.freeze({ profileId: current ? CURRENT_PROFILE : FROZEN_PROFILE, codecNamespace: current ? CURRENT_NS : FROZEN_NS, schemaFile: file, schemaSha256: file === WMS_SCHEMA ? WMS_HASH : file === FIN_AR_SCHEMA ? FIN_AR_HASH : registry, registrySha256: registry, root })
}
function requireIdentity(identity: CreditWireIdentity, current: boolean): void {
  const expected = identityFor(current, identity.root)
  requireValue(Object.keys(expected).every(k => identity[k as keyof CreditWireIdentity] === expected[k as keyof CreditWireIdentity]), 'WIRE_IDENTITY')
}

// Numeric tokens remain exact through all validation/equality operations. Valid native numeric
// fields are bounded integers <= 86400; materialization to JS numbers happens only after validation.
class JsonNumber { constructor(readonly raw: string) {} }
type Json = null | boolean | string | JsonNumber | Json[] | { [key: string]: Json }
function numberParts(raw: string): { digits: string, exponent: bigint, negative: boolean } {
  const [mantissa = '', exponent = '0'] = raw.toLowerCase().split('e')
  const fraction = mantissa.includes('.') ? mantissa.length - mantissa.indexOf('.') - 1 : 0
  const digits = mantissa.replace('-', '').replace('.', '').replace(/^0+/, '')
  if (!digits) return { digits: '0', exponent: 0n, negative: false }
  const trimmed = digits.replace(/0+$/, '')
  return { digits: trimmed, exponent: BigInt(exponent) - BigInt(fraction) + BigInt(digits.length - trimmed.length), negative: mantissa.startsWith('-') }
}
function exactInteger(raw: string): bigint | null {
  const p = numberParts(raw)
  if (p.digits === '0') return 0n
  if (p.exponent < 0n || p.exponent + BigInt(p.digits.length) > 19n) return null
  return (p.negative ? -1n : 1n) * BigInt(p.digits) * 10n ** p.exponent
}
function parseStrict(raw: Uint8Array): Json {
  // ignoreBOM=true retains a BOM so it is rejected as non-JSON, matching the C# strict boundary.
  const text = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(raw)
  let index = 0
  const whitespace = () => { while (index < text.length && /[\u0020\t\r\n]/.test(text[index]!)) index++ }
  const string = (): string => {
    const start = index++
    let escaped = false
    while (index < text.length) {
      const character = text[index++]!
      if (!escaped && character === '"') return JSON.parse(text.slice(start, index)) as string
      if (!escaped && character === '\\') escaped = true
      else escaped = false
    }
    throw new CreditWireError('JSON_STRING')
  }
  const value = (): Json => {
    whitespace(); const ch = text[index]
    if (ch === '"') return string()
    if (ch === '{') {
      index++; const result: { [key: string]: Json } = Object.create(null), seen = new Set<string>()
      whitespace(); if (text[index] === '}') { index++; return result }
      while (true) {
        whitespace(); requireValue(text[index] === '"', 'JSON_PROPERTY'); const name = string()
        requireValue(!seen.has(name), 'DUPLICATE_PROPERTY_NAME'); seen.add(name)
        whitespace(); requireValue(text[index++] === ':', 'JSON_COLON'); result[name] = value(); whitespace()
        if (text[index] === '}') { index++; return result }
        requireValue(text[index++] === ',', 'JSON_COMMA')
      }
    }
    if (ch === '[') {
      index++; const result: Json[] = []; whitespace()
      if (text[index] === ']') { index++; return result }
      while (true) { result.push(value()); whitespace(); if (text[index] === ']') { index++; return result }; requireValue(text[index++] === ',', 'JSON_COMMA') }
    }
    for (const [literal, result] of [['true', true], ['false', false], ['null', null]] as const) if (text.startsWith(literal, index)) { index += literal.length; return result }
    const token = text.slice(index).match(/^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?/)
    requireValue(token, 'JSON_VALUE'); index += token[0].length; return new JsonNumber(token[0])
  }
  const result = value(); whitespace(); requireValue(index === text.length, 'JSON_TRAILING'); return result
}
function semanticKey(value: any): string {
  if (value instanceof JsonNumber) { const p = numberParts(value.raw); return `N${p.negative ? '-' : ''}${p.digits}E${p.exponent}` }
  if (value === null) return 'Z'
  if (typeof value !== 'object') return typeof value + JSON.stringify(value)
  if (Array.isArray(value)) return 'A' + JSON.stringify(value.map(semanticKey))
  return 'O' + JSON.stringify(Object.keys(value).sort().map(k => [k, semanticKey(value[k])]))
}
const same = (left: unknown, right: unknown): boolean => semanticKey(left) === semanticKey(right)
function materialize(value: Json): any {
  if (value instanceof JsonNumber) { const integer = exactInteger(value.raw); requireValue(integer !== null && integer <= BigInt(Number.MAX_SAFE_INTEGER) && integer >= BigInt(Number.MIN_SAFE_INTEGER), 'UNSAFE_NUMBER'); return Number(integer) }
  if (value === null || typeof value !== 'object') return value
  if (Array.isArray(value)) return Object.freeze(value.map(materialize))
  const object: Record<string, unknown> = Object.create(null)
  for (const key of Object.keys(value)) object[key] = materialize(value[key]!)
  return Object.freeze(object)
}
// Schemas themselves contain small integer annotation bounds. Strict parsing precedes conversion.
type Schema = { [key: string]: any }
function schemaPlain(value: Json): any {
  if (value instanceof JsonNumber) return Number(value.raw)
  if (value === null || typeof value !== 'object') return value
  if (Array.isArray(value)) return value.map(schemaPlain)
  const object: Schema = Object.create(null)
  for (const key of Object.keys(value)) object[key] = schemaPlain(value[key]!)
  return object
}
function decodeBase64(value: string): Uint8Array {
  const match = value.match(/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/)
  requireValue(match && match[0].length === value.length, 'BASE64')
  return Uint8Array.from(atob(value), c => c.charCodeAt(0))
}
function validDate(text: string): boolean {
  const m = text.match(/^(\d{4})-(\d{2})-(\d{2})$/)
  if (!m) return false
  const year = Number(m[1]), month = Number(m[2]), day = Number(m[3])
  const date = new Date(0); date.setUTCFullYear(year, month - 1, day)
  return year >= 1 && date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
}
function validateFormat(text: string, format: string): void {
  if (format === 'uuid') requireValue(/^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/.test(text), 'UUID')
  if (format === 'date') requireValue(validDate(text), 'DATE')
  if (format !== 'date-time') return
  const m = text.match(/^(\d{4}-\d{2}-\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d+)?(Z|[+-](\d{2}):(\d{2}))$/)
  requireValue(m && m[0].length === text.length && validDate(m[1]!) && Number(m[2]) <= 23 && Number(m[3]) <= 59 && Number(m[4]) <= 59 && (m[5] === 'Z' || (Number(m[6]) <= 23 && Number(m[7]) <= 59)), 'DATE_TIME')
}

class NativeValidator {
  constructor(private readonly docs: Record<string, Schema>, private readonly current: boolean) {}
  async root(file: string, root: string, value: Json): Promise<void> {
    this.validate(value, this.docs[file]!.$defs[root], file)
    semantic(root, value, this.current)
    // Embedded exact raw/digest verification belongs to the root; this root is not nested by another published codec.
    if (root === 'FinArObservation' && (value as any).state === 'FAILED_NO_EFFECT') {
      const v = value as any, exact = decodeBase64(v.exactCommandResultCanonicalBase64)
      requireValue(await sha256(exact) === v.exactCommandResultDigest, 'EXACT_COMMAND_RESULT_DIGEST')
      requireValue(same(parseStrict(exact), v.exactCommandResult), 'EXACT_COMMAND_RESULT_PROJECTION')
    }
  }
  private matches(value: Json, schema: Schema, file: string): boolean {
    try { this.validate(value, schema, file); return true } catch (error) { if (error instanceof CreditWireError) return false; throw error }
  }
  private validate(value: any, schema: Schema, file: string): void {
    if (typeof schema === 'boolean') { requireValue(schema, 'FALSE_SCHEMA'); return }
    if (schema.$ref) {
      const [f, pointer = ''] = (schema.$ref as string).split('#'), next = (f || file).replace(/^\.\//, '')
      requireValue(own(this.docs, next), 'SCHEMA_REF'); let target: any = this.docs[next]
      for (const part of pointer.split('/').filter(Boolean)) { const key = part.replaceAll('~1', '/').replaceAll('~0', '~'); requireValue(own(target, key), 'SCHEMA_REF'); target = target[key] }
      this.validate(value, target, next)
      if (pointer.startsWith('/$defs/')) semantic(pointer.slice(7), value, this.current)
    }
    if (schema.oneOf) requireValue(schema.oneOf.filter((s: Schema) => this.matches(value, s, file)).length === 1, 'ONE_OF')
    if (schema.allOf) for (const s of schema.allOf) this.validate(value, s, file)
    if (schema.if) { const branch = this.matches(value, schema.if, file) ? schema.then : schema.else; if (branch) this.validate(value, branch, file) }
    if (own(schema, 'const')) requireValue(same(value, schema.const), 'CONST')
    if (schema.enum) requireValue(schema.enum.some((v: unknown) => same(value, v)), 'ENUM')
    if (schema.type) {
      const kind = schema.type
      requireValue(kind === 'integer' ? value instanceof JsonNumber && exactInteger(value.raw) !== null : kind === 'null' ? value === null : kind === 'array' ? Array.isArray(value) : kind === 'object' ? value !== null && typeof value === 'object' && !Array.isArray(value) && !(value instanceof JsonNumber) : typeof value === kind, 'TYPE')
    }
    if (value instanceof JsonNumber) {
      const integer = exactInteger(value.raw)
      if (schema.minimum !== undefined) requireValue(integer !== null && integer >= BigInt(schema.minimum), 'MINIMUM')
      if (schema.maximum !== undefined) requireValue(integer !== null && integer <= BigInt(schema.maximum), 'MAXIMUM')
    }
    if (typeof value === 'string') {
      // Reject unpaired surrogate values like JsonElement.GetString; escaped-name duplicates are checked earlier.
      requireValue(!/[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/u.test(value), 'UNICODE_SCALAR')
      if (schema.minLength !== undefined) requireValue([...value].length >= schema.minLength, 'MIN_LENGTH')
      if (schema.maxLength !== undefined) requireValue([...value].length <= schema.maxLength, 'MAX_LENGTH')
      if (schema.pattern) { const m = value.match(new RegExp(schema.pattern, 'u')); requireValue(m && (!schema.pattern.endsWith('$') || m.index! + m[0].length === value.length), 'PATTERN') }
      if (schema['x-cp6-normalization']) requireValue(value.normalize('NFC') === value, 'NFC')
      if (schema.contentEncoding === 'base64') decodeBase64(value)
      if (schema.format) validateFormat(value, schema.format)
    }
    if (Array.isArray(value)) {
      if (schema.minItems !== undefined) requireValue(value.length >= schema.minItems, 'MIN_ITEMS')
      if (schema.maxItems !== undefined) requireValue(value.length <= schema.maxItems, 'MAX_ITEMS')
      if (schema.uniqueItems) requireValue(new Set(value.map(semanticKey)).size === value.length, 'UNIQUE_ITEMS')
      if (schema.items) for (const item of value) this.validate(item, schema.items, file)
      if (schema.contains) requireValue(value.some(item => this.matches(item, schema.contains, file)), 'CONTAINS')
    } else if (value !== null && typeof value === 'object' && !(value instanceof JsonNumber)) {
      for (const name of schema.required || []) requireValue(own(value, name), 'REQUIRED')
      for (const name of Object.keys(value)) {
        if (schema.properties && own(schema.properties, name)) this.validate(value[name], schema.properties[name], file)
        else if (schema.additionalProperties === false) throw new CreditWireError('UNKNOWN_MEMBER')
        else if (typeof schema.additionalProperties === 'object') this.validate(value[name], schema.additionalProperties, file)
      }
    }
  }
}

type Decimal = { coefficient: bigint, scale: number }
const decimal = (text: string): Decimal => ({ coefficient: BigInt(text.replace('.', '')), scale: text.includes('.') ? text.length - text.indexOf('.') - 1 : 0 })
const zero = (): Decimal => ({ coefficient: 0n, scale: 0 })
function align(a: Decimal, b: Decimal): [bigint, bigint, number] { const scale = Math.max(a.scale, b.scale); return [a.coefficient * 10n ** BigInt(scale - a.scale), b.coefficient * 10n ** BigInt(scale - b.scale), scale] }
const compare = (a: Decimal, b: Decimal) => { const [x, y] = align(a, b); return x < y ? -1 : x > y ? 1 : 0 }
const add = (a: Decimal, b: Decimal): Decimal => { const [x, y, scale] = align(a, b); return { coefficient: x + y, scale } }
const subtract = (a: Decimal, b: Decimal): Decimal => { const [x, y, scale] = align(a, b); return { coefficient: x - y, scale } }
function measure(ranges: any[]): Decimal { return ranges.reduce((sum, r) => { const from = decimal(r.from), to = decimal(r.to); requireValue(compare(to, from) > 0, 'RANGE'); return add(sum, subtract(to, from)) }, zero()) }
function semantic(root: string, v: any, current: boolean): void {
  if (root === 'Range') requireValue(compare(decimal(v.from), decimal(v.to)) < 0, 'RANGE')
  if (root === 'Slice') requireValue(compare(decimal(v.from), decimal(v.toExclusive)) < 0, 'SLICE')
  if (root === 'CreditOwnerRequest') requireValue(compare(measure(v.eligibleRanges), decimal(v.eligibleQuantity.value)) === 0, 'RANGE_MEASURE')
  if (root === 'InvoiceAllocation') {
    requireValue(compare(measure(v.coveredRanges), decimal(v.quantity.value)) === 0, 'RANGE_MEASURE')
    requireValue(compare(add(decimal(v.net.value), decimal(v.tax.value)), decimal(v.gross.value)) === 0 && v.net.currency === v.tax.currency && v.net.currency === v.gross.currency, 'MONEY_INVARIANT')
  }
  if (root === 'CommercialOutcomeComponent' && v.coveredRanges) requireValue(compare(measure(v.coveredRanges), decimal(v.quantity.value)) === 0, 'RANGE_MEASURE')
  if (root === 'CommercialOutcomeRef') {
    const components: any[] = v.components, singleton = components.filter(c => c.kind === 'RETURN_ONLY' || c.kind === 'REJECTED')
    requireValue(singleton.length === 0 || (singleton.length === 1 && components.length === 1), 'SINGLETON_BUNDLE')
    const credits = new Map<string, any>()
    for (const c of components.filter(c => c.kind === 'CREDIT')) {
      const key = semanticKey(c.componentRef); requireValue(!credits.has(key), 'DUPLICATE_CREDIT_REF'); credits.set(key, c)
      const approved = c.invoiceAllocations.reduce((sum: Decimal, a: any) => add(sum, decimal(a.quantity.value)), zero())
      requireValue(compare(approved, decimal(c.approvedQuantity.value)) === 0, 'APPROVED_QUANTITY')
    }
    for (const c of components.filter(c => c.kind === 'REFUND_AUTHORIZATION')) {
      const credit = credits.get(semanticKey(c.creditComponentRef)); requireValue(credit, 'REFUND_CREDIT_LINK')
      let cap = zero()
      for (const a of credit.invoiceAllocations) { requireValue(a.gross.currency === c.authorizedAmount.currency && c.currency === c.authorizedAmount.currency, 'CURRENCY_MISMATCH'); cap = add(cap, decimal(a.gross.value)) }
      requireValue(compare(decimal(c.authorizedAmount.value), cap) <= 0, 'REFUND_CAP')
    }
    const claimed: { from: Decimal, to: Decimal }[] = []
    for (const c of components) for (const group of c.kind === 'CREDIT' ? c.invoiceAllocations : c.coveredRanges ? [c] : []) for (const range of group.coveredRanges) {
      const from = decimal(range.from), to = decimal(range.to)
      requireValue(!claimed.some(p => compare(from, p.to) < 0 && compare(p.from, to) < 0), 'RANGE_OVERLAP'); claimed.push({ from, to })
    }
  }
  if (root === 'CreditAuthorityBody') requireValue(compare(add(decimal(v.maxNet), decimal(v.maxTax)), decimal(v.maxGross)) === 0, 'MONEY_INVARIANT')
  if (root === 'CommandResult' && v.kind === 'COMPLETED') {
    requireValue(v.outcome !== 'APPLIED' || v.resultRef !== null, 'OWNER_RESULT_REQUIRED')
    requireValue(!(current ? v.outcome === 'REJECTED' : v.outcome !== 'APPLIED') || v.resultRef === null, 'OWNER_RESULT_FORBIDDEN')
  }
  if (root === 'SafeOperationDetail') requireValue(exactInteger(v.expectedMemberCount.raw) === BigInt(v.members.length), 'MEMBER_COUNT')
  if (root === 'FinArObservation') {
    if (v.state === 'FOUND') requireValue(same(v.exactCommandResult.resultRef, v.ownerResultRef), 'OWNER_RESULT_PROJECTION')
    if (v.state === 'PENDING') requireValue(same(v.exactCommandResult.operationId, v.operationId) && v.exactCommandResult.queryRef === v.queryRef, 'PENDING_OWNER_RESULT_PROJECTION')
  }
}

// Constructor capability is module-private, and no validated mutable schema table is exposed.
const constructionKey = Symbol('verified-schema-bundle')
export class CreditCodecSet {
  readonly codecNamespace: string
  readonly #current: boolean
  readonly #validator: NativeValidator
  constructor(key: symbol, current: boolean, docs: Record<string, Schema>) { requireValue(key === constructionKey, 'VERIFIED_FACTORY_REQUIRED'); this.#current = current; this.codecNamespace = current ? CURRENT_NS : FROZEN_NS; this.#validator = new NativeValidator(docs, current) }
  identityFor(root: string): CreditWireIdentity { return identityFor(this.#current, root) }
  async decode(identity: CreditWireIdentity, raw: Uint8Array): Promise<NativeCreditDocument> { requireIdentity(identity, this.#current); return this.decodeCore(identity, raw) }
  private async decodeCore(identity: CreditWireIdentity, raw: Uint8Array): Promise<NativeCreditDocument> {
    const stable = copyBytes(raw), snapshotIdentity = Object.freeze({ ...identity }), parsed = parseStrict(stable)
    await this.#validator.root(snapshotIdentity.schemaFile, snapshotIdentity.root, parsed)
    return Object.freeze({ identity: snapshotIdentity, sha256: await sha256(stable), value: materialize(parsed), originalUtf8: () => copyBytes(stable) })
  }
  private codec(root: string): NativeCreditCodec { return Object.freeze({ identity: this.identityFor(root), parse: (raw: Uint8Array) => this.decode(this.identityFor(root), raw) }) }
  get publishedCodecs(): Readonly<Record<keyof typeof roots, NativeCreditCodec>> { return Object.freeze(Object.fromEntries(Object.entries(roots).map(([name, root]) => [name, this.codec(root)]))) as Readonly<Record<keyof typeof roots, NativeCreditCodec>> }
  get finArPostInput(): NativeCreditCodec { return this.codec('PostInput') }
  parseWmsMoney(raw: Uint8Array): Promise<NativeCreditDocument> { return this.codec('Money').parse(raw) }
  parseFinArD8(raw: Uint8Array): Promise<NativeCreditDocument> { return this.codec('D8').parse(raw) }
  /** Full saved response guard; separate from the 14 public native factories. */
  async captureReplay(operation: string, commandKey: string, request: Uint8Array, requestHash: string, status: number, response: Uint8Array, responseHash: string): Promise<FrozenHttpReplay> {
    requireValue(!this.#current && ['C01', 'C02'].includes(operation) && status === 201 && commandKey.length > 0, 'FROZEN_REPLAY_IDENTITY')
    const requestCopy = copyBytes(request), responseCopy = copyBytes(response)
    requireValue(await sha256(requestCopy) === requestHash && await sha256(responseCopy) === responseHash, 'FROZEN_REPLAY_DIGEST')
    parseStrict(requestCopy)
    const saved = await this.decodeCore(identityFor(false, `EC_${operation}_COMMITTED_CommercialDraftView`, true), responseCopy)
    return new FrozenHttpReplay(constructionKey, commandKey, requestCopy, saved)
  }
}
export type FrozenR8CreditCodec = CreditCodecSet
async function load(current: boolean, files: Readonly<Record<string, Uint8Array>>): Promise<CreditCodecSet> {
  const expected: Record<string, string> = { [WMS_SCHEMA]: WMS_HASH, [FIN_AR_SCHEMA]: FIN_AR_HASH, [REGISTRY_FILE]: current ? CURRENT_HASH : FROZEN_HASH }
  requireValue(Object.keys(files).length === 3 && Object.keys(files).every(k => own(expected, k)), 'SCHEMA_BUNDLE')
  const stable = Object.fromEntries(Object.keys(expected).map(k => [k, copyBytes(files[k]!)]))
  const docs: Record<string, Schema> = Object.create(null)
  for (const file of Object.keys(expected)) { const raw = stable[file]!; requireValue(await sha256(raw) === expected[file], 'SCHEMA_IDENTITY'); docs[file] = schemaPlain(parseStrict(raw)) }
  const result = new CreditCodecSet(constructionKey, current, docs)
  Object.freeze(result)
  return result
}
export const createCurrentCreditCodec = (files: Readonly<Record<string, Uint8Array>>) => load(true, files)
export const createFrozenR8CreditCodec = (files: Readonly<Record<string, Uint8Array>>) => load(false, files)
export async function captureOriginalBytes(identity: CreditWireIdentity, raw: Uint8Array, expectedSha256: string) {
  requireIdentity(identity, false); const stable = copyBytes(raw), snapshotIdentity = Object.freeze({ ...identity })
  const digest = await sha256(stable); requireValue(digest === expectedSha256, 'ORIGINAL_BYTES_DIGEST'); parseStrict(stable)
  return Object.freeze({ identity: snapshotIdentity, sha256: digest, nativeSchemaValidated: false as const, originalUtf8: () => copyBytes(stable) })
}
export class FrozenHttpReplay {
  readonly #request: Uint8Array
  readonly #response: NativeCreditDocument
  readonly #key: string
  readonly responseIdentity: CreditWireIdentity
  constructor(capability: symbol, commandKey: string, request: Uint8Array, response: NativeCreditDocument) { requireValue(capability === constructionKey, 'VERIFIED_FACTORY_REQUIRED'); this.#key = commandKey; this.#request = copyBytes(request); this.#response = response; this.responseIdentity = response.identity; Object.freeze(this) }
  async replay(commandKey: string, originalRequest: Uint8Array) {
    const stable = copyBytes(originalRequest)
    requireValue(commandKey === this.#key && stable.length === this.#request.length && stable.every((v, i) => v === this.#request[i]), 'COMMAND_PAYLOAD_CONFLICT')
    return Object.freeze({ statusCode: 200 as const, value: this.#response.value, bodyUtf8: () => this.#response.originalUtf8() })
  }
  originalRequestUtf8(): Uint8Array { return copyBytes(this.#request) }
}
/** No trusted Owner/issuer/Store producer ports exist in this bounded adoption. Self-asserted proof cannot change the fence. */
export const assessAdaptation = (original: NativeCreditDocument) => Object.freeze({ status: 'READ_ONLY_UNPROVEN' as const, nextAction: 'MANUAL_RECONCILIATION' as const, effectKnowledge: 'UNKNOWN' as const, canRelease: false as const, canDispatch: false as const, establishesFound: false as const, original })
