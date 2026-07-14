// @vitest-environment jsdom
// D-T2 连接器管理 tab：列表掩码渲染（hasAuth 徽标，无明文）+ 启停切换 + 新建入口。
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'

const listMock = vi.fn()
const enableMock = vi.fn()
vi.mock('@/api/oa/wfConnector', () => ({
  wfConnectorApi: {
    list: (...a: any[]) => listMock(...a),
    enable: (...a: any[]) => enableMock(...a),
    get: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
  },
}))

import WfConnectorPanel from '../WfConnectorPanel.vue'

const i18n = createI18n({ legacy: false, locale: 'zh', missingWarn: false, fallbackWarn: false, messages: {} })

function mountPanel() {
  return mount(WfConnectorPanel, { global: { plugins: [i18n, ElementPlus] } })
}
function state(w: ReturnType<typeof mountPanel>) {
  return (w.vm as unknown as { $: { setupState: Record<string, any> } }).$.setupState
}

const SAMPLE = [
  { id: 'c1', name: 'erpProd', displayName: 'ERP 正式', baseUrl: 'https://erp', timeoutSec: 30, enabled: true, hasAuth: true },
  { id: 'c2', name: 'pub', displayName: '公开', baseUrl: 'https://p', timeoutSec: 20, enabled: false, hasAuth: false },
]

describe('WfConnectorPanel 列表 + 掩码 + 启停', () => {
  beforeEach(() => {
    listMock.mockReset()
    enableMock.mockReset()
  })

  it('渲染列表（掩码：仅 hasAuth 徽标，DOM 无明文凭证字段）', async () => {
    listMock.mockResolvedValue(SAMPLE)
    const w = mountPanel()
    await flushPromises()
    expect(state(w).rows).toHaveLength(2)
    // 掩码：视图数据仅带 hasAuth 布尔，绝无 authJson/凭证明文字段
    expect(Object.keys(state(w).rows[0])).not.toContain('authJson')
    expect(w.html()).toContain('erpProd')
  })

  it('启停切换：调 enable(id, val) 并乐观置位', async () => {
    listMock.mockResolvedValue(SAMPLE)
    enableMock.mockResolvedValue(undefined)
    const w = mountPanel()
    await flushPromises()

    const sw = w.findComponent({ name: 'ElSwitch' })
    sw.vm.$emit('change', false)
    await flushPromises()
    expect(enableMock).toHaveBeenCalledWith('c1', false)
  })

  it('新建入口：openCreate 置 editing=null 并开对话框', async () => {
    listMock.mockResolvedValue([])
    const w = mountPanel()
    await flushPromises()
    state(w).openCreate()
    expect(state(w).dialogVisible).toBe(true)
    expect(state(w).editing).toBeNull()
  })
})
