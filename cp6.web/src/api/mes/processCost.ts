import http from '../http'
import type { ApiResp, WorkCenter, ProcessCostRate } from '@/types/mes/processCost'

// A2 工艺路线主数据 API（http.baseURL 已含 /api 前缀，故此处仅 /mes/...）。
// http 响应拦截器已返 body：{ code, message, data }，调用方取 res.data。

/** 工作中心 /api/mes/work-center（CRUD） */
export const workCenterApi = {
  list(keyword?: string) {
    return http.get<any, ApiResp<WorkCenter[]>>('/mes/work-center', { params: { keyword } })
  },
  save(d: WorkCenter) {
    return http.post<any, ApiResp<unknown>>('/mes/work-center/upsert', d)
  },
  remove(wgCd: string) {
    return http.delete<any, ApiResp<unknown>>(`/mes/work-center/${wgCd}`)
  },
}

/** 工序费率 /api/mes/process-cost-rate（按工作中心维护生效区间费率） */
export const processCostRateApi = {
  list(wgCd?: string) {
    return http.get<any, ApiResp<ProcessCostRate[]>>('/mes/process-cost-rate', { params: { wgCd } })
  },
  save(d: ProcessCostRate) {
    return http.post<any, ApiResp<unknown>>('/mes/process-cost-rate/upsert', d)
  },
  remove(id: string) {
    return http.delete<any, ApiResp<unknown>>(`/mes/process-cost-rate/${id}`)
  },
}
