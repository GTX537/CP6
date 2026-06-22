import http from '../http'
import type { SecurityLogRow } from '@/types/sys/security'

// S 类认证加固 T9：安全日志查询（需 query 权限，admin 已授）
export const securityLogApi = {
  getList(params: {
    eventType?: number
    userName?: string
    from?: string
    to?: string
    page: number
    pageSize: number
  }): Promise<{ rows: SecurityLogRow[]; total: number }> {
    // http 拦截器返回 response.data，故运行期即 {rows,total}；axios 静态类型仍为 AxiosResponse 需断言
    return http.get('/sys/security-log', { params }) as unknown as Promise<{ rows: SecurityLogRow[]; total: number }>
  }
}
