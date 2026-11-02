import http from '@/api/http'
import type { TenantRow, TenantDetail, CreateTenantResult } from '@/types/platform/platform'

export interface TenantConfiguration {
  name: string; expireDate: string | null; remark: string | null; timeZoneId: string | null
}
export interface TenantManagementSnapshot {
  id: string; tenantCode: string; tenantName: string; enable: boolean; expireDate: string | null
  remark: string | null; timeZoneId: string | null; revision: string
}
export interface TenantManagementCommand {
  operationRef: string; expectedRevision: string; kind: 'update' | 'suspend' | 'reactivate'
  configuration: TenantConfiguration | null
}
export interface TenantManagementReceipt {
  operationRef: string; kind: TenantManagementCommand['kind']; snapshot: TenantManagementSnapshot; committedAt: string
}

// 多租户合规 #5（块①）租户管理 API。带外平台区，后端 [RequirePlatformAdmin] 真闸门。
// http 拦截器返回 response.data，故运行期即解包后的数据；axios 静态类型仍为 AxiosResponse 需断言。
export const tenantApi = {
  management(id: string): Promise<TenantManagementSnapshot> {
    return http.get(`/platform/tenant/${id}/management-v1`) as unknown as Promise<TenantManagementSnapshot>
  },
  manage(id: string, command: TenantManagementCommand): Promise<TenantManagementReceipt> {
    return http.post(`/platform/tenant/${id}/management-v1/commands`, command) as unknown as Promise<TenantManagementReceipt>
  },
  originalManagement(id: string, operationRef: string): Promise<TenantManagementReceipt> {
    return http.post(`/platform/tenant/${id}/management-v1/commands/read`, { operationRef }) as unknown as Promise<TenantManagementReceipt>
  },
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
