import http from '../http'
export type FormAuthoringField = 'Name' | 'SchemaJson' | 'Category' | 'SubCategory'
export interface FormAuthoringView {
  definitionId: string; draftId: string; key: string; version: string; enabled: boolean; activationRevision?: string; published?: boolean; versionNumber?: number
  fields: Partial<Record<FormAuthoringField, string | null>>; writableFields: FormAuthoringField[]
}
export interface FormAuthoringCommand {
  commandId: string; expectedVersion: string | null
  body: { name: string; schemaJson: string; category: string | null; subCategory: string | null }
}
export interface FormAuthoringResult {
  commandId: string; outcome: string; resourceId: string | null; resourceVersion: string | null
  result: FormAuthoringView | null; recordedAtUtc: string | null; correlationId: string | null
}
export interface FormAuthoringEntry { canCreate: boolean; state: string; writableFields: FormAuthoringField[] }
const data = <T>(value: any): T => value?.data ?? value
export const formAuthoringApi = {
  entry: async (signal?: AbortSignal) => data<FormAuthoringEntry>(await http.get('/oa/v2/definitions/FORM/entry', { signal })),
  list: async (page: number, signal?: AbortSignal) => data<FormAuthoringView[]>(await http.get('/oa/v2/definitions/FORM', { params: { page }, signal })),
  read: async (key: string, signal?: AbortSignal) => data<FormAuthoringView>(await http.get(`/oa/v2/definitions/FORM/${encodeURIComponent(key)}/draft`, { signal })),
  save: async (key: string, command: FormAuthoringCommand, signal?: AbortSignal) => data<FormAuthoringResult>(await http.put(`/oa/v2/definitions/FORM/${encodeURIComponent(key)}/draft`, command, { signal })),
  original: async (key: string, command: string, signal?: AbortSignal) => data<FormAuthoringResult>(await http.get(`/oa/v2/commands/${encodeURIComponent(command)}`, { params: { operation: 'FORM_SAVE_DRAFT', resource: key }, signal })),
}
