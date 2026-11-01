import http from '../http'
import type { SchedApply, SchedInterval, SchedRef } from './scheduling'
export interface QueueChoice { queueId: string; siteId: string; resourceRef: SchedRef }
export interface QueueContext { tenantId: string; environmentId: string; actorId: string; resources: QueueChoice[] }
export interface QueueItem { member: { itemId: string; operationId: string; rootId: string; routeSortOrder: number }; queueOrdinal: number; state: string; interval: SchedInterval | null; reassessRequired: boolean; executionGateSummary: string }
export interface QueueView { captureId: string; queueId: string; siteId: string; resourceRef: SchedRef; queueRevision: string; items: QueueItem[]; manifestDigest: string; totalCount: number | null; completeForWrite: boolean; authorizationScope: string; asOf: string; expiresAt: string; nextCursor: number | null }
export interface QueueMove { resourceRef: SchedRef; expectedQueueRevision: string; captureId: string; moveItemId: string; position: 'BEFORE' | 'AFTER' | 'END'; beforeItemId: string | null; afterItemId: string | null; expectedManifestDigest: string; reason: string }
export interface QueuePlan { planId: string; revision: string; digest: string; expiresAt: string; input: QueueMove; oldOrder: string[]; newOrder: string[]; affectedItems: string[] }
export interface QueueReply { commandId: string; outcome: string; queueRef: SchedRef; auditRef: SchedRef; replayed: boolean }
const root = '/mes/scheduling'
export const queueApi = {
  context: () => http.get<QueueContext>(`${root}/queue-context`).then(r => r.data),
  read: (siteId: string, resourceId: string, captureId?: string, cursor = 0) => http.get<QueueView>(`${root}/resources/${resourceId}/queue`, { params: { siteId, captureId, cursor, pageSize: 20 } }).then(r => r.data),
  prepare: (input: QueueMove) => http.post<QueuePlan>(`${root}/resources/${input.resourceRef.id}/queue-plans`, input).then(r => r.data),
  plan: (id: string) => http.get<QueuePlan>(`${root}/queue-plans/${id}`).then(r => r.data),
  apply: (input: SchedApply) => http.post<QueueReply>(`${root}/queue-plans/${input.planId}/apply`, input).then(r => r.data),
  command: (id: string) => http.get<{ status: string; canInferNoEffect: false; queueReply?: QueueReply }>(`${root}/commands/${id}`).then(r => r.data)
}
