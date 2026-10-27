import http from '../http'
export type AuthoringField = 'Name' | 'SchemaJson' | 'FormKey' | 'FunctionId' | 'FlowCode'
export interface AuthoringView {
  definitionId: string; draftId: string; key: string; version: string; enabled: boolean; activationRevision?: string; published?: boolean; versionNumber?: number
  fields: Partial<Record<AuthoringField, string | null>>; writableFields: AuthoringField[]; diagnostics: string[]
}
export interface AuthoringCommand {
  commandId: string; expectedVersion: string | null
  body: { name: string; schemaJson: string; formKey: string | null; functionId: string | null; flowCode: string | null }
}
export interface AuthoringResult {
  commandId: string; outcome: string; resourceId: string | null; resourceVersion: string | null
  result: AuthoringView | null; recordedAtUtc: string | null; correlationId: string | null
}
export interface AuthoringEntry { canCreate: boolean; state: string; writableFields: AuthoringField[] }
const data = <T>(value: any): T => value?.data ?? value
export const flowAuthoringApi = {
  entry: async (signal?: AbortSignal) => data<AuthoringEntry>(await http.get('/oa/v2/definitions/FLOW/entry', { signal })),
  list: async (page: number, signal?: AbortSignal) => data<AuthoringView[]>(await http.get('/oa/v2/definitions/FLOW', { params: { page }, signal })),
  read: async (key: string, signal?: AbortSignal) => data<AuthoringView>(await http.get(`/oa/v2/definitions/FLOW/${encodeURIComponent(key)}`, { signal })),
  save: async (key: string, command: AuthoringCommand, signal?: AbortSignal) => data<AuthoringResult>(await http.put(`/oa/v2/definitions/FLOW/${encodeURIComponent(key)}/draft`, command, { signal })),
  original: async (key: string, command: string, signal?: AbortSignal) => data<AuthoringResult>(await http.get(`/oa/v2/commands/${encodeURIComponent(command)}`, { params: { operation: 'FLOW_SAVE_DRAFT', resource: key }, signal })),
}
