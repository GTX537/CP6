import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ElMessage, ElMessageBox } from 'element-plus'
import { designSourcesApi } from '@/api/space/designSources'
import DesignSourceList from './DesignSourceList.vue'

vi.mock('@/api/space/designSources', () => ({
  designSourcesApi: {
    list: vi.fn(),
    getRemovalPreview: vi.fn(),
    remove: vi.fn(),
  },
}))

vi.mock('element-plus', async (importOriginal) => {
  const actual = await importOriginal<typeof import('element-plus')>()
  return {
    ...actual,
    ElMessage: { success: vi.fn() },
    ElMessageBox: { confirm: vi.fn() },
  }
})

const source = {
  id: 'source-1',
  modelVersionId: 'version-1',
  sourceType: 'Dwg',
  fileId: 'file-1',
  displayName: 'warehouse.dwg',
  sha256: 'a'.repeat(64),
  state: 'Ready',
  rowVersion: 'AAAAAAAAAAE=',
}

function mountList(readonly = false) {
  return mount(DesignSourceList, {
    props: { versionId: 'version-1', readonly },
    global: {
      directives: { loading: {} },
    },
  })
}

describe('DesignSourceList', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(designSourcesApi.list).mockResolvedValue({ items: [source] })
  })

  it('distinguishes active blockers from retained audit evidence', async () => {
    vi.mocked(designSourcesApi.getRemovalPreview).mockResolvedValue({
      sourceId: source.id,
      fileId: source.fileId,
      displayName: source.displayName,
      sourceType: source.sourceType,
      state: source.state,
      versionContentRevision: 7,
      sourceRowVersion: source.rowVersion,
      canRemove: false,
      physicalFileRetained: true,
      references: [
        { code: 'ACTIVE_JOB_REFERENCE', count: 1, blocksRemoval: true },
        { code: 'JOB_AUDIT_REFERENCE', count: 2, blocksRemoval: false },
      ],
    })
    const wrapper = mountList()
    await flushPromises()

    await wrapper.get('[data-test="inspect-source-source-1"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('阻断 · 后台任务仍在运行 × 1')
    expect(wrapper.text()).toContain('保留 · 已完成任务审计 × 2')
    expect(wrapper.get('[data-test="remove-source-source-1"]').attributes('disabled'))
      .toBeDefined()
  })

  it('applies preview fences and keeps file/audit evidence', async () => {
    vi.mocked(designSourcesApi.getRemovalPreview).mockResolvedValue({
      sourceId: source.id,
      fileId: source.fileId,
      displayName: source.displayName,
      sourceType: source.sourceType,
      state: source.state,
      versionContentRevision: 8,
      sourceRowVersion: source.rowVersion,
      canRemove: true,
      physicalFileRetained: true,
      references: [
        { code: 'JOB_AUDIT_REFERENCE', count: 1, blocksRemoval: false },
      ],
    })
    vi.mocked(ElMessageBox.confirm).mockResolvedValue('confirm' as never)
    vi.mocked(designSourcesApi.remove).mockResolvedValue({
      sourceId: source.id,
      versionContentRevision: 9,
      physicalFileRetained: true,
      idempotentReplay: false,
    })
    vi.mocked(designSourcesApi.list)
      .mockResolvedValueOnce({ items: [source] })
      .mockResolvedValueOnce({ items: [] })
    const wrapper = mountList()
    await flushPromises()

    await wrapper.get('[data-test="inspect-source-source-1"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-test="remove-source-source-1"]').trigger('click')
    await flushPromises()

    expect(designSourcesApi.remove).toHaveBeenCalledWith(
      'version-1',
      'source-1',
      {
        expectedContentRevision: 8,
        expectedSourceRowVersion: 'AAAAAAAAAAE=',
      },
    )
    expect(ElMessageBox.confirm).toHaveBeenCalledWith(
      expect.stringContaining('物理文件和审计证据会继续保留'),
      '移除来源',
      expect.any(Object),
    )
    expect(ElMessage.success).toHaveBeenCalledWith('来源已移出工作台，证据仍保留')
    expect(wrapper.emitted('sourceRemoved')).toEqual([['source-1', 9]])
  })
})
