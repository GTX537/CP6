import { beforeEach, describe, expect, it, vi } from 'vitest'
const factory = vi.hoisted(() => ({ build: vi.fn() }))
vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl() { return this }; withAutomaticReconnect() { return this }; configureLogging() { return this }
    build() { return factory.build() }
  }, LogLevel: { Warning: 3 }, HubConnectionState: { Disconnected: 'Disconnected' },
}))
function connection() { return { state: 'Disconnected', start: vi.fn().mockResolvedValue(undefined), stop: vi.fn().mockResolvedValue(undefined) } }
beforeEach(() => { vi.resetModules(); factory.build.mockReset() })
describe('MES connection ownership', () => {
  it('keeps a replacement connection when an earlier stop completes late', async () => {
    const old = connection(), fresh = connection()
    let finish!: () => void
    old.stop.mockImplementation(() => new Promise<void>(resolve => { finish = resolve }))
    factory.build.mockReturnValueOnce(old).mockReturnValueOnce(fresh)
    const mes = await import('./mesHub')
    expect(mes.getMesHub()).toBe(old)
    const stopping = mes.stopMesHub()
    expect(mes.getMesHub()).toBe(fresh)
    finish(); await stopping
    expect(mes.getMesHub()).toBe(fresh)
  })
  it('stops an obsolete connection whose start finishes after disposal', async () => {
    const old = connection(); let finish!: () => void
    old.start.mockImplementation(() => new Promise<void>(resolve => { finish = resolve }))
    factory.build.mockReturnValue(old)
    const mes = await import('./mesHub')
    const starting = mes.startMesHub(); await mes.stopMesHub()
    old.state = 'Connected'; finish(); await starting
    expect(old.stop).toHaveBeenCalledTimes(2)
  })
})
