import { mount } from '@vue/test-utils'
import { describe, it, expect } from 'vitest'
import Panel from './RequirementConflictDecisionPanel.vue'
import type { RequirementSetConflict } from '@/api/plm/requirementSet'
const conflict = (): RequirementSetConflict => ({ id: 'C1', left: { owner: 'Main.Plm.EngineeringRequirement.v1', id: 'left', version: '1', digest: 'a'.repeat(64) }, right: { owner: 'Main.Plm.EngineeringRequirement.v1', id: 'right', version: '2', digest: 'b'.repeat(64) }, intersection: 'Proposed text', proposal: 'SCOPE_SEPARATION', basis: 'Proposal' })
const click = async (w: ReturnType<typeof mount>, text: string) => { await w.findAll('button').find(x => x.text() === text)!.trigger('click') }
describe('explicit source conflict decision', () => {
  it('cancel creates no decision; explicit confirmation retains exact chosen route and basis', async () => {
    const w = mount(Panel, { props: { conflict: conflict(), disabled: false, canDecide: true } })
    await w.get('[aria-label="裁决依据"]').setValue('Both source authorities confirm distinct customers')
    await w.get('[aria-label="保留双方原要求"]').setValue(true)
    await click(w, '准备来源裁决'); expect(w.get('[role="dialog"]').text()).toContain('right / 2')
    await click(w, '取消裁决'); expect(w.emitted('decision')).toBeUndefined()
    await click(w, '准备来源裁决'); await click(w, '确认编入裁决')
    expect(w.emitted('decision')![0]![0]).toMatchObject({ kind: 'SCOPE_SEPARATION', preservesRequirements: true, basis: 'Both source authorities confirm distinct customers' })
    w.unmount()
  })
  it('invalidates an open confirmation when source selection or current permission changes', async () => {
    const w = mount(Panel, { props: { conflict: conflict(), disabled: false, canDecide: true } })
    await w.get('[aria-label="裁决依据"]').setValue('Exact original decision')
    await w.get('[aria-label="保留双方原要求"]').setValue(true); await click(w, '准备来源裁决')
    await w.setProps({ conflict: { ...conflict(), proposal: 'ECM' } }); expect(w.find('[role="dialog"]').exists()).toBe(false)
    await w.get('[aria-label="保留双方原要求"]').setValue(true); await click(w, '准备来源裁决')
    await w.setProps({ canDecide: false }); expect(w.find('[role="dialog"]').exists()).toBe(false)
    expect(w.emitted('decision')).toBeUndefined(); expect(w.text()).toContain('不能因此批准集合')
    w.unmount()
  })
})
