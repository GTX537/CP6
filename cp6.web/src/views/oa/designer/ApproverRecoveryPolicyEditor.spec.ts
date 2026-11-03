// @vitest-environment jsdom
import { expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import Editor from './ApproverRecoveryPolicyEditor.vue'
it('requires explicit opt-in with eligible roles and preserves stage inheritance when the override is removed', async () => {
  const w = mount(Editor, { props: { roles: [], stage: true }, global: { stubs: { ElSelect: true, ElOption: true } } })
  expect(w.text()).toContain('沿用节点策略'); await w.get('input').setValue(true)
  const value = w.emitted('update:modelValue')![0]![0] as any
  expect(value).toEqual({ mode: 'MANUAL_ASSIGNMENT', eligibleRoleIds: [], excludeStarter: true, excludePublisher: true })
  await w.setProps({ modelValue: value }); expect(w.get('[role="alert"]').text()).toContain('至少选择一个角色')
  await w.setProps({ modelValue: { ...value, eligibleRoleIds: [72] } }); await w.findAll('input')[1]!.setValue(false)
  expect(w.emitted('update:modelValue')!.at(-1)![0]).toMatchObject({ eligibleRoleIds: [72], excludeStarter: false, excludePublisher: true })
  await w.findAll('input')[0]!.setValue(false); expect(w.emitted('update:modelValue')!.at(-1)![0]).toBeUndefined(); w.unmount()
})
