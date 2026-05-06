import http from './http'
import type {
  ApiResult,
  PlateMoldDto,
  PlateMoldHistoryItemDto,
  PlateMoldQueryDto,
  PlateMoldListItemDto,
  PlateMoldOrderLinkResultDto,
  PlateMoldPeApiResultDto,
} from '@/types/plateMold'

export interface PlateMoldSaveResult {
  wdPtnNo: string
  wdRev: number
  orderLink: PlateMoldOrderLinkResultDto
  peApi: PlateMoldPeApiResultDto
}

export const plateMoldApi = {
  // ─── PA140 ───

  getNextSequence() {
    return http.get<any, ApiResult<{ sequence: string }>>('/plate-molds/next-seq')
  },
  getByEstimateCalc(calcNo: string) {
    return http.get<any, ApiResult<PlateMoldDto>>(
      `/plate-molds/by-estimate-calc/${encodeURIComponent(calcNo)}`
    )
  },
  getByNo(wdPtnNo: string, rev?: number) {
    return http.get<any, ApiResult<PlateMoldDto>>(
      `/plate-molds/${encodeURIComponent(wdPtnNo)}`,
      { params: { rev } }
    )
  },
  history(wdPtnNo: string) {
    return http.get<any, ApiResult<PlateMoldHistoryItemDto[]>>(
      `/plate-molds/${encodeURIComponent(wdPtnNo)}/history`
    )
  },
  create(dto: PlateMoldDto) {
    return http.post<any, ApiResult<PlateMoldSaveResult>>('/plate-molds', dto)
  },
  revise(wdPtnNo: string, dto: PlateMoldDto) {
    return http.put<any, ApiResult<PlateMoldSaveResult>>(
      `/plate-molds/${encodeURIComponent(wdPtnNo)}/revise`, dto
    )
  },
  update(wdPtnNo: string, wdRev: number, dto: PlateMoldDto) {
    return http.put<any, ApiResult<null>>(
      `/plate-molds/${encodeURIComponent(wdPtnNo)}/${wdRev}`, dto
    )
  },
  remove(wdPtnNo: string, wdRev: number, rowVersion?: string) {
    return http.delete<any, ApiResult<null>>(
      `/plate-molds/${encodeURIComponent(wdPtnNo)}/${wdRev}`,
      { params: { rowVersion } }
    )
  },

  // ─── PA150 ───

  search(query: PlateMoldQueryDto) {
    return http.get<any, ApiResult<{ rows: PlateMoldListItemDto[]; total: number }>>(
      '/plate-molds/list',
      { params: query, paramsSerializer: { indexes: null } }
    )
  },
  exportCsv(query: PlateMoldQueryDto) {
    return http.get<Blob>('/plate-molds/list/export.csv', {
      params: query,
      responseType: 'blob',
      paramsSerializer: { indexes: null },
    })
  },
  /** ラベル発行 CSV */
  issueLabel(targets: { wdPtnNo: string; wdRev: number }[]) {
    return http.post<Blob>('/plate-molds/label', { targets }, { responseType: 'blob' })
  },
}
