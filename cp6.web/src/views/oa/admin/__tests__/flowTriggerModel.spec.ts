import { describe, it, expect } from 'vitest'
import { TRIGGER_TYPES, CRON_PRESETS, typeTone, validateTriggerForm, buildConfigJson } from '../flowTriggerModel'

describe('flowTriggerModel', () => {
  it('three trigger types with stable codes', () => {
    expect(TRIGGER_TYPES.map(t => t.value)).toEqual([0, 1, 2])
  })
  it('cron presets include daily/monday/day25/monthEnd(≈28th)', () => {
    const crons = CRON_PRESETS.map(p => p.cron)
    expect(crons).toContain('0 9 * * *')
    expect(crons).toContain('0 9 * * 1')
    expect(crons).toContain('0 9 25 * *')
    expect(crons).toContain('0 9 28 * *')   // 每月末近似（NCrontab 无 L，映射表③）
  })
  it('typeTone maps to Cp tones (no hardcoded colors)', () => {
    expect(['ok', 'info', 'warn', 'muted']).toContain(typeTone(0))
  })
  it('validateTriggerForm flags missing per-type fields', () => {
    expect(validateTriggerForm({ triggerType: 0, flowKey: 'fk', starterUserId: 'u', cron: '' }).length).toBeGreaterThan(0)
    expect(validateTriggerForm({ triggerType: 1, flowKey: 'fk', starterUserId: 'u', eventKey: '' }).length).toBeGreaterThan(0)
    expect(validateTriggerForm({ triggerType: 0, flowKey: '', starterUserId: 'u', cron: '0 9 * * *' }).length).toBeGreaterThan(0)
    expect(validateTriggerForm({ triggerType: 0, flowKey: 'fk', starterUserId: 'u', cron: '0 9 * * *' })).toEqual([])
  })
  it('buildConfigJson per type', () => {
    expect(JSON.parse(buildConfigJson({ triggerType: 0, cron: '0 9 * * *', varsJson: '{"a":1}' }))).toEqual({ cron: '0 9 * * *', varsJson: '{"a":1}' })
    expect(JSON.parse(buildConfigJson({ triggerType: 1, varsMap: { orderNo: '$.No' } }))).toEqual({ varsMap: { orderNo: '$.No' } })
    expect(JSON.parse(buildConfigJson({ triggerType: 2, varsSchema: ['orderNo'] }))).toEqual({ varsSchema: ['orderNo'] })
  })
})
