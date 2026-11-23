export interface TriggerFormState {
  triggerType: number
  flowKey?: string
  starterUserId?: string
  cron?: string
  varsJson?: string
  eventKey?: string
  varsMap?: Record<string, string>
  varsSchema?: string[]
}

export const TRIGGER_TYPES = [
  { value: 0, labelKey: 'oa.flowtrigger.type.timer' },
  { value: 1, labelKey: 'oa.flowtrigger.type.event' },
  { value: 2, labelKey: 'oa.flowtrigger.type.message' },
] as const

/** cron 常用预设（spec §4；「每月末」按 28 日近似——NCrontab 无 L 语义，映射表③，文案已注明） */
export const CRON_PRESETS = [
  { labelKey: 'oa.flowtrigger.preset.daily', cron: '0 9 * * *' },
  { labelKey: 'oa.flowtrigger.preset.monday', cron: '0 9 * * 1' },
  { labelKey: 'oa.flowtrigger.preset.day25', cron: '0 9 25 * *' },
  { labelKey: 'oa.flowtrigger.preset.monthEnd', cron: '0 9 28 * *' },
] as const

/** CpTag tone（零硬编码色）：timer=info / event=ok / message=warn */
export function typeTone(triggerType: number): 'ok' | 'info' | 'warn' | 'muted' {
  return triggerType === 0 ? 'info' : triggerType === 1 ? 'ok' : triggerType === 2 ? 'warn' : 'muted'
}

/** 客户端镜像校验（后端权威 E-WF-022/023）；返回 i18n 键数组，空=通过 */
export function validateTriggerForm(f: TriggerFormState): string[] {
  const errs: string[] = []
  if (!f.flowKey) errs.push('oa.flowtrigger.err.flowKey')
  if (!f.starterUserId) errs.push('oa.flowtrigger.err.starter')
  if (f.triggerType === 0 && !f.cron) errs.push('oa.flowtrigger.err.cron')
  if (f.triggerType === 1 && !f.eventKey) errs.push('oa.flowtrigger.err.eventKey')
  return errs
}

export function buildConfigJson(f: Partial<TriggerFormState> & { triggerType: number }): string {
  if (f.triggerType === 0) return JSON.stringify({ cron: f.cron ?? '', ...(f.varsJson ? { varsJson: f.varsJson } : {}) })
  if (f.triggerType === 1) return JSON.stringify({ varsMap: f.varsMap ?? {} })
  return JSON.stringify({ varsSchema: f.varsSchema ?? [] })
}
