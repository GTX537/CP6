import { beforeEach, describe, expect, it, vi } from 'vitest'
const http = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../http', () => ({ default: http }))
import { stockApi } from './stock'

describe('stock original operation client', () => {
  beforeEach(() => vi.resetAllMocks())
  it('preserves the caller operation key and does not retry an uncertain apply', async () => {
    const command = { operationId: crypto.randomUUID(), txnType: 'ADJ' as const, warehouseCd: 'W', locationCd: 'L', productCd: 'P', lotNo: '', qty: 1.12345678 }
    http.post.mockRejectedValueOnce(new Error('unknown commit'))
    await expect(stockApi.apply(command)).rejects.toThrow('unknown commit')
    expect(http.post).toHaveBeenCalledOnce()
    expect(http.post).toHaveBeenCalledWith('/wms/stock/commands/v2/apply', command)
    expect(command.qty).toBe(1.12345678)
  })
  it('keeps MOVE separate and queries the original without submitting a new move', async () => {
    const command = { operationId: crypto.randomUUID(), warehouseCd: 'W', fromLocationCd: 'A', toLocationCd: 'B', productCd: 'P', lotNo: '', qty: 2 }
    http.post.mockRejectedValueOnce(new Error('timeout'))
    await expect(stockApi.move(command)).rejects.toThrow('timeout')
    http.get.mockResolvedValue({ data: { operationId: command.operationId, state: 'UNKNOWN' } })
    expect((await stockApi.readOperation(command.operationId)).data.state).toBe('UNKNOWN')
    expect(http.post).toHaveBeenCalledOnce()
    expect(http.post).toHaveBeenCalledWith('/wms/stock/commands/v2/move', command)
    expect(http.get).toHaveBeenCalledWith(`/wms/stock/operations/v1/${command.operationId}`)
  })
  it('requires explicit close and obtains the current server scope', async () => {
    const id = crypto.randomUUID()
    http.post.mockResolvedValue({ data: { operationId: id, state: 'CLOSED' } })
    expect((await stockApi.closeOperation(id)).data.state).toBe('CLOSED')
    await stockApi.operationScope()
    expect(http.post).toHaveBeenCalledWith(`/wms/stock/operations/v1/${id}/close`)
    expect(http.get).toHaveBeenCalledWith('/wms/stock/operations/v1/scope')
    expect(http.post).toHaveBeenCalledOnce()
  })
})
