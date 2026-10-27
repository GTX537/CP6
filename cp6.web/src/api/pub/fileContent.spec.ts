import { expect, it, vi } from 'vitest'
const transport = vi.hoisted(() => ({ post: vi.fn(), get: vi.fn() }))
vi.mock('../http', () => ({ default: transport }))
import { fileContentApi } from './fileContent'
it('keeps Unicode upload metadata out of the URL and preserves bounded raw bytes', async () => {
  const command = { operationId: 'op', fileId: 'file', parent: { owner: 'Main.Plm.EngineeringRevision.v1', id: 'parent', version: '1', digest: 'a'.repeat(64) }, fileName: '精确原件.pdf', byteLength: 3, digest: 'b'.repeat(64), reason: '文'.repeat(4000) }
  const bytes = new Uint8Array([1, 2, 3]).buffer; await fileContentApi.upload(command, bytes)
  const [url, body, config] = transport.post.mock.calls[0]!
  expect(url).toBe('/file-content/owner-v1/uploads'); expect(body).toBe(bytes); expect(config.params).toBeUndefined()
  expect(config.headers['X-File-Command'].length).toBeLessThan(30000)
  const decoded = Uint8Array.from(atob(config.headers['X-File-Command']), c => c.charCodeAt(0))
  expect(JSON.parse(new TextDecoder().decode(decoded))).toEqual(command)
})
