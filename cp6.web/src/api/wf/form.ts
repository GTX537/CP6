import http from '../http'

// OA 表单 API（/api/wf）。
export const formApi = {
  saveDef(data: { formKey: string; formName: string; schemaJson: string }) {
    return http.post('/wf/form/def', data)
  },
  getDef(formKey: string) {
    return http.get(`/wf/form/def/${formKey}`)
  },
  submitData(data: { formKey: string; bizId?: string; dataJson: string }) {
    return http.post('/wf/form/data', data)
  },
}
