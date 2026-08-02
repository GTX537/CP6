// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import { permission } from '@/directives/permission'
import { siteApi } from '@/api/space/site'
import { spaceAiAdminApi, type SpaceAiPolicy, type SpaceAiUsagePage } from '@/api/space/aiAdmin'
import SpaceAiAdminView from '../SpaceAiAdminView.vue'

const { permissionState } = vi.hoisted(() => ({
  permissionState: { has: (_key: string) => true },
}))

vi.mock('@/stores/permission', () => ({
  usePermissionStore: () => ({ loaded: true, has: (key: string) => permissionState.has(key) }),
}))

vi.mock('@/api/space/site', () => ({
  siteApi: { list: vi.fn() },
}))

vi.mock('@/api/space/aiAdmin', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api/space/aiAdmin')>()
  return {
    ...actual,
    spaceAiAdminApi: {
      getPolicy: vi.fn(),
      updatePolicy: vi.fn(),
      getUsage: vi.fn(),
    },
  }
})

const policy: SpaceAiPolicy = {
  version: 0,
  dataPolicy: 'Disabled',
  allowedSiteIds: [],
  allowedProviderAliases: [],
  maxConcurrentRuns: 3,
  externalProviderEnabled: false,
  dailyBudgetMinor: null,
  monthlyBudgetMinor: null,
  currency: null,
  approvedProviders: [{ alias: 'local-approved', kind: 'Local' }],
}

const usage: SpaceAiUsagePage = {
  items: [{
    id: 'usage-1', runId: 'run-1', providerAlias: 'local-approved', providerModel: 'model-v1',
    inputUnits: 12, outputUnits: 4, estimatedCostMinor: 3, actualCostMinor: null,
    currency: 'USD', latencyMs: 250, outcome: 'Succeeded', recordedAtUtc: '2026-08-02T10:00:00Z',
  }],
  total: 1, page: 1, pageSize: 25,
  summary: {
    totalRuns: 1, inputUnits: 12, outputUnits: 4, estimatedCostMinor: 3,
    actualCostMinor: 0, hasUnpricedUsage: false,
    dailyBudget: { limitMinor: 100, consumedMinor: 3, remainingMinor: 97, currency: 'USD' },
    monthlyBudget: { limitMinor: 1000, consumedMinor: 3, remainingMinor: 997, currency: 'USD' },
  },
}

function mountView() {
  const i18n = createI18n({
    legacy: false,
    locale: 'en',
    missingWarn: false,
    fallbackWarn: false,
    messages: {
      en: {
        space: { aiAdmin: {
          title: 'AI policy, budgets, and usage',
          safetyDescription: 'Approved aliases only; endpoints, URLs, and keys are not collected.',
          savePolicy: 'Save policy',
          policyDisabled: 'Disabled',
          metadataOnly: 'Metadata only',
          structuredFeatures: 'Structured features',
          estimated: 'Estimated {amount}',
          actual: 'Actual {amount}',
          succeeded: 'Succeeded', failed: 'Failed', unknown: 'Unknown',
        } },
      },
    },
  })
  return mount(SpaceAiAdminView, {
    global: { plugins: [i18n, ElementPlus], directives: { permission } },
  })
}

describe('SpaceAiAdminView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    permissionState.has = () => true
    vi.mocked(spaceAiAdminApi.getPolicy).mockResolvedValue(policy)
    vi.mocked(spaceAiAdminApi.getUsage).mockResolvedValue(usage)
    vi.mocked(spaceAiAdminApi.updatePolicy).mockResolvedValue({ policy: { ...policy, version: 1 }, idempotentReplay: false })
    vi.mocked(siteApi.list).mockResolvedValue({
      code: 0, message: '', data: [{ id: 'site-1', siteCode: 'NYC', siteName: 'New York', enable: true }],
    })
  })

  it('loads the fail-closed policy, sites, approved aliases, and usage', async () => {
    const wrapper = mountView()
    await flushPromises()

    expect(spaceAiAdminApi.getPolicy).toHaveBeenCalledOnce()
    expect(spaceAiAdminApi.getUsage).toHaveBeenCalledOnce()
    expect(siteApi.list).toHaveBeenCalledOnce()
    expect(wrapper.text()).toContain('local-approved')
    expect(wrapper.text()).toContain('model-v1')
    expect(wrapper.text()).toContain('USD 3')
  })

  it('does not render tenant-editable endpoint, URL, or secret fields', async () => {
    const wrapper = mountView()
    await flushPromises()
    const labels = wrapper.findAll('.el-form-item__label').map(item => item.text()).join('|')

    expect(labels).not.toMatch(/endpoint|api.?key|secret|url/i)
  })

  it('removes the save action when manage permission is absent', async () => {
    permissionState.has = key => key !== 'space-ai-admin:manage'
    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.findAll('.el-button').some(button => button.text() === 'Save policy')).toBe(false)
  })
})
