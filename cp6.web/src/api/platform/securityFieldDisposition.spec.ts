import { beforeEach, expect, it, vi } from 'vitest'
const mock = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('@/api/http', () => ({ default: mock }))
import { securityFieldDispositionApi as api } from './securityFieldDisposition'
beforeEach(() => vi.resetAllMocks())
it('preserves plain HTTP Owner results and null policies without inventing an envelope or an existing result', async () => {
  mock.get.mockResolvedValueOnce(null); expect(await api.policy('policy')).toBeNull()
  const absent = { operationId: 'op', state: 'NOT_FOUND', kind: 'CLOSE' }; mock.get.mockResolvedValueOnce(absent); expect(await api.operation('op')).toBe(absent)
  const original = { operationId: 'op', state: 'COMMITTED', kind: 'EXECUTE', execution: { clearedValueCount: 2 } }; mock.post.mockResolvedValueOnce(original)
  expect(await api.execute('plan', { operationId: 'op', expectedVersion: 'approved', expectedScopeDigest: 'exact', confirmationRef: 'basis' })).toBe(original)
  expect(mock.post).toHaveBeenCalledTimes(1); expect(mock.post).toHaveBeenCalledWith('/platform/privacy-dispositions-v1/security-fields/plans/plan/execute', { operationId: 'op', expectedVersion: 'approved', expectedScopeDigest: 'exact', confirmationRef: 'basis' })
})
