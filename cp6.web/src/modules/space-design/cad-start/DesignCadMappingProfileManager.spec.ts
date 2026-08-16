import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import DesignCadMappingProfileManager from './DesignCadMappingProfileManager.vue'
import { designCadMappingProfileApi } from '@/api/space/designCadMappingProfiles'

vi.mock('@/api/space/designCadMappingProfiles', async (loadOriginal) => {
  const original = await loadOriginal<typeof import('@/api/space/designCadMappingProfiles')>()
  return {
    ...original,
    designCadMappingProfileApi: {
      listProfiles: vi.fn(),
      getProfile: vi.fn(),
      save: vi.fn(),
    },
  }
})

const system = {
  id: 'system-1',
  name: 'CP6 standard warehouse',
  scope: 'System' as const,
  version: 1,
  isReadOnly: true,
  isEnabled: true,
  definitionSha256: 'a'.repeat(64),
  rules: [{
    ruleId: 'rack-layer',
    priority: 100,
    sourceKind: 'Layer' as const,
    matchKind: 'Glob' as const,
    pattern: '*RACK*',
    target: 'Rack' as const,
    geometryRule: 'DirectGeometry' as const,
    confidenceWeight: .9,
    isRequired: false,
  }],
}

describe('DesignCadMappingProfileManager', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('crypto', { randomUUID: vi.fn(() => 'idempotency-1') })
    vi.mocked(designCadMappingProfileApi.listProfiles).mockResolvedValue([system])
    vi.mocked(designCadMappingProfileApi.getProfile).mockResolvedValue(system)
  })

  it('copies the read-only system profile into a tenant-owned version', async () => {
    const tenant = {
      ...system,
      id: 'tenant-1',
      name: 'Warehouse A',
      scope: 'Tenant' as const,
      isReadOnly: false,
      rowVersion: 'AQAAAA==',
      basedOnProfileId: system.id,
      basedOnVersion: 1,
    }
    vi.mocked(designCadMappingProfileApi.save).mockResolvedValue({
      profile: tenant,
      created: true,
      idempotentReplay: false,
    })
    vi.mocked(designCadMappingProfileApi.listProfiles)
      .mockResolvedValueOnce([system])
      .mockResolvedValueOnce([system, tenant])
    vi.mocked(designCadMappingProfileApi.getProfile)
      .mockResolvedValueOnce(system)
      .mockResolvedValueOnce(tenant)

    const wrapper = mount(DesignCadMappingProfileManager, {
      props: { initialProfileId: system.id, initialProfileVersion: 1 },
      attachTo: document.body,
    })
    await wrapper.get('.manager-toggle').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('系统只读')
    await wrapper.get('input[aria-label="CAD Mapping Profile 名称"]')
      .setValue('Warehouse A')
    await wrapper.get('.manager-actions .primary').trigger('click')
    await flushPromises()

    expect(designCadMappingProfileApi.save).toHaveBeenCalledWith({
      profileId: null,
      name: 'Warehouse A',
      isEnabled: true,
      rules: system.rules,
      expectedRowVersion: null,
      copyFromProfileId: system.id,
      copyFromVersion: 1,
    }, 'idempotency-1')
    expect(wrapper.emitted('saved')?.[0]).toEqual([tenant])
    expect(wrapper.text()).toContain('租户私有')
    wrapper.unmount()
  })

  it('appends a new immutable version for a tenant profile', async () => {
    const tenant = {
      ...system,
      id: 'tenant-1',
      name: 'Warehouse A',
      scope: 'Tenant' as const,
      isReadOnly: false,
      rowVersion: 'AQAAAA==',
    }
    const v2 = { ...tenant, version: 2, rowVersion: 'AgAAAA==' }
    vi.mocked(designCadMappingProfileApi.listProfiles)
      .mockResolvedValueOnce([system, tenant])
      .mockResolvedValueOnce([system, v2])
    vi.mocked(designCadMappingProfileApi.getProfile)
      .mockResolvedValueOnce(tenant)
      .mockResolvedValueOnce(v2)
    vi.mocked(designCadMappingProfileApi.save).mockResolvedValue({
      profile: v2,
      created: false,
      idempotentReplay: false,
    })

    const wrapper = mount(DesignCadMappingProfileManager, {
      props: { initialProfileId: tenant.id, initialProfileVersion: 1 },
    })
    await wrapper.get('.manager-toggle').trigger('click')
    await flushPromises()
    await wrapper.get('input[aria-label="规则 1 置信度"]').setValue('0.88')
    await wrapper.get('.manager-actions .primary').trigger('click')
    await flushPromises()

    expect(designCadMappingProfileApi.save).toHaveBeenCalledWith(
      expect.objectContaining({
        profileId: tenant.id,
        expectedRowVersion: tenant.rowVersion,
        copyFromProfileId: null,
        copyFromVersion: null,
        rules: [expect.objectContaining({ confidenceWeight: .88 })],
      }),
      'idempotency-1',
    )
    expect(wrapper.text()).toContain('将追加 v3')
    wrapper.unmount()
  })
})
