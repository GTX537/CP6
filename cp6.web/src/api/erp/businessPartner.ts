import http from '../http'
import type {
  ApiResult, BusinessPartnerDto, BpQueryDto, BpListItemDto,
} from '@/types/erp/businessPartner'

// MSBBPA110 / 120 — Web 取引先マスタ API
export const bpApi = {
  /** PA110 取引先 1 件取得 */
  getByCd(bpCd: string, includeDeleted = false) {
    return http.get<any, ApiResult<BusinessPartnerDto>>(
      `/business-partners/${encodeURIComponent(bpCd)}`,
      { params: { includeDeleted } },
    )
  },

  /** 取引先 CD 重複チェック */
  checkExists(bpCd: string) {
    return http.get<any, ApiResult<{ exists: boolean }>>(
      `/business-partners/check-exists/${encodeURIComponent(bpCd)}`,
    )
  },

  /** 新規登録（preRegister=true なら事前登録） */
  create(data: BusinessPartnerDto, preRegister = false) {
    return http.post<any, ApiResult<BusinessPartnerDto>>(
      '/business-partners', data, { params: { preRegister } },
    )
  },

  /** 訂正 */
  update(bpCd: string, data: BusinessPartnerDto) {
    return http.put<any, ApiResult<BusinessPartnerDto>>(
      `/business-partners/${encodeURIComponent(bpCd)}`, data,
    )
  },

  /** 削除（管理者のみ） */
  remove(bpCd: string, rowVersion?: string) {
    return http.delete<any, ApiResult<null>>(
      `/business-partners/${encodeURIComponent(bpCd)}`,
      { data: { rowVersion } },
    )
  },

  /** PA120 一覧検索 */
  search(query: BpQueryDto) {
    return http.get<any, ApiResult<{ rows: BpListItemDto[]; total: number }>>(
      '/business-partners/list',
      { params: query, paramsSerializer: { indexes: null } },
    )
  },

  /** PA120 CSV 出力 */
  exportCsv(query: BpQueryDto) {
    return http.get<Blob>('/business-partners/export-csv', {
      params: query,
      responseType: 'blob',
      paramsSerializer: { indexes: null },
    })
  },
}
