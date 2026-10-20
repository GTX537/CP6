import { test, expect, type BrowserContext } from '@playwright/test'
test.beforeEach(async ({ page }) => {
  page.on('pageerror', error => console.log('PAGE_ERROR', error.message))
  page.on('requestfailed', request => console.log('REQUEST_FAILED', new URL(request.url()).pathname, request.failure()?.errorText))
  page.on('response', response => { if (response.status() >= 400) console.log('HTTP_ERROR', response.status(), new URL(response.url()).pathname) })
})
const editor = { tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002',
  user: null, referencesVersion: 'fixture-refs', roles: [{ roleId: 71, roleName: '普通角色', enable: true }], departments: [], managers: [] }
async function fixture(context: BrowserContext) {
  const writes: any[] = [], resolutions: string[] = []; let lost = false, closed = false
  await context.route('**/__iam-users-fixture', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html lang="zh"><head><title>IAM fixture</title></head><body><div id="app"></div><script type="module" src="/e2e/iam-users-fixture.ts"></script></body></html>' }))
  await context.route(url => url.pathname.startsWith('/api/'), async route => {
    const url = new URL(route.request().url()), method = route.request().method()
    const send = (body: unknown) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) })
    if (url.pathname === '/api/iam/users/editor') return send(editor)
    if (url.pathname === '/api/user') return send({ rows: [], total: 0 })
    if (url.pathname === '/api/iam/users' && method === 'POST') {
      const input = route.request().postDataJSON(); writes.push(input)
      if (lost) return route.abort('connectionreset')
      return send({ operationId: input.operationId, kind: 'user-create', state: 'COMMITTED' })
    }
    if (url.pathname.includes('/iam/users/operations/')) {
      resolutions.push(url.pathname); const parts = url.pathname.split('/'), close = parts.at(-1) === 'close'; if (close) closed = true
      return send({ operationId: parts.at(close ? -2 : -1), kind: url.searchParams.get('kind'), state: closed ? 'CLOSED' : 'UNKNOWN' })
    }
    return send({})
  })
  return { writes, resolutions, lose: () => { lost = true } }
}
test('real user form submits primary/additional roles once and never persists the initial password', async ({ context, page }) => {
  const f = await fixture(context); await page.goto('/__iam-users-fixture'); await page.getByRole('button', { name: '新增用户' }).click()
  await page.getByLabel('用户名', { exact: true }).fill('fixture-user'); await page.getByLabel('初始密码').fill('Synthetic1!Password')
  await page.getByRole('checkbox', { name: '普通角色' }).check(); await page.getByLabel('主角色', { exact: true }).selectOption('71')
  await page.getByLabel('变更理由').fill('Owned browser assignment'); await page.getByRole('button', { name: '确认保存' }).click()
  await expect(page.getByText('Main 已保存。下游投影状态需由对应系统确认。')).toBeVisible()
  expect(f.writes).toHaveLength(1); expect(f.writes[0]).toMatchObject({ roleId: 71, roleIds: [71], expectedVersion: 'absent', expectedReferencesVersion: 'fixture-refs' })
  expect(await page.evaluate(() => JSON.stringify(localStorage))).not.toContain('Synthetic1!Password')
})
test('lost save is recovered by another real tab after reload without any second write', async ({ context, page }) => {
  const f = await fixture(context); f.lose(); await page.goto('/__iam-users-fixture'); await page.getByRole('button', { name: '新增用户' }).click()
  await page.getByLabel('用户名', { exact: true }).fill('fixture-user'); await page.getByLabel('初始密码').fill('Synthetic1!Password')
  await page.getByLabel('变更理由').fill('Owned uncertain save'); await page.getByRole('button', { name: '确认保存' }).click()
  await expect(page.getByRole('button', { name: '查询原操作' })).toBeVisible(); const id = f.writes[0].operationId
  const other = await context.newPage(); await other.goto('/__iam-users-fixture'); await other.reload()
  await expect(other.getByRole('button', { name: '新增用户' })).toBeDisabled()
  await other.getByRole('button', { name: '查询原操作' }).click(); await expect(other.getByText('尚未确认原操作结果；可继续查询或封闭，不能重新保存。')).toBeVisible()
  await other.getByRole('button', { name: '封闭原操作' }).click(); await expect(other.getByRole('button', { name: '新增用户' })).toBeEnabled()
  expect(f.writes).toHaveLength(1); expect(f.resolutions.every(x => x.includes(id))).toBe(true)
  expect(await other.evaluate(() => JSON.stringify(localStorage))).not.toContain('Synthetic1!Password')
  await other.screenshot({ path: (process.env.CP6_AUTH_BROWSER_RESULTS || 'e2e/results') + '/iam-operation-closed.png', animations: 'disabled' })
})

test('user recovery remains actionable inside the open modal after response loss', async ({ context, page }) => {
  const f = await fixture(context); f.lose(); await page.goto('/__iam-users-fixture')
  await page.getByRole('button', { name: '新增用户' }).click()
  await page.getByLabel('用户名', { exact: true }).fill('fixture-user')
  await page.getByLabel('初始密码').fill('Synthetic1!Password'); await page.getByLabel('变更理由').fill('Owned modal recovery')
  await page.getByRole('button', { name: '确认保存' }).click()
  const close = page.getByRole('dialog').getByRole('button', { name: '封闭原操作' })
  await expect(close).toBeVisible(); await close.click()
  await page.getByRole('button', { name: '重新读取并保留草稿' }).click()
  await expect(page.getByLabel('用户名', { exact: true })).toHaveValue('fixture-user')
  await expect(page.getByLabel('初始密码')).toHaveValue(''); expect(f.writes).toHaveLength(1)
})
