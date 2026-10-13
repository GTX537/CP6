import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { createCurrentCreditCodec, createFrozenR8CreditCodec, acceptsRegistry, captureOriginalBytes, assessAdaptation, sha256, WMS_SCHEMA, FIN_AR_SCHEMA, REGISTRY_FILE, type CreditCodecSet } from './creditCodec'

const repo = resolve(process.cwd(), '..')
const task = resolve(repo, 'eng/contracts/s01-11')
const read = (path: string) => new Uint8Array(readFileSync(path))
const json = (path: string) => JSON.parse(readFileSync(path, 'utf8'))
const bytes = (text: string) => new TextEncoder().encode(text)
const fixtures = json(resolve(task, 'fixtures/native-objects.json')).objects
const bundle = (profile: string) => ({
  [WMS_SCHEMA]: read(resolve(task, 'assets/native', WMS_SCHEMA)),
  [FIN_AR_SCHEMA]: read(resolve(task, 'assets/native', FIN_AR_SCHEMA)),
  [REGISTRY_FILE]: read(resolve(task, 'assets', profile, REGISTRY_FILE)),
})
const set = (profile: string) => profile === 'current' ? createCurrentCreditCodec(bundle(profile)) : createFrozenR8CreditCodec(bundle(profile))
const objectBytes = (root: string) => bytes(JSON.stringify(fixtures[root]))
type Case = { id: string, profile: string, root: string, raw: string, accepted: boolean }

describe('ERP-CREDIT native codecs', () => {
  for (const c of json(resolve(task, 'fixtures/cases.json')).cases as Case[]) {
    it(c.id, async () => {
      const codec = await set(c.profile)
      const decode = () => codec.decode(codec.identityFor(c.root), bytes(c.raw))
      if (!c.accepted) await expect(decode()).rejects.toThrow()
      else {
        const parsed = await decode()
        expect(parsed.originalUtf8()).toEqual(bytes(c.raw))
        expect(parsed.sha256).toBe(await sha256(bytes(c.raw)))
        expect(parsed.identity.root).toBe(c.root)
        expect(parsed.identity.codecNamespace).toBe(codec.codecNamespace)
      }
    })
  }
  for (const c of json(resolve(repo, 'eng/contracts/s01/vectors.json')).cases.filter((v: { id: string }) => /^(VERSION|BYTES)-/.test(v.id))) {
    it(c.id, async () => {
      const input = c.input, expected = c.expected
      if (c.id.startsWith('VERSION-')) expect(acceptsRegistry(c.profile_id, input.codec_namespace, input.schema_file, input.schema_sha256)).toBe(expected.accepted)
      else {
        const codec = await set('frozen'), identity = codec.identityFor(input.native_schema_root)
        const original = await captureOriginalBytes(identity, bytes(input.original_utf8), expected.original_sha256)
        const replay = await captureOriginalBytes(identity, bytes(input.replay_utf8), expected.replay_sha256)
        expect([...original.originalUtf8()].join(',') === [...replay.originalUtf8()].join(',')).toBe(expected.bytes_equal)
        expect(original.sha256 === replay.sha256).toBe(expected.digest_equal)
        expect(original.nativeSchemaValidated).toBe(false)
        await expect(codec.decode(identity, original.originalUtf8())).rejects.toThrow()
      }
    })
  }
  for (const profile of ['current', 'frozen']) {
    it(profile + ' exact routes, schema pins, immutable snapshots', async () => {
      const codec = await set(profile), identity = codec.identityFor('PostInput')
      expect(Object.keys(codec.publishedCodecs)).toHaveLength(14)
      expect(Object.values(codec.publishedCodecs).map(c => c.identity.root).sort()).toEqual(json(resolve(task, 'surface.json')).published_factories.map((f: { root: string }) => f.root).sort())
      const input = objectBytes('PostInput'), original = input.slice()
      const pending = codec.finArPostInput.parse(input)
      input[0] = 0
      const parsed = await pending
      expect(parsed.originalUtf8()).toEqual(original)
      const copy = parsed.originalUtf8(); copy[0] = 0
      expect(parsed.originalUtf8()).toEqual(original)
      expect(Object.isFrozen(parsed.value)).toBe(true)
      for (const change of [{ codecNamespace: 'unknown' }, { schemaSha256: '0'.repeat(64) }, { root: 'WholeC06PublicRequest' }, { root: 'EC_C06_200_StoredTerminal' }, { root: 'EC_C06_202_StoredAdmission' }]) {
        await expect(codec.decode({ ...identity, ...change }, original)).rejects.toThrow()
      }
      await expect(codec.decode(identity, new Uint8Array([34, 255, 34]))).rejects.toThrow()
      const wrong = bundle(profile); wrong[REGISTRY_FILE] = bundle(profile === 'current' ? 'frozen' : 'current')[REGISTRY_FILE]!
      const factory = profile === 'current' ? createCurrentCreditCodec : createFrozenR8CreditCodec
      await expect(factory(wrong)).rejects.toThrow()
      const changing = bundle(profile); const loading = factory(changing); changing[WMS_SCHEMA]![0] = 0
      const stable = await loading
      await expect(stable.finArPostInput.parse(original)).resolves.toBeDefined()
    })
  }
  for (const operation of ['C01', 'C02']) {
    it(operation + ' replays the complete original HTTP201 COMMITTED body as HTTP200', async () => {
      const codec = await createFrozenR8CreditCodec(bundle('frozen'))
      const request = bytes('{ "commandId": "fixture" }\r\n')
      const body = bytes(' ' + JSON.stringify(fixtures[`EC_${operation}_COMMITTED_CommercialDraftView`]) + '\r\n')
      const original = body.slice(), requestHash = await sha256(request), bodyHash = await sha256(body)
      const pending = codec.captureReplay(operation, 'scope/command', request, requestHash, 201, body, bodyHash)
      body[0] = 0
      const saved = await pending, replay = await saved.replay('scope/command', request)
      expect(replay.statusCode).toBe(200); expect(replay.bodyUtf8()).toEqual(original)
      expect((replay.value as Record<string, unknown>).resultKind).toBe('COMMITTED')
      const copy = replay.bodyUtf8(); copy[0] = 0
      expect((await saved.replay('scope/command', request)).bodyUtf8()).toEqual(original)
      await expect(saved.replay('scope/other', request)).rejects.toThrow()
      await expect(saved.replay('scope/command', bytes('{}'))).rejects.toThrow()
      const renamed = bytes(new TextDecoder().decode(original).replaceAll('COMMITTED', 'COMMITTED_REPLAY'))
      await expect(codec.captureReplay(operation, 'scope/command', request, requestHash, 201, renamed, await sha256(renamed))).rejects.toThrow()
      await expect(codec.captureReplay(operation, 'scope/command', request, requestHash, 200, original, bodyHash)).rejects.toThrow()
      await expect(codec.captureReplay(operation, 'scope/command', request, requestHash, 201, original, '0'.repeat(64))).rejects.toThrow()
    })
  }
  for (const root of ['CommandResult', 'FinArObservation']) {
    it(root + ' cannot authorize FOUND or release without trusted Owner integration', async () => {
      const codec: CreditCodecSet = await set('current'), parsed = await codec.decode(codec.identityFor(root), objectBytes(root))
      const assessment = assessAdaptation(parsed)
      expect(assessment.status).toBe('READ_ONLY_UNPROVEN'); expect(assessment.nextAction).toBe('MANUAL_RECONCILIATION')
      expect(assessment.effectKnowledge).toBe('UNKNOWN'); expect(assessment.canRelease).toBe(false)
      expect(assessment.canDispatch).toBe(false); expect(assessment.establishesFound).toBe(false)
      expect(assessment.original.originalUtf8()).toEqual(parsed.originalUtf8())
    })
  }
})
