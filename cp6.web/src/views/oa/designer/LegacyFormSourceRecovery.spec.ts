// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { expect, it, vi } from 'vitest'
import ElementPlus from 'element-plus'
import Panel from './LegacyFormSourceMapping.vue'
const transport = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('@/api/http', () => ({ default: transport }))
vi.mock('@/api/oa/formPublication', () => ({ formPublicationApi: { versions: vi.fn() } }))
vi.mock('@/auth/identity', () => ({ AUTH_EVENT: 'source-recovery-identity', identityStamp: () => 'source-recovery-user' }))
vi.mock('element-plus', async load => ({ ...await load<typeof import('element-plus')>(), ElMessageBox: { confirm: vi.fn().mockResolvedValue('confirm') } }))
it('an actual null original-result envelope keeps the unresolved mapping fenced until explicit close', async () => {
  const id = '33333333-3333-3333-3333-333333333333', cache = 'cp6-legacy-form-source-v1:source-recovery-user'
  localStorage.clear(); localStorage.setItem(cache, id); transport.get.mockResolvedValue({ code: 0, data: null })
  transport.post.mockResolvedValue({ code: 0, data: { operationId: id, state: 'CLOSED', result: null } })
  const panel = mount(Panel, { props: { legacyVersionId: '11111111-1111-1111-1111-111111111111' }, global: { plugins: [ElementPlus] } })
  try {
    await flushPromises(); expect(panel.text()).toContain('不代表没有效果'); expect(panel.text()).toContain(id); expect(panel.text()).not.toContain('当前来源映射已保存')
    expect(panel.get('input[aria-label="目标原生表单 Key"]').attributes('disabled')).toBeDefined(); expect(transport.post).not.toHaveBeenCalled(); expect(localStorage.getItem(cache)).toBe(id)
    await panel.findAll('button').find(x => x.text() === '查询映射原结果')!.trigger('click'); await flushPromises(); expect(panel.text()).toContain('不代表没有效果'); expect(transport.post).not.toHaveBeenCalled()
    await panel.findAll('button').find(x => x.text() === '关闭映射原操作')!.trigger('click'); await flushPromises()
    expect(transport.post).toHaveBeenCalledTimes(1); expect(transport.post).toHaveBeenCalledWith('/oa/v2/legacy-form-sources/operations/' + id + '/close'); expect(panel.text()).toContain('迟到配置不会生效')
  } finally { panel.unmount(); localStorage.clear() }
})
