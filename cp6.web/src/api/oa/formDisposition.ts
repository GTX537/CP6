import http from '../http'
export interface DispositionView {
  requestId: string; draftId: string; sourceVersionId: string; targetVersionId: string; version: string
  state: string; recordedAtUtc: string; hold: { version: string; active: boolean } | null
  dispositionRef: { requestId: string; receiptVersion: string } | null; retainUntilUtc: string | null
  consumption?: { commandRef: string; commandId: string; actualActorId: string; processorId: string; draftVersion: string; recordedAtUtc: string } | null
}
export interface DispositionDetail {
  request: DispositionView; historyId: string | null; retentionPolicyRef: string | null
  losses: { field: string; path: string; kind: string; valueJson: string }[] | null
  history: { version: string; previousVersion: string | null; state: string; actorId: string; reason: string; recordedAtUtc: string; retainUntilUtc: string | null }[] | null
}
export interface DispositionOperation { operationId: string; state: 'UNKNOWN' | 'CLOSED' | 'COMMITTED'; request: DispositionView | null }
const root = '/oa/form-field-dispositions/v1', unwrap = <T>(value: any): T => value?.data ?? value
export const formDispositionApi = {
  read: async (id: string, signal?: AbortSignal) => unwrap<DispositionDetail>(await http.get(`${root}/requests/${encodeURIComponent(id)}`, { signal })),
  write: async (id: string, action: string, input: unknown, signal?: AbortSignal) => unwrap<DispositionOperation>(await http.post(`${root}/requests/${encodeURIComponent(id)}/${action}`, input, { signal })),
  request: async (input: unknown, signal?: AbortSignal) => unwrap<DispositionOperation>(await http.post(`${root}/requests`, input, { signal })),
  original: async (id: string, close = false, signal?: AbortSignal) => unwrap<DispositionOperation>(await (close
    ? http.post(`${root}/operations/${encodeURIComponent(id)}/close`, undefined, { signal })
    : http.get(`${root}/operations/${encodeURIComponent(id)}`, { signal }))),
}
