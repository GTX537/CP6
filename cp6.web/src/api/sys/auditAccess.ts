import http from '../http'

export type AuditClass = 'TECH_TELEMETRY' | 'SECURITY_EVENT' | 'FIELD_HISTORY'
export interface AuditFilters {
  keyword?: string
  eventType?: number
  entityName?: string
  entityKey?: string
  from?: string
  to?: string
  fromUtc?: string
  toUtc?: string
  timeSource?: string
}
export interface AuditEvidence {
  source: string
  reliability: string
  actorKind: string
  actualActor?: string
  effectiveActor?: string
  service?: string
  permissionVersion?: string
  operationId?: string
  outcome?: string
  action?: string
  transactionRef?: string
  entityVersion?: string
  recordedAtUtc?: string
}
export interface AuditSummary {
  id: number | string
  auditAtUtc?: string
  timeSource?: string
  evidenceGap?: boolean
}
export interface AuditDetail extends AuditSummary {
  httpMethod?: string
  requestUrl?: string
  statusCode?: number
  elapsedMs?: number
  evidence?: AuditEvidence
  nativeDigest?: string
  evidenceStatus?: string
  changes?: { field: string; old: string | null; new: string | null }[]
}
export interface AuditPage<T> {
  rows: T[]
  total: number
  nextCursor?: string | null
  policyVersion?: string
}
export interface AuditExport {
  id: string
  policyVersion: string
  rowCount: number
  sha256: string
  expiresAt: string
}
export interface AuditDisposal {
  planId: string
  retentionClass: AuditClass
  policyVersion: string
  cutoff: string
  expiresAt: string
  eligible: number
  state: 'READY' | 'COMPLETED' | 'BLOCKED' | 'EXPIRED'
  affected: number
  reason?: string
  completedAt?: string
  method?: string
}

export const auditAccess = {
  detail(key: AuditClass, id: string | number) {
    return http.get(`/sys/audit/${key}/records/${encodeURIComponent(id)}`) as unknown as Promise<AuditDetail>
  },
  export(key: AuditClass, purposeRef: string, expectedPolicyVersion: string, filter: AuditFilters) {
    return http.post(`/sys/audit/${key}/exports`, { purposeRef, expectedPolicyVersion, filter }) as unknown as Promise<AuditExport>
  },
  download(key: AuditClass, id: string) {
    return http.get(`/sys/audit/${key}/exports/${id}/download`, { responseType: 'blob' }) as unknown as Promise<Blob>
  },
  preview(key: AuditClass, id: string) {
    return http.post(`/operlog/retention/plans/${id}`, undefined, { params: { retentionClass: key } }) as unknown as Promise<AuditDisposal>
  },
  readPlan(id: string) {
    return http.get(`/operlog/retention/plans/${id}`) as unknown as Promise<AuditDisposal>
  },
  executePlan(id: string) {
    return http.post(`/operlog/retention/plans/${id}/execute`) as unknown as Promise<AuditDisposal>
  },
}

export function auditTime(row: AuditSummary, legacy?: string | null): string {
  if (row.auditAtUtc) return new Date(row.auditAtUtc).toISOString()
  return legacy ? `${legacy.replace('T', ' ')}（旧本地时间，时区未核定）` : '时间未记录'
}

export const auditGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function auditDateFilter(timeSource: '' | 'UTC' | 'LEGACY_LOCAL', range: [string, string] | null): AuditFilters {
  if (timeSource !== 'UTC' || !range) return { timeSource: timeSource || undefined, from: range?.[0], to: range?.[1] }
  const end = new Date(`${range[1]}T00:00:00Z`)
  end.setUTCDate(end.getUTCDate() + 1)
  return { timeSource, fromUtc: `${range[0]}T00:00:00Z`, toUtc: end.toISOString() }
}
