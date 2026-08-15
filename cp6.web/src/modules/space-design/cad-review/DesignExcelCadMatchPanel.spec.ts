// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { designExcelCadMatchApi } from '@/api/space/designExcelCadMatch'
import DesignExcelCadMatchPanel from './DesignExcelCadMatchPanel.vue'

vi.mock('@/api/space/designExcelCadMatch', () => ({
  designExcelCadMatchApi: {
    get: vi.fn(),
    confirm: vi.fn(),
    getConfirmation: vi.fn(),
  },
}))

const response = {
  jobId: 'job-1',
  modelVersionId: 'version-1',
  jobStatus: 'Succeeded',
  processorVersion: 'space-excel-cad-match-v1',
  excelSourceId: 'excel-1',
  preflightJobId: 'preflight-1',
  cadSourceId: 'cad-1',
  cadParseJobId: 'cad-job-1',
  floorLogicalId: 'floor-1',
  expectedContentRevision: 7,
  artifactId: 'artifact-1',
  artifactPayloadSha256: 'a'.repeat(64),
  fileSha256: 'b'.repeat(64),
  canConfirm: true,
  summary: {
    excelRackRowCount: 1,
    newCount: 1,
    updateCount: 0,
    unchangedCount: 0,
    unmatchedCount: 0,
    conflictCount: 0,
    errorCount: 0,
    locatableCount: 1,
  },
  totalRowCount: 1,
  returnedRowCount: 1,
  rows: [{
    excelRowId: 'row-1',
    sourceSheet: 'Racks',
    rowNumber: 2,
    values: { rackCode: 'R-001' },
    disposition: 'New',
    matchedSourceRef: 'H:160',
    differenceFields: [],
    errorCodes: [],
    location: {
      kind: 'Entity',
      floorLogicalId: 'floor-1',
      anchor: { x: 3_000, y: 3_500, z: 0 },
      suggestedPaddingMillimeters: 250,
      canFocusCanvas: true,
    },
    matchEvidenceSha256: 'c'.repeat(64),
  }],
}

const editableProps = {
  versionId: 'version-1',
  jobId: 'job-1',
  currentContentRevision: 7,
  currentFloorRevision: 3,
  clientInstanceId: 'client-1',
  leaseId: 'lease-1',
}

describe('DesignExcelCadMatchPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(designExcelCadMatchApi.get).mockResolvedValue(response as never)
    vi.mocked(designExcelCadMatchApi.confirm).mockResolvedValue({
      matchJobId: 'job-1',
      applyJobId: 'apply-1',
      commandBatchId: 'batch-1',
      jobStatus: 'Queued',
      jobStatusUrl: '/apply-1',
      idempotentReplay: false,
    } as never)
    vi.mocked(designExcelCadMatchApi.getConfirmation).mockResolvedValue({
      matchJobId: 'job-1',
      applyJobId: 'apply-1',
      commandBatchId: 'batch-1',
      jobStatus: 'Succeeded',
      expectedContentRevision: 7,
      idempotentReplay: false,
      result: {
        schemaVersion: 2,
        historySha256: 'd'.repeat(64),
        historyCommandCount: 3,
      },
    } as never)
  })

  it('loads only server-authoritative rows and emits a locate intent', async () => {
    const wrapper = mount(DesignExcelCadMatchPanel, {
      props: {
        ...editableProps,
      },
      global: { plugins: [ElementPlus] },
    })
    await flushPromises()

    expect(designExcelCadMatchApi.get).toHaveBeenCalledWith(
      'version-1',
      'job-1',
      {
        disposition: undefined,
        rackCode: undefined,
        sourceRef: undefined,
        onlyLocatable: false,
        limit: 50,
        cursor: undefined,
      },
    )
    expect(wrapper.get('[data-test="match-summary"]').text()).toContain('新增 1')
    expect(wrapper.text()).toContain('满足后续确认条件')
    expect(wrapper.text()).toContain('R-001')
    await wrapper.get('[data-test="match-row"]').trigger('click')
    expect(wrapper.emitted('locate')?.[0]?.[0]).toMatchObject({
      excelRowId: 'row-1',
      matchedSourceRef: 'H:160',
    })
  })

  it('marks a drifted Draft stale and disables canvas location', async () => {
    const wrapper = mount(DesignExcelCadMatchPanel, {
      props: {
        ...editableProps,
        currentContentRevision: 8,
      },
      global: { plugins: [ElementPlus] },
    })
    await flushPromises()

    expect(wrapper.get('[data-test="match-stale"]').text())
      .toContain('当前 Draft 已发生变化')
    expect(wrapper.get('[data-test="match-row"]').attributes('disabled'))
      .toBeDefined()
    expect(wrapper.text()).toContain('当前仅可审阅')
  })

  it('keeps confirmation read-only without an owned edit lease', async () => {
    const wrapper = mount(DesignExcelCadMatchPanel, {
      props: {
        ...editableProps,
        leaseId: undefined,
      },
      global: { plugins: [ElementPlus] },
    })
    await flushPromises()

    expect(wrapper.get('[data-test="confirm-match"]').attributes('disabled'))
      .toBeDefined()
    expect(wrapper.text()).toContain('当前仅可审阅')
    expect(designExcelCadMatchApi.confirm).not.toHaveBeenCalled()
  })

  it('requires an explicit click and confirms the exact artifact identity', async () => {
    const wrapper = mount(DesignExcelCadMatchPanel, {
      props: {
        ...editableProps,
      },
      global: { plugins: [ElementPlus] },
    })
    await flushPromises()

    expect(designExcelCadMatchApi.confirm).not.toHaveBeenCalled()
    await wrapper.get('[data-test="confirm-match"]').trigger('click')
    await flushPromises()

    expect(designExcelCadMatchApi.confirm).toHaveBeenCalledWith(
      'version-1',
      'job-1',
      {
        confirmed: true,
        artifactId: 'artifact-1',
        artifactPayloadSha256: 'a'.repeat(64),
        expectedContentRevision: 7,
        clientInstanceId: 'client-1',
        leaseId: 'lease-1',
        expectedFloorRevision: 3,
      },
      `excel-cad-apply:job-1:${'a'.repeat(64)}`,
    )
    expect(designExcelCadMatchApi.getConfirmation).toHaveBeenCalledWith(
      'version-1',
      'job-1',
      'apply-1',
    )
    expect(wrapper.get('[data-test="confirmation-succeeded"]').text())
      .toContain('重复确认不会重复创建货架')
    expect(wrapper.emitted('applied')?.[0]?.[0]).toMatchObject({
      applyJobId: 'apply-1',
      result: {
        historySha256: 'd'.repeat(64),
        historyCommandCount: 3,
      },
    })
  })
})
