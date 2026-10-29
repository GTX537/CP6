import http from '../http'
import type { EngineeringEnvelope } from '../plm/engineeringProducts'
export interface RmaRef { owner: string; id: string; version: string; digest: string }
export interface RmaQty { value: string; uom: { code: string; version: string } }
export interface RmaOriginal { shipmentRef: RmaRef; shipmentLineId: string; shipmentLineVersion: string; orderRef: RmaRef; orderLineId: string; productRef: RmaRef; baseQty: RmaQty; commercialSnapshotRef: RmaRef | null }
export interface SalesReturnTerms { currencyCode: string; fxRate: string; headerSalesPriceDivision: string | null; quantity: string | null; quantityUnit: string | null; priceUnit: string | null; setUnitPrice: string | null; individualUnitPrice: string | null; amount: string | null; specialPriceFlag: string | null; provisionalPrice: boolean; lineSalesPriceDivision: string | null }
export interface SalesReturnSource {
  reference: RmaRef
  original: { shipmentRef: RmaRef; shipmentLineId: string; orderRef: RmaRef; orderLineId: string; orderNumber: string; orderLineNumber: number; productCode: string; customerRef: RmaRef; capturedAt: string; terms: SalesReturnTerms }
  current: { customerCode: string; productCode: string; orderDeleted: boolean; lineDeleted: boolean; cancelledAt: string | null; terms: SalesReturnTerms } | null
  currentState: 'LINE_MISSING' | 'TERMS_UNCHANGED' | 'TERMS_CHANGED'
}
export interface RmaOption { optionRef: RmaRef; original: RmaOriginal; remainingAuthorizableQuantity: RmaQty | null; coverage: string; gates: string[] }
export interface RmaPage<T> { items: T[]; nextCursor: string | null; snapshotRef: RmaRef; capturedAt: string; coverage: string }
export interface RmaContext { scopeOptions: { optionRef: RmaRef; warehouseId: string; siteId: string; label: string; capabilities: string[] }[]; capabilities: string[] }
export interface RmaRequestedLine { lineId: string; originalSelectionRef: RmaRef; requestedQuantity: RmaQty; reasonDetail: string | null; evidenceRefs: RmaRef[] }
export interface RmaIntent { customerRef: RmaRef; reasonCode: string; requestedOutcome: string; lines: RmaRequestedLine[]; remarks: string | null }
export interface RmaCreate extends RmaIntent { scopeOptionRef: RmaRef; clientApplicationId: string }
export interface RmaTransition { expectedApplicationVersion: number; expectedApplicationHeadRef: RmaRef; reason: string }
export interface RmaEdit extends RmaIntent, RmaTransition {}
export interface FormalApplication {
  applicationId: string; applicationVersion: number; applicationRef: RmaRef; state: 'DRAFT' | 'SUBMITTED' | 'WITHDRAWN' | 'SUPERSEDED'
  scope: { warehouseId: string; siteId: string }; customerRef: RmaRef; reasonCode: string; requestedOutcome: string; requestedAt: string
  requestedLines: (RmaRequestedLine & { originalSnapshot: RmaOriginal })[]
}
export interface RmaSummary { rmaId: string; displayNo: string; customerRef: RmaRef; applicationState: FormalApplication['state']; recordedAt: string; headRef: RmaRef }
export interface RmaSourceLabels { outboundNumber: string; customerCode: string; customerRef: RmaRef; facts: { reference: RmaRef; originalJson: string }[] }
export interface RmaOrderFact { number: string; lines: { id: string; number: number; productCode: string }[] }
export interface RmaSearch { scopeOptionRef: RmaRef; keyword: string | null; customerRef: RmaRef | null; shipmentRef: RmaRef | null; deliveredOnly: boolean; cursor: string | null; pageSize: number }
export interface RmaListQuery { scopeOptionRef: RmaRef; states: string[]; cursor: string | null; pageSize: number }
export type RmaCommandKind = 'CREATE' | 'EDIT' | 'SUBMIT' | 'WITHDRAW'
export interface RmaPending { version: 1; commandId: string; applicationId: string; kind: RmaCommandKind; scopeOptionRef: RmaRef; body: RmaCreate | RmaEdit | RmaTransition; etag: string | null }
export const rmaRoute = (kind: RmaCommandKind) => '/api/wms/rma/v1/applications' + (kind === 'CREATE' ? '' : '/{applicationId}' + (kind === 'SUBMIT' ? '/submit' : kind === 'WITHDRAW' ? '/withdrawals' : ''))
const root = '/wms/rma/v1', headers = () => ({ 'X-Correlation-Id': crypto.randomUUID(), 'Cache-Control': 'no-store' })
export const rmaFormalApi = {
  commercial: (outboundId: string, lineId: string) => http.get<any, EngineeringEnvelope<SalesReturnSource>>(`/erp/sales-return-sources/v1/${encodeURIComponent(outboundId)}/${encodeURIComponent(lineId)}`, { headers: headers() }).then(x => x.data),
  context: () => http.get<any, RmaContext>(`${root}/context`, { headers: headers() }),
  sources: (q: RmaSearch) => http.post<any, RmaPage<RmaOption>>(`${root}/original-fulfilment-options/search`, q, { headers: headers() }),
  labels: (outboundId: string) => http.get<any, EngineeringEnvelope<RmaSourceLabels>>(`/wms/rma/native-applications-v1/sources/${encodeURIComponent(outboundId)}/fulfilment`).then(x => x.data),
  list: (q: RmaListQuery) => {
    const params = new URLSearchParams({ pageSize: String(q.pageSize) })
    for (const [key, value] of Object.entries(q.scopeOptionRef)) params.set(`scopeOptionRef.${key}`, value)
    for (const state of q.states) params.append('states', state)
    if (q.cursor) params.set('cursor', q.cursor)
    return http.get<any, RmaPage<RmaSummary>>(`${root}/applications`, { params, headers: headers() })
  },
  read: (id: string, version?: number) => http.get<any, FormalApplication>(`${root}/applications/${encodeURIComponent(id)}`, { params: { version }, headers: headers() }),
  editContext: (id: string) => http.get<any, { application: FormalApplication; remarks: string | null }>(`${root}/applications/${encodeURIComponent(id)}/edit-context`, { headers: headers() }),
  dispatch: (p: RmaPending) => http.request<any, FormalApplication>({
    url: rmaRoute(p.kind).replace('/api', '').replace('{applicationId}', encodeURIComponent(p.applicationId)),
    method: p.kind === 'EDIT' ? 'PUT' : 'POST', data: p.body,
    headers: { ...headers(), 'Idempotency-Key': p.commandId, ...(p.etag ? { 'If-Match': p.etag } : {}) },
  }),
  query: (p: RmaPending) => http.post<any, { status: 'FOUND'; resultRefs: RmaRef[] } | { status: 'NOT_OBSERVED' }>(`${root}/commands/query`, {
    scopeOptionRef: p.scopeOptionRef, commandId: p.commandId, method: p.kind === 'EDIT' ? 'PUT' : 'POST', routeTemplate: rmaRoute(p.kind),
  }, { headers: headers() }),
}
