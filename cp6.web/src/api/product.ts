import http from './http'
import type {
  ApiResult,
  ProductDto,
  ProductListItemDto,
  ProductQuery,
  ProductMemberDto,
  ProductBasicInfoDto,
  WipCheckResultDto,
} from '@/types/productMaster'

// MSBBPA050 / MSBBPA060 Web 製品マスタ API
// 对应后端 CP6.WebApi/Controllers/ProductController.cs
export const productApi = {
  /** MSBBPA060 分页一覧 */
  getList(params: ProductQuery) {
    return http.get<any, ApiResult<{ total: number; rows: ProductListItemDto[] }>>(
      '/products',
      { params, paramsSerializer: { indexes: null } }
    )
  },

  /** 採番（11 桁の次連番取得） */
  getNextSequence() {
    return http.get<any, ApiResult<{ sequence: string }>>('/products/next-seq')
  },

  /** 按製品 CD 查询单条（含 5 表全部） */
  getByCd(productCd: string, includeDeleted = false) {
    return http.get<any, ApiResult<ProductDto>>(
      `/products/${encodeURIComponent(productCd)}`,
      { params: { includeDeleted } }
    )
  },

  /** 登録 */
  create(data: ProductDto) {
    return http.post<any, ApiResult<ProductDto>>('/products', data)
  },

  /** 訂正 */
  update(productCd: string, data: ProductDto) {
    return http.put<any, ApiResult<ProductDto>>(
      `/products/${encodeURIComponent(productCd)}`,
      data
    )
  },

  /** 削除（軟削除） */
  remove(productCd: string, rowVersion?: string) {
    return http.delete<any, ApiResult<null>>(
      `/products/${encodeURIComponent(productCd)}`,
      { data: { rowVersion } }
    )
  },

  /** コピー */
  copy(productCd: string) {
    return http.post<any, ApiResult<ProductDto>>(
      `/products/${encodeURIComponent(productCd)}/copy`
    )
  },

  /** 仕掛チェック（mcframe7 連携無し時は常に level=0） */
  checkWip(productCd: string) {
    return http.get<any, ApiResult<WipCheckResultDto>>(
      `/products/check-wip/${encodeURIComponent(productCd)}`
    )
  },

  /** 御見積書 NO による部材一覧引入 */
  getMembersByQuotation(quotationNo: string) {
    return http.get<any, ApiResult<ProductMemberDto[]>>(
      `/products/by-quotation/${encodeURIComponent(quotationNo)}`
    )
  },

  /** 見積計算書 NO による基本情報引入 */
  getBasicInfoByEstimateCalc(estimateCalcNo: string) {
    return http.get<any, ApiResult<ProductBasicInfoDto>>(
      `/products/by-estimate-calc/${encodeURIComponent(estimateCalcNo)}`
    )
  },

  /** CSV 出力（一覧画面の現在検索条件） */
  exportCsv(params: ProductQuery) {
    return http.get<any, Blob>('/products/export.csv', {
      params,
      paramsSerializer: { indexes: null },
      responseType: 'blob',
    })
  },
}
