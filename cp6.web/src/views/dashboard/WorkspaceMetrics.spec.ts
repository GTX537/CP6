import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { metricDetailsLink, metricKeys, workspaceMetricsApi, type Availability, type MetricKey, type WorkspaceCard } from '@/api/sys/workspaceMetrics'
import WorkspaceMetricsPanel from './WorkspaceMetricsPanel.vue'
import WorkspaceMetricDetailView from './WorkspaceMetricDetailView.vue'
import WorkspacePreferencesPanel from './WorkspacePreferencesPanel.vue'

vi.mock('@/api/http', () => ({ default: {} }))
vi.mock('vue-i18n', async importOriginal => {
  const { ref } = await import('vue')
  return { ...await importOriginal<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key, locale: ref('zh-CN') }) }
})

function card(key: MetricKey, availability: Availability, value: number | null = null, stale = false): WorkspaceCard {
  const asOfUtc = new Date().toISOString()
  return {
    definition: { key, version: '1', sourceOwner: 'Main.ERP.Order', sourceRef: 'T_Order.CreateDate',
      selection: '应用当日零点起的可读订单数', timeBasis: 'Main 应用业务时钟', targetPath: '/workspace/metrics/' + key },
    availability, value, asOfUtc,
    staleAfterUtc: new Date(Date.now() + (stale ? -1000 : 60000)).toISOString(),
    windowStart: '2026-11-19T00:00:00',
    queryRef: { key, version: '1', asOfUtc }, rows: [],
  }
}
function navigation() {
  return createRouter({ history: createMemoryHistory(), routes: [
    { path: '/dashboard', component: WorkspaceMetricsPanel },
    { path: '/workspace/metrics/:metricKey', name: 'workspace-metric-details', component: WorkspaceMetricDetailView },
  ] })
}
beforeEach(() => {
  localStorage.clear()
  sessionStorage.clear()
  localStorage.setItem('cp6_authed', '1')
  localStorage.setItem(AUTH_EPOCH, 'tenant-A')
})
afterEach(() => vi.restoreAllMocks())

describe('工作台授权指标', () => {
  it('区分真实零、无权、来源故障和过期，并可单独重读', async () => {
    const read = vi.spyOn(workspaceMetricsApi, 'getCard').mockImplementation(async key => {
      if (key === 'todayOrders') return card(key, 'READY', 0)
      if (key === 'monthOrders') return card(key, 'UNAVAILABLE')
      if (key === 'activeWorkOrders') return card(key, 'READY', 9, true)
      return card(key, 'HIDDEN')
    })
    const router = navigation()
    await router.push('/dashboard')
    const wrapper = mount(WorkspaceMetricsPanel, { global: { plugins: [router] } })
    try {
      await flushPromises()
      expect(read).toHaveBeenCalledTimes(metricKeys.length)
      expect(wrapper.get('[data-metric=todayOrders] .val').text()).toBe('0')
      expect(wrapper.get('[data-metric=monthOrders]').text()).toContain('来源暂不可用')
      expect(wrapper.get('[data-metric=activeWorkOrders]').text()).toContain('数据已过期')
      expect(wrapper.get('[data-metric=activeWorkOrders] .val').text()).toBe('—')
      expect(wrapper.get('[data-metric=pendingOutbound]').text()).toContain('当前无权查看')
      await wrapper.get('[data-metric=monthOrders] button').trigger('click')
      expect(read).toHaveBeenCalledTimes(metricKeys.length + 1)
      expect(read.mock.calls.at(-1)?.[0]).toBe('monthOrders')
    } finally { wrapper.unmount() }
  })

  it('租户切换取消已有读取并拒绝晚到旧数', async () => {
    let release!: (result: WorkspaceCard) => void
    let oldSignal: AbortSignal | undefined
    const old = new Promise<WorkspaceCard>(resolve => { release = resolve })
    vi.spyOn(workspaceMetricsApi, 'getCard').mockImplementation((key, signal) => {
      if (localStorage.getItem(AUTH_EPOCH) === 'tenant-A' && key === 'todayOrders') {
        oldSignal = signal
        return old
      }
      return Promise.resolve(card(key, key === 'todayOrders' ? 'READY' : 'HIDDEN', 2))
    })
    const router = navigation()
    await router.push('/dashboard')
    const wrapper = mount(WorkspaceMetricsPanel, { global: { plugins: [router] } })
    try {
      localStorage.setItem(AUTH_EPOCH, 'tenant-B')
      window.dispatchEvent(new Event(AUTH_EVENT))
      await flushPromises()
      release(card('todayOrders', 'READY', 700))
      await flushPromises()
      expect(oldSignal?.aborted).toBe(true)
      expect(wrapper.get('[data-metric=todayOrders] .val').text()).toBe('2')
      expect(wrapper.text()).not.toContain('700')
    } finally { wrapper.unmount() }
  })

  it('深链保留口径时间但目标页独立重查，当前撤权清除旧结果', async () => {
    const result = {
      ...card('todayOrders', 'READY', 1),
      rows: [{ reference: { owner: 'Main.ERP.Order', id: 'fixture-order-id', version: 'fixture-version' }, ownerState: 'CURRENT' }],
    }
    const query = result.queryRef!
    const read = vi.spyOn(workspaceMetricsApi, 'getDrilldown')
      .mockResolvedValueOnce(result)
      .mockRejectedValue({ response: { status: 403 } })
    const router = navigation()
    await router.push(metricDetailsLink(query))
    const wrapper = mount(WorkspaceMetricDetailView, { global: { plugins: [router] } })
    try {
      await flushPromises()
      expect(read).toHaveBeenNthCalledWith(1, query, 1, expect.any(AbortSignal))
      expect(wrapper.text()).toContain('fixture-order-id')
      expect(wrapper.text()).toContain('fixture-version')
      window.dispatchEvent(new Event('cp6-menu-updated'))
      await flushPromises()
      expect(wrapper.text()).toContain('当前无权查看')
      expect(wrapper.text()).not.toContain('fixture-order-id')
      expect(router.currentRoute.value.fullPath).not.toContain('fixture-order-id')
    } finally { wrapper.unmount() }
  })
})

describe('工作台个性配置', () => {
  it('412 后重读版本保留草稿，丢失确认时查询原回执', async () => {
    const base = [...metricKeys]
    vi.spyOn(workspaceMetricsApi, 'getPreferences')
      .mockResolvedValueOnce({ revision: '1', visibleMetricKeys: base })
      .mockResolvedValueOnce({ revision: '2', visibleMetricKeys: base })
    const write = vi.spyOn(workspaceMetricsApi, 'savePreferences')
      .mockRejectedValueOnce({ response: { status: 412 } })
      .mockRejectedValueOnce(new Error('模拟响应丢失'))
    const readOriginal = vi.spyOn(workspaceMetricsApi, 'readPreferenceOperation').mockImplementation(async operationRef => ({
      operationRef, snapshot: { revision: '3', visibleMetricKeys: base.slice(1) }, appliedAtUtc: new Date().toISOString(),
    }))
    const wrapper = mount(WorkspacePreferencesPanel)
    const button = (text: string) => wrapper.findAll('button').find(item => item.text() === text)!
    try {
      await flushPromises()
      await wrapper.find('input[type=checkbox]').setValue(false)
      await button('保存个性配置').trigger('click')
      await flushPromises()
      expect(wrapper.text()).toContain('草稿已保留')
      await button('读取最新版本并保留草稿').trigger('click')
      await flushPromises()
      expect(wrapper.text()).toContain('已读取最新版本')
      await button('保存个性配置').trigger('click')
      await flushPromises()
      expect(write.mock.calls[1]?.[0]).toMatchObject({ expectedRevision: '2', visibleMetricKeys: base.slice(1) })
      const original = write.mock.calls[1]![0]
      await button('查询原保存结果').trigger('click')
      await flushPromises()
      expect(readOriginal).toHaveBeenCalledWith(original.operationRef, expect.any(AbortSignal))
      expect(write).toHaveBeenCalledTimes(2)
      expect(wrapper.text()).toContain('已保存，版本 3')
      expect(wrapper.emitted('visible')?.at(-1)?.[0]).toEqual(base.slice(1))
    } finally { wrapper.unmount() }
  })
})
