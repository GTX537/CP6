import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { FileCommand, FileSnapshot, FileUpload } from '@/api/pub/fileContent'
const api = vi.hoisted(() => ({ scope: vi.fn(), list: vi.fn(), read: vi.fn(), upload: vi.fn(), save: vi.fn(), download: vi.fn(), operation: vi.fn(), parents: vi.fn(), denied: [] as string[], authenticated: true }))
vi.mock('@/api/pub/fileContent', () => ({ fileContentApi: api }))
vi.mock('@/api/plm/engineeringRevisions', () => ({ revisionApi: { list: api.parents } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !api.denied.includes(key), loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './FileContentView.vue'
import { pendingFile } from '@/auth/fileContentRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const guid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { tenantId: guid(1), actorId: guid(2) }
const parent = { reference: { owner: 'Main.Plm.EngineeringRevision.v1', id: guid(3), version: '5', digest: 'b'.repeat(64) }, isCurrent: true, revisionCode: 'ENG-1', draft: { goal: 'Engineering revision' } }
const row: FileSnapshot = { tenantId: scope.tenantId, contentRef: { owner: 'Main.File.Content.v1', id: guid(4), version: '1', hashAlgorithm: 'SHA256', digest: 'a'.repeat(64), byteLength: 3, mediaType: 'text/plain' }, parent: parent.reference, fileName: 'Private original.txt', stateVersion: '3', etag: guid(5), inspection: { status: 'CLEAN', provider: 'Scanner fixture', engineVersion: 'engine', signatureVersion: 'signature', checkedAt: '2026-10-27T00:00:00Z', reason: 'Scan' }, held: false, availability: 'AVAILABLE', actorId: scope.actorId, recordedAt: '2026-10-27T00:00:00Z', action: 'INSPECT', reason: 'Inspected', isCurrent: true, originalDigest: 'c'.repeat(64) }
const ok = <T,>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined, persisted: FileSnapshot
const button = (text: string) => view!.findAll('button').find(x => x.text() === text)!
const field = (name: string) => view!.get(`[aria-label="${name}"]`)
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function submit(label: string) { field(label).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function render() { view = mount(View); await flushPromises() }
async function choose() { await render(); await submit('工程修订搜索'); await field('工程修订选择').setValue(`${parent.reference.owner}:${parent.reference.id}:${parent.reference.version}:${parent.reference.digest}`); await click('读取文件') }
async function pick(size = 3) { const file = new File([new Uint8Array([1, 2, 3])], 'Original.txt'); Object.defineProperty(file, 'size', { value: size }); Object.defineProperty(file, 'arrayBuffer', { value: async () => new Uint8Array([1, 2, 3]).buffer }); Object.defineProperty(field('原件文件').element, 'files', { configurable: true, value: [file] }); await field('原件文件').trigger('change'); await field('上传原因').setValue('Explicit source upload') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.denied = []; persisted = structuredClone(row)
  api.scope.mockResolvedValue(ok(scope)); api.parents.mockResolvedValue(ok([parent])); api.list.mockImplementation(async () => ok([structuredClone(persisted)])); api.read.mockImplementation(async () => ok(structuredClone(persisted)))
  api.upload.mockImplementation(async (input: FileUpload) => ok({ operationId: input.operationId, kind: 'FILE_CONTENT', objectId: input.fileId, version: '1', digest: 'c'.repeat(64) }))
  api.save.mockImplementation(async (input: FileCommand) => { persisted = { ...persisted, stateVersion: '4', held: input.action === 'HOLD', availability: input.action === 'HOLD' ? 'HELD' : 'AVAILABLE' }; return ok({ operationId: input.operationId, kind: 'FILE_CONTENT', objectId: input.fileId, version: '4', digest: 'c'.repeat(64) }) })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
  Object.defineProperty(crypto, 'subtle', { configurable: true, value: { digest: vi.fn(async () => new Uint8Array(32).fill(170).buffer) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('lists only the chosen current engineering parent and clears files when it changes', async () => {
  await choose(); expect(api.list).toHaveBeenCalledWith(parent.reference.id, 1); expect(view!.text()).toContain(row.fileName)
  await field('工程修订选择').setValue(''); expect(view!.text()).not.toContain(row.fileName)
})
it('uploads exact parent, hash and binary content without persisting file metadata or bytes', async () => {
  await choose(); await pick(); await submit('上传原因')
  const [command, bytes] = api.upload.mock.calls[0]!
  expect(command).toMatchObject({ parent: parent.reference, fileName: 'Original.txt', byteLength: 3, digest: 'a'.repeat(64), reason: 'Explicit source upload' })
  expect([...new Uint8Array(bytes)]).toEqual([1, 2, 3]); expect(pendingFile(scope)).toBeNull(); expect(view!.text()).toContain('原件已保存')
})
it('rejects oversized content before reading bytes or creating an operation', async () => {
  await choose(); await pick(20 * 1024 * 1024 + 1); await submit('上传原因'); expect(api.upload).not.toHaveBeenCalled(); expect(pendingFile(scope)).toBeNull(); expect(view!.text()).toContain('20 MiB')
})
it('keeps only original IDs after lost upload confirmation and recovers without reupload', async () => {
  await choose(); await pick(); api.upload.mockRejectedValue(new Error('Connection interrupted')); await submit('上传原因')
  const original = pendingFile(scope)!; expect(original).not.toBeNull(); const stored = Object.values(localStorage).join(''); expect(stored).not.toContain('Original.txt'); expect(stored).not.toContain('Explicit source upload')
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: { operationId: original.operationId, kind: 'FILE_CONTENT', objectId: original.objectId, version: '1', digest: 'c'.repeat(64) } })); await click('查询原操作')
  expect(api.upload).toHaveBeenCalledTimes(1); expect(pendingFile(scope)).toBeNull()
})
it('retains UNKNOWN and rejects a receipt for a different file', async () => {
  await choose(); await pick(); api.upload.mockRejectedValue(new Error('Unknown')); await submit('上传原因'); const original = pendingFile(scope)!
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingFile(scope)).toEqual(original)
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: { operationId: original.operationId, kind: 'FILE_CONTENT', objectId: guid(80), version: '1', digest: 'a'.repeat(64) } })); await click('查询原操作'); expect(pendingFile(scope)).toEqual(original)
})
it('keeps pending inspections and held files unavailable for download', async () => {
  persisted.availability = 'PENDING_INSPECTION'; persisted.inspection.status = 'PENDING'; await choose(); expect(button('下载原件').attributes('disabled')).toBeDefined(); await click('下载原件'); expect(api.download).not.toHaveBeenCalled()
})
it('uses current state CAS for hold and preserves exact historical inspection reads', async () => {
  await choose(); await click('查看文件'); await field('状态原因').setValue('Preserve evidence'); await click('设置保留')
  expect(api.save.mock.calls[0]![0]).toMatchObject({ action: 'HOLD', fileId: row.contentRef.id, expectedVersion: '3', expectedEtag: row.etag, reason: 'Preserve evidence' }); expect(view!.text()).toContain('保留中')
  await field('历史状态版本').setValue('1'); await submit('历史状态版本'); expect(api.read).toHaveBeenLastCalledWith(row.contentRef.id, '1'); expect(view!.text()).toContain('历史检查状态不代表当前可下载')
})
it('clears filenames and history on current permission denial', async () => {
  await choose(); await click('查看文件'); api.read.mockRejectedValue({ response: { status: 403 } }); await submit('历史状态版本'); expect(view!.text()).not.toContain(row.fileName); expect(view!.text()).toContain('当前无权')
})
it('ignores a late private response after identity changes', async () => {
  await choose(); let resolve!: (value: unknown) => void; api.read.mockImplementation(() => new Promise(r => { resolve = r })); await button('查看文件').trigger('click'); await flushPromises()
  api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(ok(row)); await flushPromises(); expect(view!.text()).not.toContain(row.fileName)
})
it('does not offer upload or state mutation without the corresponding action', async () => {
  api.denied = ['file-content:add', 'file-content:edit']; await choose(); await click('查看文件'); expect(button('上传原件')).toBeUndefined(); expect(button('设置保留')).toBeUndefined(); expect(button('下载原件')).toBeDefined()
})
it('rejects download bytes that do not match the exact original length', async () => {
  await choose(); api.download.mockResolvedValue({ arrayBuffer: async () => new Uint8Array([1]).buffer }); await click('下载原件'); expect(api.download).toHaveBeenCalledWith(row.contentRef); expect(view!.text()).toContain('下载内容与原版本不一致')
})
it('refuses mutations when browser coordination is unavailable', async () => {
  await choose(); await pick(); Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await submit('上传原因'); expect(api.upload).not.toHaveBeenCalled(); expect(pendingFile(scope)).toBeNull()
})
