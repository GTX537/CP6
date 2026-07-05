import http from '../http'
import type { SaveFlowBody } from '@/types/oa/designer'

// 服务目录（C-T3 后端 GET /oa/designer/service-catalog）。Ok2 包壳 {code,message,data}，
// data 为 camelCase {actions,connectors}，各项 {name,label}。剥壳沿用 DesignerView 的 `res.data ?? res` 约定。
export interface ServiceCatalogItem { name: string; label: string }
export interface ServiceCatalog { actions: ServiceCatalogItem[]; connectors: ServiceCatalogItem[] }

export const designerApi = {
  list:  (functionId?: string) => http.get('/oa/designer/list', { params: { functionId } }),
  load:  (flowKey: string) => http.get(`/oa/designer/load/${flowKey}`),
  save:  (body: SaveFlowBody) => http.post('/oa/designer/save', body),
  clone: (sourceFlowKey: string, newFlowKey: string, newFlowName: string) =>
           http.post('/oa/designer/clone', { sourceFlowKey, newFlowKey, newFlowName }),
  getServiceCatalog: async (): Promise<ServiceCatalog> => {
    const res = await http.get('/oa/designer/service-catalog') as any
    const d = res?.data ?? res
    return { actions: d?.actions ?? [], connectors: d?.connectors ?? [] }
  },
}
