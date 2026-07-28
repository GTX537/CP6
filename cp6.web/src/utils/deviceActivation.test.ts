import { describe, expect, it } from 'vitest'
import { buildDeviceActivationPayload } from './deviceActivation'

describe('device activation payload', () => {
  it('provisions Android scanner framing and duplicate suppression', () => {
    const payload = buildDeviceActivationPayload({
      server: ' https://wms.example.test/api/ ',
      tenant: ' CP6 ',
      token: 'one-time+token',
      platform: 'Android',
      scanPrefix: ']C1',
      scanSuffix: '~',
      scanTerminator: 'Tab',
      scanDuplicateMs: 900,
    })

    const uri = new URL(payload)
    expect(uri.searchParams.get('server')).toBe('https://wms.example.test/api/')
    expect(uri.searchParams.get('tenant')).toBe('CP6')
    expect(uri.searchParams.get('token')).toBe('one-time+token')
    expect(uri.searchParams.get('scanPrefix')).toBe(']C1')
    expect(uri.searchParams.get('scanSuffix')).toBe('~')
    expect(uri.searchParams.get('scanTerminator')).toBe('Tab')
    expect(uri.searchParams.get('scanDuplicateMs')).toBe('900')
  })

  it('keeps Windows payloads free of Android scanner settings', () => {
    const payload = buildDeviceActivationPayload({
      server: 'https://wms.example.test',
      tenant: 'CP6',
      token: 'token',
      platform: 'Windows',
      scanPrefix: ']C1',
      scanTerminator: 'Enter',
      scanDuplicateMs: 750,
    })

    const uri = new URL(payload)
    expect(uri.searchParams.has('scanPrefix')).toBe(false)
    expect(uri.searchParams.has('scanTerminator')).toBe(false)
    expect(uri.searchParams.has('scanDuplicateMs')).toBe(false)
  })
})
