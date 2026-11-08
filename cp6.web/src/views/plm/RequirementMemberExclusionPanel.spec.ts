import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import Panel from './RequirementMemberExclusionPanel.vue'
const reference = { owner: 'Main.Plm.Requirement.v1', id: 'source-one', version: '2', digest: 'abc' }
const member = { requirement: { ...reference, id: 'requirement-one' }, evaluationControl: null, approval: null, disposition: 'EXCLUDED' as const, basis: '' }
const source = { reference, isCurrent: true } as any
const props = { member, source, disabled: false, canDecide: true, scopeText: 'Original application scope' }
async function prepared(wrapper: ReturnType<typeof mount>) { await wrapper.get('textarea').setValue('  Current source accepts optional omission  '); await wrapper.get('button').trigger('click') }
describe('exact optional source decision', () => {
  it('separates prepare, cancel and exact confirmation without trimming the original reason', async () => {
    const wrapper = mount(Panel, { props }); await prepared(wrapper)
    expect(wrapper.emitted('decision')).toBeUndefined(); await wrapper.get('[role="dialog"]').findAll('button')[1]!.trigger('click')
    expect(wrapper.emitted('decision')).toBeUndefined(); expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    await wrapper.get('button').trigger('click'); await wrapper.get('[role="dialog"] button').trigger('click')
    expect(wrapper.emitted('decision')?.[0]?.[0]).toEqual({ schema: 'requirement-member-exclusion/1', source: reference, basis: '  Current source accepts optional omission  ' })
  })
  it('discards prepared authority when exact source, member or permission changes', async () => {
    const wrapper = mount(Panel, { props }); await prepared(wrapper)
    await wrapper.setProps({ source: { ...source, reference: { ...reference, version: '3' } } })
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false); await wrapper.get('button').trigger('click')
    await wrapper.setProps({ scopeText: 'Changed application scope' }); expect(wrapper.find('[role="dialog"]').exists()).toBe(false); await wrapper.get('button').trigger('click')
    await wrapper.setProps({ canDecide: false }); expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    await wrapper.get('button').trigger('click'); expect(wrapper.emitted('decision')).toBeUndefined()
  })
  it('requires a current original source and keeps a decision removal explicit', async () => {
    const wrapper = mount(Panel, { props: { ...props, source: null } }); await prepared(wrapper)
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    await wrapper.setProps({ source, member: { ...member, exclusion: { schema: 'requirement-member-exclusion/1', source: reference, basis: 'Original decision' } } as any })
    const remove = wrapper.findAll('button').find(button => button.text() === '本草稿移除排除决定')!; await remove.trigger('click')
    expect(wrapper.emitted('decision')?.[0]?.[0]).toBeNull()
  })
})
