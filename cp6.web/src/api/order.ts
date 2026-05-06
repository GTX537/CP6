import http from './http'
import type {
  ApiResult,
  OrderDto,
  OrderDetailDto,
  OrderProcessDto,
  OrderMaterialDto,
  OrderQueryDto,
  OrderListItemDto,
  OrderPriceCorrectionQueryDto,
  OrderPriceCorrectionItemDto,
  OrderPriceCorrectionBatchUpdateDto,
  OrderPriceCorrectionBatchResultDto,
  IsEditableResultDto,
  OrderWipCheckResultDto,
  CreditCheckResultDto,
  LeadTimeRequestDto,
} from '@/types/order'

// MSBBPA070 / 080 / 090 Web 受注 API
// 对应后端 CP6.WebApi/Controllers/OrderController.cs
export const orderApi = {
  // ─────── PA070 CRUD ───────

  getNextSequence() {
    return http.get<any, ApiResult<{ sequence: string }>>('/orders/next-seq')
  },

  getByWebOrderNo(no: string, includeDeleted = false) {
    return http.get<any, ApiResult<OrderDto>>(
      `/orders/${encodeURIComponent(no)}`,
      { params: { includeDeleted } }
    )
  },

  create(data: OrderDto) {
    return http.post<any, ApiResult<OrderDto>>('/orders', data)
  },

  update(no: string, data: OrderDto) {
    return http.put<any, ApiResult<OrderDto>>(`/orders/${encodeURIComponent(no)}`, data)
  },

  remove(no: string, rowVersion?: string) {
    return http.delete<any, ApiResult<null>>(
      `/orders/${encodeURIComponent(no)}`,
      { params: { rowVersion } }
    )
  },

  // ─────── PA070 引入仕様（5 套） ───────

  lookupByHaibaiNo(no1?: string, no2?: string, no3?: string) {
    return http.get<any, ApiResult<OrderDto>>('/orders/by-haibai-no', {
      params: { no1, no2, no3 },
    })
  },

  lookupBySetProductCd(cd: string) {
    return http.get<any, ApiResult<OrderDetailDto[]>>(
      `/orders/by-set-product/${encodeURIComponent(cd)}`
    )
  },

  lookupProductMaster(cd: string) {
    return http.get<any, ApiResult<OrderDetailDto>>(
      `/orders/lookup-product-master/${encodeURIComponent(cd)}`
    )
  },

  lookupProductProcesses(cd: string) {
    return http.get<any, ApiResult<OrderProcessDto[]>>(
      `/orders/lookup-product-processes/${encodeURIComponent(cd)}`
    )
  },

  lookupProductMaterials(cd: string) {
    return http.get<any, ApiResult<OrderMaterialDto[]>>(
      `/orders/lookup-product-materials/${encodeURIComponent(cd)}`
    )
  },

  // ─────── PA070 業務ルール ───────

  calcIsEditable(orderType: string, catBig?: string, productCd?: string) {
    return http.get<any, ApiResult<IsEditableResultDto>>('/orders/calc-is-editable', {
      params: { orderType, catBig, productCd },
    })
  },

  checkWip(webOrderNo: string, detailNo: number) {
    return http.get<any, ApiResult<OrderWipCheckResultDto>>('/orders/check-wip', {
      params: { webOrderNo, detailNo },
    })
  },

  creditCheck(customerCd: string, amount: number) {
    return http.get<any, ApiResult<CreditCheckResultDto>>('/orders/credit-check', {
      params: { customerCd, amount },
    })
  },

  consignedCheck(webOrderNo: string, detailNo: number, qty: number) {
    return http.get<any, ApiResult<{ ok: boolean }>>('/orders/consigned-check', {
      params: { webOrderNo, detailNo, qty },
    })
  },

  calcLeadTime(req: LeadTimeRequestDto) {
    return http.post<any, ApiResult<string[]>>('/orders/lead-time', req)
  },

  // ─────── PA080 一覧 ───────

  searchList(query: OrderQueryDto) {
    return http.get<any, ApiResult<{ rows: OrderListItemDto[]; total: number }>>(
      '/orders/list',
      { params: query, paramsSerializer: { indexes: null } }
    )
  },

  exportListCsv(query: OrderQueryDto) {
    return http.get<Blob>('/orders/list/export.csv', {
      params: query,
      responseType: 'blob',
      paramsSerializer: { indexes: null },
    })
  },

  // ─────── PA090 単価訂正 ───────

  searchPriceCorrection(query: OrderPriceCorrectionQueryDto) {
    return http.get<any, ApiResult<{ rows: OrderPriceCorrectionItemDto[]; total: number }>>(
      '/orders/price-correction/list',
      { params: query, paramsSerializer: { indexes: null } }
    )
  },

  batchUpdatePrice(req: OrderPriceCorrectionBatchUpdateDto) {
    return http.put<any, ApiResult<OrderPriceCorrectionBatchResultDto>>(
      '/orders/price-correction/batch',
      req
    )
  },
}
