import { afterEach, describe, expect, it, vi } from 'vitest'
import { DirtyLocationBatcher } from './DirtyLocationBatcher'

describe('DirtyLocationBatcher', () => {
  afterEach(() => vi.useRealTimers())

  it('flushes a fixed two-second window even while events keep arriving', async () => {
    vi.useFakeTimers()
    const flush = vi.fn().mockResolvedValue(undefined)
    const batcher = new DirtyLocationBatcher(flush)

    batcher.add('A')
    await vi.advanceTimersByTimeAsync(1000)
    batcher.add('B')
    await vi.advanceTimersByTimeAsync(999)
    expect(flush).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)

    expect(flush).toHaveBeenCalledTimes(1)
    expect(flush).toHaveBeenCalledWith(['A', 'B'])
    batcher.dispose()
  })

  it('requeues a failed batch and retries without repeated error toasts', async () => {
    vi.useFakeTimers()
    const flush = vi.fn()
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValue(undefined)
    const onError = vi.fn()
    const batcher = new DirtyLocationBatcher(flush, onError)

    batcher.add('A')
    await vi.advanceTimersByTimeAsync(2000)
    expect(onError).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(5000)

    expect(flush).toHaveBeenCalledTimes(2)
    expect(flush).toHaveBeenLastCalledWith(['A'])
    expect(onError).toHaveBeenCalledTimes(1)
    batcher.dispose()
  })
})
