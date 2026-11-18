import http from '../http'
import type { SecurityEventType } from '@/types/sys/security'
import type { AuditFilters, AuditPage, AuditSummary } from './auditAccess'

export type SecurityAuditRow = AuditSummary & { eventType: SecurityEventType; createdAt: string }
export const securityLogApi = {
  getList(params: AuditFilters & { page: number; pageSize: number }) {
    return http.get('/sys/security-log', { params }) as unknown as Promise<AuditPage<SecurityAuditRow>>
  }
}
