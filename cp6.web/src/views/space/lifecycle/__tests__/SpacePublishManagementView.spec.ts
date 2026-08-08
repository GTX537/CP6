// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus, { ElMessageBox, ElSelect } from 'element-plus'
import SpacePublishManagementView from '../SpacePublishManagementView.vue'
import { siteApi } from '@/api/space/site'
import { publishManagementApi } from '@/api/space/publishManagement'

vi.mock('@/api/space/site', () => ({ siteApi: { list: vi.fn() } }))
vi.mock('@/api/space/publishManagement', () => ({
  publishManagementApi: {
    getModel: vi.fn(),
    getVersions: vi.fn(),
    getActivities: vi.fn(),
    createValidation: vi.fn(),
    getValidation: vi.fn(),
    getPreview: vi.fn(),
    createAttempt: vi.fn(),
    getAttempt: vi.fn(),
    retryAttempt: vi.fn(),
    startRepublish: vi.fn(),
    getRepublish: vi.fn(),
  },
}))

const model = {
  id: 'm1', siteId: 's1', mode: 'Design', cutoverState: 'Ready',
  currentPublishedVersionId: 'v1', rowVersion: 'rv',
}
const versions = [
  { id: 'v3', modelId: 'm1', siteId: 's1', versionNo: '3', name: 'Q3 layout', status: 'Ready', purpose: 'Production' },
  { id: 'v1', modelId: 'm1', siteId: 's1', versionNo: '1', name: 'Current', status: 'Published', purpose: 'Production' },
  { id: 'v0', modelId: 'm1', siteId: 's1', versionNo: '0', name: 'Stable history', status: 'Superseded', purpose: 'Production' },
]
const passedValidation = {
  id: 'val1', modelVersionId: 'v3', status: 'Passed', blockingCount: 0, warningCount: 1, infoCount: 2, issues: [],
}
const preview = {
  targetVersionId: 'v3', baseVersionId: 'v1', validationRunId: 'val1', validationStatus: 'Passed',
  validationBlockingCount: 0, planHash: '1234567890abcdef', adapterId: 'wms-test', publishable: true,
  itemCount: 1, changeCount: 1, matchedItemCount: 1,
  changes: { createCount: 1, updateMasterCount: 0, updateGeometryOnlyCount: 0, disableCount: 0, restoreCount: 0, noOpCount: 0 },
  wmsImpact: { wmsCreateCount: 1, wmsUpdateCount: 0, wmsDisableCount: 0, wmsRestoreCount: 0, wmsNoOpCount: 0, runtimeOnlyCount: 0, blockingCount: 0 },
  items: [{ sequenceNo: 1, objectType: 'Location', logicalId: 'l1', action: 'Create', afterCode: 'A-01', impactCode: 'WmsCreate', blocking: false }],
}
const completedAttempt = {
  id: 'a1', siteId: 's1', targetVersionId: 'v3', status: 'Completed', currentStep: 'Completed',
  summary: 'Published', jobAttemptCount: 1, jobMaxAttempts: 5, batches: [], auditEvents: [], openReconciliationIssueCount: 0,
}

function mountView() {
  return mount(SpacePublishManagementView, {
    global: {
      plugins: [createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': {} }, missingWarn: false, fallbackWarn: false }), ElementPlus],
      directives: { permission: { mounted: () => undefined } },
      stubs: { RouterLink: RouterLinkStub },
    },
    attachTo: document.body,
  })
}

async function selectSite(wrapper: ReturnType<typeof mountView>) {
  await flushPromises()
  wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', 's1')
  await flushPromises()
}

describe('SpacePublishManagementView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('crypto', { randomUUID: () => '00000000-0000-4000-8000-000000000001' })
    vi.mocked(siteApi.list).mockResolvedValue({ code: 0, message: '', data: [{ id: 's1', siteCode: 'SEA', siteName: 'Seattle', enable: true }] })
    vi.mocked(publishManagementApi.getModel).mockResolvedValue(model)
    vi.mocked(publishManagementApi.getVersions).mockResolvedValue({ items: versions })
    vi.mocked(publishManagementApi.getActivities).mockResolvedValue({ items: [] })
    vi.mocked(publishManagementApi.createValidation).mockResolvedValue({ validation: { ...passedValidation, status: 'Queued' }, reused: false })
    vi.mocked(publishManagementApi.getValidation).mockResolvedValue(passedValidation)
    vi.mocked(publishManagementApi.getPreview).mockResolvedValue(preview)
    vi.mocked(publishManagementApi.createAttempt).mockResolvedValue({ attempt: completedAttempt, idempotentReplay: false })
    vi.mocked(publishManagementApi.getAttempt).mockResolvedValue(completedAttempt)
  })

  afterEach(() => {
    document.body.innerHTML = ''
    vi.unstubAllGlobals()
  })

  it('完成验证、差异确认并携带稳定幂等键启动发布', async () => {
    const wrapper = mountView()
    await selectSite(wrapper)
    expect(publishManagementApi.getModel).toHaveBeenCalledWith('s1')
    expect(wrapper.text()).toContain('v1 · Current')

    const validate = wrapper.findAll('button').find(button => button.text().includes('开始验证'))!
    await validate.trigger('click')
    await flushPromises()
    expect(publishManagementApi.createValidation).toHaveBeenCalledWith('v3')
    expect(publishManagementApi.getPreview).toHaveBeenCalledWith('v3', expect.objectContaining({ limit: 100 }))
    expect(wrapper.text()).toContain('A-01')

    const checkbox = wrapper.find('.risk-check input')
    await checkbox.setValue(true)
    const publishButton = wrapper.findAll('button').find(button => button.text().includes('启动生产发布'))!
    await publishButton.trigger('click')
    await flushPromises()
    expect(publishManagementApi.createAttempt).toHaveBeenCalledWith(
      'v3',
      expect.objectContaining({ expectedPublishedVersionId: 'v1', validationRunId: 'val1', planHash: '1234567890abcdef' }),
      'space-publish-00000000-0000-4000-8000-000000000001',
    )
    expect(wrapper.text()).toContain('已完成')
  })

  it('恢复失败发布记录并提交带原因的人工重试', async () => {
    const failed = { ...completedAttempt, id: 'a-failed', status: 'FailedNoEffect', currentStep: 'ApplyWms', lastErrorCode: 'WMS_TIMEOUT', summary: 'No external effect' }
    vi.mocked(publishManagementApi.getActivities).mockResolvedValue({ items: [{
      id: 'a-failed', siteId: 's1', targetVersionId: 'v3', targetVersionNo: '3', targetVersionName: 'Q3 layout',
      status: 'FailedNoEffect', currentStep: 'ApplyWms', startedAtUtc: '2026-08-08T12:00:00Z', jobStatus: 'Failed',
      jobAttemptCount: 5, jobMaxAttempts: 5, openReconciliationIssueCount: 0,
    }] })
    vi.mocked(publishManagementApi.getAttempt).mockResolvedValue(failed)
    vi.mocked(publishManagementApi.retryAttempt).mockResolvedValue({ attempt: completedAttempt, idempotentReplay: false })
    const wrapper = mountView()
    await selectSite(wrapper)
    await wrapper.find('.activity-item').trigger('click')
    await flushPromises()
    await wrapper.findAll('button').find(button => button.text().includes('人工重试'))!.trigger('click')
    await flushPromises()
    const textareas = document.body.querySelectorAll('textarea')
    ;(textareas[0] as HTMLTextAreaElement).value = 'WMS 已恢复'
    textareas[0].dispatchEvent(new Event('input'))
    await flushPromises()
    const confirm = Array.from(document.body.querySelectorAll('button')).find(button => button.textContent?.includes('确认重试'))!
    confirm.click()
    await flushPromises()
    expect(publishManagementApi.retryAttempt).toHaveBeenCalledWith(
      'a-failed',
      expect.objectContaining({ reason: 'WMS 已恢复' }),
      'space-publish-retry-00000000-0000-4000-8000-000000000001',
    )
  })

  it('把 409 冲突作为可恢复业务状态展示，不丢失当前发布幂等键', async () => {
    const alert = vi.spyOn(ElMessageBox, 'alert').mockResolvedValue('confirm')
    vi.mocked(publishManagementApi.createAttempt).mockRejectedValue({ response: { status: 409, data: { code: 'SPACE_VERSION_CONFLICT', detail: '线上版本已经变化', recoveryAction: 'refresh-publish-preview' } } })
    const wrapper = mountView()
    await selectSite(wrapper)
    await wrapper.findAll('button').find(button => button.text().includes('开始验证'))!.trigger('click')
    await flushPromises()
    await wrapper.find('.risk-check input').setValue(true)
    await wrapper.findAll('button').find(button => button.text().includes('启动生产发布'))!.trigger('click')
    await flushPromises()
    expect(alert).toHaveBeenCalledWith(expect.stringContaining('线上版本已经变化'), 'SPACE_VERSION_CONFLICT', expect.objectContaining({ type: 'warning' }))
  })
})
