import http from '../http'
import type {
  ApiResult,
  QuotationDto,
  QuotationQuery,
  QuotationListItem,
  QuotationCalcCandidate,
  QuotationConfirmRequest,
  QuotationIssueRequest,
} from '@/types/erp/quotation'

// MSBBPA030 / MSBBPA040 御見積書 API
// 对应后端 CP6.WebApi/Controllers/QuotationController.cs
export const quotationApi = {
  /** MSBBPA040 分页一覧 */
  getList(params: QuotationQuery) {
    return http.get<any, ApiResult<{ total: number; rows: QuotationListItem[] }>>(
      '/quotations',
      { params, paramsSerializer: { indexes: null } } // statuses=0&statuses=9
    )
  },

  /** 按 No 查询单条（含 calcs / details） */
  getByNo(qtnNo: string, includeDeleted = false) {
    return http.get<any, ApiResult<QuotationDto>>(
      `/quotations/${encodeURIComponent(qtnNo)}`,
      { params: { includeDeleted } }
    )
  },

  /** 登録 */
  create(data: QuotationDto) {
    return http.post<any, ApiResult<QuotationDto>>('/quotations', data)
  },

  /** 訂正 */
  update(qtnNo: string, data: QuotationDto) {
    return http.put<any, ApiResult<QuotationDto>>(
      `/quotations/${encodeURIComponent(qtnNo)}`,
      data
    )
  },

  /** 削除（逻辑删除） */
  remove(qtnNo: string, rowVersion?: string) {
    return http.delete<any, ApiResult<null>>(
      `/quotations/${encodeURIComponent(qtnNo)}`,
      { data: { rowVersion } }
    )
  },

  /** コピー新建 */
  copy(qtnNo: string) {
    return http.post<any, ApiResult<QuotationDto>>(
      `/quotations/${encodeURIComponent(qtnNo)}/copy`
    )
  },

  /** 確定登録 */
  confirm(qtnNo: string, req: QuotationConfirmRequest) {
    return http.post<any, ApiResult<QuotationDto>>(
      `/quotations/${encodeURIComponent(qtnNo)}/confirm`,
      req
    )
  },

  /** 確定取消 */
  cancelConfirm(qtnNo: string, rowVersion?: string) {
    return http.post<any, ApiResult<QuotationDto>>(
      `/quotations/${encodeURIComponent(qtnNo)}/cancel-confirm`,
      { rowVersion }
    )
  },

  /** 発行（PDF 生成 + 発行日更新） */
  issue(qtnNo: string, req: QuotationIssueRequest) {
    return http.post<any, ApiResult<{ files: string[] }>>(
      `/quotations/${encodeURIComponent(qtnNo)}/issue`,
      req
    )
  },

  /**
   * 取得関連見積計算書候选（§2.6.1 案件NO設定後联动刷新）
   * currentQuotationNo 传入时，已经关联的计算书 isLinked=true
   */
  getCalcCandidates(params: {
    customerCd: string
    projectNoParent?: string
    projectNoChild?: string
    projectNoMaterial?: string
    currentQuotationNo?: string
  }) {
    return http.get<any, ApiResult<QuotationCalcCandidate[]>>('/quotations/calcs', { params })
  },
}
