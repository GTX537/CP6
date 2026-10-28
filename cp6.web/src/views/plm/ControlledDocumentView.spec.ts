import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import type { DocumentCommand, DocumentSnapshot } from '@/api/plm/controlledDocument'
const api = vi.hoisted(() => ({ scope: vi.fn(), list: vi.fn(), read: vi.fn(), diff: vi.fn(), save: vi.fn(), download: vi.fn(), operation: vi.fn(), parents: vi.fn(), parentRead: vi.fn(), files: vi.fn(), denied: [] as string[], authenticated: true }))
vi.mock('@/api/plm/controlledDocument', () => ({ documentApi: api }))
vi.mock('@/api/pub/fileContent', () => ({ fileContentApi: { list: api.files } }))
vi.mock('@/api/plm/engineeringRevisions', () => ({ revisionApi: { list: api.parents, read: api.parentRead } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (key: string) => !api.denied.includes(key), loadMyActions: vi.fn() }) }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => api.authenticated }))
import View from './ControlledDocumentView.vue'
import { pendingDocument } from '@/auth/documentRecovery'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const guid = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const scope = { tenantId: guid(1), actorId: guid(2) }
const parent = { reference: { owner: 'Main.Plm.EngineeringRevision.v1', id: guid(3), version: '5', digest: 'b'.repeat(64) }, isCurrent: true, revisionCode: 'ENG-1', draft: { goal: 'Engineering revision', productRef: { owner: 'Main.Plm.EngineeringProduct.v1', id: guid(8), version: '2', digest: 'd'.repeat(64) } } }
const file = { tenantId: scope.tenantId, contentRef: { owner: 'Main.File.Content.v1' as const, id: guid(4), version: '1', hashAlgorithm: 'SHA256' as const, digest: 'a'.repeat(64), byteLength: 3, mediaType: 'text/plain' }, parent: parent.reference, fileName: 'Private source.txt', stateVersion: '3', availability: 'AVAILABLE' }
const row: DocumentSnapshot = { tenantId: scope.tenantId, reference: { owner: 'Main.Plm.ControlledDocument.v1', id: guid(6), version: '2', digest: 'c'.repeat(64) }, etag: guid(7), isCurrent: true, body: { name: 'Original document', revisionLabel: 'A', revisionOrigin: parent.reference, productRef: parent.draft.productRef, source: { kind: 'EXTERNAL', ownerName: 'Actual supplier', organization: 'Origin unit', sourceVersion: 'v2', sourceDate: '2026-10-27', acquisitionBasis: 'Supplied original', authorityLanguage: 'zh-CN', authorityBasis: 'Review required' }, contents: [{ occurrenceId: guid(9), role: 'SOURCE', file: file.contentRef, derivedFromOccurrenceId: null, language: 'zh-CN', derivationBasis: '' }] }, action: 'REVISE', reason: 'Actual correction', parentVersion: '1', actorId: scope.actorId, recordedAt: '2026-10-27T00:00:00Z', originalJson: '{}', missing: ['DOCUMENT_PROFESSIONAL_REVIEW_REQUIRED'], contentStatus: [{ occurrenceId: guid(9), fileName: file.fileName, availability: 'AVAILABLE', stateVersion: '3' }], state: 'DRAFT', readiness: 'REVIEW_REQUIRED' }
const ok = <T,>(data: T) => ({ code: 0, data })
let view: VueWrapper | undefined, persisted: DocumentSnapshot
const button = (text: string) => view!.findAll('button').find(x => x.text() === text)!
const field = (name: string) => view!.get(`[aria-label="${name}"]`)
async function click(text: string) { await button(text).trigger('click'); await flushPromises() }
async function submit(label: string) { field(label).element.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await flushPromises() }
async function choose() { view = mount(View, { global: { stubs: { ControlledDocumentReviewPanel: true } } }); await flushPromises(); await field('工程修订搜索').setValue('ENG'); await submit('工程修订搜索'); await field('工程修订选择').setValue(`${parent.reference.owner}:${parent.reference.id}:${parent.reference.version}:${parent.reference.digest}`); await click('读取文档') }
async function opened() { await choose(); await click('查看文档'); await field('修订原因').setValue('Explicit revision reason') }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); api.authenticated = true; api.denied = []; persisted = structuredClone(row)
  api.scope.mockResolvedValue(ok(scope)); api.parents.mockResolvedValue(ok([parent])); api.parentRead.mockResolvedValue(ok(parent)); api.files.mockResolvedValue(ok([file])); api.list.mockImplementation(async () => ok([structuredClone(persisted)])); api.read.mockImplementation(async () => ok(structuredClone(persisted)))
  api.save.mockImplementation(async (input: DocumentCommand) => ok({ operationId: input.operationId, kind: 'CONTROLLED_DOCUMENT', objectId: input.documentId, version: '3', digest: 'c'.repeat(64) }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, action: (lock: object) => unknown) => action({}) } })
  Object.defineProperty(crypto, 'subtle', { configurable: true, value: { digest: vi.fn(async () => new Uint8Array(32).fill(170).buffer) } })
})
afterEach(() => { view?.unmount(); view = undefined })
it('opens the workflow document link with its exact parent version and review panel', async () => {
  view = mount(View, { props: { documentId: row.reference.id, reviewSubmission: guid(44) }, global: { stubs: { ControlledDocumentReviewPanel: true } } }); await flushPromises()
  expect(api.read).toHaveBeenCalledWith(row.reference.id); expect(api.parentRead).toHaveBeenCalledWith(parent.reference.id, parent.reference.version)
  expect(view.text()).toContain(row.body.name); expect(view.findComponent({ name: 'ControlledDocumentReviewPanel' }).props('document').reference).toEqual(row.reference)
  expect(view.findComponent({ name: 'ControlledDocumentReviewPanel' }).props('initialSubmission')).toBe(guid(44))
})
it.each(['APPROVED', 'UNKNOWN'] as const)('shows the observed %s state without inventing a pending review', async state => {
  persisted.state = state
  persisted.readiness = state === 'APPROVED' ? 'CONTROLLED' : 'REVIEW_STATUS_UNAVAILABLE'
  await choose()
  expect(view!.get('article').text()).toContain(state === 'APPROVED' ? '已专业批准' : '审批状态未确认')
  expect(view!.get('article').text()).not.toContain('待专业评审')
})
it('preserves search and loads only the chosen exact engineering context', async () => { await choose(); expect(api.parents).toHaveBeenCalledWith('ENG', 1); expect(api.list).toHaveBeenCalledWith(parent.reference.id, '', 1); await field('工程修订选择').setValue(''); expect(view!.text()).not.toContain(row.body.name) })
it('keeps the same source search when loading later engineering pages', async () => { api.parents.mockResolvedValue(ok(Array.from({ length: 20 }, (_, i) => ({ ...parent, reference: { ...parent.reference, id: i === 0 ? parent.reference.id : guid(100 + i) } })))); await choose(); expect((field('工程修订搜索').element as HTMLInputElement).value).toBe('ENG'); await click('更多工程修订'); expect(api.parents).toHaveBeenLastCalledWith('ENG', 2) })
it('creates a document from an actual file choice without fabricating source facts', async () => {
  await choose(); await click('登记文档'); await field('文档名称').setValue('New actual source'); await field('修订标记').setValue('B'); await click('读取可选文件'); await field('文件版本').setValue(file.contentRef.id); await click('加入内容'); await field('修订原因').setValue('Register source'); await submit('修订原因')
  const input = api.save.mock.calls[0]![0] as DocumentCommand; expect(input.action).toBe('CREATE'); expect(input.body.contents[0]!.file).toEqual(file.contentRef); expect(input.body.revisionOrigin).toEqual(parent.reference); expect(input.body.source.authorityLanguage).toBe(''); expect(input.body.source.sourceDate).toBeNull(); expect(pendingDocument(scope)).toBeNull()
})
it('saves exact CAS and immutable content refs while retaining source metadata', async () => { await opened(); await field('文档名称').setValue('New title'); await submit('修订原因'); expect(api.save.mock.calls[0]![0]).toMatchObject({ documentId: row.reference.id, expectedVersion: '2', expectedEtag: row.etag, action: 'REVISE', body: { name: 'New title', source: row.body.source, contents: row.body.contents } }); expect(view!.text()).toContain('仍待专业评审') })
it('records separate derived content with the chosen original occurrence', async () => {
  await opened(); const derived = { ...file, contentRef: { ...file.contentRef, id: guid(10), digest: 'e'.repeat(64) } }; api.files.mockResolvedValue(ok([derived])); await click('读取可选文件'); await field('文件版本').setValue(guid(10)); await click('加入内容'); await field('内容角色 2').setValue('TRANSLATION'); await field('派生来源 2').setValue(guid(9)); await field('派生依据 2').setValue('Translated from exact original'); await field('内容语言 2').setValue('en'); await submit('修订原因'); expect(api.save.mock.calls[0]![0].body.contents[1]).toMatchObject({ role: 'TRANSLATION', derivedFromOccurrenceId: guid(9), file: derived.contentRef })
})
it('retains original operation IDs after lost confirmation and never resubmits during recovery', async () => {
  await opened(); api.save.mockRejectedValue(new Error('Lost confirmation')); await submit('修订原因'); const original = pendingDocument(scope)!; expect(original.objectId).toBe(row.reference.id); expect(Object.values(localStorage).join('')).not.toContain('Actual supplier'); expect(Object.values(localStorage).join('')).not.toContain(file.fileName)
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: { operationId: original.operationId, kind: 'CONTROLLED_DOCUMENT', objectId: original.objectId, version: '3', digest: 'c'.repeat(64) } })); await click('查询原操作'); expect(api.save).toHaveBeenCalledTimes(1); expect(pendingDocument(scope)).toBeNull()
})
it('retains UNKNOWN and rejects a result belonging to another document', async () => {
  await opened(); api.save.mockRejectedValue(new Error('Lost')); await submit('修订原因'); const original = pendingDocument(scope)!
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'UNKNOWN' })); await click('查询原操作'); expect(pendingDocument(scope)).toEqual(original)
  api.operation.mockResolvedValue(ok({ operationId: original.operationId, state: 'COMMITTED', result: { operationId: original.operationId, kind: 'CONTROLLED_DOCUMENT', objectId: guid(80), version: '3', digest: 'c'.repeat(64) } })); await click('查询原操作'); expect(pendingDocument(scope)).toEqual(original)
})
it('does not substitute current content when reading unavailable historical versions', async () => { await opened(); const historic = structuredClone(row); historic.reference.version = '1'; historic.contentStatus[0]!.availability = 'QUARANTINED'; api.read.mockResolvedValue(ok(historic)); await field('历史工作版本').setValue('1'); await submit('历史工作版本'); expect(api.read).toHaveBeenLastCalledWith(row.reference.id, '1'); expect(button('下载历史内容').attributes('disabled')).toBeDefined(); expect(view!.text()).toContain('历史引用保持原版本') })
it('clears private source metadata and content on current permission denial', async () => { await opened(); api.read.mockRejectedValue({ response: { status: 403 } }); await submit('历史工作版本'); expect(view!.text()).not.toContain('Actual supplier'); expect(view!.text()).not.toContain(file.fileName); expect(view!.text()).toContain('当前无权') })
it('discards late document responses after identity changes', async () => {
  await choose(); let resolve!: (v: unknown) => void; api.read.mockImplementation(() => new Promise(r => { resolve = r })); await button('查看文档').trigger('click'); await flushPromises(); api.authenticated = false; localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); resolve(ok(row)); await flushPromises(); expect(view!.text()).not.toContain(file.fileName); expect(view!.text()).not.toContain(row.body.name)
})
it('does not allow write actions or unsafe browser coordination', async () => { api.denied = ['plm-controlled-document:add', 'plm-controlled-document:edit']; await opened(); expect(button('登记文档')).toBeUndefined(); expect(field('文档名称').element.closest('fieldset')!.disabled).toBe(true); await submit('修订原因'); expect(api.save).not.toHaveBeenCalled() })
it('requires browser coordination before storing or sending a new operation', async () => { await opened(); Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await submit('修订原因'); expect(api.save).not.toHaveBeenCalled(); expect(pendingDocument(scope)).toBeNull() })
it('verifies exact document version and downloaded bytes before exposing a URL', async () => { await opened(); api.download.mockResolvedValue({ arrayBuffer: async () => new Uint8Array([1]).buffer }); await click('下载准确内容'); expect(api.download).toHaveBeenCalledWith(row.reference.id, '2', guid(9)); expect(view!.text()).toContain('下载内容与文档原版本不一致') })
it('rejects foreign tenant or parent file choices', async () => { await opened(); api.files.mockResolvedValue(ok([{ ...file, tenantId: guid(88) }])); await click('读取可选文件'); expect(view!.text()).toContain('文件上下文不一致'); expect(field('文件版本').findAll('option')).toHaveLength(1) })
