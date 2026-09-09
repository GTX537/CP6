import axios, { AxiosError, type AxiosAdapter } from 'axios'
import { afterEach, describe, expect, it, vi } from 'vitest'
import http from '../http'
import { authApi } from './auth'

const effects = vi.hoisted(() => ({ push: vi.fn(), error: vi.fn() }))
vi.mock('@/router', () => ({ default: { push: effects.push } }))
vi.mock('element-plus', () => ({ ElMessage: { error: effects.error } }))
vi.mock('@/i18n', () => ({ default: { global: { t: (key: string) => key } } }))
vi.mock('@/stores/oaActingAs', () => ({ getActingAs: () => null }))
const original = axios.defaults.adapter
const originalHttp = http.defaults.adapter
afterEach(() => {
  axios.defaults.adapter = original
  http.defaults.adapter = originalHttp
  vi.clearAllMocks()
})

describe('login page session probe', () => {
  function transport(status: number) {
    const calls: string[] = []
    const adapter: AxiosAdapter = async config => {
      calls.push(config.url!)
      const response = { status, statusText: '', data: { mustChangePassword: false }, headers: {}, config }
      if (status >= 400) throw new AxiosError('unauthorized', undefined, config, undefined, response)
      return response
    }
    axios.defaults.adapter = http.defaults.adapter = adapter
    return calls
  }

  it('leaves an anonymous login on its continuation without refresh or error toasts', async () => {
    const calls = transport(401)
    await expect(authApi.profile({ passive: true })).rejects.toBeDefined()
    expect(calls).toHaveLength(1)
    expect(effects.push).not.toHaveBeenCalled()
    expect(effects.error).not.toHaveBeenCalled()
  })

  it('returns the existing session profile for silent SSO continuation', async () => {
    const calls = transport(200)
    await expect(authApi.profile({ passive: true })).resolves.toEqual({ mustChangePassword: false })
    expect(calls).toHaveLength(1)
  })

  it('keeps refresh and expired-session handling for normal protected requests', async () => {
    const calls = transport(401)
    await expect(authApi.profile()).rejects.toBeDefined()
    expect(calls).toEqual(['/auth/profile', '/auth/refresh'])
    expect(effects.push).toHaveBeenCalledWith('/login')
    expect(effects.error).toHaveBeenCalled()
  })
})
