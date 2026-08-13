import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { designLayoutApi } from './designLayout'

vi.mock('@/api/http', () => ({
  default: {
    post: vi.fn(),
  },
}))

describe('designLayoutApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(http.post).mockResolvedValue({})
  })

  it('submits an atomic hierarchy batch with both revision fences', async () => {
    await designLayoutApi.apply(
      'version-1',
      'floor-1',
      7,
      12,
      '22222222-2222-2222-2222-222222222222',
      '33333333-3333-3333-3333-333333333333',
      [
        {
          commandId: '',
          type: 'CreateZone',
          targetLogicalId: '11111111-1111-1111-1111-111111111111',
          createZone: {
            zoneCode: 'Z-A',
            zoneType: 1,
            polygonJson: '{"schemaVersion":1,"points":[]}',
          },
        },
      ],
    )

    const [url, body] = vi.mocked(http.post).mock.calls[0]!
    expect(url).toBe(
      '/space/design/v1/versions/version-1/floors/floor-1/layout-commands',
    )
    expect(body).toMatchObject({
      schemaVersion: 1,
      clientInstanceId: '22222222-2222-2222-2222-222222222222',
      leaseId: '33333333-3333-3333-3333-333333333333',
      expectedFloorRevision: 7,
      expectedContentRevision: 12,
      commands: [
        {
          type: 'CreateZone',
          targetLogicalId: '11111111-1111-1111-1111-111111111111',
        },
      ],
    })
    expect((body as LayoutBody).commandBatchId).toBeTruthy()
    expect((body as LayoutBody).commands[0]?.commandId).toBeTruthy()
  })

  it('reuses a prepared envelope for an idempotent retry', async () => {
    const envelope = designLayoutApi.createEnvelope(
      7,
      12,
      '22222222-2222-2222-2222-222222222222',
      '33333333-3333-3333-3333-333333333333',
      [
        {
          commandId: '44444444-4444-4444-4444-444444444444',
          type: 'CreateZone',
          targetLogicalId: '11111111-1111-1111-1111-111111111111',
          createZone: {
            zoneCode: 'Z-A',
            zoneType: 1,
            polygonJson: '{"schemaVersion":1,"points":[]}',
          },
        },
      ],
    )

    await designLayoutApi.sendEnvelope('version-1', 'floor-1', envelope)
    await designLayoutApi.sendEnvelope('version-1', 'floor-1', envelope)

    expect(vi.mocked(http.post).mock.calls[0]?.[1]).toBe(envelope)
    expect(vi.mocked(http.post).mock.calls[1]?.[1]).toBe(envelope)
  })
})

interface LayoutBody {
  commandBatchId: string
  commands: Array<{ commandId: string }>
}
