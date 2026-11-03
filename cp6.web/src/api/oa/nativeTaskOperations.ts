import http from '@/api/http'
import type { FormMask, TaskVersion } from './personalForm'
export type TaskOperation = 'OA_TASK_TRANSFER' | 'OA_TASK_ADDSIGN' | 'OA_TASK_SENDBACK' | 'OA_APPROVER_RESOLVE' | 'SFS_RETURNED_EDIT' | 'SFS_RETURNED_RESUBMIT'
export interface OperationResult { commandId: string; outcome: 'SUCCEEDED' | 'CLOSED' | 'NOT_OBSERVED'; result: unknown }
export interface OperationIntent { identity: string; operation: TaskOperation; resource: string; command: string }
export interface BackTarget { targetKind: 'STARTER' | 'NODE' | 'PREV_STAGE'; targetNodeId: string | null }
export interface BackPreview extends BackTarget { taskId: string; instanceId: string; scope: string; affectedTaskIds: string[]; affectedTokenIds: string[]; wholeInstanceRefill: boolean; taskVersion: string; instanceVersion: string; previewDigest: string }
export interface OperationContext { task: TaskVersion; allowedActions: string[]; candidates: { userId: string; name: string }[]; sendBackTargets: (BackTarget & { name: string })[] }
export interface ReturnedContext { instanceId: string; formDataId: string; returnRevision: number; executionCycleId: string; formDefVersionId: string; flowDefVersionId: string; instanceVersion: string; formDataVersion: string; name: string; schemaJson: string; dataJson: string; fieldMask: FormMask; canEdit: boolean; canResubmit: boolean; reasons: string[] }
export interface ResolutionContext { resolutionId: string; resolutionVersion: string; instanceId: string; instanceVersion: string; executionCycleId: string; nodeId: string; tokenId: string; stageIndex: number; stageRound: number; flowDefVersionId: string; routePolicyRef: string; reason: string; state: string; candidatePrincipalIds: string[]; candidates: { userId: string; name: string }[]; canAssign: boolean; blockingReasons: string[] }
const root = '/oa/v2', key = encodeURIComponent, unwrap = <T>(v: any): T => v?.data ?? v
const route = (i: OperationIntent) => i.operation === 'SFS_RETURNED_EDIT' ? `${root}/instances/${i.resource}/returned-form`
  : i.operation === 'SFS_RETURNED_RESUBMIT' ? `${root}/instances/${i.resource}/resubmit`
    : i.operation === 'OA_APPROVER_RESOLVE' ? `${root}/pending-resolutions/${i.resource}/assign`
      : `${root}/tasks/${i.resource}/${{ OA_TASK_TRANSFER: 'transfer', OA_TASK_ADDSIGN: 'addsign', OA_TASK_SENDBACK: 'sendback' }[i.operation]}`
const originalRoute = (i: OperationIntent) => `${root}/${i.operation.startsWith('SFS_') ? 'personal-forms/commands' : 'tasks/operations'}/${i.operation}/${i.resource}/${key(i.command)}`
export const nativeTaskOperationsApi = {
  context: async (task: string, signal?: AbortSignal) => unwrap<OperationContext>(await http.get(`${root}/tasks/${task}/operations-context`, { signal })),
  preview: async (task: string, target: BackTarget, signal?: AbortSignal) => unwrap<BackPreview>(await http.post(`${root}/tasks/${task}/sendback-preview`, target, { signal })),
  returned: async (instance: string, signal?: AbortSignal) => unwrap<ReturnedContext>(await http.get(`${root}/instances/${instance}/returned-form`, { signal })),
  resolutions: async (instance: string, signal?: AbortSignal) => unwrap<ResolutionContext[]>(await http.get(`${root}/instances/${instance}/pending-resolutions`, { signal })),
  resolution: async (id: string, signal?: AbortSignal) => unwrap<ResolutionContext>(await http.get(`${root}/pending-resolutions/${id}/context`, { signal })),
  send: async (i: OperationIntent, raw: string) => unwrap<OperationResult>(await http.request({ url: route(i), method: i.operation === 'SFS_RETURNED_EDIT' ? 'put' : 'post', data: raw, headers: { 'Content-Type': 'application/json' } })),
  original: async (i: OperationIntent) => unwrap<OperationResult | null>(await http.get(originalRoute(i))),
  close: async (i: OperationIntent) => unwrap<OperationResult>(await http.post(originalRoute(i) + '/close')),
}
