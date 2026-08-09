import http from '../http'

export interface DraftListItem {
  id: string
  formKey: string
  formName: string
  formVersion: number
  latestPublishedVersion: number
  dataJson: string
  title?: string
  updatedAtUtc: string
  stale: boolean
  rowVersion?: string
}

export interface DraftDetail extends DraftListItem {
  formDefVersionId: string
  schemaJson: string
  dataJson: string
}

export const draftApi = {
  list: (page = 1, pageSize = 20) =>
    http.get('/oa/drafts', { params: { page, pageSize } }),
  get: (id: string) => http.get(`/oa/drafts/${id}`),
  create: (formKey: string, data: Record<string, unknown>, title?: string) =>
    http.post(`/oa/forms/${encodeURIComponent(formKey)}/drafts`, { data, title }),
  update: (id: string, data: Record<string, unknown>, title: string | undefined, rowVersion?: string) =>
    http.put(`/oa/drafts/${id}`, { data, title, rowVersion }),
  rebase: (id: string, targetVersion: number, confirmRemovedValues: boolean, rowVersion?: string) =>
    http.post(`/oa/drafts/${id}/rebase`, { targetVersion, confirmRemovedValues, rowVersion }),
  submit: (id: string, rowVersion: string | undefined, idempotencyKey: string) =>
    http.post(`/oa/drafts/${id}/submit`, { rowVersion }, {
      headers: { 'Idempotency-Key': idempotencyKey },
    }),
  remove: (id: string) => http.delete(`/oa/drafts/${id}`),
}
