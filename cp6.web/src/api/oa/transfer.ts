import http from '../http'

export const transferApi = {
  transfer: (taskId: string, toUserId: string, comment?: string) =>
    http.post('/oa/inbox/transfer', { taskId, toUserId, comment }),
}
