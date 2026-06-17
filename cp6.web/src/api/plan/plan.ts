import http from '../http'
import type {
  ApiResp, ItemPolicy, MrpRun, PlannedOrder, NetRequirement, MrpRunRequest,
} from '@/types/plan/plan'

// 计划中台 API（/api/plan）。http 拦截器已返 body：{ code, message, data }，调用方取 res.data。

/** MRP 运算 /api/plan/mrp（运行 + 计划订单/净需求钻取 + 确认/转单/忽略） */
export const mrpApi = {
  run(req: MrpRunRequest) {
    return http.post<any, ApiResp<{ runId: string; runNo: string; status: number }>>('/plan/mrp/run', req)
  },
  runs() {
    return http.get<any, ApiResp<MrpRun[]>>('/plan/mrp/runs')
  },
  plannedOrders(runId: string) {
    return http.get<any, ApiResp<PlannedOrder[]>>(`/plan/mrp/run/${runId}/planned-orders`)
  },
  netRequirements(runId: string) {
    return http.get<any, ApiResp<NetRequirement[]>>(`/plan/mrp/run/${runId}/net-requirements`)
  },
  confirm(id: string) {
    return http.post<any, ApiResp<unknown>>(`/plan/mrp/planned-order/${id}/confirm`)
  },
  convert(id: string) {
    return http.post<any, ApiResp<{ docNo: string }>>(`/plan/mrp/planned-order/${id}/convert`)
  },
  ignore(id: string) {
    return http.post<any, ApiResp<unknown>>(`/plan/mrp/planned-order/${id}/ignore`)
  },
}

/** 计划主数据 /api/plan/item-policy（品目计划策略 CRUD） */
export const itemPolicyApi = {
  list(keyword?: string) {
    return http.get<any, ApiResp<ItemPolicy[]>>('/plan/item-policy', { params: { keyword } })
  },
  save(data: ItemPolicy) {
    return http.post<any, ApiResp<unknown>>('/plan/item-policy', data)
  },
  remove(itemCd: string) {
    return http.delete<any, ApiResp<unknown>>(`/plan/item-policy/${itemCd}`)
  },
}
