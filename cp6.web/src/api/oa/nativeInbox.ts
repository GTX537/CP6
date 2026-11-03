import http from '@/api/http'
import type { FormMask } from './personalForm'
export type InboxKind = 'pending' | 'cc' | 'running' | 'done'
export interface NativeInboxItem { instanceId: string; rootInstanceId: string; contextKind: 'SFS' | 'APP'; flowKey: string; instanceStatus: number; title: string | null; businessType: string | null; taskIds: string[]; ccIds: string[]; unreadCount: number; createdAt: string }
export interface NativeInboxPage { items: NativeInboxItem[]; totalItems: number; totalGroups: number; page: number; pageSize: number; rowMode: 'expanded' | 'merged'; actualUserId: string; effectiveUserId: string }
export interface NativeReadFact { kind: 'TASK' | 'CC'; targetId: string; principalId: string; actualUserId: string; firstReadAtUtc: string; firstCommandId: string }
export interface NativeTask { taskId: string; instanceId: string; nodeId: string; tokenId: string | null; stageIndex: number; stageRound: number; principalId: string; createdPrincipalId?: string; status: number; taskRowVersion: string; read: NativeReadFact | null; canRequestDecisionContext: boolean }
export interface NativeDetail {
  instanceId: string; rootInstanceId: string; contextKind: 'SFS' | 'APP'; actualUserId: string; effectiveUserId: string
  graph: { instanceId: string; parentInstanceId: string | null; flowVersionId: string; formVersionId: string | null; flowKey: string; status: number; instanceRowVersion: string }[]
  tasks: NativeTask[]; cc: { ccId: string; instanceId: string; recipientId: string; nodeId: string | null; read: NativeReadFact | null }[]
  history: { eventId: string; instanceId: string; nodeId: string; action: string; actorId: string; actorKind: string; comment: string | null; recordedAt: string }[]
  votes: { voteId: string; taskId: string; instanceId: string; approved: boolean; comment: string; principalId: string; actualUserId: string; onBehalfOfId: string | null; occurredAtUtc: string; evidenceKind: string }[]
  form: { formDataId: string; formVersionId: string; title: string | null; schemaJson: string; dataJson: string; fieldMask: FormMask; dataSource: string } | null
  business: { owner: string; businessType: string; subjectKind: string; subjectId: string; businessRevision: string; submissionId: string; detailRoute: string | null; applicationState: string } | null
}
export interface NativeReadResult { commandId: string; state: 'READ' | 'CLOSED' | 'NOT_OBSERVED'; result: NativeReadFact | null; queryRef: string }
export interface NativeReadIntent { identity: string; kind: 'TASK' | 'CC'; target: string; command: string }
const root = '/oa/v2/inbox', key = encodeURIComponent
const unwrap = <T>(value: any): T => value?.data ?? value
export const nativeInboxApi = {
  list: async (kind: InboxKind, page: number, rowMode: string, search: string, signal?: AbortSignal) => unwrap<NativeInboxPage>(await http.get(`${root}/${kind}`, { params: { page, rowMode, search }, signal })),
  detail: async (id: string, signal?: AbortSignal) => unwrap<NativeDetail>(await http.get(`${root}/instances/${id}`, { signal })),
  read: async (intent: NativeReadIntent, signal?: AbortSignal) => unwrap<NativeReadResult>(await http.post(`${root}/${intent.kind === 'TASK' ? 'tasks' : 'cc'}/${intent.target}/read`, { commandId: intent.command, expectedVersion: null, body: {} }, { signal })),
  original: async (intent: NativeReadIntent, signal?: AbortSignal) => unwrap<NativeReadResult>(await http.get(`${root}/read-commands/${intent.kind}/${intent.target}/${key(intent.command)}`, { signal })),
  close: async (intent: NativeReadIntent, signal?: AbortSignal) => unwrap<NativeReadResult>(await http.post(`${root}/read-commands/${intent.kind}/${intent.target}/${key(intent.command)}/close`, undefined, { signal })),
}
