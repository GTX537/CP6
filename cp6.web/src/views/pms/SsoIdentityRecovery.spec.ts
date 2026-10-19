import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('@/api/http', () => ({ default: api }))
import SsoIdentityRecovery from './SsoIdentityRecovery.vue'
function render() {
  return mount(SsoIdentityRecovery, { global: { stubs: {
    ElInput: { props: ['modelValue'], emits: ['update:modelValue'], template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' },
    ElButton: { props: ['disabled'], template: '<button :disabled="disabled"><slot /></button>' },
  } } })
}
beforeEach(() => vi.resetAllMocks())
describe('original SSO identity recovery', () => {
  it('queries exact reference and only offers closure after UNKNOWN; a lost closure keeps the same reference', async () => {
    const view = render(), reference = 'a'.repeat(64)
    await view.get('input').setValue(reference)
    expect(view.findAll('button')[1]!.attributes('disabled')).toBeDefined()
    api.get.mockResolvedValue({ operationRef: reference, state: 'UNKNOWN' })
    await view.findAll('button')[0]!.trigger('click'); await flushPromises()
    expect(api.get).toHaveBeenCalledWith('/sys/sso-config/identity-operations/' + reference)
    api.post.mockRejectedValue(new Error('lost'))
    await view.findAll('button')[1]!.trigger('click'); await flushPromises()
    expect(view.text()).toContain('同一原操作编号'); expect((view.get('input').element as HTMLInputElement).value).toBe(reference)
    await view.get('input').setValue('b'.repeat(64))
    expect(view.findAll('button')[1]!.attributes('disabled')).toBeDefined(); view.unmount()
  })
  it('preserves committed identity and rejects a receipt for another operation', async () => {
    const view = render(), reference = 'a'.repeat(64)
    await view.get('input').setValue(reference)
    api.get.mockResolvedValue({ operationRef: 'b'.repeat(64), state: 'USER_PROVISIONED' })
    await view.findAll('button')[0]!.trigger('click'); await flushPromises()
    expect(view.text()).toContain('未确认')
    api.get.mockResolvedValue({ operationRef: reference, state: 'USER_PROVISIONED' })
    await view.findAll('button')[0]!.trigger('click'); await flushPromises()
    expect(view.text()).toContain('账号继续保留'); expect(view.findAll('button')[1]!.attributes('disabled')).toBeDefined()
    expect(api.post).not.toHaveBeenCalled(); view.unmount()
  })
})
