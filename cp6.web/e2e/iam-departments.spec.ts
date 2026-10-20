import { test, expect, type BrowserContext, type Page } from '@playwright/test'
const a = '30000000-0000-0000-0000-000000000001', b = '30000000-0000-0000-0000-000000000002', child = '30000000-0000-0000-0000-000000000003', grand = '30000000-0000-0000-0000-000000000004'
const department = (id: string, deptCode: string, deptName: string, parentId: string | null = null) => ({ id, deptCode, deptName, parentId, leaderId: null, sort: 0, enable: true, available: true, diagnostic: null as string | null, version: 'v1', path: '/' + id + '/' })
async function fixture(context: BrowserContext) {
  const editor = { tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002', treeVersion: 'tree1', referencesVersion: 'refs1', leaders: [], departments: [department(a, 'A', '集团A'), department(b, 'B', '集团B'), department(child, 'CHILD', '研发部门', a), department(grand, 'GRAND', '测试组', child), { ...department('40000000-0000-0000-0000-000000000001', 'BROKEN', '异常部门', 'missing'), available: false, diagnostic: 'IAM_TREE_UNHEALTHY' }] }
  const writes: any[] = [], resolutions: string[] = [], operations = new Map<string, any>(); let mode = 'normal', pause: Promise<void> | null = null
  await context.route('**/__iam-departments-fixture', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html lang="zh"><head><title>Organization fixture</title></head><body><div id="app"></div><script type="module" src="/e2e/iam-departments-fixture.ts"></script></body></html>' }))
  await context.route(url => url.pathname.startsWith('/api/'), async route => {
    const url = new URL(route.request().url()), method = route.request().method()
    const send = (body: unknown, status = 200) => route.fulfill({ contentType: 'application/json', status, body: JSON.stringify(body) })
    if (url.pathname === '/api/iam/departments/editor') return send(editor)
    if (url.pathname.endsWith('/preview')) {
      if (pause) await pause
      return send({ id: child, oldParentId: editor.departments.find(d => d.id === child)!.parentId, newParentId: url.searchParams.get('parentId'), treeVersion: editor.treeVersion, referencesVersion: editor.referencesVersion, version: 'v1', affectedDescendantCount: 1, affectedUsersCount: 7, scopeRefs: [{ roleId: 71, resourceKey: 'order', scopeType: 4 }], approvalRouteWarnings: [], previewVersion: editor.treeVersion + '-preview' })
    }
    if (url.pathname.endsWith('/move') && method === 'POST') {
      const input = route.request().postDataJSON(); writes.push(input)
      if (mode === 'conflict') { mode = 'normal'; editor.treeVersion = 'tree2'; editor.referencesVersion = 'refs2'; return send({ code: 'IAM_REFERENCES_CHANGED' }, 412) }
      editor.departments.find(d => d.id === child)!.parentId = input.parentId
      const result = { operationId: input.operationId, kind: 'dept-move', state: 'COMMITTED', entityId: child, treeVersion: 'tree3' }; operations.set(input.operationId, result)
      if (mode === 'lost') return route.abort('connectionreset')
      return send(result)
    }
    if (url.pathname.includes('/operations/')) {
      resolutions.push(url.pathname); const close = url.pathname.endsWith('/close'), id = url.pathname.split('/').at(close ? -2 : -1)!
      const result = operations.get(id) || { operationId: id, kind: url.searchParams.get('kind'), state: close ? 'CLOSED' : 'UNKNOWN' }
      if (close) operations.set(id, result); return send(result)
    }
    if (url.pathname.endsWith('/references')) return send({ id: child, childCount: 1, userCount: 7, scopeRefs: [], deletionAllowed: false, unverifiedReferenceCategories: ['business', 'approval-history', 'privacy-retention-hold'] })
    return send({})
  })
  return { writes, resolutions, mode: (next: string) => { mode = next }, delay: () => { let release!: () => void; pause = new Promise<void>(resolve => { release = resolve }); return release } }
}
async function prepare(page: Page) {
  await page.getByRole('row').filter({ hasText: 'CHILD' }).getByRole('button', { name: '移动', exact: true }).click()
  await page.getByLabel('新上级', { exact: true }).selectOption(b)
  await page.getByLabel('变更理由', { exact: true }).fill('Owned organization move')
  await expect(page.getByRole('button', { name: '确认移动' })).toBeDisabled()
  await page.getByRole('button', { name: '预览移动影响' }).click()
  await expect(page.getByText('受影响子部门 1 个，用户 7 人，范围引用 1 项。')).toBeVisible()
}
test('real organization page previews impact, preserves draft after CAS and explicitly reconfirms', async ({ context, page }) => {
  const f = await fixture(context); f.mode('conflict'); await page.goto('/__iam-departments-fixture')
  await expect(page.getByRole('alert').filter({ hasText: '目录数据异常' })).toContainText('异常部门')
  await expect(page.getByRole('row').filter({ hasText: 'BROKEN' })).toHaveCount(0)
  await prepare(page); await page.screenshot({ path: (process.env.CP6_AUTH_BROWSER_RESULTS || 'e2e/results') + '/organization-preview.png', animations: 'disabled' })
  await page.getByRole('button', { name: '确认移动' }).click(); await expect(page.getByRole('button', { name: '封闭原操作' })).toBeVisible()
  await page.getByRole('button', { name: '封闭原操作' }).click(); await page.getByRole('button', { name: '重新读取并保留草稿' }).click()
  await expect(page.getByLabel('变更理由', { exact: true })).toHaveValue('Owned organization move'); await expect(page.getByLabel('新上级', { exact: true })).toHaveValue(b)
  await expect(page.getByRole('button', { name: '确认移动' })).toBeDisabled(); expect(f.writes).toHaveLength(1)
  await page.getByRole('button', { name: '预览移动影响' }).click(); await expect(page.getByRole('button', { name: '确认移动' })).toBeEnabled(); await page.getByRole('button', { name: '确认移动' }).click()
  await expect(page.getByText('Main 已保存组织变更。下游投影与业务采用需另行确认。')).toBeVisible()
  expect(f.writes).toHaveLength(2); expect(f.writes[1]).toMatchObject({ expectedTreeVersion: 'tree2', previewVersion: 'tree2-preview' })
  expect(f.writes[1].operationId).not.toBe(f.writes[0].operationId)
})
test('another real tab recovers a committed move after response loss without writing again', async ({ context, page }) => {
  const f = await fixture(context); f.mode('lost'); await page.goto('/__iam-departments-fixture'); await prepare(page)
  await page.getByRole('button', { name: '确认移动' }).click(); await expect(page.getByRole('button', { name: '查询原操作' })).toBeVisible()
  const other = await context.newPage(); await other.goto('/__iam-departments-fixture'); await other.reload()
  await expect(other.getByRole('button', { name: '新增根部门' })).toBeDisabled(); await other.getByRole('button', { name: '查询原操作' }).click()
  await expect(other.getByText('原操作已在 Main 保存，不再重放。')).toBeVisible(); expect(f.writes).toHaveLength(1)
  expect(f.resolutions.every(x => x.includes('/departments/operations/' + f.writes[0].operationId))).toBe(true)
  expect(await other.evaluate(() => JSON.stringify(localStorage))).not.toContain('Owned organization move')
  await expect(page.getByRole('button', { name: '查询原操作' })).toHaveCount(0)
})
test('a late preview cannot restore old organization data after shared identity changes', async ({ context, page }) => {
  const f = await fixture(context); await page.goto('/__iam-departments-fixture'); const release = f.delay()
  await page.getByRole('row').filter({ hasText: 'CHILD' }).getByRole('button', { name: '移动', exact: true }).click()
  await page.getByLabel('新上级', { exact: true }).selectOption(b); await page.getByRole('button', { name: '预览移动影响' }).click()
  await page.evaluate(() => { localStorage.setItem('cp6_auth_epoch', crypto.randomUUID()); window.dispatchEvent(new Event('cp6-auth-changed')) }); release()
  await expect(page.getByText('登录身份已改变，请重新进入部门管理。')).toBeVisible(); await expect(page.getByRole('row').filter({ hasText: 'CHILD' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: '确认移动' })).not.toBeVisible(); expect(f.writes).toHaveLength(0)
})
