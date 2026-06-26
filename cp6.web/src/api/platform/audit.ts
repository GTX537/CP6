import http from '@/api/http'
import type { AuditRow } from '@/types/platform/platform'

// 多租户合规 #5（块④ R10）跨租户安全审计查询 API。带外全租户取证（仅平台超管）。
export const platformAuditApi = {
  list(params: {
    tenantCode?: string
    eventType?: number
    from?: string
    to?: string
    page: number
    pageSize: number
  }): Promise<{ rows: AuditRow[]; total: number }> {
    return http.get('/platform/audit', { params }) as unknown as Promise<{ rows: AuditRow[]; total: number }>
  }
}
