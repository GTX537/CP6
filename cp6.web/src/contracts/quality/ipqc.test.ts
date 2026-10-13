import { describe, expect, it } from 'vitest'
import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { decodeIpqc, decodeIpqcPayload, encodeIpqc, type IpqcRoute } from './ipqc'
import type { PayloadType, R4PayloadSchemaType } from './ipqc.g'
import { ipqcSchemas, payloadRoutes, type IpqcContracts } from './ipqcSchemas.g'

const taskRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../../../eng/contracts/s01-07')
const read = (path: string) => readFileSync(resolve(taskRoot, path), 'utf8')
interface Publication { payloadType: PayloadType; schemaType: R4PayloadSchemaType; body: Record<string, unknown> }
const publicationBytes = read('inputs/protocol/PAYLOAD_PUBLICATIONS_RECOVERY_R4_SYSTEMIC.json')
const publications = (JSON.parse(publicationBytes) as { publications: Publication[] }).publications
const manual = (JSON.parse(read('ts/manual-fixtures.json')) as { fixtures: Partial<Record<IpqcRoute, Record<string, unknown>>> }).fixtures
const fixtures = { ...manual, ...Object.fromEntries(publications.map(p => [p.schemaType, p.body])) } as Record<IpqcRoute, Record<string, unknown>>
const copy = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T
const rootNames = Object.keys(fixtures) as IpqcRoute[]
const json = (value: unknown) => JSON.stringify(value)

it('pins the independent native publications and exact 23-root boundary', () => {
  expect(createHash('sha256').update(publicationBytes).digest('hex')).toBe('27edb910bb416a4ffe2d6f489e70f17923e64803975e5354cdac6c0d6a7f2020')
  expect(publications).toHaveLength(5)
  expect(rootNames).toHaveLength(23)
  expect(rootNames.sort()).toEqual(Object.keys(ipqcSchemas).sort())
})

describe.each(rootNames)('complete native root %s', route => {
  it('accepts and roundtrips every field', () => {
    const value = decodeIpqc(route, json(fixtures[route]))
    expect(value).toEqual(fixtures[route])
    expect(JSON.parse(encodeIpqc(route, value))).toEqual(fixtures[route])
  })
  it('rejects each missing required field', () => {
    const required = ipqcSchemas[route].required!
    expect(required.length).toBeGreaterThan(0)
    for (const name of required) {
      const bad = copy(fixtures[route]); delete bad[name]
      expect(() => decodeIpqc(route, json(bad)), `${route}.${name}`).toThrow()
    }
  })
  it('rejects unknown fields and a wrong root kind', () => {
    expect(() => decodeIpqc(route, json({ ...fixtures[route], unexpected: null }))).toThrow()
    expect(() => decodeIpqc(route, '[]')).toThrow()
    expect(() => decodeIpqc(route, 'null')).toThrow()
  })
})

describe('five exact native payload identities', () => {
  for (const original of publications) {
    it(`accepts original ${original.payloadType}`, () => {
      expect(payloadRoutes[original.payloadType]).toBe(original.schemaType)
      expect(decodeIpqcPayload(original.payloadType, original.schemaType, json(original.body))).toEqual(original.body)
    })
    for (const foreign of publications.filter(p => p.payloadType !== original.payloadType)) {
      it(`rejects ${foreign.schemaType} sent as ${original.payloadType}`, () => {
        expect(() => decodeIpqcPayload(original.payloadType, original.schemaType, json(foreign.body))).toThrow()
        expect(() => decodeIpqcPayload(original.payloadType, foreign.schemaType, json(original.body))).toThrow()
      })
    }
  }
  it('rejects unknown event or schema version and crossed frozen metadata', () => {
    const original = publications[0]!
    expect(() => decodeIpqcPayload('MES_OUTPUT_QUALITY_TASK_RESULT_V2' as PayloadType, original.schemaType, json(original.body))).toThrow()
    expect(() => decodeIpqc('R4FrozenPayload', json({ ...fixtures.R4FrozenPayload, schemaType: 'OutQualityDisposition' }))).toThrow()
    expect(() => decodeIpqc('ExecQualityDisposition', json({ ...fixtures.ExecQualityDisposition, schema: 'EXEC-QA-DISPOSITION-PROPOSED-2' }))).toThrow()
  })
})

interface FrozenVector { id: string; input: { supplied_type: string }; applicable_fields: string[]; expected: { accepted: boolean } }
const vectorBytes = read('inputs/pins/vectors.json')
const vectors = (JSON.parse(vectorBytes) as { cases: FrozenVector[] }).cases.filter(v => v.id.startsWith('IPQC-TYPE-'))
describe('six immutable type bindings, exercised inside complete native objects', () => {
  it('pins the exact original IDs and expected bytes', () => {
    expect(createHash('sha256').update(vectorBytes).digest('hex')).toBe('b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01')
    expect(vectors.map(v => v.id)).toEqual(Array.from({ length: 6 }, (_, i) => `IPQC-TYPE-00${i + 1}`))
  })
  for (const vector of vectors) it(vector.id, () => {
    const field = vector.applicable_fields[0]!
    const route = field.startsWith('R4FrozenPayload') ? 'R4FrozenPayload' : 'R4QapFullCountPlanContent'
    const body = copy(fixtures[route])
    const value = vector.input.supplied_type === 'Uom' ? { code: 'kg', version: '1' } : fixtures[vector.input.supplied_type as IpqcRoute]
    if (route === 'R4FrozenPayload') body.registrationRef = value
    else {
      const characteristic = (body.characteristics as Record<string, unknown>[])[0]!
      if (field.endsWith('measurementUnitRef')) characteristic.measurementUnitRef = value
      else (characteristic.rule as Record<string, unknown>).unitRef = value
    }
    if (vector.expected.accepted) expect(decodeIpqc(route, json(body))).toEqual(body)
    else expect(() => decodeIpqc(route, json(body))).toThrow()
  })
})

describe('IPQC native decoder boundary', () => {
  it('rejects a foreign owner on a QAP unit', () => {
    expect(() => decodeIpqc('QapUom', '{"id":"kg","version":"1","owner":"MES"}')).toThrow()
  })
  it('rejects escaped-equivalent duplicate member names', () => {
    expect(() => decodeIpqc('QapUom', '{"id":"kg","version":"1","\\u0076ersion":"2"}')).toThrow()
  })
  it('rejects an absent required member', () => {
    expect(() => decodeIpqc('QapUom', '{"id":"kg"}')).toThrow()
  })
  it.each(['0', '01', '+1', '-1', '1.0', '1e2', '', ' 1'])('native version %j', version => {
    const call = () => decodeIpqc('QapRef', json({ ...fixtures.QapRef, version }))
    if (version === '0') expect(call().version).toBe(version)
    else expect(call).toThrow()
  })
  it('does not cap arbitrary version strings or round exact quantities', () => {
    const version = '9'.repeat(200)
    expect(decodeIpqc('QapRef', json({ ...fixtures.QapRef, version })).version).toBe(version)
    const value = '9999999999999.99999999'
    expect(decodeIpqc('ExecQuantity', json({ ...fixtures.ExecQuantity, value })).value).toBe(value)
    for (const bad of ['10000000000000', '1.123456789', '01', '1e2', 1]) {
      expect(() => decodeIpqc('ExecQuantity', json({ ...fixtures.ExecQuantity, value: bad }))).toThrow()
    }
  })
  it.each(['\n', '\r', '\r\n', '\u2028', '\u2029'])('preserves native ECMAScript terminal-$ semantics %j', ending => {
    const version = '1' + ending
    // Without a multiline flag, the native JavaScript pattern rejects every trailing terminator.
    expect(new RegExp('^(0|[1-9][0-9]*)$', 'u').test(version)).toBe(false)
    expect(() => decodeIpqc('QapRef', json({ ...fixtures.QapRef, version }))).toThrow()
    expect(() => decodeIpqc('QapRef', json({ ...fixtures.QapRef, version: '1' + ending + '2' }))).toThrow()
  })
  it('counts Unicode code points and keeps native owner length', () => {
    const owner = '😀'.repeat(256)
    expect(decodeIpqc('QapEvidence', json({ ...fixtures.QapEvidence, owner })).owner).toBe(owner)
    expect(() => decodeIpqc('QapEvidence', json({ ...fixtures.QapEvidence, owner: owner + '😀' }))).toThrow()
  })
  it('keeps flat QAP evidence and nested Exec/Out evidence separate', () => {
    expect(() => decodeIpqc('QapEvidence', json(fixtures.ExecEvidence))).toThrow()
    expect(() => decodeIpqc('ExecEvidence', json(fixtures.QapEvidence))).toThrow()
    expect(() => decodeIpqc('OutEvidence', json(fixtures.OutFlatRef))).toThrow()
    expect(() => decodeIpqc('ExecScope', json({ ...fixtures.ExecScope, ranges: [fixtures.OutRange] }))).toThrow()
    expect(() => decodeIpqc('OutRange', json(fixtures.ExecRange))).toThrow()
    expect(() => decodeIpqc('ExecScope', json({ ...fixtures.ExecScope, ranges: [] }))).toThrow()
  })
  it('validates nested identities, fields, and real calendar dates', () => {
    const source = copy(fixtures.ExecQaSource)
    const ref = (source.actualManifestRef as { ref: Record<string, unknown> }).ref
    ref.version = 2
    expect(() => decodeIpqc('ExecQaSource', json(source))).toThrow()
    for (const frozenAt of ['2026-02-30T00:00:00Z', '2026-10-13', '2026-10-13T24:00:00Z', '2026-10-13T00:00:00Z\n', '1990-12-31T15:59:60Z', '1990-12-30T23:59:60Z']) {
      expect(() => decodeIpqc('R4FrozenPayload', json({ ...fixtures.R4FrozenPayload, frozenAt }))).toThrow()
    }
    expect(decodeIpqc('R4FrozenPayload', json({ ...fixtures.R4FrozenPayload, frozenAt: '2024-02-29t23:59:59.123+05:30' })).frozenAt).toContain('+05:30')
    expect(decodeIpqc('R4FrozenPayload', json({ ...fixtures.R4FrozenPayload, frozenAt: '1990-12-31T15:59:60-08:00' })).frozenAt).toContain(':60')
  })
  it('keeps legal nullable alternatives and still requires presence', () => {
    const source = { ...fixtures.OutQualitySource, previousRef: fixtures.OutEvidence, sampleBatchRef: fixtures.OutFlatRef }
    expect(decodeIpqc('OutQualitySource', json(source))).toEqual(source)
    const plan = { ...fixtures.R4QapFullCountPlanContent, previousRef: fixtures.QapEvidence }
    expect(decodeIpqc('R4QapFullCountPlanContent', json(plan))).toEqual(plan)
  })
  it.each(['0000-02-29T00:00:00Z', '0000-01-01T00:00:00+23:59', '1991-01-01T00:59:60+01:00', '2016-12-31T23:59:60.123456789Z'])('preserves native date-time text %s', frozenAt => {
    expect(decodeIpqc('R4FrozenPayload', json({ ...fixtures.R4FrozenPayload, frozenAt })).frozenAt).toBe(frozenAt)
  })
  it('rejects nested duplicates, malformed JSON, and prototype route names', () => {
    const text = json(fixtures.ExecEvidence).replace('"version":"2"', '"version":"2","\\u0076ersion":"3"')
    expect(() => decodeIpqc('ExecEvidence', text)).toThrow('DUPLICATE_PROPERTY_NAME')
    for (const bad of ['{} {}', '{"id":"a",}', '/*comment*/{}', '{"id":NaN}', '{"id":"\\x20"}']) {
      expect(() => decodeIpqc('QapUom', bad)).toThrow()
    }
    expect(() => decodeIpqc('toString' as IpqcRoute, '{}')).toThrow('UNKNOWN_IPQC_ROUTE')
    expect(() => decodeIpqc('QapUom', '{"id":"kg","version":"1","__proto__":{}}')).toThrow()
  })
  it('validates programmatic values on encode as well as decode', () => {
    expect(() => encodeIpqc('QapUom', { id: 'kg', version: 1 } as unknown as IpqcContracts['QapUom'])).toThrow()
    expect(() => encodeIpqc('R4FrozenPayload', { ...fixtures.R4FrozenPayload, registrationRef: null } as unknown as IpqcContracts['R4FrozenPayload'])).toThrow()
  })
})
