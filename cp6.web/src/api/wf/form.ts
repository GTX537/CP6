import http from '../http'

// OA 表单 API（/api/wf）。
export const formApi = {
  saveDef(data: { formKey: string; formName: string; schemaJson: string }) {
    return http.post('/wf/form/def', data)
  },
  getDef(formKey: string) {
    return http.get(`/wf/form/def/${formKey}`)
  },
  getDraft(formKey: string) {
    return http.get(`/oa/form-defs/${encodeURIComponent(formKey)}/draft`)
  },
  saveDraft(formKey: string, data: { name: string; schemaJson: string; rowVersion?: string }) {
    return http.put(`/oa/form-defs/${encodeURIComponent(formKey)}/draft`, data)
  },
  publish(formKey: string, rowVersion?: string) {
    return http.post(`/oa/form-defs/${encodeURIComponent(formKey)}/publish`, { rowVersion })
  },
  submitData(data: { formKey: string; bizId?: string; dataJson: string }) {
    return http.post('/wf/form/data', data)
  },
  submit(formKey: string, data: Record<string, unknown>, idempotencyKey: string, draftId?: string) {
    return http.post(
      `/oa/forms/${encodeURIComponent(formKey)}/submissions`,
      { data, draftId },
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },
}
