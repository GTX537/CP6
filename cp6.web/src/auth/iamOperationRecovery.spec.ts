import { beforeEach, describe, expect, it, vi } from 'vitest'
const api = vi.hoisted(() => ({ resolve: vi.fn() }))
vi.mock('@/api/sys/iam', () => ({ iamApi: api }))
import { pendingIamOperation, resolveIamOperation, saveIamOperation } from './iamOperationRecovery'
const scope = { tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002' }
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks()
  let queue = Promise.resolve()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: () => Promise<unknown>) => {
    const next = queue.then(run); queue = next.then(() => undefined, () => undefined); return next
  } } })
})
describe('IAM original operation recovery', () => {
  it('retains only the original reference after lost response and refuses a second tab write until close', async () => {
    let first!: () => void
    const started = new Promise<void>(resolve => { first = resolve })
    let fail!: (error: Error) => void
    const write = vi.fn(async () => { first(); return new Promise<any>((_resolve, reject) => { fail = reject }) })
    const a = saveIamOperation(scope, 'user-create', undefined, write); await started
    const original = pendingIamOperation(scope)!
    const b = saveIamOperation(scope, 'user-create', undefined, write)
    const denied = expect(b).rejects.toThrow('上一次操作'); const lost = expect(a).rejects.toThrow('lost')
    fail(new Error('lost')); await lost; await denied
    expect(write).toHaveBeenCalledTimes(1); expect(pendingIamOperation(scope)).toEqual(original)
    api.resolve.mockResolvedValue({ ...original, state: 'UNKNOWN' }); await resolveIamOperation(scope)
    expect(pendingIamOperation(scope)).toEqual(original)
    api.resolve.mockResolvedValue({ ...original, state: 'CLOSED' }); await resolveIamOperation(scope, true)
    expect(api.resolve).toHaveBeenLastCalledWith(original.operationId, 'user-create', true)
    expect(pendingIamOperation(scope)).toBeNull()
  })
  it('does not accept a response from the previous identity or mismatched operation', async () => {
    await expect(saveIamOperation(scope, 'user-create', undefined, async id => {
      localStorage.setItem('cp6_auth_epoch_v1', 'switched')
      localStorage.setItem('userName', 'other')
      return { operationId: id, kind: 'user-create', state: 'COMMITTED' }
    })).rejects.toThrow()
    expect(pendingIamOperation(scope)).not.toBeNull()
    api.resolve.mockResolvedValue({ operationId: crypto.randomUUID(), kind: 'user-create', state: 'COMMITTED' })
    await expect(resolveIamOperation(scope)).rejects.toThrow('不匹配')
    expect(pendingIamOperation(scope)).not.toBeNull()
  })
  it('fails before any write when persistent storage or cross-tab locking is unavailable', async () => {
    const write = vi.fn(); Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined })
    await expect(saveIamOperation(scope, 'user-create', undefined, write)).rejects.toThrow('恢复锁')
    expect(write).not.toHaveBeenCalled()
  })
})
