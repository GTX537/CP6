import http from '../http'

// S 类认证加固 T9：安全日志查询（需 query 权限，admin 已授）
export const securityLogApi = {
  getList(params: {
    eventType?: number | ''
    userName?: string
    from?: string | undefined
    to?: string | undefined
    page: number
    pageSize: number
  }) {
    return http.get('/sys/security-log', { params })
  }
}
