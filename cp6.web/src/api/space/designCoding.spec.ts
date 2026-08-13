import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { designCodingApi } from './designCoding'

vi.mock('@/api/http', () => ({
  default: { post: vi.fn() },
}))

describe('designCodingApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(http.post).mockResolvedValue({})
  })

  it('previews without an edit lease and binds both revision fences', async () => {
    await designCodingApi.preview('version-1', 'floor-1', {
      schemaVersion: 1,
      mode: 'fill-empty',
      expectedFloorRevision: 7,
      expectedContentRevision: 12,
    })

    expect(http.post).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/floors/floor-1/location-codes:preview',
      {
        schemaVersion: 1,
        mode: 'fill-empty',
        expectedFloorRevision: 7,
        expectedContentRevision: 12,
      },
    )
  })

  it('reuses the same proposal and command batch for an idempotent retry', async () => {
    const envelope = designCodingApi.createEnvelope({
      schemaVersion: 1,
      modelVersionId: 'version-1',
      floorLogicalId: 'floor-1',
      mode: 'rebuild',
      scopeZoneLogicalId: 'zone-1',
      baseFloorRevision: 7,
      baseContentRevision: 12,
      proposalHash: 'a'.repeat(64),
      ruleSetHash: 'b'.repeat(64),
      changedCount: 2,
      unchangedCount: 0,
      protectedCount: 1,
      rules: [],
      items: [],
    }, 'client-1', 'lease-1')

    await designCodingApi.apply('version-1', 'floor-1', envelope)
    await designCodingApi.apply('version-1', 'floor-1', envelope)

    expect(envelope).toMatchObject({
      clientInstanceId: 'client-1',
      leaseId: 'lease-1',
      mode: 'rebuild',
      scopeZoneLogicalId: 'zone-1',
      expectedFloorRevision: 7,
      expectedContentRevision: 12,
      proposalHash: 'a'.repeat(64),
    })
    expect(envelope.commandBatchId).toBeTruthy()
    expect(vi.mocked(http.post).mock.calls[0]?.[1]).toBe(envelope)
    expect(vi.mocked(http.post).mock.calls[1]?.[1]).toBe(envelope)
  })
})
