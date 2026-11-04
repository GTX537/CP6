import { beforeEach, describe, expect, it, vi } from 'vitest'
const get = vi.hoisted(() => vi.fn())
vi.mock('@/api/http', () => ({ default: { get } }))
import { gdprApi } from './gdpr'

describe('subject export file and scope', () => {
  beforeEach(() => { vi.restoreAllMocks(); get.mockReset() })
  it('downloads the actual returned package once and returns only its manifest to the view', async () => {
    const manifest = { subjectId: 'subject', complete: false, sources: [] }
    const blob = { text: async () => JSON.stringify({ manifest, user: { Email: 'simulated@example.invalid' } }) }
    get.mockResolvedValue(blob)
    const create = vi.fn(() => 'blob:owned-export'), revoke = vi.fn()
    vi.stubGlobal('URL', { createObjectURL: create, revokeObjectURL: revoke })
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    expect(await gdprApi.exportSubject('subject')).toEqual(manifest)
    expect(get).toHaveBeenCalledWith('/platform/gdpr/export/subject/subject', { responseType: 'blob' })
    expect(create).toHaveBeenCalledWith(blob); expect(click).toHaveBeenCalledTimes(1); expect(revoke).toHaveBeenCalledWith('blob:owned-export')
    expect(document.querySelector('a')).toBeNull(); vi.unstubAllGlobals()
  })
  it('does not turn a failed Owner read into an empty downloaded package', async () => {
    get.mockRejectedValue(new Error('Owner unavailable'))
    const create = vi.fn(); vi.stubGlobal('URL', { createObjectURL: create })
    await expect(gdprApi.exportSubject('subject')).rejects.toThrow('Owner unavailable')
    expect(create).not.toHaveBeenCalled(); vi.unstubAllGlobals()
  })
})
