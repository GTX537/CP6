import http from '../http'
import type { AuthoringView } from './flowAuthoring'
export interface Activation {
  definitionId: string; key: string; revision: string; enabled: boolean; publishedVersionId: string | null
  changedAtUtc: string | null; inflightUnchanged: boolean; consumerState: string
}
export interface ActivationPreview {
  current: Activation; versions: { versionId: string; number: number; name: string | null }[]
  page: number; affectedEntries: string[]; inflightCount: number | null
}
export interface ActivationHistory {
  revision: string; previousRevision: string; enabled: boolean; publishedVersionId: string; actorId: string; reason: string; changedAtUtc: string
}
export interface ActivationIntent {
  key: string; definitionId: string
  command: { commandId: string; expectedVersion: string; body: { enabled: boolean; expectedActivationRevision: string; publishedVersionId: string; reason: string } }
}
export interface ActivationResult {
  commandId: string; outcome: string; resourceId: string | null; resourceVersion: string | null; result: Activation | null
}
const root = (key: string) => `/oa/v2/definitions/FLOW/${encodeURIComponent(key)}`
const data = <T>(v: any): T => v?.data ?? v
export const flowActivationApi = {
  list: async (page: number, signal?: AbortSignal) => data<AuthoringView[]>(await http.get('/oa/v2/activations/FLOW', { params: { page }, signal })),
  preview: async (key: string, page: number, signal?: AbortSignal) => data<ActivationPreview>(await http.get(root(key) + '/activation', { params: { page }, signal })),
  history: async (key: string, page: number, signal?: AbortSignal) => data<ActivationHistory[]>(await http.get(root(key) + '/activation-history', { params: { page }, signal })),
  execute: async (intent: ActivationIntent, signal?: AbortSignal) => data<ActivationResult>(await http.post(root(intent.key) + '/activation', intent.command, { signal })),
  original: async (intent: ActivationIntent, signal?: AbortSignal) => data<ActivationResult>(await http.get(`/oa/v2/commands/${encodeURIComponent(intent.command.commandId)}`, { params: { operation: 'FLOW_SET_ACTIVATION', resource: intent.key }, signal })),
}
