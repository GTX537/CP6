import http from '../http'
import type { SaveFlowBody } from '@/types/oa/designer'

export const designerApi = {
  list:  (functionId?: string) => http.get('/oa/designer/list', { params: { functionId } }),
  load:  (flowKey: string) => http.get(`/oa/designer/load/${flowKey}`),
  save:  (body: SaveFlowBody) => http.post('/oa/designer/save', body),
  clone: (sourceFlowKey: string, newFlowKey: string, newFlowName: string) =>
           http.post('/oa/designer/clone', { sourceFlowKey, newFlowKey, newFlowName }),
}
