// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { designExcelCadMatchApi } from '@/api/space/designExcelCadMatch'
import DesignExcelCadMatchPanel from './DesignExcelCadMatchPanel.vue'

vi.mock('@/api/space/designExcelCadMatch', () => ({
  designExcelCadMatchApi: {
    get: vi.fn(),
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

describe('DesignExcelCadMatchPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(designExcelCadMatchApi.get).mockResolvedValue(response as never)
  })

  it('loads only server-authoritative rows and emits a locate intent', async () => {
    const wrapper = mount(DesignExcelCadMatchPanel, {
      props: {
        versionId: 'version-1',
        jobId: 'job-1',
        currentContentRevision: 7,
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
        versionId: 'version-1',
        jobId: 'job-1',
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
})
