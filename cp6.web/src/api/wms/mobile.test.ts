import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('../http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

import http from '../http'
import { mobileApi } from './mobile'

describe('WMS mobile v2 API', () => {
  beforeEach(() => vi.clearAllMocks())

  it('uses the literal paged v2 route and forwards filters', () => {
    const query = { page: 2, pageSize: 25, assignedTo: 'alice', openOnly: true }

    mobileApi.tasks(query)

    expect(http.get).toHaveBeenCalledWith('/v2/wms/tasks', { params: query })
  })

  it('encodes task numbers in detail and mutation routes', () => {
    mobileApi.get('MOVE/2026 01')
    mobileApi.assign('MOVE/2026 01', { assignedTo: 'bob', rowVersion: 'AQID' })

    expect(http.get).toHaveBeenCalledWith('/v2/wms/tasks/MOVE%2F2026%2001')
    expect(http.post).toHaveBeenCalledWith(
      '/v2/wms/tasks/MOVE%2F2026%2001/assign',
      expect.objectContaining({ assignedTo: 'bob', rowVersion: 'AQID', operationId: expect.any(String) }),
    )
  })

  it('always sends the concurrency token when cancelling', () => {
    mobileApi.cancel('MTK-1', 'row-version')

    expect(http.post).toHaveBeenCalledWith(
      '/v2/wms/tasks/MTK-1/cancel',
      expect.objectContaining({ rowVersion: 'row-version', reason: '', operationId: expect.any(String) }),
    )
  })
})
