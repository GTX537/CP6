// @vitest-environment jsdom
// 年历管理页的周规则、版本切换与原命令恢复。
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import { nextTick } from 'vue'
import {
  ymd, toExceptionMap, stateForDate, dayTone, hasTag,
} from '../workCalendarModel'
import type { WorkCalendarDay } from '@/api/oa/workCalendar'

const listMock = vi.fn()
const executeMock = vi.fn()
vi.mock('@/api/oa/workCalendar', () => ({
  workCalendarApi: {
    list: (...a: any[]) => listMock(...a),
    execute: (...a: any[]) => executeMock(...a),
  },
}))

import WorkCalendar from '../WorkCalendar.vue'

const i18n = createI18n({ legacy: false, locale: 'zh', missingWarn: false, fallbackWarn: false, messages: {} })

function mountView() {
  return mount(WorkCalendar, { global: { plugins: [i18n, ElementPlus] } })
}

describe('workCalendarModel 反转态映射', () => {
  const ex = toExceptionMap([
    { date: '2026-05-16T00:00:00', isWorkday: true, note: '補班' },   // 周六补班
    { date: '2026-01-01T00:00:00', isWorkday: false, note: '元日' },  // 平日假日
  ] as WorkCalendarDay[])

  it('例外补班 → makeup（带 note）', () => {
    const s = stateForDate('2026-05-16', ex)
    expect(s.kind).toBe('makeup')
    expect(s.note).toBe('補班')
  })
  it('例外假日 → closed', () => {
    expect(stateForDate('2026-01-01', ex).kind).toBe('closed')
  })
  it('无例外周六 → weekend', () => {
    expect(stateForDate('2026-05-23', ex).kind).toBe('weekend')   // 普通周六
  })
  it('无例外平日 → normal（无标签）', () => {
    const s = stateForDate('2026-05-20', ex)   // 普通周三
    expect(s.kind).toBe('normal')
    expect(hasTag(s.kind)).toBe(false)
  })
  it('dayTone 全落在 Cp 语义色调（零硬编码色）', () => {
    for (const k of ['makeup', 'closed', 'weekend', 'normal'] as const)
      expect(['ok', 'warn', 'danger', 'info', 'muted']).toContain(dayTone(k))
  })
  it('ymd 用本地日期（避 UTC 偏移）', () => {
    expect(ymd(new Date(2026, 4, 6))).toBe('2026-05-06')
  })
})

describe('WorkCalendar.vue 日历版本与原命令', () => {
  const year = new Date().getFullYear()
  const snapshot = {
    calendarId: '8b62e78a-40aa-4ea1-b059-07d7a42ab4c6', revision: 0, year, items: [],
    japaneseHolidays: { years: [year], version: '测试包/1', sha256: 'a'.repeat(64) },
  }
  beforeEach(() => {
    listMock.mockReset()
    executeMock.mockReset()
    listMock.mockResolvedValue(snapshot)
  })
  afterEach(() => sessionStorage.clear())

  it('没有例外仍显示默认周历，导入丢响应后重放同一个命令', async () => {
    executeMock.mockRejectedValueOnce(new Error('丢回包'))
      .mockImplementationOnce(async intent => ({ commandId: intent.command.commandId, inserted: ['2026-01-01'] }))
    const w = mountView()
    await flushPromises()

    expect(w.text()).toContain('默认周一至周五工作')
    expect(w.findComponent({ name: 'ElCalendar' }).exists()).toBe(true)
    const btn = w.findAll('button').find(b => b.text().includes('oa.workcal.importJp'))
    expect(btn).toBeTruthy()
    await btn!.trigger('click')
    await flushPromises()
    await w.findAll('button').find(b => b.text() === '按原命令重试')!.trigger('click')
    await flushPromises()
    expect(executeMock).toHaveBeenCalledTimes(2)
    expect(executeMock.mock.calls[0]![0]).toMatchObject({ kind: 'import-jp', command: { year, expectedRevision: 0 } })
    expect(executeMock.mock.calls[1]![0]).toStrictEqual(executeMock.mock.calls[0]![0])
    w.unmount()
  })

  it('显示版本、租户时区与来源包', async () => {
    listMock.mockResolvedValue({ ...snapshot, revision: 1, timeZoneId: 'Asia/Tokyo', timeZoneSource: 'TENANT_SYSTEM_RULES' })
    const w = mountView()
    await flushPromises()
    expect(w.text()).toContain('版本 1')
    expect(w.text()).toContain('Asia/Tokyo')
    expect(w.text()).toContain('测试包/1')
    w.unmount()
  })

  it('年切换：calDate 跨年 → 以新年重新拉取', async () => {
    const w = mountView()
    await flushPromises()
    expect(listMock).toHaveBeenLastCalledWith(year, undefined, expect.any(AbortSignal))

    listMock.mockClear()
    // el-calendar v-model 发射新年份日期 → 经 v-model 更新 calDate ref → watch 触发跨年重拉
    w.findComponent({ name: 'ElCalendar' }).vm.$emit('update:modelValue', new Date(year + 1, 0, 15))
    await nextTick()
    await flushPromises()
    expect(listMock).toHaveBeenCalledWith(year + 1, undefined, expect.any(AbortSignal))
    w.unmount()
  })
})
