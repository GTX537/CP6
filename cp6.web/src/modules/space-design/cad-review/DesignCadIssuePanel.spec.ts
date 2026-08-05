// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import DesignCadIssuePanel from './DesignCadIssuePanel.vue'
import type { CadReviewWorkspace } from './cadReviewWorkspace'

const workspace = {
  schemaVersion: 1,
  isReadOnlyWorkspace: true,
  tenantId: '55555555-5555-5555-5555-555555555555',
  modelVersionId: 'aaaaaaaa-1111-2222-3333-444444444444',
  floorLogicalId: '44444444-4444-4444-4444-444444444444',
  floorCode: 'F01',
  diagnosticIndexSha256: 'a'.repeat(64),
  editorContentRevision: 7,
  editorSnapshotSha256: 'b'.repeat(64),
  items: [
    {
      reviewItemId: 'cad-review-1',
      trackingKey: 'cad-proposal:H:160:Rack:-',
      kind: 'LowConfidenceProposal',
      severity: 'Warning',
      status: 'Open',
      code: 'SPACE_CAD_LOW_CONFIDENCE',
      relatedCodes: [],
      suggestedActionCode: 'review-candidate',
      sourceRef: 'H:160',
      confidenceBand: 'Low',
      location: {
        kind: 'Entity',
        floorLogicalId: '44444444-4444-4444-4444-444444444444',
        anchor: { x: 3000, y: 3500, z: 0 },
        suggestedPaddingMillimeters: 250,
        canFocusCanvas: true,
      },
      upstreamEvidenceSha256: 'c'.repeat(64),
    },
  ],
  summary: {
    totalCount: 1,
    openCount: 1,
    resolvedCount: 0,
    openInfoCount: 0,
    openWarningCount: 1,
    openBlockingCount: 0,
    locatableCount: 1,
    unlocatableCount: 0,
    cadDiagnosticCount: 0,
    proposalReviewCount: 1,
    excelReviewCount: 0,
  },
  workspaceSha256: 'd'.repeat(64),
} as CadReviewWorkspace

describe('DesignCadIssuePanel', () => {
  it('renders summary and emits a typed focus intent for an open item', async () => {
    const wrapper = mount(DesignCadIssuePanel, {
      props: { workspace },
      global: { plugins: [ElementPlus] },
    })

    expect(wrapper.get('[data-test="cad-review-panel"]').text())
      .toContain('CAD 问题与未匹配项')
    expect(wrapper.text()).toContain('SPACE_CAD_LOW_CONFIDENCE')
    expect(wrapper.text()).toContain('H:160')

    await wrapper.get('[data-test="cad-review-item"]').trigger('click')
    expect(wrapper.emitted('select')?.[0]?.[0]).toMatchObject({
      reviewItemId: 'cad-review-1',
      sourceRef: 'H:160',
    })
  })

  it('shows stale evidence and disables focus actions', async () => {
    const wrapper = mount(DesignCadIssuePanel, {
      props: { workspace, stale: true },
      global: { plugins: [ElementPlus] },
    })

    expect(wrapper.get('[data-test="cad-review-stale"]').text())
      .toContain('已禁用画布定位')
    expect(wrapper.get('[data-test="cad-review-item"]').attributes('disabled'))
      .toBeDefined()
    await wrapper.get('[data-test="cad-review-item"]').trigger('click')
    expect(wrapper.emitted('select')).toBeUndefined()
  })
})
