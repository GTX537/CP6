import http from '../http'
import type { SchedRef, SchedInterval, WoExpected, SchedCapture } from './scheduling'
export interface BoardSite { siteRef: SchedRef; zoneId: string; defaultWindow: SchedInterval; processes: SchedRef[]; workgroups: SchedRef[]; resources: SchedRef[] }
export interface BoardContext { tenantId: string; environmentId: string; actorId: string; sites: BoardSite[]; actorCapabilities: string[] }
export interface BoardQuery { siteId: string; from: string; to: string; zoneId: string; statuses: string; processRef?: string; workgroupRef?: string; resourceRef?: string; sort: string }
export interface BoardMetric { value: string | null; unit: string; coverage: string }
export interface BoardBar { capabilities: string[]; gates: { code: string; state: string }[]; ownerLinks: Record<string, string>; operationId: string; woId: string; workOrderNo: string; productLabel: string; processLabel: string; routeSortOrder: number; occurrenceRef: SchedRef | null; snapshotRef: SchedRef | null; woExpected: WoExpected | null; scheduleRevision: string | null; queueHeads: SchedRef[]; processRef: SchedRef | null; workgroupRef: SchedRef | null; resourceRef: SchedRef | null; resourceAssignmentRef: SchedRef | null; plannedInterval: SchedInterval | null; actualInterval: SchedInterval | null; actualCoverage: string; deliveryDate: string | null; planQty: { value: string } | null; goodQty: { value: string } | null; defectQty: { value: string } | null; state: string; workOrderState: string; priority: string; coverage: string; mode: string; delayed: boolean; conflicts: { operationId: string | null; interval: SchedInterval; coverage: string }[] }
export interface BoardPage { captureId: string; queryScopeHash: string; asOf: string; expiresAt: string; zoneId: string; bars: BoardBar[]; nextCursor: number | null; kpi: Record<string, BoardMetric | string>; coverage: string; warnings: string[]; dataKind: string }
export interface BoardDetail { operation: BoardBar; rootScheduleContext: SchedCapture['root'] | null; completeForRoot: boolean; zoneId: string; asOf: string }
const root = '/mes/scheduling'
export const schedulingBoardApi = {
  context: () => http.get<BoardContext>(`${root}/board-context`).then(r => r.data),
  board: (query: BoardQuery, captureId?: string, cursor?: number) => http.get<BoardPage>(`${root}/board`, { params: { ...query, captureId, cursor, pageSize: 20 } }).then(r => r.data),
  detail: (id: string) => http.get<BoardDetail>(`${root}/operations/${id}`).then(r => r.data)
}
