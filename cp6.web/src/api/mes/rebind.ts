import http from '../http'
import type { SchedApply, SchedCapture, SchedChange, SchedContext, SchedInterval, SchedPlan, SchedRef, SchedReply, WoExpected } from './scheduling'
export interface RebindChoice { choiceRef: SchedRef; resourceRef: SchedRef; useInterval: SchedInterval; qualified: boolean; expiresAt: string; protocol: string }
export interface RebindContext { contextId: string; capture: SchedCapture; operationId: string; oldBindingRef: SchedRef; oldClaims: SchedRef[]; oldExecutions: { workRef: SchedRef; state: string }[]; choices: RebindChoice[]; completeForRebind: boolean; dataKind: string }
export interface RebindWork { workId: string; planId: string; rootId: string; operationId: string; revision: string; state: string; pendingPhase: string | null; stopped: boolean; noNewExecution: boolean; accepted: WoExpected; receipts: { phase: string; outcome: string; receiptRef: SchedRef; failureCode?: string | null }[]; bindingResult: SchedReply['result'] | null; queueMigrationState: string }
export interface RebindReply { commandId: string; outcome: string; work: RebindWork; replayed: boolean }
export interface RebindAction { commandId: string; expectedWorkRevision: string; reason: string }
const root = '/mes/scheduling'
export const rebindApi = {
  workContext: () => http.get<Omit<SchedContext, 'roots'>>(`${root}/work-context`).then(r => r.data),
  context: () => http.get<SchedContext>(`${root}/context`).then(r => r.data),
  capture: (id: string) => http.get<SchedCapture>(`${root}/roots/${id}/capture`).then(r => r.data),
  rebindContext: (rootId: string, operationId: string) => http.get<RebindContext>(`${root}/operations/${operationId}/rebind-context`, { params: { rootId } }).then(r => r.data),
  prepare: (body: SchedChange) => http.post<SchedPlan>(`${root}/change-plans`, body).then(r => r.data),
  plan: (id: string) => http.get<SchedPlan>(`${root}/change-plans/${id}`).then(r => r.data),
  apply: (body: SchedApply) => http.post<RebindReply>(`${root}/change-plans/${body.planId}/apply`, body).then(r => r.data),
  read: (id: string) => http.get<RebindWork>(`${root}/rebind-works/${id}`).then(r => r.data),
  continue: (id: string, body: RebindAction) => http.post<RebindReply>(`${root}/rebind-works/${id}/continue`, body).then(r => r.data),
  stop: (id: string, body: RebindAction) => http.post<RebindReply>(`${root}/rebind-works/${id}/stop`, body).then(r => r.data),
  reconcile: (id: string) => http.post<RebindWork>(`${root}/rebind-works/${id}/reconcile`).then(r => r.data),
  migrate: (id: string) => http.post(`${root}/rebind-works/${id}/migrate-queue`).then(r => r.data),
  command: (id: string) => http.get<{ status: string; canInferNoEffect: false; rebindReply?: RebindReply }>(`${root}/commands/${id}`).then(r => r.data)
}
