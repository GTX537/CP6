// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { designExcelCadMatchApi } from '@/api/space/designExcelCadMatch'
import { designExcelMappingApi } from '@/api/space/designExcelMapping'
import { designExcelPreflightApi } from '@/api/space/designExcelPreflight'
import { designSourcesApi } from '@/api/space/designSources'
import DesignExcelCadStartWizard from './DesignExcelCadStartWizard.vue'

vi.mock('@/api/space/designExcelCadMatch', () => ({
  designExcelCadMatchApi: { start: vi.fn() },
}))
vi.mock('@/api/space/designExcelMapping', () => ({
  designExcelMappingApi: { listProfiles: vi.fn() },
}))
vi.mock('@/api/space/designExcelPreflight', () => ({
  designExcelPreflightApi: {
    upload: vi.fn(),
    start: vi.fn(),
    get: vi.fn(),
    downloadReport: vi.fn(),
  },
}))
vi.mock('@/api/space/designSources', () => ({
  designSourcesApi: { list: vi.fn() },
}))

const props = {
  versionId: 'version-1',
  floorLogicalId: 'floor-1',
  cadSourceId: 'cad-source-1',
  cadParseJobId: 'cad-parse-1',
  currentContentRevision: 7,
}

const cleanPreflight = {
  jobId: 'preflight-1',
  modelVersionId: 'version-1',
  sourceId: 'excel-source-1',
  status: 'Succeeded',
  sourceState: 'Ready',
  mappingProfileId: 'profile-1',
  mappingProfileVersion: 2,
  mappingDefinitionHash: 'a'.repeat(64),
  parserVersion: 'excel-v1',
  canConfirm: true,
  infoCount: 1,
  warningCount: 0,
  blockingCount: 0,
  sheetCount: 1,
  dataRowCount: 12,
  validRowCount: 12,
  returnedIssueCount: 0,
  issuesTruncated: false,
  errorReportUrl: '/report',
  issues: [],
}

describe('DesignExcelCadStartWizard', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(designExcelMappingApi.listProfiles).mockResolvedValue([{
      id: 'profile-1',
      name: '标准货架主数据',
      scope: 'System',
      version: 2,
      isReadOnly: true,
      definitionHash: 'a'.repeat(64),
      definition: {
        schemaVersion: 1,
        unknownColumnPolicy: 'Warning',
        emptyValuePolicy: 'Reject',
        duplicateRowPolicy: 'Reject',
        sheets: [],
      },
    }])
    vi.mocked(designSourcesApi.list).mockResolvedValue({
      items: [
        {
          id: 'cad-source-1',
          modelVersionId: 'version-1',
          sourceType: 'Dwg',
          displayName: 'warehouse.dwg',
          sha256: 'b'.repeat(64),
          state: 'Ready',
          rowVersion: 'cad-rv',
        },
        {
          id: 'excel-source-1',
          modelVersionId: 'version-1',
          sourceType: 'Excel',
          displayName: 'master-data.xlsx',
          sha256: 'c'.repeat(64),
          state: 'Ready',
          rowVersion: 'excel-rv',
        },
      ],
    })
    vi.mocked(designExcelPreflightApi.upload).mockResolvedValue({
      file: {} as never,
      source: {
        id: 'excel-source-1',
        displayName: 'master-data.xlsx',
        state: 'Ready',
      },
      reused: false,
    } as never)
    vi.mocked(designExcelPreflightApi.start).mockResolvedValue({
      jobId: 'preflight-1',
      status: 'Queued',
    } as never)
    vi.mocked(designExcelPreflightApi.get).mockResolvedValue(cleanPreflight)
    vi.mocked(designExcelCadMatchApi.start).mockResolvedValue({
      jobId: 'match-1',
      jobStatus: 'Queued',
    })
  })

  it('uploads, preflights and explicitly starts an authoritative match against current CAD', async () => {
    const wrapper = mount(DesignExcelCadStartWizard, { props, attachTo: document.body })
    await flushPromises()

    const input = wrapper.get('input[type="file"]')
    const workbook = new File(['xlsx'], 'master-data.xlsx', {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    })
    Object.defineProperty(input.element, 'files', {
      configurable: true,
      value: [workbook],
    })
    await input.trigger('change')
    await wrapper.get('.fields > .primary').trigger('click')
    await flushPromises()

    expect(designExcelPreflightApi.upload).toHaveBeenCalledWith('version-1', workbook)
    expect(designExcelPreflightApi.start).toHaveBeenCalledWith(
      'version-1',
      'excel-source-1',
      { mappingProfileId: 'profile-1', mappingProfileVersion: 2 },
      expect.any(String),
    )
    expect(wrapper.get('[data-test="excel-preflight-summary"]').text())
      .toContain('数据行 12')
    expect(wrapper.emitted('preflightStarted')).toEqual([
      ['excel-source-1', 'preflight-1'],
    ])

    await wrapper.get('.confirmation input').setValue(true)
    await wrapper.get('footer .primary').trigger('click')
    await flushPromises()

    expect(designExcelCadMatchApi.start).toHaveBeenCalledWith(
      'version-1',
      {
        excelSourceId: 'excel-source-1',
        preflightJobId: 'preflight-1',
        cadSourceId: 'cad-source-1',
        cadParseJobId: 'cad-parse-1',
        floorLogicalId: 'floor-1',
        expectedContentRevision: 7,
      },
      expect.any(String),
    )
    expect(wrapper.emitted('started')).toEqual([['match-1']])
    wrapper.unmount()
  })

  it('resumes a persisted preflight without re-uploading the workbook', async () => {
    const wrapper = mount(DesignExcelCadStartWizard, {
      props: {
        ...props,
        initialExcelSourceId: 'excel-source-1',
        initialPreflightJobId: 'preflight-1',
      },
    })
    await flushPromises()

    expect(designExcelPreflightApi.get).toHaveBeenCalledWith(
      'version-1',
      'excel-source-1',
      'preflight-1',
    )
    expect(designExcelPreflightApi.upload).not.toHaveBeenCalled()
    expect(wrapper.get('[data-test="excel-preflight-summary"]').text())
      .toContain('有效 12')
  })

  it('blocks match generation when server preflight reports a blocking issue', async () => {
    vi.mocked(designExcelPreflightApi.get).mockResolvedValue({
      ...cleanPreflight,
      canConfirm: false,
      blockingCount: 1,
      validRowCount: 11,
      returnedIssueCount: 1,
      issues: [{
        id: 'issue-1',
        severity: 'Blocking',
        code: 'SPACE_EXCEL_RACK_CODE_REQUIRED',
        sheet: 'Racks',
        row: 7,
        column: 'RackCode',
        messageArgsJson: '{}',
        fixHint: '补齐货架编码后重新上传。',
        createdAtUtc: new Date('2026-08-16T00:00:00Z'),
      }],
    } as never)
    const wrapper = mount(DesignExcelCadStartWizard, {
      props: {
        ...props,
        initialExcelSourceId: 'excel-source-1',
        initialPreflightJobId: 'preflight-1',
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('SPACE_EXCEL_RACK_CODE_REQUIRED')
    expect(wrapper.get('.confirmation input').attributes('disabled')).toBeDefined()
    expect(wrapper.get('footer .primary').attributes('disabled')).toBeDefined()
    expect(designExcelCadMatchApi.start).not.toHaveBeenCalled()
  })
})
