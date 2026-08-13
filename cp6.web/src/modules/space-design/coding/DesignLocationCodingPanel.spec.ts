// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import DesignLocationCodingPanel from './DesignLocationCodingPanel.vue'

const preview = {
  schemaVersion: 1,
  modelVersionId: 'version-1',
  floorLogicalId: 'floor-1',
  mode: 'fill-empty',
  baseFloorRevision: 4,
  baseContentRevision: 8,
  proposalHash: 'a'.repeat(64),
  ruleSetHash: 'b'.repeat(64),
  changedCount: 1,
  unchangedCount: 1,
  protectedCount: 1,
  rules: [{
    ruleId: 'rule-1', ruleName: '仓库默认编码', scopeType: 0, ruleHash: 'c'.repeat(64),
  }],
  items: [
    {
      locationLogicalId: 'location-1', rackLogicalId: 'rack-1', rackCode: 'R-01',
      columnNo: 1, levelNo: 1, depthNo: 1, proposedCode: 'Z-A-R-01-01',
      decision: 'modify', reason: 'fill-empty', ruleId: 'rule-1',
    },
    {
      locationLogicalId: 'location-2', rackLogicalId: 'rack-1', rackCode: 'R-01',
      columnNo: 2, levelNo: 1, depthNo: 1, currentCode: 'WMS-01', proposedCode: 'WMS-01',
      decision: 'protected', reason: 'wms-bound', ruleId: 'rule-1',
    },
  ],
}

function mountPanel(extra: Record<string, unknown> = {}) {
  return mount(DesignLocationCodingPanel, {
    props: {
      zones: [{ logicalId: 'zone-1', code: 'Z-A', name: '存储区 A' }],
      preview: null,
      readonly: false,
      busy: false,
      ...extra,
    },
    global: { directives: { permission: () => undefined } },
  })
}

describe('DesignLocationCodingPanel', () => {
  it('requests a zone-scoped rebuild preview', async () => {
    const wrapper = mountPanel()
    await wrapper.get('input[value="rebuild"]').setValue(true)
    await wrapper.get('[data-test="coding-scope"]').setValue('zone-1')
    await wrapper.get('[data-test="preview-location-codes"]').trigger('click')

    expect(wrapper.emitted('preview')?.[0]?.[0]).toEqual({
      mode: 'rebuild', scopeZoneLogicalId: 'zone-1',
    })
  })

  it('shows protected reasons and requires explicit confirmation before apply', async () => {
    const wrapper = mountPanel({ preview })
    expect(wrapper.text()).toContain('将修改 1')
    expect(wrapper.text()).toContain('已绑定 WMS')
    expect(wrapper.get('[data-test="apply-location-codes"]').attributes('disabled')).toBeDefined()

    await wrapper.get('[data-test="confirm-location-codes"]').setValue(true)
    expect(wrapper.get('[data-test="apply-location-codes"]').attributes('disabled')).toBeUndefined()
    await wrapper.get('[data-test="apply-location-codes"]').trigger('click')
    expect(wrapper.emitted('apply')).toHaveLength(1)
  })

  it('disables preview and apply in readonly mode', () => {
    const wrapper = mountPanel({ preview, readonly: true })
    expect(wrapper.get('[data-test="preview-location-codes"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-test="apply-location-codes"]').attributes('disabled')).toBeDefined()
  })
})
