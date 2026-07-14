import http from '@/api/http'
import type { TenantRow, TenantDetail, CreateTenantResult } from '@/types/platform/platform'

// 多租户合规 #5（块①）租户管理 API。带外平台区，后端 [RequirePlatformAdmin] 真闸门。
// http 拦截器返回 response.data，故运行期即解包后的数据；axios 静态类型仍为 AxiosResponse 需断言。
export const tenantApi = {
  list(params: {
    keyword?: string
    enable?: boolean
    page: number
    pageSize: number
  }): Promise<{ rows: TenantRow[]; total: number }> {
    return http.get('/platform/tenant', { params }) as unknown as Promise<{ rows: TenantRow[]; total: number }>
  },
  get(id: string): Promise<TenantDetail> {
    return http.get(`/platform/tenant/${id}`) as unknown as Promise<TenantDetail>
  },
  create(body: {
    code: string
    name: string
    expire?: string | null
    remark?: string | null
    adminUserName: string
  }): Promise<CreateTenantResult> {
    return http.post('/platform/tenant', body) as unknown as Promise<CreateTenantResult>
  },
  update(
    id: string,
    body: { name: string; expire?: string | null; remark?: string | null; timeZoneId?: string | null }
  ): Promise<unknown> {
    return http.put(`/platform/tenant/${id}`, body) as unknown as Promise<unknown>
  },
  suspend(id: string): Promise<unknown> {
    return http.post(`/platform/tenant/${id}/suspend`) as unknown as Promise<unknown>
  },
  reactivate(id: string): Promise<unknown> {
    return http.post(`/platform/tenant/${id}/reactivate`) as unknown as Promise<unknown>
  }
}
