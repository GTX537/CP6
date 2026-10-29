import http from '../http'
import type { RmaQty, RmaRef } from './rmaFormalApplications'
import type { AuthorizationDecision, ReturnRange, ReturnShare } from './rmaAuthorizations'
export interface PhysicalLine { lineId: string; returnShareRef: RmaRef; ranges: ReturnRange[]; quantity: RmaQty; locationOptionRef: RmaRef; lotRefs: RmaRef[]; serialRefs: RmaRef[]; lpnRefs: RmaRef[]; occurredAt: string }
export interface PhysicalRegister { expectedAuthorizationHeadRef: RmaRef; expectedRmaHeadRef: RmaRef; receiptLines: PhysicalLine[]; remarks: string | null }
export interface PhysicalAction { expectedWorkRef: RmaRef; expectedRmaHeadRef: RmaRef; reason: string }
export interface PhysicalWork {
  workRef: RmaRef; work: { workId: string; revision: number; state: 'REGISTERED' | 'PREPARED' | 'SUCCEEDED' | 'SEALED_NO_EFFECT' }; rmaHeadRef: RmaRef
  lines: { reference: RmaRef; input: PhysicalLine; location: { warehouseCode: string; locationCode: string; label: string } }[]
  preparations: { stockPostingPlanRequest: { use: { action: 'RETURN' | 'RECEIVE' } }; canCommit: boolean; bindings: { routeBinding: { route: string } }[] }[]
  results: { owner: string; receiptOperationRef: RmaRef; stockResult: { status: string; commitKnowledge: string; simulation: boolean }; physicalReceipts: { quantity: RmaQty; movementRef: RmaRef; receiptState: string }[] }[]
  noEffectProofRef: RmaRef | null
}
export interface PhysicalContext {
  rmaRef: RmaRef; headRef: RmaRef; authorization: AuthorizationDecision | null
  shares: { shareRef: RmaRef; share: ReturnShare & { original: { lotRefs: RmaRef[]; serialRefs: RmaRef[]; lpnRefs: RmaRef[] } }; availableRanges: ReturnRange[] }[]
  locations: { optionRef: RmaRef; warehouseId: string; locationId: string; warehouseCode: string; locationCode: string; label: string }[]; works: PhysicalWork[]
}
export type PhysicalPending = { version: 1; commandId: string; applicationId: string } & ({ kind: 'REGISTER'; body: PhysicalRegister } | { kind: 'PREPARE' | 'COMMIT' | 'SEAL'; workId: string; body: PhysicalAction })
const root = '/wms/rma/v1', headers = () => ({ 'X-Correlation-Id': crypto.randomUUID(), 'Cache-Control': 'no-store' })
export const rmaPhysicalApi = {
  context: (app: string) => http.get<any, PhysicalContext>(`${root}/applications/${encodeURIComponent(app)}/physical/context`, { headers: headers() }),
  read: (app: string, work: string, revision?: number) => http.get<any, PhysicalWork>(`${root}/applications/${encodeURIComponent(app)}/physical/works/${encodeURIComponent(work)}`, { params: { revision }, headers: headers() }),
  dispatch: (p: PhysicalPending) => http.post<any, PhysicalWork>(`${root}/applications/${encodeURIComponent(p.applicationId)}/physical/${p.kind === 'REGISTER' ? 'registrations' : `works/${encodeURIComponent(p.workId)}/${p.kind.toLowerCase()}`}`, p.body, { headers: { ...headers(), 'Idempotency-Key': p.commandId } }),
  query: (p: PhysicalPending) => http.post<any, { status: 'FOUND'; value: PhysicalWork; canInferNoEffect: false } | { status: 'NOT_OBSERVED'; value: null; canInferNoEffect: false }>(`${root}/physical/commands/query`, { applicationId: p.applicationId, commandId: p.commandId }, { headers: headers() }),
}
