import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { designUnderlayApi } from './designUnderlay'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
  },
}))

describe('designUnderlayApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('sends explicit detach with lease, revisions and idempotency', async () => {
    const request = {
      sourceId: null,
      expectedFloorRevision: 8,
      expectedContentRevision: 12,
      clientInstanceId: 'client-1',
      leaseId: 'lease-1',
      commandBatchId: 'batch-1',
    }
    await designUnderlayApi.attach(
      'version-1',
      'floor-1',
      request,
      'detach-key-1',
    )
    expect(http.put).toHaveBeenCalledWith(
      '/space/design/v1/versions/version-1/floors/floor-1/underlay',
      request,
      { headers: { 'Idempotency-Key': 'detach-key-1' } },
    )
  })

  it('sends calibration and compensation through the same lease fence', async () => {
    const calibration = {
      floorLogicalId: 'floor-1',
      pageNumber: 1,
      pixelWidth: 100,
      pixelHeight: 100,
      point1: { pixelX: 0, pixelY: 0, worldX: 0, worldY: 0 },
      point2: { pixelX: 10, pixelY: 0, worldX: 100, worldY: 0 },
      validationPoint: { pixelX: 0, pixelY: 10, worldX: 0, worldY: 100 },
      expectedFloorRevision: 8,
      expectedContentRevision: 12,
      clientInstanceId: 'client-1',
      leaseId: 'lease-1',
      commandBatchId: 'batch-1',
    }
    await designUnderlayApi.calibrate(
      'version-1',
      'source-1',
      calibration,
      'calibrate-key-1',
    )
    expect(http.post).toHaveBeenNthCalledWith(
      1,
      '/space/design/v1/versions/version-1/sources/source-1/underlay-calibration',
      calibration,
      { headers: { 'Idempotency-Key': 'calibrate-key-1' } },
    )

    const compensation = {
      schemaVersion: 1,
      originalCommandBatchId: 'batch-1',
      direction: 'Undo',
      commandBatchId: 'undo-1',
      clientInstanceId: 'client-1',
      leaseId: 'lease-1',
      expectedFloorRevision: 9,
      expectedContentRevision: 13,
      historySha256: 'a'.repeat(64),
    }
    await designUnderlayApi.compensate(
      'version-1',
      'floor-1',
      compensation,
      'undo-key-1',
    )
    expect(http.post).toHaveBeenNthCalledWith(
      2,
      '/space/design/v1/versions/version-1/floors/floor-1/underlay:compensate',
      compensation,
      { headers: { 'Idempotency-Key': 'undo-key-1' } },
    )
  })
})
