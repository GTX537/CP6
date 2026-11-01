import http from '../http'
export interface RepRef { owner: string; id: string; version: string; digestDomain: string; codecVersion: string; digest: string }
export interface RepQuantity { value: string; uomRef: RepRef }
export interface RepScope { siteRef: RepRef; warehouseRef: RepRef }
export interface RepRule { ref: RepRef; targetLocationRefs: RepRef[]; productRef: RepRef; technicalRef: RepRef; ownerRef: RepRef; baseUomRef: RepRef; minQuantity: RepQuantity; maxQuantity: RepQuantity; lotMultiple: RepQuantity | null; coverage: string }
export interface RepContext { tenantId: string; actorId: string; environmentId: string; scopes: { scope: RepScope; cutRef: RepRef; dataKind: string; coverage: string; rules: RepRule[] }[]; dependencyHealth: string; capabilities: string[]; dispatchCapability: string }
export interface RepRow { lineKey: string; ruleRef: RepRef; targetLocationRef: RepRef; productRef: RepRef; baseUomRef: RepRef; onHand: RepQuantity | null; openIncoming: RepQuantity | null; effectivePosition: RepQuantity | null; needed: RepQuantity | null; proposed: RepQuantity | null; allocationPlanRef: RepRef | null; coverage: string; reasons: string[]; sources: { sourceLocationRef: RepRef; quantity: RepQuantity; spans: { rootRef: RepRef; from: string; to: string }[] }[] }
export interface RepSuggestion { ref: RepRef; cutRef: RepRef; rows: RepRow[]; coverage: string }
export interface RepBatch { ref: RepRef; registration: string; dispatchSummary: string; items: { lineKey: string; totalQuantity: RepQuantity; outcome: string; splits: { splitKey: string; orderRef: RepRef; quantity: RepQuantity; outcome: string }[] }[] }
export interface RepCommand { commandKey: string; expectedRevision: null; reason: string }
export interface RepSuggestInput { command: RepCommand; scope: RepScope; ruleRefs: RepRef[]; targetLocationRefs: RepRef[] }
export interface RepSelection { lineKey: string; allocationPlanRef: RepRef }
export interface RepBatchInput { command: RepCommand; suggestionRef: RepRef; selectedPlans: RepSelection[]; selectionDigest: string }
export type RepPending = { kind: 'suggest'; body: RepSuggestInput } | { kind: 'batch'; body: RepBatchInput }
const root = '/wms/replenish/v1'
export const replenishPlanningApi = {
  context: () => http.get<any, RepContext>(`${root}/context`),
  suggest: (body: RepSuggestInput) => http.post<any, RepSuggestion>(`${root}/suggestions`, body),
  generate: (body: RepBatchInput) => http.post<any, RepBatch>(`${root}/batches`, body),
  operation: (key: string) => http.get<any, { commandKey: string; state: 'UNKNOWN' | 'COMMITTED'; result: RepSuggestion | RepBatch | null }>(`${root}/operations/${encodeURIComponent(key)}`),
}
export async function repSelectionDigest(selection: RepSelection[]) {
  const rows = [...selection].sort((a, b) => a.lineKey < b.lineKey ? -1 : a.lineKey > b.lineKey ? 1 : 0).map(x => ({ lineKey: x.lineKey, allocationPlanRef: { owner: x.allocationPlanRef.owner, id: x.allocationPlanRef.id, version: x.allocationPlanRef.version, digestDomain: x.allocationPlanRef.digestDomain, codecVersion: x.allocationPlanRef.codecVersion, digest: x.allocationPlanRef.digest } }))
  const bytes = new TextEncoder().encode(JSON.stringify(rows))
  return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))).map(x => x.toString(16).padStart(2, '0')).join('')
}
