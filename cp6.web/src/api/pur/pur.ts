import http from '../http'
import type {
  ApiResp, SupplierPrice, PurchaseOrder, PoCreateForm,
  GoodsReceipt, GrCreateForm, ThreeWayMatch, MatchInvoiceForm, MatchResult,
  Rfq, RfqQuoteLineForm, RfqSelectionForm,
  PurchaseRequest, PrCreateForm,
} from '@/types/pur/pur'

// 采购 API（/api/pur）。http 拦截器已返 body：{ code, message, data }，调用方取 res.data。

/** 采购价表 /api/pur/supplier-price（供应商×物料 阶梯价 + 带价解析） */
export const supplierPriceApi = {
  list(supplierId: string, itemId?: string) {
    return http.get<any, ApiResp<SupplierPrice[]>>('/pur/supplier-price', { params: { supplierId, itemId } })
  },
  resolve(supplierId: string, itemId: string, qty: number, onDate?: string) {
    return http.get<any, ApiResp<{ price: number | null }>>('/pur/supplier-price/resolve', {
      params: { supplierId, itemId, qty, onDate },
    })
  },
  save(data: SupplierPrice) {
    return http.post<any, ApiResp<SupplierPrice>>('/pur/supplier-price', data)
  },
  remove(id: string) {
    return http.delete<any, ApiResp<unknown>>(`/pur/supplier-price/${id}`)
  },
}

/** 采购订单 /api/pur/po（建单带出 + 派生状态机 + 送审/取消） */
export const poApi = {
  list(supplierId?: string, status?: number) {
    return http.get<any, ApiResp<PurchaseOrder[]>>('/pur/po', { params: { supplierId, status } })
  },
  get(poNo: string) {
    return http.get<any, ApiResp<PurchaseOrder>>(`/pur/po/${poNo}`)
  },
  create(data: PoCreateForm) {
    return http.post<any, ApiResp<PurchaseOrder>>('/pur/po', data)
  },
  submit(poNo: string) {
    return http.post<any, ApiResp<PurchaseOrder>>(`/pur/po/${poNo}/submit`)
  },
  cancel(poNo: string) {
    return http.post<any, ApiResp<unknown>>(`/pur/po/${poNo}/cancel`)
  },
}

/** 收货 /api/pur/gr（双基准收货 + 检收应用，回写 PO 三累计锚） */
export const grApi = {
  list(poNo?: string, supplierId?: string) {
    return http.get<any, ApiResp<GoodsReceipt[]>>('/pur/gr', { params: { poNo, supplierId } })
  },
  get(grNo: string) {
    return http.get<any, ApiResp<GoodsReceipt>>(`/pur/gr/${grNo}`)
  },
  confirm(data: GrCreateForm) {
    return http.post<any, ApiResp<GoodsReceipt>>('/pur/gr', data)
  },
  applyQc(grNo: string) {
    return http.post<any, ApiResp<GoodsReceipt>>(`/pur/gr/${grNo}/apply-qc`)
  },
}

/** 三单匹配 /api/pur/match（容差匹配→自动建应付 / 挂起→放行·拒绝） */
export const matchApi = {
  list(poNo?: string, status?: number) {
    return http.get<any, ApiResp<ThreeWayMatch[]>>('/pur/match', { params: { poNo, status } })
  },
  get(matchNo: string) {
    return http.get<any, ApiResp<ThreeWayMatch>>(`/pur/match/${matchNo}`)
  },
  match(data: MatchInvoiceForm) {
    return http.post<any, ApiResp<MatchResult>>('/pur/match', data)
  },
  release(matchNo: string, note?: string) {
    return http.post<any, ApiResp<MatchResult>>(`/pur/match/${matchNo}/release`, { note })
  },
  reject(matchNo: string, note?: string) {
    return http.post<any, ApiResp<ThreeWayMatch>>(`/pur/match/${matchNo}/reject`, { note })
  },
}

/** 采购申请 /api/pur/pr（需求入口：手工建单 + 送审 + PR→PO 按建议供应商分组转单） */
export const prApi = {
  list(status?: number, source?: string) {
    return http.get<any, ApiResp<PurchaseRequest[]>>('/pur/pr', { params: { status, source } })
  },
  get(prNo: string) {
    return http.get<any, ApiResp<PurchaseRequest>>(`/pur/pr/${prNo}`)
  },
  create(data: PrCreateForm) {
    return http.post<any, ApiResp<PurchaseRequest>>('/pur/pr', data)
  },
  submit(prNo: string) {
    return http.post<any, ApiResp<PurchaseRequest>>(`/pur/pr/${prNo}/submit`)
  },
  convert(prNo: string) {
    return http.post<any, ApiResp<string[]>>(`/pur/pr/${prNo}/convert`)
  },
}

/** 询价 /api/pur/rfq（价格发现：从PR发起→邀供应商→收报价→比价→选定→回写价表→转PO） */
export const rfqApi = {
  list(status?: number) {
    return http.get<any, ApiResp<Rfq[]>>('/pur/rfq', { params: { status } })
  },
  get(rfqNo: string) {
    return http.get<any, ApiResp<Rfq>>(`/pur/rfq/${rfqNo}`)
  },
  createFromPr(prNo: string) {
    return http.post<any, ApiResp<Rfq>>(`/pur/rfq/from-pr/${prNo}`)
  },
  addSuppliers(rfqNo: string, supplierIds: string[]) {
    return http.post<any, ApiResp<Rfq>>(`/pur/rfq/${rfqNo}/suppliers`, supplierIds)
  },
  recordQuote(rfqNo: string, supplierId: string, lines: RfqQuoteLineForm[]) {
    return http.post<any, ApiResp<Rfq>>(`/pur/rfq/${rfqNo}/quote`, { supplierId, lines })
  },
  rank(rfqNo: string, asOf?: string) {
    return http.post<any, ApiResp<Rfq>>(`/pur/rfq/${rfqNo}/rank`, { asOf })
  },
  select(rfqNo: string, selections: RfqSelectionForm[]) {
    return http.post<any, ApiResp<Rfq>>(`/pur/rfq/${rfqNo}/select`, selections)
  },
  writeBack(rfqNo: string) {
    return http.post<any, ApiResp<Rfq>>(`/pur/rfq/${rfqNo}/writeback`)
  },
  convert(rfqNo: string) {
    return http.post<any, ApiResp<string[]>>(`/pur/rfq/${rfqNo}/convert`)
  },
}
