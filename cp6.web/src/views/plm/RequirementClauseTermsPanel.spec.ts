import { mount } from '@vue/test-utils'
import { describe, it, expect } from 'vitest'
import Panel from './RequirementClauseTermsPanel.vue'
import type { RequirementClauseTerms } from '@/api/plm/engineeringRequirements'

describe('source-qualified clause terms', () => {
  it('starts unknown without silently granting universal scope or waivability', async () => {
    const w = mount(Panel, { props: { index: 1, disabled: false, canConfirm: true } })
    await w.get('button').trigger('click')
    const value = w.emitted('update:modelValue')![0]![0] as RequirementClauseTerms
    expect(value.customers.mode).toBe('UNKNOWN'); expect(value.fromUnbounded).toBe(false); expect(value.waivability).toBe('UNKNOWN')
    await w.setProps({ modelValue: value })
    await w.get('[aria-label="客户范围 1"]').setValue('VALUES')
    const next = w.emitted('update:modelValue')!.at(-1)![0] as RequirementClauseTerms
    await w.setProps({ modelValue: next })
    await w.get('[aria-label="客户标识 1"]').setValue('Customer-A\ncustomer-a')
    expect((w.emitted('update:modelValue')!.at(-1)![0] as RequirementClauseTerms).customers.keys).toEqual(['Customer-A', 'customer-a'])
    await w.get('[aria-label="范围开始 1"]').setValue('2026-11-04T00:00:00.0000001Z')
    expect((w.emitted('update:modelValue')!.at(-1)![0] as RequirementClauseTerms).from).toBe('2026-11-04T00:00:00.0000001Z')
    w.unmount()
  })
  it('requires source-confirm permission rather than ordinary editor access', () => {
    const w = mount(Panel, { props: { index: 1, disabled: false, canConfirm: false } })
    expect(w.get('button').attributes('disabled')).toBeDefined(); expect(w.text()).toContain('尚未确认')
    w.unmount()
  })
})
