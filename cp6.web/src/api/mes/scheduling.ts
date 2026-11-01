import http from '../http'
export interface SchedRef { owner: string; id: string; version: string; digest: string }
export interface WoExpected { revision: string; controlEpoch: string; evidenceVersion: string; bindingRegistryRevision: string; snapshotId: string }
export interface SchedInterval { start: string; end: string }
export interface RootSchedule { interval: SchedInterval | null; priority: string; reason: string }
export interface SchedOperation { operationId: string; occurrenceRef: SchedRef; routeSortOrder: number; processLabel: string; scheduleRevision: string; interval: SchedInterval | null; assignmentRef: SchedRef | null; resourceRef: SchedRef | null; goodQty: { value: string } | null; defectQty: { value: string } | null }
export interface SchedContext { tenantId: string; actorId: string; environmentId: string; roots: { rootId: string; workOrderNo: string; siteRef: SchedRef; zoneId: string }[] }
export interface SchedCapture { captureId: string; asOf: string; expiresAt: string; dataKind: string; completeForRoot: boolean; root: { rootId: string; workOrderNo: string; zoneId: string; expected: WoExpected; schedule: RootSchedule; operations: SchedOperation[] } }
export interface SchedChange { captureId: string; operationId: string; expectedScheduleRevision: string; woExpected: WoExpected; kind: 'TIME_ONLY'; proposedStart: string; proposedEnd: string; proposedPriority: string | null; resourceChoiceRef: null; reason: string }
export interface SchedPlan { planId: string; revision: string; digest: string; eligible: boolean; input: SchedChange; capture: SchedCapture; rootApplyGroup: { oldRootSchedule: RootSchedule; newRootSchedule: RootSchedule; changedOperations: { operationId: string; newInterval: SchedInterval }[]; unchangedOperationRefs: SchedRef[] }; gates: { code: string; operationId: string; state: string }[] }
export interface SchedApply { commandId: string; planId: string; expectedPlanRevision: string; expectedPlanDigest: string; reason: string }
export interface SchedReply { commandId: string; outcome: string; workId: string; replayed: boolean; result: { applicationRef: SchedRef; oldWoExpected: WoExpected; newWoExpected: WoExpected; ownTransitionRef: SchedRef; operationResults: { operationId: string; oldScheduleRevision: string; newScheduleRevision: string; acceptedInterval: SchedInterval }[] } }
const root = '/mes/scheduling'
export const schedulingApi = {
  context: () => http.get<SchedContext>(`${root}/context`).then(r => r.data),
  capture: (id: string) => http.get<SchedCapture>(`${root}/roots/${id}/capture`).then(r => r.data),
  prepare: (body: SchedChange) => http.post<SchedPlan>(`${root}/change-plans`, body).then(r => r.data),
  plan: (id: string) => http.get<SchedPlan>(`${root}/change-plans/${id}`).then(r => r.data),
  apply: (body: SchedApply) => http.post<SchedReply>(`${root}/change-plans/${body.planId}/apply`, body).then(r => r.data),
  command: (id: string) => http.get<{ status: string; canInferNoEffect: false; reply?: SchedReply }>(`${root}/commands/${id}`).then(r => r.data)
}
