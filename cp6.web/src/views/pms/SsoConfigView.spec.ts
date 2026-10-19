import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ getConfig: vi.fn(), setConfig: vi.fn(), getOperation: vi.fn(), closeOperation: vi.fn(), roles: vi.fn(), warning: vi.fn() }))
vi.mock('@/api/sys/sso', () => ({ ssoApi: api }))
vi.mock('@/api/sys/role', () => ({ roleApi: { getAll: api.roles } }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (k: string) => k }) }))
vi.mock('element-plus', async original => ({ ...await original<object>(), ElMessage: { success: vi.fn(), warning: api.warning } }))
import SsoConfigView from './SsoConfigView.vue'
const cfg = { tenantId: '00000000-0000-0000-0000-000000000001', actorId: '00000000-0000-0000-0000-000000000002',
  configVersion: 'v1', policyVersion: 'none', admissionPolicy: null, authority: 'https://idp.example', clientId: 'client',
  scopes: 'openid email', emailClaim: 'email', enabled: false, enforced: false, autoProvision: false, defaultRoleId: null, hasClientSecret: true }
let view: ReturnType<typeof mount> | undefined
async function render() {
  view = mount(SsoConfigView, { global: { directives: { loading: {} }, stubs: {
    ElCard: { template: '<div><slot /></div>' }, ElForm: { template: '<form><slot /></form>', methods: { validate: () => Promise.resolve() } },
    ElFormItem: { template: '<div><slot /></div>' }, ElButton: { props: ['loading'], template: '<button :disabled="loading"><slot /></button>' },
    ElAlert: { props: ['title'], template: '<p role="status">{{title}}</p>' }, ElInput: true, ElSelect: true, ElOption: true, ElSwitch: true, ElCheckbox: true, ElDivider: true,
  } } }); await flushPromises(); return view.vm as any
}
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_: string, __: unknown, fn: () => Promise<unknown>) => fn() } })
  api.getConfig.mockResolvedValue(structuredClone(cfg)); api.roles.mockResolvedValue([{ roleId: 17, roleName: 'Enabled', enable: true }, { roleId: 18, roleName: 'Disabled', enable: false }])
})
afterEach(() => view?.unmount())
describe('explicit SSO admission and uncertain-save recovery', () => {
  it('does not preapprove issuer authority and refuses an unconfirmed approval', async () => {
    const vm = await render(); expect(view!.text()).toContain('尚未批准')
    expect(vm.form.admission.issuerIsEmailAuthority).toBe(false); expect(vm.roleOptions).toEqual([{ label: 'Enabled', value: 17 }])
    vm.form.reason = 'Approve'; vm.form.admission.action = 'approve'; vm.form.admission.approvedIssuer = 'https://issuer.example'
    await vm.handleSave(); expect(api.setConfig).not.toHaveBeenCalled(); expect(api.warning).toHaveBeenCalled()
  })
  it('retains the original operation through remount and exposes read/close without resending the secret', async () => {
    let vm = await render(); vm.form.reason = 'explicit'; vm.form.clientSecret = 'secret-draft'
    api.setConfig.mockRejectedValue(new Error('response lost')); await vm.handleSave(); await flushPromises()
    const id = vm.pending; expect(id).toBeTruthy(); expect(vm.form.clientSecret).toBe('')
    expect(view!.text()).toContain('查询原操作'); expect(JSON.stringify(localStorage)).not.toContain('secret-draft')
    view!.unmount(); vm = await render(); expect(vm.pending).toBe(id)
    await vm.handleSave(); expect(api.setConfig).toHaveBeenCalledTimes(1)
    api.getOperation.mockResolvedValue({ operationId: id, state: 'UNKNOWN' }); await vm.recover(false)
    expect(vm.pending).toBe(id); expect(api.setConfig).toHaveBeenCalledTimes(1)
    api.closeOperation.mockResolvedValue({ operationId: id, state: 'CLOSED' }); await vm.recover(true)
    expect(vm.pending).toBeNull(); expect(vm.form.admission.issuerIsEmailAuthority).toBe(false)
    expect(api.closeOperation).toHaveBeenCalledWith(id); expect(api.setConfig).toHaveBeenCalledTimes(1)
  })
  it('sends the loaded CAS versions and does not reload with an invalidated session after commit', async () => {
    const vm = await render(); vm.form.reason = 'explicit'; vm.form.admission.action = 'approve'
    vm.form.admission.issuerIsEmailAuthority = true; vm.form.admission.approvedIssuer = 'https://issuer.example'
    api.setConfig.mockImplementation(async input => ({ operationId: input.operationId, state: 'COMMITTED' }))
    await vm.handleSave(); await flushPromises()
    expect(api.setConfig.mock.calls[0]![0]).toMatchObject({ expectedConfigVersion: 'v1', expectedPolicyVersion: 'none', reason: 'explicit' })
    expect(api.getConfig).toHaveBeenCalledTimes(1); expect(vm.committed).toBe(true); expect(view!.text()).toContain('重新登录')
    await vm.handleSave(); expect(api.setConfig).toHaveBeenCalledTimes(1)
  })
})
