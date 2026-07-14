// @vitest-environment jsdom
// A-T4 年历管理页：反转态映射（model）+ 空态渲染 + 年切换（component）。
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import { nextTick } from 'vue'
import {
  ymd, toExceptionMap, stateForDate, dayTone, hasTag,
} from '../workCalendarModel'
import type { WorkCalendarDay } from '@/api/oa/workCalendar'

// ── 后端 api mock（可切换空态/非空态返回）──
const listMock = vi.fn()
const importMock = vi.fn()
vi.mock('@/api/oa/workCalendar', () => ({
  workCalendarApi: {
    list: (...a: any[]) => listMock(...a),
    importJp: (...a: any[]) => importMock(...a),
    toggle: vi.fn(),
    clear: vi.fn(),
  },
}))

import WorkCalendar from '../WorkCalendar.vue'

const i18n = createI18n({ legacy: false, locale: 'zh', missingWarn: false, fallbackWarn: false, messages: {} })

function mountView() {
  return mount(WorkCalendar, { global: { plugins: [i18n, ElementPlus] } })
}

function state(w: ReturnType<typeof mountView>) {
  return (w.vm as unknown as { $: { setupState: Record<string, any> } }).$.setupState
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

describe('WorkCalendar.vue 空态渲染 + 年切换', () => {
  beforeEach(() => {
    listMock.mockReset()
    importMock.mockReset()
  })

  it('空态：渲染导入按钮，点击调 importJp', async () => {
    listMock.mockResolvedValue({ year: 2026, isEmpty: true, items: [] })
    importMock.mockResolvedValue({ inserted: 35 })
    const w = mountView()
    await flushPromises()

    expect(state(w).isEmpty).toBe(true)
    const btn = w.findAll('button').find(b => b.text().includes('oa.workcal.importJp'))
    expect(btn).toBeTruthy()
    await btn!.trigger('click')
    await flushPromises()
    expect(importMock).toHaveBeenCalledOnce()
  })

  it('非空态：不渲染空态，渲染年历', async () => {
    listMock.mockResolvedValue({
      year: 2026, isEmpty: false,
      items: [{ date: '2026-01-01T00:00:00', isWorkday: false, note: '元日' }],
    })
    const w = mountView()
    await flushPromises()
    expect(state(w).isEmpty).toBe(false)
    expect(w.findComponent({ name: 'ElCalendar' }).exists()).toBe(true)
  })

  it('年切换：calDate 跨年 → 以新年重新拉取', async () => {
    listMock.mockResolvedValue({ year: 2026, isEmpty: false, items: [] })
    const w = mountView()
    await flushPromises()
    expect(listMock).toHaveBeenLastCalledWith(new Date().getFullYear())

    listMock.mockClear()
    // el-calendar v-model 发射新年份日期 → 经 v-model 更新 calDate ref → watch 触发跨年重拉
    w.findComponent({ name: 'ElCalendar' }).vm.$emit('update:modelValue', new Date(2027, 0, 15))
    await nextTick()
    await flushPromises()
    expect(listMock).toHaveBeenCalledWith(2027)
  })
})
