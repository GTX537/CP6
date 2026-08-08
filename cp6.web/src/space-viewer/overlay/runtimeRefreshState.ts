import { isUsableDataSource } from '@/types/space/dataSource'
import type { SpaceRuntimeSource } from '@/types/space/runtime'

export type RuntimeFailureState = 'never' | 'active' | 'recovered'

export interface RuntimeRefreshState {
  lastSuccessfulAtUtc: string | null
  lastFailureAtUtc: string | null
  lastFailureCode: string | null
  failureState: RuntimeFailureState
}

export function initialRuntimeRefreshState(): RuntimeRefreshState {
  return {
    lastSuccessfulAtUtc: null,
    lastFailureAtUtc: null,
    lastFailureCode: null,
    failureState: 'never',
  }
}

export function recordRuntimeResult(
  state: RuntimeRefreshState,
  source: SpaceRuntimeSource,
): RuntimeRefreshState {
  if (!isUsableDataSource(source)) {
    return {
      ...state,
      lastFailureAtUtc: source.receivedAtUtc || state.lastFailureAtUtc,
      lastFailureCode: source.dataSourceId === 'EMPTY_FLOOR_SCOPE'
        ? 'EMPTY_FLOOR_SCOPE'
        : 'WMS_SOURCE_UNAVAILABLE',
      failureState: 'active',
    }
  }

  return {
    ...state,
    lastSuccessfulAtUtc: source.receivedAtUtc,
    failureState: state.failureState === 'active' ? 'recovered' : state.failureState,
  }
}

export function recordRuntimeFailure(
  state: RuntimeRefreshState,
  failedAtUtc: string,
  code: string,
): RuntimeRefreshState {
  return {
    ...state,
    lastFailureAtUtc: failedAtUtc,
    lastFailureCode: code,
    failureState: 'active',
  }
}

export function runtimeFailureCode(error: unknown): string {
  const candidate = error as {
    response?: { status?: unknown; data?: { code?: unknown } }
  }
  const problemCode = candidate?.response?.data?.code
  if (
    typeof problemCode === 'string' &&
    /^[A-Za-z0-9._-]{1,64}$/.test(problemCode)
  ) {
    return problemCode
  }
  const status = candidate?.response?.status
  if (typeof status === 'number' && Number.isInteger(status) && status >= 100 && status <= 599) {
    return `HTTP_${status}`
  }
  return 'RUNTIME_REFRESH_FAILED'
}
