import http from '../http'
import type { ApprovalPanelDetail } from '@/types/oa/approval'
import type { ApiResp } from '@/types/pur/pur'

export const approvalApi = {
  detail(bizType: string, bizId: string) {
    return http.get<any, ApiResp<ApprovalPanelDetail>>('/oa/approval/detail', {
      params: { bizType, bizId },
    })
  },
  decide(taskId: string, decision: 'approve' | 'reject', comment?: string) {
    return http.post(`/oa/tasks/${taskId}/decision`, {
      decision,
      comment,
      dataPatch: {},
      expectedFormDataRowVersion: null,
    })
  },
}
