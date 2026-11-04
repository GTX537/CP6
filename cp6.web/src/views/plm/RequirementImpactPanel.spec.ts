import { mount } from '@vue/test-utils'
import { describe, it, expect } from 'vitest'
import Panel from './RequirementImpactPanel.vue'
import type { ImpactContext, ImpactConsumer } from '@/api/plm/requirementImpact'
const reference = (version: string) => ({ owner: 'Main.Plm.RequirementSet.v1', id: 'set', version, digest: 'a'.repeat(64) })
const row = (): ImpactConsumer => ({ iteration: { ...reference('3'), id: 'iteration', owner: 'Main.Plm.EngineeringIteration.v1' }, name: '实际工程迭代', adopted: reference('1'), inputVersion: '1', planVersion: '2', stage: 'IN_DESIGN', state: 'PENDING', case: { id: 'case', requestId: 'request', candidate: reference('2'), capturedIteration: { ...reference('3'), id: 'iteration' }, adopted: reference('1'), inputVersion: '1', capturedPlanVersion: '2', capturedStage: 'IN_DESIGN', actorId: 'source-owner', recordedAt: '2026-11-04T10:00:00Z', assessment: null } })
const source = (): ImpactContext => ({ candidate: reference('2'), coverage: 'CURRENT_MAIN_ITERATION_ONLY', etag: 'context', request: null, consumers: [row()] })
const click = async (w: ReturnType<typeof mount>, label: string) => { await w.findAll('button').find(b => b.text() === label)!.trigger('click') }
describe('actual requirement impact decisions', () => {
  it('renders precise criterion values without numeric conversion and only offers withdrawal for an older draft', () => {
    const current = source(); current.isCurrent = false; current.candidateState = 'DRAFT'; current.request = { id: 'request', action: 'REQUEST', actorId: 'source-owner', reason: 'Original impact', recordedAt: '2026-11-04T10:00:00Z' }
    const exact = '100000000000000000000000000.0000000000000000000002'
    const w = mount(Panel, { props: { source: current, disabled: false, canWrite: true, difference: { before: reference('1'), after: reference('2'), changes: [{ requirementId: 'requirement', area: 'CRITERION', item: 'force', change: 'ADDED', before: null, after: JSON.stringify({ upper: exact, upperInclusive: false }) }] } } })
    expect(w.text()).toContain(`上限：${exact}`); expect(w.text()).toContain('包含上限：否')
    expect(w.findAll('button').some(b => b.text() === '发起影响请求')).toBe(false); expect(w.findAll('button').some(b => b.text() === '撤回此影响请求')).toBe(true); w.unmount()
  })
  it('cancel makes no request; confirmation preserves the explicit request basis', async () => {
    const w = mount(Panel, { props: { source: source(), disabled: false, canWrite: true } })
    await w.get('[aria-label="影响请求依据"]').setValue('核对更改对当前产品设计的影响')
    await w.get('form').trigger('submit'); await click(w, '取消'); expect(w.emitted('request')).toBeUndefined()
    await w.get('form').trigger('submit'); await click(w, '确认发起')
    expect(w.emitted('request')![0]).toEqual(['REQUEST', '核对更改对当前产品设计的影响'])
    expect(w.text()).toContain('外部系统和其他业务链不在此查询范围'); w.unmount()
  })
  it('readiness records a plan only; permission loss discards unconfirmed assessment', async () => {
    const value = row(), w = mount(Panel, { props: { iteration: { iteration: value.iteration, cases: [value] }, disabled: false, canWrite: true } })
    await click(w, '评估此迭代影响'); await w.get('[aria-label="影响结论"]').setValue('READY_TO_ADOPT')
    await w.get('[aria-label="影响评估依据"]').setValue('批准后由原迭代命令替换输入'); await w.get('form').trigger('submit')
    await w.setProps({ canWrite: false }); expect(w.find('[aria-label="确认影响评估"]').exists()).toBe(false); expect(w.emitted('assess')).toBeUndefined()
    await w.setProps({ canWrite: true }); await click(w, '评估此迭代影响')
    await w.get('[aria-label="影响结论"]').setValue('READY_TO_ADOPT'); await w.get('[aria-label="影响评估依据"]').setValue('新版批准后采用')
    await w.get('form').trigger('submit'); await click(w, '确认评估')
    expect(w.emitted('assess')![0]).toEqual([value, 'READY_TO_ADOPT', '新版批准后采用']); expect(value.adopted.version).toBe('1'); w.unmount()
  })
})
