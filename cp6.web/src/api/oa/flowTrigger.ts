import http from '../http'

export interface FlowTriggerItem {
  id: string
  flowKey: string
  triggerType: number
  enabled: boolean
  eventKey?: string | null
  starterUserId: string
  nextDueUtc?: string | null
  lastFiredUtc?: string | null
  hasApiKey: boolean
  configJson: string
}

export interface TriggerFireItem {
  id: string
  idempotencyKey: string
  firedUtc: string
  instanceId?: string | null
  source: number
  error?: string | null
}

export interface FlowTriggerSaveBody {
  flowKey: string
  triggerType: number
  configJson: string
  enabled: boolean
  eventKey?: string | null
  starterUserId: string
}

const unwrap = (res: any) => res?.data ?? res

export const flowTriggerApi = {
  list: async (): Promise<FlowTriggerItem[]> => unwrap(await http.get('/oa/flow-triggers/list')) ?? [],
  get: async (id: string): Promise<FlowTriggerItem> => unwrap(await http.get(`/oa/flow-triggers/${id}`)),
  create: async (body: FlowTriggerSaveBody): Promise<{ id: string; apiKeyPlain?: string | null }> =>
    unwrap(await http.post('/oa/flow-triggers', body)),
  update: (id: string, body: FlowTriggerSaveBody) => http.put(`/oa/flow-triggers/${id}`, body),
  enable: (id: string, enabled: boolean) => http.post(`/oa/flow-triggers/${id}/enable`, { enabled }),
  resetKey: async (id: string): Promise<{ apiKeyPlain: string }> =>
    unwrap(await http.post(`/oa/flow-triggers/${id}/reset-key`)),
  manualFire: async (id: string): Promise<{ instanceId?: string }> =>
    unwrap(await http.post(`/oa/flow-triggers/${id}/manual-fire`)),
  fires: async (id: string, take = 20): Promise<TriggerFireItem[]> =>
    unwrap(await http.get(`/oa/flow-triggers/${id}/fires`, { params: { take } })) ?? [],
  cronPreview: async (cron: string): Promise<{ next: string[] }> =>
    unwrap(await http.post('/oa/flow-triggers/cron-preview', { cron })),
}
