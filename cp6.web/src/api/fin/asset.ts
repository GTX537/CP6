import http from '../http'
import type { ApiResp } from '@/types/fin/fin'
import type {
  AssetCategory, AssetCard, DepreciationEntryDto, DepreciationScheduleRow,
  DepreciationRun, AssetDisposal,
} from '@/types/fin/asset'

// A3 固定资产 API（/api/fin/asset-*）。http 拦截器已返 body，调用方取 res.data。

export const assetCategoryApi = {
  list() { return http.get<any, ApiResp<AssetCategory[]>>('/fin/asset-category') },
  get(id: string) { return http.get<any, ApiResp<AssetCategory>>(`/fin/asset-category/${id}`) },
  create(d: AssetCategory) { return http.post<any, ApiResp<{ id: string }>>('/fin/asset-category', d) },
  update(id: string, d: AssetCategory) { return http.put<any, ApiResp<unknown>>(`/fin/asset-category/${id}`, d) },
  remove(id: string) { return http.delete<any, ApiResp<unknown>>(`/fin/asset-category/${id}`) },
}

export const assetCardApi = {
  list(categoryId?: string, status?: number) {
    return http.get<any, ApiResp<AssetCard[]>>('/fin/asset-card', { params: { categoryId, status } })
  },
  get(id: string) { return http.get<any, ApiResp<AssetCard>>(`/fin/asset-card/${id}`) },
  create(d: AssetCard) { return http.post<any, ApiResp<{ id: string; assetNo: string }>>('/fin/asset-card', d) },
  update(id: string, d: AssetCard) { return http.put<any, ApiResp<unknown>>(`/fin/asset-card/${id}`, d) },
  activate(id: string) { return http.post<any, ApiResp<unknown>>(`/fin/asset-card/${id}/activate`, null) },
  schedule(id: string) { return http.get<any, ApiResp<DepreciationScheduleRow[]>>(`/fin/asset-card/${id}/schedule`) },
}

export const assetDeprecApi = {
  preview(periodId: string) {
    return http.get<any, ApiResp<DepreciationEntryDto[]>>('/fin/asset-deprec/preview', { params: { periodId } })
  },
  list(periodId?: string) {
    return http.get<any, ApiResp<DepreciationRun[]>>('/fin/asset-deprec/list', { params: { periodId } })
  },
  entries(runId: string) {
    return http.get<any, ApiResp<any[]>>(`/fin/asset-deprec/${runId}/entries`)
  },
  run(periodId: string) {
    return http.post<any, ApiResp<unknown>>('/fin/asset-deprec/run', null, { params: { periodId } })
  },
  setWorkload(entryId: string, workload: number) {
    return http.put<any, ApiResp<unknown>>(`/fin/asset-deprec/entry/${entryId}/workload`, { workload })
  },
  post(runId: string) { return http.post<any, ApiResp<unknown>>(`/fin/asset-deprec/${runId}/post`, null) },
  reverse(runId: string, reason: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/asset-deprec/${runId}/reverse`, { reason })
  },
}

export const assetDisposalApi = {
  list(status?: number, assetCardId?: string) {
    return http.get<any, ApiResp<AssetDisposal[]>>('/fin/asset-disposal', { params: { status, assetCardId } })
  },
  get(id: string) { return http.get<any, ApiResp<AssetDisposal>>(`/fin/asset-disposal/${id}`) },
  create(d: AssetDisposal) {
    return http.post<any, ApiResp<{ id: string; no: string }>>('/fin/asset-disposal', d)
  },
  confirm(id: string) { return http.post<any, ApiResp<unknown>>(`/fin/asset-disposal/${id}/confirm`, null) },
  reverse(id: string, reason: string) {
    return http.post<any, ApiResp<unknown>>(`/fin/asset-disposal/${id}/reverse`, { reason })
  },
}
