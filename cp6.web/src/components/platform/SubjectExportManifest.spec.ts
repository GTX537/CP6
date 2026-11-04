import { mount } from '@vue/test-utils'
import { describe, it, expect } from 'vitest'
import Panel from './SubjectExportManifest.vue'
import type { SubjectExportManifest } from '@/api/platform/gdpr'

const manifest = (): SubjectExportManifest => ({
  version: 'cp6.subject-export-manifest.v1', tenantId: 'target-tenant', subjectId: 'stable-subject', state: 'PARTIAL', complete: false,
  directoryCoverage: 'REGISTERED_AND_UNKNOWN', consistency: 'SEPARATE_OWNER_READS', sources: [
    { owner: 'MAIN.AUTHORIZATION', source: 'Sys_UserRole', status: 'INCLUDED', reason: 'ACTUAL_OWNER_READ', selector: 'TARGET_TENANT_AND_STABLE_USER_ID',
      recordCount: 0, readStartedAtUtc: '2026-11-04T10:00:00Z', readCompletedAtUtc: '2026-11-04T10:00:01Z', versionBasis: 'UNVERSIONED_LIVE_ROWS_READ_WINDOW',
      fields: [{ name: 'Creator', status: 'EXCLUDED_WITH_REASON', reason: 'OWNER_FIELD_NOT_CLASSIFIED_FOR_SUBJECT_EXPORT' }] },
    { owner: 'CRM', source: 'CRM business', status: 'UNKNOWN_SCOPE', reason: 'SUBJECT_MAPPING_AND_EXPORT_ADAPTER_NOT_ADOPTED', selector: 'NOT_QUERIED',
      recordCount: null, readStartedAtUtc: null, readCompletedAtUtc: null, versionBasis: 'NOT_CAPTURED', fields: [] },
  ],
})
describe('subject export coverage', () => {
  it('shows genuine zero separately from unqueried sources and does not claim complete export', () => {
    const w = mount(Panel, { props: { manifest: manifest() } })
    expect(w.get('tbody').text()).toContain('角色关联0 条')
    expect(w.text()).toContain('1 个来源尚未确认主体归属或尚未接入导出')
    expect(w.text()).toContain('客户关系资料'); expect(w.text()).toContain('本次导出为部分资料')
    expect(w.text()).toContain('并非同一时点快照'); expect(w.text()).toContain('1 项字段未纳入')
    expect(w.find('a').exists()).toBe(false); expect(w.find('button').exists()).toBe(false); w.unmount()
  })
  it('replaces prior subject scope when a different result is displayed', async () => {
    const w = mount(Panel, { props: { manifest: manifest() } })
    await w.setProps({ manifest: { ...manifest(), subjectId: 'another-subject', tenantId: 'another-tenant', sources: [] } })
    expect(w.text()).not.toContain('stable-subject'); expect(w.text()).not.toContain('target-tenant')
    expect(w.text()).not.toContain('客户关系资料'); expect(w.text()).toContain('another-subject'); w.unmount()
  })
})
