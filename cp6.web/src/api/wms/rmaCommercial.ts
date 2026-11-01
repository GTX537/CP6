import http from '../http'
import type { RmaQty, RmaRef } from './rmaFormalApplications'
export const commercialOutcomes = { CREDIT: '贷项', REFUND: '贷项与退款授权', REPLACEMENT: '换货', SUPPLEMENTARY_SHIPMENT: '补发', RETURN_ONLY: '仅退货', REJECT: '申请拒绝处理' } as const
export interface CommercialRequest { expectedRmaHeadRef: RmaRef; expectedAuthorizationRef: RmaRef; returnShareRefs: RmaRef[]; requestedOutcome: keyof typeof commercialOutcomes; reasonCode: string }
export interface CommercialWork {
  requestRef: RmaRef; request: { requestId: string; requestedOutcome: keyof typeof commercialOutcomes; reasonCode: string }; revision: number
  observationState: string; evidenceMode: string; reasonCodes: string[]; observedAt: string; canInferNoEffect: false; financialSourceCurrent?: boolean
  commercialOutcome: { outcomeRef: RmaRef; resultVersion: string; components: { kind: string; quantity?: RmaQty; approvedQuantity?: RmaQty; authorizedAmount?: { value: string; currency: string }; invoiceAllocations?: { gross: { value: string; currency: string } }[] }[] } | null
  financialOutcome: { aggregateState: string; axes: { axis: string; state: string; amount: string | null; currency: string | null; coverage: string; postingReceiptRefs: RmaRef[] }[] } | null
}
export interface CommercialContext { applicationRef: RmaRef; authorization: { decisionRef: RmaRef; outcome: string } | null; shareRefs: RmaRef[]; requests: CommercialWork[]; capabilities: string[] }
export interface CommercialPending { version: 1; applicationId: string; commandId: string; kind: 'request'; body: CommercialRequest }
const root = '/wms/rma/v1/commercial'
const headers = () => ({ 'X-Correlation-Id': crypto.randomUUID(), 'Cache-Control': 'no-store' })
export const rmaCommercialApi = {
  context: (id: string) => http.get<any, CommercialContext>(`${root}/applications/${encodeURIComponent(id)}/context`, { headers: headers() }),
  dispatch: (p: CommercialPending) => http.post<any, CommercialWork>(`${root}/applications/${encodeURIComponent(p.applicationId)}/requests`, p.body, { headers: { ...headers(), 'Idempotency-Key': p.commandId } }),
  query: (p: CommercialPending) => http.post<any, { status: 'FOUND'; value: CommercialWork } | { status: 'NOT_OBSERVED'; value: null }>(`${root}/commands/query`, { applicationId: p.applicationId, commandId: p.commandId }, { headers: headers() }),
  observe: (id: string, request: string, submit: boolean) => http.post<any, CommercialWork>(`${root}/applications/${encodeURIComponent(id)}/requests/${encodeURIComponent(request)}/${submit ? 'dispatch' : 'refresh'}`, {}, { headers: headers() }),
}
