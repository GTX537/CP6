import http from '../http'

// OA 流程 API（/api/wf）。http 拦截器已返 body：{ code, message, data }，调用方取 res.data。
export const flowApi = {
  // 流程定义
  saveDef(data: { flowKey: string; flowName: string; formKey: string; schemaJson: string }) {
    return http.post('/wf/flow/def', data)
  },
  getDef(flowKey: string) {
    return http.get(`/wf/flow/def/${flowKey}`)
  },
  getDraft(flowKey: string) {
    return http.get(`/oa/flow-defs/${encodeURIComponent(flowKey)}/draft`)
  },
  saveDraft(flowKey: string, data: { name: string; formKey?: string; schemaJson: string; rowVersion?: string }) {
    return http.put(`/oa/flow-defs/${encodeURIComponent(flowKey)}/draft`, data)
  },
  publish(flowKey: string, rowVersion?: string) {
    return http.post(`/oa/flow-defs/${encodeURIComponent(flowKey)}/publish`, { rowVersion })
  },

  // 起流程 / 办理
  submit(data: { flowKey: string; varsJson?: string; bizType?: string; bizId?: string }) {
    return http.post('/wf/flow/submit', data)
  },
  act(taskId: string, data: { approve: boolean; comment?: string }) {
    return http.post(`/wf/task/${taskId}/act`, data)
  },

  // 实例详情（含痕迹）
  instance(id: string) {
    return http.get(`/wf/flow/instance/${id}`)
  },

  // 待办中心
  myTodos() {
    return http.get('/wf/my-todos')
  },
  myApplications() {
    return http.get('/wf/my-applications')
  },
  withdraw(instanceId: string) {
    return http.post(`/wf/flow/${instanceId}/withdraw`)
  },
}
