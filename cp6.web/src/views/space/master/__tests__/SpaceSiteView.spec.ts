// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { siteApi } from '@/api/space/site'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import SpaceSiteView from '../SpaceSiteView.vue'
import type { SiteVO } from '@/types/space/scene'

// 站点 API 全 mock（list 返回两行样例；create 成功）
vi.mock('@/api/space/site', () => ({
  siteApi: {
    list: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
  },
}))

// 视图内 useRouter（「楼层」跳转）——测试无需真实路由
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))

const rows: SiteVO[] = [
  { id: 's1', siteCode: 'TKY', siteName: '東京DC', enable: true, warehouseCd: 'WH01', address: '東京都' },
  { id: 's2', siteCode: 'OSK', siteName: '大阪DC', enable: false, warehouseCd: null, address: null },
]

function mountView() {
  const i18n = createI18n({ legacy: false, locale: 'ja', missingWarn: false, fallbackWarn: false, messages: {} })
  return mount(SpaceSiteView, { global: { plugins: [i18n] } })
}

describe('SpaceSiteView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(siteApi.list).mockResolvedValue({ code: 0, message: '', data: rows })
    vi.mocked(siteApi.create).mockResolvedValue({ code: 0, message: '', data: { id: 'new' } })
  })

  it('挂载后渲染两行站点编码', async () => {
    const w = mountView()
    await flushPromises()
    expect(siteApi.list).toHaveBeenCalled()
    expect(w.text()).toContain('TKY')
    expect(w.text()).toContain('OSK')
  })

  it('点「新建站点」后 CpFormDialog 可见', async () => {
    const w = mountView()
    await flushPromises()
    expect(w.findComponent(CpFormDialog).props('modelValue')).toBe(false)
    await w.find('.cp-page-actions el-button').trigger('click')
    expect(w.findComponent(CpFormDialog).props('modelValue')).toBe(true)
  })

  it('保存（create）后触发 list 二次调用', async () => {
    const w = mountView()
    await flushPromises()
    expect(siteApi.list).toHaveBeenCalledTimes(1)

    await w.find('.cp-page-actions el-button').trigger('click')
    const dlg = w.findComponent(CpFormDialog)
    // 走对话框 submit 契约（onSave → siteApi.create），再触发 saved → reload
    await (dlg.props('submit') as (f: Record<string, unknown>) => Promise<void>)(dlg.props('form'))
    dlg.vm.$emit('saved')
    await flushPromises()

    expect(siteApi.create).toHaveBeenCalledTimes(1)
    expect(siteApi.list).toHaveBeenCalledTimes(2)
  })
})
