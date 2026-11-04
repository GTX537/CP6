import { expect, it, vi } from 'vitest'
const http = vi.hoisted(() => ({ post: vi.fn(), get: vi.fn() }))
vi.mock('../http', () => ({ default: http }))
import { locationRuleWithdrawalApi as api } from './locationRuleWithdrawal'
it('returns the committed application conflict while preserving unrelated HTTP failures', async () => {
  const result = { code: 0, data: { state: 'CONFLICT', appliedReceipt: { application: 'APPLIED' } } }
  http.post.mockRejectedValueOnce({ response: { status: 409, data: result } })
  expect(await api.withdraw('rule', 'submission', 'version', { commandId: 'operation', reason: 'current request' }, null)).toBe(result)
  const stale = { response: { status: 412, data: { code: 412, message: 'LOCATION_RULE_REVIEW_CONTROL_CHANGED' } } }
  http.post.mockRejectedValueOnce(stale)
  await expect(api.withdraw('rule', 'submission', 'version', { commandId: 'operation', reason: 'current request' }, null)).rejects.toBe(stale)
})
