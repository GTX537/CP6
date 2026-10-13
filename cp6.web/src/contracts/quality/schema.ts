/** The bounded JSON Schema vocabulary present in the pinned IPQC native roots. */
export interface Schema {
  type?: string
  properties?: Record<string, Schema>
  required?: readonly string[]
  additionalProperties?: boolean
  oneOf?: readonly Schema[]
  items?: Schema
  minItems?: number
  maxItems?: number
  minLength?: number
  maxLength?: number
  pattern?: string
  format?: string
  const?: unknown
  enum?: readonly unknown[]
  $id?: string
  $schema?: string
}

const own = (value: object, name: string) => Object.hasOwn(value, name)
const patterns = new Map<string, RegExp>()
function matches(pattern: string, value: string): boolean {
  let regex = patterns.get(pattern)
  if (!regex) {
    // Preserve the native ECMAScript expression. StableId's separate absolute-anchor patch
    // is not an authorized revision of the NCR/IPQC patterns.
    regex = new RegExp(pattern, 'u')
    patterns.set(pattern, regex)
  }
  return regex.test(value)
}

function dateTime(value: string): boolean {
  const parts = /^(\d{4})-(\d{2})-(\d{2})[tT](\d{2}):(\d{2}):(\d{2})(?:\.\d+)?(?:[zZ]|([+-])(\d{2}):(\d{2}))(?![\s\S])/.exec(value)
  if (!parts) return false
  const year = Number(parts[1]), month = Number(parts[2]), day = Number(parts[3])
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0)
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
  const hour = Number(parts[4]), minute = Number(parts[5]), second = Number(parts[6])
  if (!(month >= 1 && month <= 12 && day >= 1 && day <= days[month - 1]! &&
    hour <= 23 && minute <= 59 && second <= 60 &&
    (!parts[7] || (Number(parts[8]) <= 23 && Number(parts[9]) <= 59)))) return false
  if (second < 60) return true
  // RFC3339 §5.7: leap-second positions are shifted by the offset, at a UTC month end.
  // This checks representable positions, not an external IERS announcement calendar.
  const offset = parts[7] ? (Number(parts[8]) * 60 + Number(parts[9])) * (parts[7] === '+' ? 1 : -1) : 0
  const utc = new Date(0)
  utc.setUTCFullYear(year, month - 1, day)
  utc.setUTCHours(hour, minute - offset, 59, 0)
  const followingDay = new Date(utc.getTime() + 86_400_000)
  return utc.getUTCHours() === 23 && utc.getUTCMinutes() === 59 && followingDay.getUTCDate() === 1
}

function equal(left: unknown, right: unknown): boolean {
  if (left === right) return true
  if (Array.isArray(left) && Array.isArray(right)) return left.length === right.length && left.every((v, i) => equal(v, right[i]))
  if (left && right && typeof left === 'object' && typeof right === 'object' && !Array.isArray(left) && !Array.isArray(right)) {
    const a = left as Record<string, unknown>, b = right as Record<string, unknown>
    return Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(k => own(b, k) && equal(a[k], b[k]))
  }
  return false
}

/** Only the vocabulary verified by the generator is consumed; no permissive unknown-schema fallback. */
export function assertSchema(schema: Schema, value: unknown, path = '$'): void {
  const fail = (reason: string): never => { throw new TypeError(`${path}: ${reason}`) }
  if (own(schema, 'const') && !equal(schema.const, value)) fail('CONST')
  if (schema.enum && !schema.enum.some(candidate => equal(candidate, value))) fail('ENUM')
  if (schema.oneOf) {
    let count = 0
    for (const branch of schema.oneOf) {
      try { assertSchema(branch, value, path); count++ } catch (error) { if (!(error instanceof TypeError)) throw error }
    }
    if (count !== 1) fail('ONE_OF')
  }
  switch (schema.type) {
    case undefined: break
    case 'null': if (value !== null) fail('NULL'); break
    case 'string': if (typeof value !== 'string') fail('STRING'); break
    case 'boolean': if (typeof value !== 'boolean') fail('BOOLEAN'); break
    case 'integer': if (typeof value !== 'number' || !Number.isSafeInteger(value)) fail('INTEGER'); break
    case 'number': if (typeof value !== 'number' || !Number.isFinite(value)) fail('NUMBER'); break
    case 'array': if (!Array.isArray(value)) fail('ARRAY'); break
    case 'object': if (!value || typeof value !== 'object' || Array.isArray(value)) fail('OBJECT'); break
    default: fail('UNSUPPORTED_SCHEMA_TYPE')
  }
  if (typeof value === 'string') {
    const size = Array.from(value).length
    if (schema.minLength !== undefined && size < schema.minLength) fail('MIN_LENGTH')
    if (schema.maxLength !== undefined && size > schema.maxLength) fail('MAX_LENGTH')
    if (schema.pattern && !matches(schema.pattern, value)) fail('PATTERN')
    if (schema.format === 'uuid' && !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?![\s\S])/i.test(value)) fail('UUID')
    if (schema.format === 'date-time' && !dateTime(value)) fail('DATE_TIME')
    if (schema.format && !['uuid', 'date-time'].includes(schema.format)) fail('UNSUPPORTED_FORMAT')
  }
  if (Array.isArray(value)) {
    if (schema.minItems !== undefined && value.length < schema.minItems) fail('MIN_ITEMS')
    if (schema.maxItems !== undefined && value.length > schema.maxItems) fail('MAX_ITEMS')
    if (schema.items) value.forEach((item, index) => assertSchema(schema.items!, item, `${path}[${index}]`))
  } else if (value && typeof value === 'object') {
    const object = value as Record<string, unknown>
    for (const name of schema.required ?? []) if (!own(object, name)) fail(`REQUIRED ${name}`)
    for (const name of Object.keys(object)) {
      if (schema.properties && own(schema.properties, name)) assertSchema(schema.properties[name]!, object[name], `${path}.${name}`)
      else if (schema.additionalProperties === false) fail(`UNKNOWN ${name}`)
    }
  }
}
