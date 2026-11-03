import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import FlowTimeline from './FlowTimeline.vue'

vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))

describe('historical actor disclosure', () => {
  it('shows recorded people without certifying the original duty or delegation', async () => {
    const row = { stepSeq: 1, nodeId: 'a', expectedHandlerId: 'recipient', expectedHandlerName: '收件人',
      onBehalfOfId: 'principal', onBehalfOfName: '原字段人员', status: 0, sentAt: '2026-01-01T00:00:00Z',
      actorEvidenceStatus: 'LEGACY_ACTOR_INCOMPLETE' as const }
    const wrapper = mount(FlowTimeline, { props: { timeline: [row], forecast: [] }, global: { stubs: {
      ElTimeline: { template: '<div><slot /></div>' }, ElTimelineItem: { template: '<div><slot /></div>' },
      CpTag: { template: '<span><slot /></span>' }, CpEmpty: true,
    } } })
    expect(wrapper.text()).toContain('历史身份未经核实')
    expect(wrapper.text()).toContain('不能作为新版审批凭据')
    expect(wrapper.text()).toContain('原记录应办：收件人')
    expect(wrapper.text()).not.toContain('代 原字段人员 签')
    await wrapper.setProps({ timeline: [{ ...row, status: 1, actualHandlerName: '收件人', actualHandlerId: 'recipient', onBehalfOfName: undefined, onBehalfOfId: undefined }] })
    expect(wrapper.text()).toContain('原记录实办：收件人')
    expect(wrapper.text()).toContain('历史身份未经核实')
    await wrapper.setProps({ timeline: [] })
    expect(wrapper.text()).not.toContain('历史身份未经核实')
  })
})
