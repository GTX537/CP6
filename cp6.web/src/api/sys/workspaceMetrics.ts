import http from '../http'

export const metricKeys = [
  'todayOrders', 'monthOrders', 'activeWorkOrders', 'monthCompleted',
  'pendingOutbound', 'stockWarnings', 'pendingApprovals', 'totalProducts',
] as const

export type MetricKey = typeof metricKeys[number]
export type Availability = 'READY' | 'HIDDEN' | 'UNAVAILABLE' | 'STALE'
export type MetricState = Availability | 'LOADING'

export function metricStateLabel(state: MetricState) {
  switch (state) {
    case 'LOADING': return '加载中…'
    case 'READY': return '当前授权范围'
    case 'HIDDEN': return '当前无权查看'
    case 'UNAVAILABLE': return '来源暂不可用'
    case 'STALE': return '数据已过期'
  }
}
export interface MetricQuery {
  key: MetricKey
  version: string
  asOfUtc: string
}
export function metricDetailsLink(ref: MetricQuery) {
  return { name: 'workspace-metric-details', params: { metricKey: ref.key }, query: { version: ref.version, asOfUtc: ref.asOfUtc } }
}

export interface WorkspaceCard {
  definition: {
    key: MetricKey
    version: string
    sourceOwner: string
    sourceRef: string
    selection: string
    timeBasis: string
    targetPath: string | null
  }
  availability: Availability
  value: number | null
  asOfUtc: string | null
  staleAfterUtc: string | null
  windowStart: string | null
  queryRef: MetricQuery | null
  rows: { reference: { owner: string; id: string; version: string } | null; ownerState: string }[] | null
}

export interface WorkspacePreferenceSnapshot {
  revision: string
  visibleMetricKeys: MetricKey[]
}
export interface WorkspacePreferenceCommand {
  operationRef: string
  expectedRevision: string
  visibleMetricKeys: MetricKey[]
}
export interface WorkspacePreferenceReceipt {
  operationRef: string
  snapshot: WorkspacePreferenceSnapshot
  appliedAtUtc: string
}
export interface WorkspaceNavigation {
  menuVersion: string
  policyVersion: string
  menus: { id: number; menuName: string; routePath: string | null; icon: string | null; parentId: number | null; orderNo: number }[]
  actionKeys: string[]
}
export type WorkspaceCrmEntry =
  | { featureRef: 'CRM.Workspace'; availability: 'READY'; target: string; host: string }
  | { featureRef: 'CRM.Workspace'; availability: 'NOT_REGISTERED' }
const base = '/dashboard/scoped-v1'

export const workspaceMetricsApi = {
  navigation(signal: AbortSignal) {
    return http.get<unknown, WorkspaceNavigation>(base + '/navigation', { signal })
  },
  crmEntry(signal: AbortSignal) {
    return http.get<unknown, WorkspaceCrmEntry>(base + '/navigation/crm', { signal })
  },
  getPreferences(signal: AbortSignal) {
    return http.get<unknown, WorkspacePreferenceSnapshot>(base + '/preferences', { signal })
  },
  savePreferences(command: WorkspacePreferenceCommand) {
    return http.post<unknown, WorkspacePreferenceReceipt>(base + '/preferences', command)
  },
  readPreferenceOperation(operationRef: string, signal: AbortSignal) {
    return http.get<unknown, WorkspacePreferenceReceipt>(`${base}/preferences/operations/${operationRef}`, { signal })
  },
  getCard(key: MetricKey, signal: AbortSignal) {
    return http.get<unknown, WorkspaceCard>(base + '/' + key, { signal })
  },
  getDrilldown(ref: MetricQuery, page: number, signal: AbortSignal) {
    return http.get<unknown, WorkspaceCard>(base + '/' + ref.key + '/drilldown', {
      signal, params: { version: ref.version, asOfUtc: ref.asOfUtc, page },
    })
  },
}
