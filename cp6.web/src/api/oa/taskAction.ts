import http from '@/api/http'
import type { PersonalTask, FormMask, TaskVersion } from './personalForm'
export interface TaskActionContext {
  taskId: string; instanceId: string; contextKind: 'SFS' | 'APP'; flowDefVersionId: string; formDefVersionId: string | null
  taskRowVersion: string; instanceRowVersion: string; formDataVersion: string | null; tokenId: string; nodeId: string; stageIndex: number; stageRound: number
  expectedAssigneeId: string; currentPrincipalId: string; createdPrincipalId: string; actualHandlerId: string; onBehalfOfId: string | null
  currentStatus: number; instanceStatus: number; allowedActions: string[]; fieldMask: FormMask
  grantId: string | null; grantRevision: string | null; approvalSubjectRef: { owner: string; id: string; version: string } | null
  form: PersonalTask | null; commentRequired: boolean; commentLimit: number; commandIdProfile: string
}
export interface AppTaskResult { commandId: string; state: 'COMPLETED'; result: { voteId: string; decisionId: string | null; task: TaskVersion }; queryRef: string }
export interface AppTaskResolution { commandId: string; state: 'COMPLETED' | 'CLOSED' | 'NOT_OBSERVED'; result: AppTaskResult | null }
const root = '/oa/v2/tasks', key = encodeURIComponent
const unwrap = <T>(v: any): T => v?.data ?? v
export const taskActionApi = {
  context: async (task: string, signal?: AbortSignal) => unwrap<TaskActionContext>(await http.get(`${root}/${key(task)}/action-context`, { signal })),
  decideApp: async (task: string, raw: string) => unwrap<AppTaskResult>(await http.post(`${root}/${key(task)}/decisions`, raw, { headers: { 'Content-Type': 'application/json' } })),
  originalApp: async (task: string, command: string) => unwrap<AppTaskResolution>(await http.get(`${root}/${key(task)}/decision-commands/${key(command)}`)),
  closeApp: async (task: string, command: string) => unwrap<AppTaskResolution>(await http.post(`${root}/${key(task)}/decision-commands/${key(command)}/close`, {})),
}
