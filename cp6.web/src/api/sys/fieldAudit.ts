import http from '../http'
import type { FieldAuditListItem, FieldAuditTimelinePage } from '@/types/sys/fieldAudit'
import type { AuditFilters, AuditPage } from './auditAccess'

// #4 字段级审计 T7：字段审计查询（需 sys-field-audit:query 权限，admin 已授）
export const fieldAuditApi = {
  // 列表（分页 + 筛选），返回 {rows,total}
  getList(params: AuditFilters & { page: number; pageSize: number }) {
    return http.get('/sys/field-audit', { params }) as unknown as Promise<AuditPage<FieldAuditListItem>>
  },

  // 当前 Owner 安全时间线；每一页都重新授权，游标不携带授权。
  getRecordTimeline(
    entityName: string,
    entityKey: string,
    afterId?: string
  ): Promise<FieldAuditTimelinePage> {
    return http.get('/sys/field-audit/record', {
      params: { entityName, entityKey, afterId, pageSize: 100 }
    }) as unknown as Promise<FieldAuditTimelinePage>
  }
}
