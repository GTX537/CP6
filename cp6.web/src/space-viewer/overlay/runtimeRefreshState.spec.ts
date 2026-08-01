import { describe, expect, it } from 'vitest'
import {
  initialRuntimeRefreshState,
  recordRuntimeFailure,
  recordRuntimeResult,
  runtimeFailureCode,
} from './runtimeRefreshState'
import type { SpaceRuntimeSource } from '@/types/space/runtime'

const real: SpaceRuntimeSource = {
  kind: 'Real',
  adapterId: 'cp6-wms-v1',
  dataSourceId: 'CP6_WMS',
  observedAtUtc: '2026-08-01T12:00:00Z',
  receivedAtUtc: '2026-08-01T12:00:02Z',
  delayMilliseconds: 2000,
  clockSkewMilliseconds: 0,
  isSimulated: false,
  isAvailable: true,
}

describe('runtimeRefreshState', () => {
  it('records success, active failure, and recovery without losing failure history', () => {
    const success = recordRuntimeResult(initialRuntimeRefreshState(), real)
    expect(success).toMatchObject({
      lastSuccessfulAtUtc: real.receivedAtUtc,
      failureState: 'never',
    })

    const failed = recordRuntimeFailure(success, '2026-08-01T12:00:05Z', 'HTTP_503')
    expect(failed).toMatchObject({
      lastSuccessfulAtUtc: real.receivedAtUtc,
      lastFailureAtUtc: '2026-08-01T12:00:05Z',
      lastFailureCode: 'HTTP_503',
      failureState: 'active',
    })

    const recovered = recordRuntimeResult(failed, {
      ...real,
      receivedAtUtc: '2026-08-01T12:00:10Z',
    })
    expect(recovered).toMatchObject({
      lastSuccessfulAtUtc: '2026-08-01T12:00:10Z',
      lastFailureAtUtc: '2026-08-01T12:00:05Z',
      lastFailureCode: 'HTTP_503',
      failureState: 'recovered',
    })
  })

  it('treats an explicit unavailable response as an active failure', () => {
    const state = recordRuntimeResult(initialRuntimeRefreshState(), {
      ...real,
      kind: 'Unavailable',
      isAvailable: false,
    })

    expect(state).toMatchObject({
      lastFailureAtUtc: real.receivedAtUtc,
      lastFailureCode: 'WMS_SOURCE_UNAVAILABLE',
      failureState: 'active',
    })
  })

  it('extracts only safe problem or HTTP codes', () => {
    expect(runtimeFailureCode({ response: { status: 503, data: { code: 'SPACE.WMS_UNAVAILABLE' } } }))
      .toBe('SPACE.WMS_UNAVAILABLE')
    expect(runtimeFailureCode({ response: { status: 502, data: { code: 'unsafe code!' } } }))
      .toBe('HTTP_502')
    expect(runtimeFailureCode(new Error('secret transport detail')))
      .toBe('RUNTIME_REFRESH_FAILED')
  })
})
