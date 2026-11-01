import http from '../http'
import type { RootSchedule, SchedCapture, SchedInterval, SchedRef, SchedReply } from './scheduling'
export interface ArrangementCapture { captureId: string; siteId: string; asOf: string; expiresAt: string; coverage: string; manifestDigest: string; roots: { capture: SchedCapture }[] }
export interface ArrangementRequest { siteId: string; captureId: string; operationIds: string[]; baseTime: string; defaultDurationMinutes: number; algorithm: string; resourceScopeRefs: SchedRef[]; keepLocked: true; reason: string }
export interface ArrangementItem { rootId: string; operationId: string; occurrenceRef: SchedRef; resourceRef: SchedRef | null; oldInterval: SchedInterval | null; newInterval: SchedInterval | null; durationBasis: string; rank: number; inclusion: string; reasonCodes: string[] }
export interface ArrangementGroup { group: { groupId: string; rootWoId: string; oldRootSchedule: RootSchedule; newRootSchedule: RootSchedule; changedOperations: unknown[] }; capture: SchedCapture; predecessorGroupIds: string[]; eligible: boolean; reasonCodes: string[] }
export interface ArrangementPreview { previewId: string; revision: string; digest: string; expiresAt: string; input: ArrangementRequest; algorithmVersion: string; assumptions: string[]; items: ArrangementItem[]; manifestCount: number; manifestDigest: string; atomicGroups: ArrangementGroup[]; coverage: string }
export interface ArrangementPage extends ArrangementPreview { nextCursor: number | null }
export interface ArrangementApply { commandId: string; previewId: string; expectedPreviewRevision: string; expectedPreviewDigest: string; explicitAtomicGroupIds: string[]; expectedManifestDigest: string; reason: string }
export interface ArrangementResult { batchId: string; commandId: string; outcome: string; replayed: boolean; submittedCount: number; committedCount: number; rejectedCount: number; unknownCount: number; waitingCount: number; cancelledCount: number; groups: { groupId: string; rootId: string; state: string; errorCode: string | null; result: SchedReply['result'] | null }[] }
const route = '/mes/scheduling'
export const arrangementApi = {
  context: () => http.get<{ tenantId: string; actorId: string; environmentId: string }>(`${route}/preview-context`).then(r => r.data),
  capture: (siteId: string) => http.get<ArrangementCapture>(`${route}/arrangement-captures`, { params: { siteId } }).then(r => r.data),
  prepare: (body: ArrangementRequest) => http.post<ArrangementPreview>(`${route}/arrangement-previews`, body).then(r => r.data),
  preview: (id: string, cursor = 0) => http.get<ArrangementPage>(`${route}/arrangement-previews/${id}`, { params: { cursor, pageSize: 20 } }).then(r => r.data),
  batch: (id: string) => http.get<ArrangementResult>(`${route}/arrangement-batches/${id}`).then(r => r.data),
  apply: (body: ArrangementApply) => http.post<ArrangementResult>(`${route}/arrangement-previews/${body.previewId}/apply`, body).then(r => r.data),
  command: (id: string) => http.get<{ status: string; canInferNoEffect: false; batch?: ArrangementResult }>(`${route}/commands/${id}`).then(r => r.data),
  continue: (id: string) => http.post<ArrangementResult>(`${route}/arrangement-batches/${id}/continue`).then(r => r.data),
  stop: (id: string) => http.post<ArrangementResult>(`${route}/arrangement-batches/${id}/stop`).then(r => r.data)
}
