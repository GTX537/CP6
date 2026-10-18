import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import { spaceRuntimeApi } from '../runtime'
import { StockOverlay } from '@/space-viewer/overlay/StockOverlay'

vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn(),
    put: vi.fn(),
    post: vi.fn(),
  },
}))

describe('spaceRuntimeApi', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(http.get).mockResolvedValue({})
    vi.mocked(http.put).mockResolvedValue({})
    vi.mocked(http.post).mockResolvedValue({})
  })

  it('puts an idempotent putaway recommendation request under the operations API', async () => {
    const request = {
      materialNumber: 'SKU-01',
      inboundQuantity: 5,
      allowExactStockConsolidation: true,
      maximumCandidates: 10,
    }

    await spaceRuntimeApi.generatePutawayRecommendation(
      'site/1',
      'recommendation/1',
      request,
    )

    expect(http.put).toHaveBeenCalledWith(
      '/space/operations/v1/sites/site%2F1/putaway-recommendations/recommendation%2F1',
      request,
    )
  })

  it('puts an idempotent dispatch recommendation request under the operations API', async () => {
    const request = {
      taskType: 'PICK',
      allowCrossFloor: false,
      includeSimulatedPersonnel: false,
      maximumAssignments: 20,
    }

    await spaceRuntimeApi.generateDispatchRecommendation(
      'site/1',
      'recommendation/1',
      request,
    )

    expect(http.put).toHaveBeenCalledWith(
      '/space/operations/v1/sites/site%2F1/dispatch-recommendations/recommendation%2F1',
      request,
    )
  })

  it('gets immutable dispatch recommendation evidence by identity', async () => {
    await spaceRuntimeApi.dispatchRecommendation('site/1', 'recommendation/1')

    expect(http.get).toHaveBeenCalledWith(
      '/space/operations/v1/sites/site%2F1/dispatch-recommendations/recommendation%2F1',
    )
  })

  it('submits, reads, and cancels an idempotent dispatch approval request', async () => {
    const request = { selectedRanks: [1, 3], reason: 'Release the reviewed batch' }

    await spaceRuntimeApi.submitDispatchApproval(
      'site/1',
      'recommendation/1',
      'approval/1',
      request,
    )
    await spaceRuntimeApi.dispatchApproval(
      'site/1',
      'recommendation/1',
      'approval/1',
    )
    await spaceRuntimeApi.cancelDispatchApproval(
      'site/1',
      'recommendation/1',
      'approval/1',
    )

    const base = '/space/operations/v1/sites/site%2F1' +
      '/dispatch-recommendations/recommendation%2F1' +
      '/approval-requests/approval%2F1'
    expect(http.put).toHaveBeenCalledWith(base, request)
    expect(http.get).toHaveBeenCalledWith(base)
    expect(http.post).toHaveBeenCalledWith(`${base}/cancel`)
  })

  it('reads execution evidence and puts idempotent retry and compensation actions', async () => {
    const request = { reason: 'Operator reviewed the whole batch' }
    const base = '/space/operations/v1/sites/site%2F1' +
      '/dispatch-recommendations/recommendation%2F1' +
      '/approval-requests/approval%2F1'

    await spaceRuntimeApi.dispatchExecution('site/1', 'recommendation/1', 'approval/1')
    await spaceRuntimeApi.retryDispatchExecution(
      'site/1',
      'recommendation/1',
      'approval/1',
      'action/1',
      request,
    )
    await spaceRuntimeApi.compensateDispatchExecution(
      'site/1',
      'recommendation/1',
      'approval/1',
      'action/2',
      request,
    )

    expect(http.get).toHaveBeenCalledWith(`${base}/execution`)
    expect(http.put).toHaveBeenNthCalledWith(1, `${base}/retry-requests/action%2F1`, request)
    expect(http.put).toHaveBeenNthCalledWith(2, `${base}/compensation-requests/action%2F2`, request)
  })

  it('reads the outcome evaluation under the immutable approval identity', async () => {
    await spaceRuntimeApi.dispatchOutcomeEvaluation(
      'site/1',
      'recommendation/1',
      'approval/1',
    )

    expect(http.get).toHaveBeenCalledWith(
      '/space/operations/v1/sites/site%2F1' +
        '/dispatch-recommendations/recommendation%2F1' +
        '/approval-requests/approval%2F1/evaluation',
    )
  })

  it('gets immutable putaway recommendation evidence by identity', async () => {
    await spaceRuntimeApi.putawayRecommendation('site/1', 'recommendation/1')

    expect(http.get).toHaveBeenCalledWith(
      '/space/operations/v1/sites/site%2F1/putaway-recommendations/recommendation%2F1',
    )
  })

  it('requests operations diagnostics with an explicit half-open UTC window', async () => {
    await spaceRuntimeApi.operationsDiagnostics(
      'site-1',
      '2026-08-02T04:00:00.000Z',
      '2026-08-02T12:00:00.000Z',
    )

    const [url, config] = vi.mocked(http.get).mock.calls[0]!
    expect(url).toBe('/space/operations/v1/sites/site-1/diagnostics')
    const params = config?.params as URLSearchParams
    expect(params.get('fromUtc')).toBe('2026-08-02T04:00:00.000Z')
    expect(params.get('toUtc')).toBe('2026-08-02T12:00:00.000Z')
  })

  it('requests a warehouse overview with an explicit ABC window', async () => {
    await spaceRuntimeApi.warehouseOverview('site-1', 120)

    const [url, config] = vi.mocked(http.get).mock.calls[0]!
    expect(url).toBe('/space/design/v1/sites/site-1/runtime/overview')
    expect((config?.params as URLSearchParams).get('abcWindowDays')).toBe('120')
  })

  it.each([0, 101, 10_000])('posts all %i selected GUIDs once, with a constant URL and no full-site fallback', async count => {
    const ids = Array.from({ length: count }, (_, i) => `10000000-0000-0000-0000-${i.toString().padStart(12, '0')}`)
    const site = '20000000-0000-0000-0000-000000000001'
    const result = { siteId: site, publishedVersionId: 'published', source: { kind: 'Real' }, items: [] }
    vi.mocked(http.post).mockResolvedValueOnce(result)
    expect(await spaceRuntimeApi.inventory(site, [...ids, ...ids])).toBe(result)
    expect(http.get).not.toHaveBeenCalled()
    expect(http.post).toHaveBeenCalledExactlyOnceWith(
      `/space/design/v1/sites/${site}/runtime/inventory`, { locationLogicalIds: ids },
    )
    const [url, body] = vi.mocked(http.post).mock.calls[0]!
    expect(url.length).toBeLessThan(150)
    expect(JSON.stringify(body).length).toBeLessThan(1_048_576)
    if (count === 10_000) {
      const oldQuery = new URLSearchParams(ids.map(id => ['locationLogicalId', id]))
      expect(oldQuery.toString().length).toBeGreaterThan(8_192)
    }
  })

  it('keeps the actual request target below the HTTP request-line limit for 10,000 real GUIDs', async () => {
    const ids = Array.from({ length: 10_000 }, (_, i) => `10000000-0000-0000-0000-${i.toString().padStart(12, '0')}`)
    await spaceRuntimeApi.inventory('20000000-0000-0000-0000-000000000001', ids)
    const get = vi.mocked(http.get).mock.calls[0]
    const target = get ? `${get[0]}?${get[1]?.params}` : vi.mocked(http.post).mock.calls[0]![0]
    expect(target.length).toBeLessThan(8_192)
  })

  it('propagates the single request failure without falling back or returning partial inventory', async () => {
    const error = new Error('Published scope rejected')
    vi.mocked(http.post).mockRejectedValueOnce(error)
    await expect(spaceRuntimeApi.inventory('site', ['location'])).rejects.toBe(error)
    expect(http.post).toHaveBeenCalledTimes(1)
    expect(http.get).not.toHaveBeenCalled()
  })

  it('carries a large Overlay selection through the real inventory entry to one POST', async () => {
    const locations = Array.from({ length: 101 }, (_, i) => ({
      locationLogicalId: `10000000-0000-0000-0000-${i.toString().padStart(12, '0')}`, locationCode: `L-${i}`,
    }))
    const source = { kind: 'Simulated', adapterId: 'fixture', dataSourceId: 'fixture', observedAtUtc: '2026-10-18T00:00:00Z',
      receivedAtUtc: '2026-10-18T00:00:00Z', delayMilliseconds: 0, clockSkewMilliseconds: 0, isSimulated: true, isAvailable: true }
    vi.mocked(http.post).mockResolvedValueOnce({ siteId: 'site', publishedVersionId: 'published', warehouseCode: 'WH', source, items: [] })
    const overlay = new StockOverlay({ setInstanceColors: vi.fn(), requestRender: vi.fn() } as never)
    expect(await overlay.refresh('site', locations)).toBe(true)
    expect(http.post).toHaveBeenCalledExactlyOnceWith('/space/design/v1/sites/site/runtime/inventory',
      { locationLogicalIds: locations.map(l => l.locationLogicalId) })
    expect(http.get).not.toHaveBeenCalled()
    expect(overlay.source).toBe(source)
    expect(overlay.getStock(locations[100]!.locationLogicalId)).not.toBeNull()
  })

  it('serializes normalized owner, material, lot, and container locate criteria', async () => {
    await spaceRuntimeApi.locateInventory('site-1', {
      materialNumber: ' SKU-01 ',
      lotNumber: ' LOT-01 ',
      containerNumber: ' BOX-01 ',
      ownerId: ' owner-a ',
    })

    const [url, config] = vi.mocked(http.get).mock.calls[0]!
    expect(url).toBe('/space/design/v1/sites/site-1/runtime/inventory/locate')
    const params = config?.params as URLSearchParams
    expect(params.get('materialNumber')).toBe('SKU-01')
    expect(params.get('lotNumber')).toBe('LOT-01')
    expect(params.get('containerNumber')).toBe('BOX-01')
    expect(params.get('ownerId')).toBe('OWNER-A')
  })

  it('omits blank locate criteria so the server can reject an empty request', async () => {
    await spaceRuntimeApi.locateInventory('site-1', {
      materialNumber: ' ',
      lotNumber: '',
    })

    const [, config] = vi.mocked(http.get).mock.calls[0]!
    expect([...(config?.params as URLSearchParams).keys()]).toEqual([])
  })

  it('normalizes a task identity without exposing business characters as path segments', async () => {
    await spaceRuntimeApi.taskPath('site-1', ' pick/001 ')

    const [url, config] = vi.mocked(http.get).mock.calls[0]!
    expect(url).toBe('/space/design/v1/sites/site-1/runtime/tasks/path')
    expect((config?.params as URLSearchParams).get('taskId')).toBe('PICK/001')
  })

  it('scopes current personnel to the active floor and preserves cursor pagination', async () => {
    await spaceRuntimeApi.currentPersonnel('site-1', 'floor-1', 500, 'next-page')

    const [url, config] = vi.mocked(http.get).mock.calls[0]!
    expect(url).toBe('/space/design/v1/sites/site-1/personnel')
    const params = config?.params as URLSearchParams
    expect(params.get('floorLogicalId')).toBe('floor-1')
    expect(params.get('limit')).toBe('500')
    expect(params.get('cursor')).toBe('next-page')
  })

  it('scopes current devices to the active floor and preserves cursor pagination', async () => {
    await spaceRuntimeApi.currentDevices('site-1', 'floor-1', 500, 'next-page')

    const [url, config] = vi.mocked(http.get).mock.calls[0]!
    expect(url).toBe('/space/design/v1/sites/site-1/devices')
    const params = config?.params as URLSearchParams
    expect(params.get('floorLogicalId')).toBe('floor-1')
    expect(params.get('limit')).toBe('500')
    expect(params.get('cursor')).toBe('next-page')
  })

  it('keeps business identities in query values and sends an explicit UTC trajectory window', async () => {
    await spaceRuntimeApi.personnelTrajectory(
      'site-1',
      ' pda/01 ',
      ' person 01/blue ',
      '2026-08-02T15:00:00.000Z',
      '2026-08-02T16:00:00.000Z',
      500,
    )

    const [url, config] = vi.mocked(http.get).mock.calls[0]!
    expect(url).toBe('/space/design/v1/sites/site-1/personnel/trajectory')
    const params = config?.params as URLSearchParams
    expect(params.get('sourceId')).toBe('PDA/01')
    expect(params.get('personExternalId')).toBe('PERSON 01/BLUE')
    expect(params.get('fromUtc')).toBe('2026-08-02T15:00:00.000Z')
    expect(params.get('toUtc')).toBe('2026-08-02T16:00:00.000Z')
  })
})
