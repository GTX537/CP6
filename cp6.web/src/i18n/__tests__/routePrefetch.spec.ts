// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'

const axiosMock = vi.hoisted(() => ({
  get: vi.fn(),
}))

vi.mock('axios', () => ({
  default: {
    create: () => axiosMock,
  },
}))

import {
  ensureNamespacesForPath,
  namespacesForPath,
  prefetchNamespacesForPath,
} from '../index'

describe('route i18n prefetch', () => {
  beforeEach(() => {
    axiosMock.get.mockReset()
    localStorage.clear()
  })

  it('maps ERP routes to both ERP namespaces', () => {
    expect(namespacesForPath('/order-list')).toEqual(['sales', 'erp'])
    expect(namespacesForPath('/erp/credit-note')).toEqual(['sales', 'erp'])
  })

  it('returns immediately and deduplicates pending namespace requests', async () => {
    let resolveRequest!: (value: { data: Record<string, string> }) => void
    const pendingRequest = new Promise<{ data: Record<string, string> }>((resolve) => {
      resolveRequest = resolve
    })
    axiosMock.get.mockReturnValue(pendingRequest)

    expect(prefetchNamespacesForPath('/order-list')).toBeUndefined()
    expect(prefetchNamespacesForPath('/order-list')).toBeUndefined()
    const completion = ensureNamespacesForPath('/order-list')

    expect(axiosMock.get.mock.calls.map(([url]) => url)).toEqual([
      '/lang/ja/ns/sales',
      '/lang/ja/ns/erp',
    ])

    resolveRequest({ data: {} })
    await completion
  })
})
