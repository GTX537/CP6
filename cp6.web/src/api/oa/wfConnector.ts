import http from '../http'

/** 连接器掩码视图（读端点 DTO）。hasAuth 指示是否已配置凭证；明文凭证绝不回显（后端 WfConnectorView）。 */
export interface WfConnectorItem {
  id: string
  name: string
  displayName: string
  baseUrl: string
  timeoutSec: number
  enabled: boolean
  hasAuth: boolean
}

/** 连接器保存请求。authJson=明文凭证：新建空→无认证；编辑空/null→保留原密文；非空→即写即加密。 */
export interface WfConnectorSaveBody {
  name: string
  displayName: string
  baseUrl: string
  authJson?: string | null
  timeoutSec: number
  enabled: boolean
}

const unwrap = (res: any) => res?.data ?? res

export const wfConnectorApi = {
  /** 列当前租户全部连接器（掩码）。 */
  list: async (): Promise<WfConnectorItem[]> => unwrap(await http.get('/oa/wf-connector')) ?? [],

  /** 取单个连接器（掩码）。 */
  get: async (id: string): Promise<WfConnectorItem> => unwrap(await http.get(`/oa/wf-connector/${id}`)),

  /** 新建。返回 { id }。E-WF-028 → 400（http 拦截器已 toast）。 */
  create: async (body: WfConnectorSaveBody): Promise<{ id: string }> =>
    unwrap(await http.post('/oa/wf-connector', body)),

  /** 编辑。authJson 留空=保留原密文。 */
  update: (id: string, body: WfConnectorSaveBody) => http.put(`/oa/wf-connector/${id}`, body),

  /** 启停切换。 */
  enable: (id: string, enabled: boolean) => http.post(`/oa/wf-connector/${id}/enabled`, { enabled }),
}
