// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import StockLegend from '../StockLegend.vue'
import type { RuntimeRefreshState } from '@/space-viewer/overlay/runtimeRefreshState'
import type { SpaceRuntimeSource } from '@/types/space/runtime'

const source: SpaceRuntimeSource = {
  kind: 'Real',
  adapterId: 'cp6-wms-v1',
  dataSourceId: 'CP6_WMS',
  observedAtUtc: '2026-08-01T12:00:00Z',
  receivedAtUtc: '2026-08-01T12:00:02Z',
  delayMilliseconds: 2000,
  clockSkewMilliseconds: 0,
  isSimulated: false,
  isAvailable: true,
}

const refreshState: RuntimeRefreshState = {
  lastSuccessfulAtUtc: '2026-08-01T12:00:02Z',
  lastFailureAtUtc: null,
  lastFailureCode: null,
  failureState: 'never',
}

function mountLegend(
  sourceOverrides: Partial<SpaceRuntimeSource> = {},
  refreshOverrides: Partial<RuntimeRefreshState> = {},
) {
  return mount(StockLegend, {
    props: {
      mode: 'status',
      polling: false,
      ts: source.observedAtUtc,
      source: { ...source, ...sourceOverrides },
      refreshState: { ...refreshState, ...refreshOverrides },
    },
    global: {
      plugins: [createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': {} } })],
    },
  })
}

describe('StockLegend', () => {
  it('shows source, connection, observation, receive, delay, and last success', () => {
    const wrapper = mountLegend()
    const text = wrapper.text()

    expect(text).toContain('REAL')
    expect(text).toContain('CP6_WMS')
    expect(text).toContain('cp6-wms-v1')
    expect(text).toContain('2026-08-01T12:00:00Z')
    expect(text).toContain('2026-08-01T12:00:02Z')
    expect(text).toContain('2.0 s')
    expect(text).toContain('本次会话未发生')
  })

  it('shows an active recent failure without replacing the last success', () => {
    const wrapper = mountLegend({}, {
      lastFailureAtUtc: '2026-08-01T12:00:05Z',
      lastFailureCode: 'HTTP_503',
      failureState: 'active',
    })

    expect(wrapper.text()).toContain('当前失败')
    expect(wrapper.text()).toContain('HTTP_503')
    expect(wrapper.text()).toContain('2026-08-01T12:00:02Z')
    expect(wrapper.find('.failure-active').exists()).toBe(true)
  })

  it('shows recovery and source clock skew explicitly', () => {
    const wrapper = mountLegend({
      kind: 'Simulated',
      isSimulated: true,
      delayMilliseconds: 0,
      clockSkewMilliseconds: 3500,
    }, {
      lastFailureAtUtc: '2026-08-01T11:59:00Z',
      lastFailureCode: 'HTTP_502',
      failureState: 'recovered',
    })

    expect(wrapper.text()).toContain('SIMULATED')
    expect(wrapper.text()).toContain('时钟超前')
    expect(wrapper.text()).toContain('3.5 s')
    expect(wrapper.text()).toContain('已恢复')
    expect(wrapper.find('.failure-recovered').exists()).toBe(true)
  })

  it('does not claim full, locked, or picking states from inventory-only data', () => {
    const wrapper = mountLegend()

    expect(wrapper.text()).toContain('空')
    expect(wrapper.text()).toContain('有库存')
    expect(wrapper.text()).not.toContain('满')
    expect(wrapper.text()).not.toContain('锁定')
    expect(wrapper.text()).not.toContain('在拣')
  })
})
