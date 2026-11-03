import http from '@/api/http'
import type { OperationResult } from './nativeTaskOperations'
export interface TransferFilter { fromUserId: string; toUserId: string; reason: string; flowKey: string | null; beforeUtc: string | null; explicitTaskIds: string[] | null }
export interface TransferPreview { batchId: string; previewedAt: string; previewDigest: string; filter: TransferFilter; items: { taskId: string; instanceId: string; contextKind: 'APP' | 'SFS'; flowKey: string; taskVersion: string; instanceVersion: string; itemCommandId: string }[] }
export interface TransferReport { batchId: string; items: { taskId: string; itemCommandId: string; state: 'SUCCEEDED' | 'REJECTED' | 'UNKNOWN'; error: string | null; result: OperationResult | null }[] }
const root = '/oa/v2/task-batches', unwrap = <T>(value: any): T => value?.data ?? value
export const nativeTaskBatchApi = {
  preview: async (raw: string) => unwrap<TransferPreview>(await http.post(root + '/transfer-preview', raw, { headers: { 'Content-Type': 'application/json' } })),
  original: async (command: string) => unwrap<TransferPreview | null>(await http.get(root + '/transfer-previews/commands/' + encodeURIComponent(command))),
  execute: async (batchId: string, previewDigest: string) => unwrap<TransferReport>(await http.post(root + '/transfer', { batchId, previewDigest })),
  report: async (batchId: string) => unwrap<TransferReport>(await http.get(root + '/' + batchId)),
}
