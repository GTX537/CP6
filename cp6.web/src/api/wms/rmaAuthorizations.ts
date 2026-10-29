import http from '../http'
import type { RmaQty, RmaRef } from './rmaFormalApplications'
import type { RmaPolicyVersion } from './rmaPolicies'
export interface ReturnRange { from: string; to: string }
export interface ReturnShare { shareId: string; rmaId: string; ranges: ReturnRange[]; quantity: RmaQty; rangeClaimRef: RmaRef; state: string; original: { deliveryProofRef: RmaRef | null } }
export interface AuthorizationDecision { decisionId: string; decisionVersion: number; decisionRef: RmaRef; applicationRef: RmaRef; outcome: 'APPROVED' | 'REJECTED' | 'REVOKED' | 'EXPIRED'; authorizedShares: ReturnShare[]; reasonCodes: string[]; decidedAt: string; validFrom: string; validTo: string | null; previousDecisionRef: RmaRef | null }
export interface AuthorizationLine { requestedLineId: string; applicationLineRef: RmaRef; entitlement: { ref: RmaRef; activeClaimQty: RmaQty; receivedQty: RmaQty; creditedQty: RmaQty; remainingAuthorizableQty: RmaQty; currentHeadRef: RmaRef; coverage: string }; coordinate: { baseUom: RmaQty['uom'] }; availableRanges: ReturnRange[]; evidenceMode: string }
export interface AuthorizationContext { applicationRef: RmaRef; authorityRef: RmaRef; policy: RmaPolicyVersion; lines: AuthorizationLine[]; currentDecision: AuthorizationDecision | null; currentShareRefs: RmaRef[] }
export interface AuthorizationDecide { expectedApplicationHeadRef: RmaRef; expectedEntitlementHeadRefs: RmaRef[]; outcome: 'APPROVED' | 'REJECTED'; authorizedShares: { requestedLineId: string; entitlementSnapshotRef: RmaRef; ranges: ReturnRange[]; quantity: RmaQty }[]; authorityRef: RmaRef; reasonCodes: string[]; validFrom: string; validTo: string | null; exceptionApprovalRef: null }
export interface AuthorizationRevoke { expectedDecisionHeadRef: RmaRef; affectedReturnShareRefs: RmaRef[]; authorityRef: RmaRef; reason: string }
export type AuthorizationPending = { version: 1; commandId: string; applicationId: string } & ({ kind: 'DECIDE'; body: AuthorizationDecide } | { kind: 'REVOKE'; body: AuthorizationRevoke })
const root = '/wms/rma/v1', headers = () => ({ 'X-Correlation-Id': crypto.randomUUID(), 'Cache-Control': 'no-store' })
export const rmaAuthorizationApi = {
  context: (application: string, includeEntitlements: boolean) => http.get<any, AuthorizationContext>(`${root}/applications/${encodeURIComponent(application)}/authorization/context`, { params: { includeEntitlements }, headers: headers() }),
  read: (application: string, version?: number) => http.get<any, AuthorizationDecision | null>(`${root}/applications/${encodeURIComponent(application)}/authorization`, { params: { version }, headers: headers() }),
  dispatch: (p: AuthorizationPending) => http.post<any, AuthorizationDecision>(`${root}/applications/${encodeURIComponent(p.applicationId)}/authorization/${p.kind === 'DECIDE' ? 'decide' : 'revoke'}`, p.body, { headers: { ...headers(), 'Idempotency-Key': p.commandId } }),
  query: (p: AuthorizationPending) => http.post<any, { status: 'FOUND'; value: AuthorizationDecision; canInferNoEffect: false } | { status: 'NOT_OBSERVED'; value: null; canInferNoEffect: false }>(`${root}/authorization/commands/query`, { applicationId: p.applicationId, commandId: p.commandId }, { headers: headers() }),
}
// Form quantities stay decimal strings; JavaScript Number cannot preserve the accepted 13+8 precision.
export function returnRangeMeasure(ranges: ReturnRange[]): string {
  const scale = 100000000n
  const parse = (text: string) => { if (!/^(0|[1-9][0-9]{0,12})(\.[0-9]{1,8})?$/.test(text)) throw new Error('请输入最多8位小数的有效数量位置。'); const [whole, fraction = ''] = text.split('.'); return BigInt(whole!) * scale + BigInt(fraction.padEnd(8, '0')) }
  let total = 0n
  for (const range of ranges) { const from = parse(range.from), to = parse(range.to); if (to <= from) throw new Error('区间终点必须大于起点。'); total += to - from }
  const fraction = (total % scale).toString().padStart(8, '0').replace(/0+$/, '')
  return (total / scale).toString() + (fraction ? '.' + fraction : '')
}
