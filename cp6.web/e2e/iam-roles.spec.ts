import { expect, test, type BrowserContext, type Page } from '@playwright/test'
async function fixture(context: BrowserContext) {
  const editor = { tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002',
    referencesVersion: 'refs1', roles: [{ roleId: 72, roleName: '角色甲', description: null, orderNo: 0, enable: true, createDate: '2026-10-20', version: 'v1' }] }
  let mode = 'normal', delay: Promise<void> | null = null
  const writes: any[] = [], operations = new Map<string, any>()
  await context.route('**/__iam-roles-fixture', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html lang="zh"><body><div id="app"></div><script type="module" src="/e2e/iam-roles-fixture.ts"></script></body></html>' }))
  await context.route(url => url.pathname.startsWith('/api/'), async route => {
    const url = new URL(route.request().url()), method = route.request().method()
    const send = (data: unknown, status = 200) => route.fulfill({ contentType: 'application/json', status, body: JSON.stringify(data) })
    if (url.pathname === '/api/iam/roles/editor') { if (delay) await delay; return send(editor) }
    if (method === 'PUT' && url.pathname === '/api/iam/roles/72') {
      const input = route.request().postDataJSON(); writes.push(input)
      if (mode === 'conflict') { mode = 'normal'; editor.referencesVersion = 'refs2'; editor.roles[0].version = 'v2'; return send({ code: 'IAM_REFERENCES_CHANGED' }, 412) }
      editor.roles[0].roleName = input.roleName
      const result = { operationId: input.operationId, kind: 'role-update', state: 'COMMITTED', roleId: 72 }
      operations.set(input.operationId, result); if (mode === 'lost') return route.abort('connectionreset')
      return send(result)
    }
    if (url.pathname.includes('/operations/')) {
      const close = url.pathname.endsWith('/close'), id = url.pathname.split('/').at(close ? -2 : -1)!
      const result = operations.get(id) || { operationId: id, kind: url.searchParams.get('kind'), state: close ? 'CLOSED' : 'UNKNOWN' }
      if (close) operations.set(id, result); return send(result)
    }
    return send({})
  })
  return { writes, mode: (next: string) => { mode = next }, delay: () => { let release!: () => void; delay = new Promise<void>(resolve => { release = resolve }); return release } }
}
async function edit(page: Page) {
  await page.getByRole('row').filter({ hasText: '角色甲' }).getByRole('button', { name: '编辑角色' }).click()
  await expect(page.getByLabel('角色编号', { exact: true })).toBeDisabled()
  await page.getByLabel('角色名称', { exact: true }).fill('调整后的角色')
  await page.getByLabel('变更理由', { exact: true }).fill('Owned role change')
}
test('actual role modal can close CAS operation and preserve draft for explicit reconfirmation', async ({ context, page }) => {
  const f = await fixture(context); f.mode('conflict'); await page.goto('/__iam-roles-fixture'); await edit(page)
  await page.getByRole('button', { name: '确认保存' }).click()
  await expect(page.getByRole('dialog').getByRole('button', { name: '封闭原操作' })).toBeVisible()
  await page.getByRole('dialog').getByRole('button', { name: '封闭原操作' }).click()
  await page.getByRole('button', { name: '重新读取并保留草稿' }).click()
  await expect(page.getByLabel('角色名称', { exact: true })).toHaveValue('调整后的角色')
  expect(f.writes).toHaveLength(1); await page.getByRole('button', { name: '确认保存' }).click()
  await expect(page.getByText('角色已保存。', { exact: true })).toBeVisible()
  expect(f.writes).toHaveLength(2); expect(f.writes[1]).toMatchObject({ expectedVersion: 'v2', expectedReferencesVersion: 'refs2' })
  expect(f.writes[1].operationId).not.toBe(f.writes[0].operationId)
})
test('another real tab recovers committed role write after reload without a second mutation', async ({ context, page }) => {
  const f = await fixture(context); f.mode('lost'); await page.goto('/__iam-roles-fixture'); await edit(page)
  await page.getByRole('button', { name: '确认保存' }).click(); await expect(page.getByRole('button', { name: '查询原操作' })).toBeVisible()
  const other = await context.newPage(); await other.goto('/__iam-roles-fixture'); await other.reload()
  await expect(other.getByRole('button', { name: '新增角色' })).toBeDisabled()
  await other.getByRole('button', { name: '查询原操作' }).click()
  await expect(other.getByText('原操作已保存，不再重放。', { exact: true })).toBeVisible(); expect(f.writes).toHaveLength(1)
  await expect(page.getByRole('button', { name: '查询原操作' })).toHaveCount(0)
  expect(await other.evaluate(() => JSON.stringify(localStorage))).not.toContain('Owned role change')
})
test('late role read cannot restore data after cross-tab identity change', async ({ context, page }) => {
  const f = await fixture(context); await page.goto('/__iam-roles-fixture')
  await expect(page.getByRole('row').filter({ hasText: '角色甲' })).toHaveCount(1)
  const release = f.delay(); await page.getByRole('button', { name: '编辑角色' }).click()
  await page.evaluate(() => { localStorage.setItem('cp6_auth_epoch', crypto.randomUUID()); window.dispatchEvent(new Event('cp6-auth-changed')) }); release()
  await expect(page.getByText('登录身份已改变，请重新进入角色管理。')).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: '角色甲' })).toHaveCount(0); expect(f.writes).toHaveLength(0)
})
