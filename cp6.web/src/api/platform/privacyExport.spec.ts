import { describe, expect, it, vi } from 'vitest'
import { privacyExportApi, savePrivacyDownload } from './privacyExport'
const http = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('@/api/http', () => ({ default: http }))
describe('privacy artifact download', () => {
  it('returns bytes to the identity-aware caller without automatically starting a download', async () => {
    const blob = new Blob(['private data']); http.get.mockResolvedValue(blob)
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    expect(await privacyExportApi.download('artifact')).toBe(blob); expect(click).not.toHaveBeenCalled(); click.mockRestore()
  })
  it('releases the object URL after an explicitly authorized download', () => {
    const create = vi.fn(() => 'blob:owned'), revoke = vi.fn(); Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: create }); Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: revoke })
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    savePrivacyDownload(new Blob(['private data']), 'artifact'); expect(click).toHaveBeenCalledTimes(1); expect(revoke).toHaveBeenCalledWith('blob:owned'); expect(document.querySelector('a')).toBeNull(); click.mockRestore()
  })
})
