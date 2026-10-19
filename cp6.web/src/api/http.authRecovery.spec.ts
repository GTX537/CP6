import { AxiosError, type AxiosAdapter } from 'axios'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import http from './http'

vi.mock('@/router', () => ({ default: { push: vi.fn() } }))
vi.mock('element-plus', () => ({ ElMessage: { error: vi.fn() } }))
vi.mock('@/i18n', () => ({ default: { global: { t: (key: string) => key } } }))
vi.mock('@/stores/oaActingAs', () => ({ getActingAs: () => null }))
const original = http.defaults.adapter
beforeEach(() => { localStorage.clear(); sessionStorage.clear(); localStorage.setItem('cp6_authed', '1') })
afterEach(() => { http.defaults.adapter = original; vi.clearAllMocks() })

describe('business effects during authentication recovery', () => {
  it('never replays a business write after a 401, even if refresh could succeed', async () => {
    const writes: unknown[] = []; const calls: string[] = []
    http.defaults.adapter = (async config => {
      calls.push(config.url!)
      const response = { status: 200, statusText: '', data: { accepted: true }, headers: {}, config }
      if (config.url === '/orders/submit') {
        writes.push(config.data)
        if (writes.length === 1) throw new AxiosError('late authorization failure', undefined, config, undefined, { ...response, status: 401 })
      }
      return response
    }) satisfies AxiosAdapter
    await expect(http.post('/orders/submit', { operationId: 'same-business-operation', quantity: 3 })).rejects.toBeDefined()
    expect(writes).toHaveLength(1)
    expect(calls).not.toContain('/auth/refresh')
  })

  it('rejects an old business response after logout clears the authentication identity', async () => {
    let finish!: () => void
    http.defaults.adapter = (config => new Promise(resolve => {
      finish = () => resolve({ status: 200, statusText: '', data: { privateOrder: 'old identity' }, headers: {}, config })
    })) satisfies AxiosAdapter
    const pending = http.get('/orders')
    await vi.waitFor(() => expect(finish).toBeDefined())
    localStorage.removeItem('cp6_authed'); localStorage.removeItem('userName')
    window.dispatchEvent(new Event('storage'))
    finish()
    await expect(pending).rejects.toBeDefined()
  })

  it('does not send a business request while an authentication outcome is unknown', async () => {
    localStorage.setItem('cp6_browser_auth_v1', JSON.stringify({ version: 1, epoch: 'test', phase: 'unknown' }))
    const adapter = vi.fn(); http.defaults.adapter = adapter
    await expect(http.post('/orders/submit', { operationId: 'original' })).rejects.toBeDefined()
    await expect(http.get('/orders')).rejects.toBeDefined()
    expect(adapter).not.toHaveBeenCalled()
  })
})
