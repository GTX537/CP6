import http from '../http'
export type FormMask = Record<string, 'edit' | 'readonly' | 'hidden'>
export interface PersonalCatalogItem { formKey: string; name: string; category: string | null; subCategory: string | null; formVersionId: string; formVersion: number; favorite: boolean }
export interface PersonalFavoriteItem { formKey: string; available: boolean; publication: PersonalCatalogItem | null }
export interface PersonalFavorite { formKey: string; favorite: boolean; updatedAtUtc: string }
export interface PersonalContext {
  formKey: string; formVersionId: string; formVersion: number; name: string; schemaJson: string
  activationRevision: string; bindingRevision: string | null; flowVersionId: string | null
  flowVersion: number | null; flowActivationRevision: string | null; allowsUnbound: boolean
  capabilities: string[]; contextVersion: string; fieldMask: FormMask; titleAccess: string
}
export interface PersonalDraft {
  draftId: string; formKey: string; formVersionId: string; formVersion: number; latestPublishedVersion: number
  latestPublishedVersionId: string; name: string; schemaJson: string; dataJson: string; title: string | null
  rowVersion: string; status: number; stale: boolean; updatedAtUtc: string; fieldMask: FormMask; capabilities: string[]; titleAccess: string
}
export type PersonalDraftItem = Pick<PersonalDraft, 'draftId' | 'formKey' | 'name' | 'formVersion' | 'latestPublishedVersion' | 'title' | 'rowVersion' | 'updatedAtUtc' | 'stale'>
export interface FormSubmission { formDataId: string; formDefVersionId: string; formVersion: number; flowInstanceId: string | null; flowDefVersionId: string | null; flowVersion: number | null; receiptRef: string; commandId: string; bindingRevision: string | null; submissionState: string }
export interface PersonalResult { commandId: string; outcome: string; resourceId: string | null; resourceVersion: string | null; result: PersonalDraft | FormSubmission | PersonalFavorite | null }
export interface RebasePreview { previewId: string; digest: string; sourceVersionId: string; targetVersionId: string; expectedVersion: string; expiresAtUtc: string; changes: { path: string; kind: string; hasValue: boolean }[]; validationErrors: string[]; requiresConfirmation: boolean }
export interface ValuePreview { dataJson: string; hiddenFields: string[]; valid: boolean; errors: string[] }
export interface TaskVersion { taskId: string; instanceId: string; taskStatus: number; instanceStatus: number; taskRowVersion: string; instanceRowVersion: string }
export interface PersonalTask { task: TaskVersion; formDataId: string; rootInstanceId: string; formVersionId: string; name: string; schemaJson: string; dataJson: string; fieldMask: FormMask; allowedDecisions: string[]; kind: 'SFS'; formDataVersion: string; formKey: string; flowVersionId: string; nodeId: string; tokenId: string; stageIndex: number; stageRound: number }
export interface PersonalTaskResult { commandId: string; state: string; voteId: string | null; task: TaskVersion | null; queryRef: string }
const unwrap = <T>(v: any): T => v?.data ?? v
const key = encodeURIComponent
const root = '/oa/v2'
export const personalFormApi = {
  favorites: async (page: number, search: string, signal?: AbortSignal) => unwrap<{ items: PersonalFavoriteItem[]; total: number; page: number; pageSize: number }>(await http.get(`${root}/catalog/favorites`, { params: { page, search }, signal })),
  catalog: async (page: number, search: string, signal?: AbortSignal) => unwrap<{ items: PersonalCatalogItem[]; total: number; page: number; pageSize: number }>(await http.get(`${root}/forms/catalog`, { params: { page, search }, signal })),
  context: async (form: string, signal?: AbortSignal) => unwrap<PersonalContext>(await http.get(`${root}/forms/${key(form)}/initiation-context`, { signal })),
  references: async (form: string, field: string, search: string, draft?: string, signal?: AbortSignal) => unwrap<{ id: string; name: string }[]>(await http.get(`${root}/forms/${key(form)}/references/${key(field)}`, { params: { search, draft }, signal })),
  draft: async (id: string, signal?: AbortSignal) => unwrap<PersonalDraft>(await http.get(`${root}/drafts/${id}`, { signal })),
  list: async (page: number, signal?: AbortSignal) => unwrap<{ items: PersonalDraftItem[]; total: number; page: number; pageSize: number }>(await http.get(`${root}/drafts`, { params: { page }, signal })),
  send: async (url: string, method: 'post' | 'put', raw: string, signal?: AbortSignal) => unwrap<PersonalResult>(await http.request({ url, method, data: raw, headers: { 'Content-Type': 'application/json' }, signal })),
  original: async (operation: string, resource: string, command: string, signal?: AbortSignal) => unwrap<PersonalResult>(await http.get(`${root}/personal-forms/commands/${key(operation)}/${key(resource)}/${key(command)}`, { signal })),
  close: async (operation: string, resource: string, command: string, signal?: AbortSignal) => unwrap<PersonalResult>(await http.post(`${root}/personal-forms/commands/${key(operation)}/${key(resource)}/${key(command)}/close`, undefined, { signal })),
  preview: async (url: string, raw: string, signal?: AbortSignal) => unwrap<ValuePreview>(await http.post(url, raw, { headers: { 'Content-Type': 'application/json' }, signal })),
  rebasePreview: async (id: string, targetVersionId: string, expectedVersion: string, signal?: AbortSignal) => unwrap<RebasePreview>(await http.post(`${root}/drafts/${id}/rebase-preview`, { targetVersionId, expectedVersion }, { signal })),
  task: async (id: string, signal?: AbortSignal) => unwrap<PersonalTask>(await http.get(`${root}/personal-forms/tasks/${id}/context`, { signal })),
  decide: async (id: string, body: unknown, token: string, signal?: AbortSignal) => unwrap<PersonalTaskResult>(await http.post(`${root}/personal-forms/tasks/${id}/decision`, body, { headers: { 'If-Match': `"${token}"` }, signal })),
  decideSfs: async (id: string, raw: string, signal?: AbortSignal) => unwrap<PersonalResult>(await http.post(`${root}/personal-forms/tasks/${id}/decisions`, raw, { headers: { 'Content-Type': 'application/json' }, signal })),
  taskOriginal: async (id: string, command: string, signal?: AbortSignal) => unwrap<PersonalTaskResult | null>(await http.get(`${root}/personal-forms/tasks/${id}/commands/${command}`, { signal })),
}
export function submissionContext(c: PersonalContext, kind: 'DIRECT' | 'DRAFT') {
  return { kind, expectedFormVersionId: c.formVersionId, expectedActivationRevision: c.activationRevision,
    expectedBindingRevision: c.bindingRevision, expectedFlowVersionId: c.flowVersionId, expectedFlowActivationRevision: c.flowActivationRevision }
}
