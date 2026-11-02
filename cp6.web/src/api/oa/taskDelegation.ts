import http from '@/api/http'
export type TaskAction = 'TASK_READ' | 'TASK_APPROVE' | 'TASK_REJECT' | 'TASK_SENDBACK' | 'TASK_TRANSFER'
export interface TaskGrantScope { actionKinds: TaskAction[]; flowKeys?: string[] | null; bizTypes?: string[] | null }
export interface TaskGrantInput { delegateId: string; validFromUtc: string; validToUtc: string; scope: TaskGrantScope; remark: string }
export interface TaskGrant {
  grantId: string; revision: string; version: string; grantorId: string; delegateId: string; fromUtc: string; toUtc: string
  scope: TaskGrantScope; enabled: boolean; revocationEpoch: string; grantedAtUtc: string; grantedBy: string
  revokedAtUtc: string | null; revokedBy: string | null; reason: string; sourceRef: string
}
export interface TaskGrantList { granted: TaskGrant[]; received: TaskGrant[]; actualUserId: string; observedAtUtc: string; legacyState: string }
export interface TaskGrantPrincipal { userId: string; name: string; grants: TaskGrant[] }
export interface TaskGrantResult { commandId: string; state: 'SUCCEEDED' | 'CLOSED' | 'NOT_OBSERVED'; result: TaskGrant | null; queryRef: string }
export interface TaskGrantIntent { identity: string; operation: 'CREATE' | 'REVISE' | 'REVOKE'; resource: string; command: string }
const root = '/oa/v2/delegations', key = encodeURIComponent
const unwrap = <T>(value: any): T => value?.data ?? value
export const taskDelegationApi = {
  list: async (signal?: AbortSignal) => unwrap<TaskGrantList>(await http.get(root, { signal })),
  received: async (signal?: AbortSignal) => unwrap<TaskGrantPrincipal[]>(await http.get(`${root}/received`, { signal })),
  targets: async (search: string, signal?: AbortSignal) => unwrap<{ id: string; name: string }[]>(await http.get(`${root}/targets`, { params: { search }, signal })),
  history: async (id: string, signal?: AbortSignal) => unwrap<TaskGrant[]>(await http.get(`${root}/${key(id)}/history`, { signal })),
  write: async (intent: TaskGrantIntent, raw: string, signal?: AbortSignal) => unwrap<TaskGrantResult>(await http.post(intent.operation === 'CREATE' ? root : `${root}/${key(intent.resource)}/${intent.operation === 'REVISE' ? 'revise' : 'revoke'}`, raw, { headers: { 'Content-Type': 'application/json' }, signal })),
  original: async (i: TaskGrantIntent, signal?: AbortSignal) => unwrap<TaskGrantResult>(await http.get(`${root}/commands/${i.operation}/${key(i.resource)}/${key(i.command)}`, { signal })),
  close: async (i: TaskGrantIntent, signal?: AbortSignal) => unwrap<TaskGrantResult>(await http.post(`${root}/commands/${i.operation}/${key(i.resource)}/${key(i.command)}/close`, undefined, { signal })),
}
