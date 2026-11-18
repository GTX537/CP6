import { afterEach, expect, it, vi } from 'vitest'
import { flushPromises, shallowMount } from '@vue/test-utils'
import { AUTH_EVENT, newEpoch } from '@/auth/identity'
import Panel from './AuditRetentionPanel.vue'

const api = vi.hoisted(() => ({ preview: vi.fn(), readPlan: vi.fn(), executePlan: vi.fn() }))
vi.mock('@/api/sys/auditAccess', async original => ({ ...await original<typeof import('@/api/sys/auditAccess')>(), auditAccess: api }))
const wrappers: Array<{ unmount(): void }> = []
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); localStorage.clear(); sessionStorage.clear(); vi.resetAllMocks() })
function mount(): any {
  const wrapper = shallowMount(Panel, { global: { config: { warnHandler: () => {} } } })
  wrappers.push(wrapper)
  return wrapper.vm
}

it('在结果未知时保留原计划，查询回执前不再次执行', async () => {
  const vm = mount()
  api.preview.mockImplementation((kind, id) => Promise.resolve({ retentionClass: kind, planId: id, state: 'READY' }))
  api.executePlan.mockRejectedValue(new Error('提交确认丢失'))
  await vm.preview()
  const id = vm.planId
  await vm.execute()
  await vm.execute()
  expect(api.executePlan.mock.calls).toEqual([[id]])
  expect(vm.message).toContain('查询原计划')

  wrappers.pop()!.unmount()
  api.readPlan.mockResolvedValue({ retentionClass: 'TECH_TELEMETRY', planId: id, state: 'COMPLETED' })
  const resumed = mount()
  resumed.open()
  await flushPromises()
  expect(api.readPlan.mock.calls).toEqual([[id]])
  expect(resumed.plan.state).toBe('COMPLETED')
  await resumed.execute()
  expect(api.executePlan).toHaveBeenCalledTimes(1)
})

it('登录状态改变后丢弃迟到的处置回执', async () => {
  const vm = mount()
  let resolve!: (result: unknown) => void
  api.preview.mockReturnValue(new Promise(r => { resolve = r }))
  const preview = vm.preview()
  const id = vm.planId
  newEpoch()
  window.dispatchEvent(new Event(AUTH_EVENT))
  resolve({ retentionClass: 'TECH_TELEMETRY', planId: id, state: 'COMPLETED' })
  await preview
  expect(vm.planId).toBe('')
  expect(vm.plan).toBeUndefined()
  expect(sessionStorage.length).toBe(0)
})
