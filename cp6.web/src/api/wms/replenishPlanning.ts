import http from '../http'
export interface RepRef { owner: string; id: string; version: string; digestDomain: string; codecVersion: string; digest: string }
export interface RepQuantity { value: string; uomRef: RepRef }
export interface RepScope { siteRef: RepRef; warehouseRef: RepRef }
export interface RepRule { ref: RepRef; targetLocationRefs: RepRef[]; productRef: RepRef; technicalRef: RepRef; ownerRef: RepRef; baseUomRef: RepRef; minQuantity: RepQuantity; maxQuantity: RepQuantity; lotMultiple: RepQuantity | null; coverage: string }
export interface RepContext { tenantId: string; actorId: string; environmentId: string; scopes: { scope: RepScope; cutRef: RepRef; dataKind: string; coverage: string; rules: RepRule[]; draftOptions: RepOrderInput[] }[]; dependencyHealth: string; capabilities: string[]; dispatchCapability: string }
export interface RepRow { lineKey: string; ruleRef: RepRef; targetLocationRef: RepRef; productRef: RepRef; baseUomRef: RepRef; onHand: RepQuantity | null; openIncoming: RepQuantity | null; effectivePosition: RepQuantity | null; needed: RepQuantity | null; proposed: RepQuantity | null; allocationPlanRef: RepRef | null; coverage: string; reasons: string[]; sources: { sourceLocationRef: RepRef; quantity: RepQuantity; spans: { rootRef: RepRef; from: string; to: string }[] }[] }
export interface RepSuggestion { ref: RepRef; cutRef: RepRef; rows: RepRow[]; coverage: string }
export interface RepBatch { ref: RepRef; registration: string; dispatchSummary: string; items: { lineKey: string; totalQuantity: RepQuantity; outcome: string; splits: { splitKey: string; orderRef: RepRef; quantity: RepQuantity; outcome: string }[] }[] }
export interface RepCommand { commandKey: string; expectedRevision: string | null; reason: string }
export interface RepSpan { rootRef: RepRef; from: string; to: string }
export interface RepSource { sourceLocationRef: RepRef; spans: RepSpan[]; quantity: RepQuantity; stockObservationRef: RepRef; expiryAt: string | null; receivedAt: string | null; masterConditionRef: RepRef }
export interface RepOrderInput { scope: RepScope; productRef: RepRef; technicalRef: RepRef; ownerRef: RepRef; baseUomRef: RepRef; toLocationRef: RepRef; source: RepSource; quantity: RepQuantity; priority: number; trigger: string; suggestionRowRef: RepRef | null; reason: string; remarks: string | null }
export interface RepDraftInput { command: RepCommand; input: RepOrderInput }
export interface RepOrder { ref: RepRef; input: RepOrderInput; selectionRef: RepRef | null; lifecycle: string; knowledge: string; planned: RepQuantity; moved: RepQuantity | null; remaining: RepQuantity | null; executionLink: unknown; stockReceiptRefs: RepRef[]; capabilities: string[]; currentCutRef: RepRef | null; revision: string }
export interface RepPrepare { ref: RepRef; orderRef: RepRef; inputDigest: string; sourceSpans: RepSpan[]; capacityObservationRefs: RepRef[]; mode: string; adoptionRef: RepRef | null; blockers: string[]; canDispatch: boolean }
export interface RepSuggestInput { command: RepCommand; scope: RepScope; ruleRefs: RepRef[]; targetLocationRefs: RepRef[] }
export interface RepSelection { lineKey: string; allocationPlanRef: RepRef }
export interface RepBatchInput { command: RepCommand; suggestionRef: RepRef; selectedPlans: RepSelection[]; selectionDigest: string }
export type RepPending = { kind: 'suggest'; body: RepSuggestInput } | { kind: 'batch'; body: RepBatchInput }
const root = '/wms/replenish/v1'
export const replenishPlanningApi = {
  context: () => http.get<any, RepContext>(`${root}/context`),
  suggest: (body: RepSuggestInput) => http.post<any, RepSuggestion>(`${root}/suggestions`, body),
  generate: (body: RepBatchInput) => http.post<any, RepBatch>(`${root}/batches`, body),
  operation: <T = RepSuggestion | RepBatch>(key: string) => http.get<any, { commandKey: string; state: 'UNKNOWN' | 'COMMITTED'; result: T | null }>(`${root}/operations/${encodeURIComponent(key)}`),
  createDraft: (body: RepDraftInput) => http.post<any, RepOrder>(`${root}/orders`, body),
  editDraft: (id: string, body: RepDraftInput) => http.patch<any, RepOrder>(`${root}/orders/${encodeURIComponent(id)}`, body),
  order: (id: string) => http.get<any, RepOrder>(`${root}/orders/${encodeURIComponent(id)}`),
  history: (id: string, afterRevision = '0') => http.get<any, { items: RepOrder[]; nextRevision: string | null }>(`${root}/orders/${encodeURIComponent(id)}/history`, { params: { afterRevision, limit: 50 } }),
  prepare: (id: string, body: RepCommand) => http.post<any, RepPrepare>(`${root}/orders/${encodeURIComponent(id)}/prepare`, body),
}
export async function repSelectionDigest(selection: RepSelection[]) {
  const rows = [...selection].sort((a, b) => a.lineKey < b.lineKey ? -1 : a.lineKey > b.lineKey ? 1 : 0).map(x => ({ lineKey: x.lineKey, allocationPlanRef: { owner: x.allocationPlanRef.owner, id: x.allocationPlanRef.id, version: x.allocationPlanRef.version, digestDomain: x.allocationPlanRef.digestDomain, codecVersion: x.allocationPlanRef.codecVersion, digest: x.allocationPlanRef.digest } }))
  const bytes = new TextEncoder().encode(JSON.stringify(rows))
  return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))).map(x => x.toString(16).padStart(2, '0')).join('')
}
