import http from '@/api/http'
import type { PlatformAdminRow } from '@/types/platform/platform'

// 多租户合规 #5（块②）平台超管授撤 API。撤最后一个超管时后端拒 E-SEC-037（拦截器统一提示）。
export const platformAdminApi = {
  list(): Promise<PlatformAdminRow[]> {
    return http.get('/platform/admin') as unknown as Promise<PlatformAdminRow[]>
  },
  grant(userId: string): Promise<unknown> {
    return http.post(`/platform/admin/${userId}/grant`) as unknown as Promise<unknown>
  },
  revoke(userId: string): Promise<unknown> {
    return http.post(`/platform/admin/${userId}/revoke`) as unknown as Promise<unknown>
  }
}
