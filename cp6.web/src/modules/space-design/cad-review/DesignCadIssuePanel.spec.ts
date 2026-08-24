// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
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

  it('filters open issues by Blocking, Warning and Info severity', async () => {
    const severityWorkspace = {
      ...workspace,
      items: [
        {
          ...workspace.items[0]!,
          reviewItemId: 'cad-review-blocking',
          trackingKey: 'cad-blocking',
          severity: 'Blocking' as const,
          code: 'CAD_BLOCKING_TEST',
        },
        {
          ...workspace.items[0]!,
          reviewItemId: 'cad-review-warning',
          trackingKey: 'cad-warning',
          code: 'CAD_WARNING_TEST',
        },
        {
          ...workspace.items[0]!,
          reviewItemId: 'cad-review-info',
          trackingKey: 'cad-info',
          severity: 'Info' as const,
          code: 'CAD_INFO_TEST',
        },
      ],
      summary: {
        ...workspace.summary,
        totalCount: 3,
        openCount: 3,
        openBlockingCount: 1,
        openWarningCount: 1,
        openInfoCount: 1,
        locatableCount: 3,
        proposalReviewCount: 3,
      },
    } satisfies CadReviewWorkspace
    const wrapper = mount(DesignCadIssuePanel, {
      props: { workspace: severityWorkspace },
      global: { plugins: [ElementPlus] },
    })
    const severitySelect = wrapper.findAllComponents({ name: 'ElSelect' })[1]!

    for (const [severity, code] of [
      ['Blocking', 'CAD_BLOCKING_TEST'],
      ['Warning', 'CAD_WARNING_TEST'],
      ['Info', 'CAD_INFO_TEST'],
    ] as const) {
      severitySelect.vm.$emit('update:modelValue', severity)
      await nextTick()
      const rows = wrapper.findAll('[data-test="cad-review-item"]')
      expect(rows).toHaveLength(1)
      expect(rows[0]!.text()).toContain(code)
    }
  })

  it('shows a locked manual correction as a disabled versioned conflict', () => {
    const lockedWorkspace = {
      ...workspace,
      sourceId: '66666666-6666-6666-6666-666666666666',
      cadParseJobId: '77777777-7777-7777-7777-777777777777',
      semanticPreviewSha256: 'e'.repeat(64),
      changesetSha256: 'f'.repeat(64),
      changes: [{
        changeId: 'cad-change-locked',
        kind: 'Conflict' as const,
        logicalId: '11111111-1111-1111-1111-111111111111',
        sourceRef: 'CAD:H:COLUMN-1',
        objectType: 'Column',
        isSelected: false,
        canApply: false,
        blockingReasonCode: 'SPACE_CAD_MANUAL_CORRECTION_LOCKED',
        isManualCorrectionLocked: true,
        userCorrectionVersion: 4,
      }],
      changeSummary: {
        totalCount: 1,
        addCount: 0,
        modifyCount: 0,
        deleteCount: 0,
        conflictCount: 1,
        lowConfidenceCount: 0,
        unrecognizedCount: 0,
        selectedCount: 0,
        applyEligibleCount: 0,
      },
    } satisfies CadReviewWorkspace
    const wrapper = mount(DesignCadIssuePanel, {
      props: { workspace: lockedWorkspace },
      global: { plugins: [ElementPlus] },
    })

    const changeset = wrapper.get('[data-test="cad-changeset"]')
    expect(changeset.text()).toContain('人工锁定 v4')
    expect(changeset.text()).toContain('SPACE_CAD_MANUAL_CORRECTION_LOCKED')
    expect(changeset.find('input[type="checkbox"]').attributes('disabled'))
      .toBeDefined()
  })

  it('shows all six change kinds and routes layout objects to the current CAD RuleOnly flow', async () => {
    const kinds = ['Add', 'Modify', 'Delete', 'Conflict', 'LowConfidence', 'Unrecognized'] as const
    const changes = kinds.map((kind, index) => ({
      changeId: `cad-change-${kind}`,
      kind,
      logicalId: `11111111-1111-1111-1111-${String(index + 1).padStart(12, '0')}`,
      sourceRef: `CAD:H:${kind}`,
      objectType: kind === 'Conflict' ? 'Rack' : 'Column',
      isSelected: kind === 'Add',
      canApply: ['Add', 'Modify', 'Delete'].includes(kind),
      blockingReasonCode: kind === 'Conflict'
        ? 'SPACE_CAD_REQUIRES_RULE_ONLY_REVIEW'
        : undefined,
      isManualCorrectionLocked: false,
      userCorrectionVersion: 0,
    }))
    const changesWorkspace = {
      ...workspace,
      sourceId: '66666666-6666-6666-6666-666666666666',
      cadParseJobId: '77777777-7777-7777-7777-777777777777',
      semanticPreviewSha256: 'e'.repeat(64),
      changesetSha256: 'f'.repeat(64),
      changes,
      changeSummary: {
        totalCount: 6,
        addCount: 1,
        modifyCount: 1,
        deleteCount: 1,
        conflictCount: 1,
        lowConfidenceCount: 1,
        unrecognizedCount: 1,
        selectedCount: 1,
        applyEligibleCount: 3,
      },
    } satisfies CadReviewWorkspace
    const wrapper = mount(DesignCadIssuePanel, {
      props: { workspace: changesWorkspace },
      global: { plugins: [ElementPlus] },
    })

    const summary = wrapper.get('[aria-label="CAD 六类变更汇总"]')
    for (const label of ['新增', '修改', '删除', '冲突', '低置信度', '未识别']) {
      expect(summary.text()).toContain(label)
    }
    wrapper.findAllComponents({ name: 'ElSelect' })[0]!
      .vm.$emit('update:modelValue', 'Conflict')
    await nextTick()
    expect(wrapper.findAll('.change-row')).toHaveLength(1)
    expect(wrapper.get('.change-row').text()).toContain('Conflict · Rack')
    expect(wrapper.text()).toContain('1 个 Zone / Aisle / Rack')
    await wrapper.get('.rule-only-route button').trigger('click')
    expect(wrapper.emitted('openRuleOnly')?.[0]).toEqual([
      '66666666-6666-6666-6666-666666666666',
    ])
  })

  it('resets the selected changes when a new sealed workspace replaces the current one', async () => {
    const change = (id: string, kind: 'Add' | 'Modify') => ({
      changeId: id,
      kind,
      logicalId: id === 'cad-change-first'
        ? '11111111-1111-1111-1111-111111111111'
        : '22222222-2222-2222-2222-222222222222',
      sourceRef: `CAD:${id}`,
      objectType: 'Column',
      isSelected: true,
      canApply: true,
      isManualCorrectionLocked: false,
      userCorrectionVersion: 0,
    })
    const withChange = (
      workspaceSha256: string,
      selected: ReturnType<typeof change>,
    ): CadReviewWorkspace => ({
      ...workspace,
      workspaceSha256,
      sourceId: '66666666-6666-6666-6666-666666666666',
      cadParseJobId: '77777777-7777-7777-7777-777777777777',
      semanticPreviewSha256: 'e'.repeat(64),
      changesetSha256: 'f'.repeat(64),
      changes: [selected],
      changeSummary: {
        totalCount: 1,
        addCount: selected.kind === 'Add' ? 1 : 0,
        modifyCount: selected.kind === 'Modify' ? 1 : 0,
        deleteCount: 0,
        conflictCount: 0,
        lowConfidenceCount: 0,
        unrecognizedCount: 0,
        selectedCount: 1,
        applyEligibleCount: 1,
      },
    })
    const wrapper = mount(DesignCadIssuePanel, {
      props: { workspace: withChange('1'.repeat(64), change('cad-change-first', 'Add')) },
      global: { plugins: [ElementPlus] },
    })

    await wrapper.get('.changeset > .el-button').trigger('click')
    expect(wrapper.emitted('applyChanges')?.[0]).toEqual([['cad-change-first']])
    await wrapper.setProps({
      workspace: withChange('2'.repeat(64), change('cad-change-second', 'Modify')),
    })
    await nextTick()
    await wrapper.get('.changeset > .el-button').trigger('click')
    expect(wrapper.emitted('applyChanges')?.[1]).toEqual([['cad-change-second']])
  })
})
