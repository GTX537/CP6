// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { designCadParseApi } from '@/api/space/designCadParse'
import DesignCadReviewCandidatePicker from './DesignCadReviewCandidatePicker.vue'

vi.mock('@/api/space/designCadParse', () => ({
  designCadParseApi: {
    listReviewCandidates: vi.fn(),
  },
}))

const current = {
  sourceId: 'source-current',
  sourceDisplayName: 'current.dwg',
  sourceType: 'Dwg',
  sourceSha256: 'a'.repeat(64),
  jobId: 'job-current',
  jobStatus: 'Succeeded',
  sourceState: 'PreviewReady',
  floorLogicalId: 'floor-1',
  baseContentRevision: 7,
  baseContentHash: 'b'.repeat(64),
  isCurrentRevision: true,
  canLoadReview: true,
  requestedAtUtc: '2026-08-16T10:00:00Z',
  finishedAtUtc: '2026-08-16T10:02:00Z',
  preferredProviderKey: 'autocad-core-console',
  preferredProviderVersion: '25.0',
  mappingProfileId: 'profile-1',
  mappingProfileVersion: 3,
}

const historical = {
  ...current,
  sourceId: 'source-old',
  sourceDisplayName: 'old.dxf',
  sourceType: 'Dxf',
  jobId: 'job-old',
  baseContentRevision: 4,
  isCurrentRevision: false,
  canLoadReview: false,
}

describe('DesignCadReviewCandidatePicker', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(designCadParseApi.listReviewCandidates).mockResolvedValue({
      modelVersionId: 'version-1',
      floorLogicalId: 'floor-1',
      currentContentRevision: 7,
      currentContentHash: 'b'.repeat(64),
      truncated: false,
      items: [current, historical],
    })
  })

  it('loads a current result without exposing internal IDs as input', async () => {
    const wrapper = mount(DesignCadReviewCandidatePicker, {
      props: {
        versionId: 'version-1',
        floorLogicalId: 'floor-1',
        readonly: false,
      },
    })
    await flushPromises()

    expect(designCadParseApi.listReviewCandidates)
      .toHaveBeenCalledWith('version-1', 'floor-1')
    expect(wrapper.findAll('input')).toHaveLength(0)
    expect(wrapper.text()).toContain('current.dwg')
    await wrapper.get('button[aria-label="加载 current.dwg 的审核结果"]').trigger('click')
    expect(wrapper.emitted('select')).toEqual([['source-current', 'job-current']])
  })

  it('routes stale results to reparse and blocks that write action in read-only mode', async () => {
    const wrapper = mount(DesignCadReviewCandidatePicker, {
      props: {
        versionId: 'version-1',
        floorLogicalId: 'floor-1',
        readonly: false,
      },
    })
    await flushPromises()

    const reparse = wrapper.get('button[aria-label="重新解析 old.dxf"]')
    expect(reparse.attributes('disabled')).toBeUndefined()
    await reparse.trigger('click')
    expect(wrapper.emitted('reparse')).toEqual([['source-old']])

    await wrapper.setProps({ readonly: true })
    expect(reparse.attributes('disabled')).toBeDefined()
  })
})
