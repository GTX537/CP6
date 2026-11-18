import http from '../http'
import type { AuditPage, AuditSummary } from './auditAccess'

export const operLogApi = {
  getList(params: { page: number; pageSize: number; keyword?: string }) {
    return http.get('/operlog', { params }) as unknown as Promise<AuditPage<AuditSummary & { createDate: string; outcome?: string }>>
  },
  // 日志只读，不需要 add/update，但 VolTable 要求这些方法存在
  add: () => Promise.reject(),
  update: () => Promise.reject(),
  del: () => Promise.reject()
}
