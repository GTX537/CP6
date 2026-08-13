import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { designElementsApi } from './designElements'
import type { ISpaceSceneElementDto } from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

vi.mock('@/api/http', () => ({
  default: {
    post: vi.fn(),
  },
}))

const element = {
  revision: {
    logicalId: '11111111-1111-1111-1111-111111111111',
  },
} as unknown as ISpaceSceneElementDto

describe('designElementsApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(http.post).mockResolvedValue({})
  })

  it('submits a strongly typed UpdateProperties command batch', async () => {
    await designElementsApi.update(
      'version-1',
      'floor-1',
      7,
      '22222222-2222-2222-2222-222222222222',
      '33333333-3333-3333-3333-333333333333',
      element,
      {
        geometryJson:
          '{"schemaVersion":1,"kind":"box","width":1,"height":1,"depth":1}',
        x: 1,
        y: 2,
        z: 3,
        rotationZ: 0,
        width: 1,
        height: 1,
        depth: 1,
        attributes: [],
      },
    )

    expect(http.post).toHaveBeenCalledOnce()
    const [url, body] = vi.mocked(http.post).mock.calls[0]!
    expect(url).toBe(
      '/space/design/v1/versions/version-1/floors/floor-1/commands',
    )
    expect(body).toMatchObject({
      schemaVersion: 1,
      clientInstanceId: '22222222-2222-2222-2222-222222222222',
      leaseId: '33333333-3333-3333-3333-333333333333',
      expectedFloorRevision: 7,
      commands: [
        {
          type: 'UpdateProperties',
          targetLogicalId: element.revision?.logicalId,
          updateProperties: {
            x: 1,
            y: 2,
            z: 3,
          },
        },
      ],
    })
  })

  it('submits DeleteObject without an update payload', async () => {
    await designElementsApi.remove(
      'version-1',
      'floor-1',
      8,
      '22222222-2222-2222-2222-222222222222',
      '33333333-3333-3333-3333-333333333333',
      element,
    )

    const body = vi.mocked(http.post).mock.calls[0]?.[1] as {
      commands: Array<Record<string, unknown>>
    }
    expect(body.commands[0]).toMatchObject({
      type: 'DeleteObject',
      targetLogicalId: element.revision?.logicalId,
    })
    expect(body.commands[0]).not.toHaveProperty('updateProperties')
  })

  it('reuses a prepared envelope for a safe retry', async () => {
    const envelope = designElementsApi.createEnvelope(
      8,
      '22222222-2222-2222-2222-222222222222',
      '33333333-3333-3333-3333-333333333333',
      [{ type: 'DeleteObject', targetLogicalId: element.revision!.logicalId! }],
    )

    await designElementsApi.sendEnvelope('version-1', 'floor-1', envelope)
    await designElementsApi.sendEnvelope('version-1', 'floor-1', envelope)

    expect(vi.mocked(http.post).mock.calls[0]?.[1]).toBe(envelope)
    expect(vi.mocked(http.post).mock.calls[1]?.[1]).toBe(envelope)
  })

  it('fails closed when the selected element has no logical identity', () => {
    expect(() =>
      designElementsApi.remove(
        'version-1',
        'floor-1',
        8,
        '22222222-2222-2222-2222-222222222222',
        '33333333-3333-3333-3333-333333333333',
        {} as ISpaceSceneElementDto,
      ),
    ).toThrow('The selected element has no logical identity.')
    expect(http.post).not.toHaveBeenCalled()
  })
})
