import { expect, test, type BrowserContext, type Page } from '@playwright/test'
async function fixture(context: BrowserContext) {
  const scope = { tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002' }
  let version = 'v1', fault = '', refreshFailure = false, delayed: Promise<void> | null = null
  let actions = [{ actionCode: 'read', actionName: '查询订单', sort: 0 }]
  const writes: any[] = [], originals = new Map<string, any>()
  await context.route('**/__iam-menu-actions?*', route => route.fulfill({ contentType: 'text/html',
    body: '<!doctype html><html lang="zh"><body><div id="app"></div><script type="module" src="/e2e/iam-role-policies-fixture.ts"></script></body></html>' }))
  await context.route(url => url.pathname.startsWith('/api/'), async route => {
    const url = new URL(route.request().url()), method = route.request().method()
    const send = (value: unknown, status = 200) => route.fulfill({ contentType: 'application/json', status, body: JSON.stringify(value) })
    if (url.pathname.includes('/menu-actions/') && url.pathname.endsWith('/editor')) {
      const menuId = Number(url.pathname.split('/').at(-2)), snapshot = { ...scope, menuId, version, referencesVersion: version, catalogVersion: version, actions: structuredClone(actions) }
      if (delayed) await delayed
      return refreshFailure ? send({ code: 'UNAVAILABLE' }, 503) : send(snapshot)
    }
    if (url.pathname.endsWith('/editor')) return send({ ...scope, mode: 'actions', roleId: null, version, referencesVersion: version,
      roles: [], warnings: [], function: { menuIds: [], actions: [] }, dataScopes: [], fieldPerms: [],
      catalog: { version, menus: [{ menuId: 0, menuName: '订单', menuKey: 'order', parentId: null, enable: true }], actions: [], fields: [], scopes: [], departments: [] } })
    if (method === 'PUT') {
      const input = route.request().postDataJSON(); writes.push(input)
      if (fault === 'conflict') { fault = ''; version = 'v2'; return send({ code: 'IAM_REFERENCES_CHANGED' }, 412) }
      if (fault === 'referenced') { fault = ''; return send({ code: 'IAM_MENU_ACTION_REFERENCED' }, 409) }
      actions = input.actions
      const result = { operationId: input.operationId, kind: 'menu-actions', state: 'COMMITTED' }; originals.set(input.operationId, result)
      if (fault === 'refresh') refreshFailure = true
      if (fault === 'lost') { fault = ''; return route.abort('connectionreset') }
      return send(result)
    }
    if (url.pathname.includes('/operations/')) {
      const close = url.pathname.endsWith('/close'), id = url.pathname.split('/').at(close ? -2 : -1)!
      const result = originals.get(id) || { operationId: id, kind: 'menu-actions', state: close ? 'CLOSED' : 'UNKNOWN' }
      if (close) originals.set(id, result)
      return send(result)
    }
    return send({})
  })
  return { writes, fault: (value: string) => { fault = value }, recover: () => { refreshFailure = false },
    delay: () => { let release!: () => void; delayed = new Promise<void>(resolve => { release = resolve }); return release } }
}
async function open(page: Page) {
  await page.goto('/__iam-menu-actions?mode=actions')
  await page.getByRole('button', { name: '菜单动作定义', exact: true }).click()
  await page.getByLabel('菜单', { exact: true }).selectOption('0')
  await page.getByLabel('变更理由', { exact: true }).fill('Owned action definition')
}
test('actual page saves zero menu identity and recovers a failed refresh without another write', async ({ context, page }) => {
  const f = await fixture(context); await open(page); f.fault('refresh')
  await page.getByRole('button', { name: '新增动作', exact: true }).click()
  await page.getByLabel('动作代码 2', { exact: true }).fill('export')
  await page.getByLabel('动作名称 2', { exact: true }).fill('导出订单')
  await page.getByLabel('动作排序 2', { exact: true }).fill('-2147483648')
  await page.getByRole('button', { name: '确认保存动作' }).click()
  await expect(page.getByText('菜单动作已保存。 动作刷新失败，请单独重新读取，无需重存。', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '确认保存动作' })).toBeDisabled()
  expect(f.writes).toHaveLength(1); expect(f.writes[0]).toMatchObject({ menuId: 0, actions: [{ actionCode: 'read' }, { actionCode: 'export', sort: -2147483648 }] })
  f.recover(); await page.getByRole('button', { name: '重新读取', exact: true }).click()
  await expect(page.getByLabel('动作代码 2', { exact: true })).toHaveValue('export')
  expect(f.writes).toHaveLength(1)
  await page.screenshot({ path: `${process.env.CP6_AUTH_BROWSER_RESULTS}/menu-actions.png`, fullPage: true })
})
test('CAS and referenced removal preserve draft until explicit close, reread and confirmation', async ({ context, page }) => {
  const f = await fixture(context); await open(page); f.fault('conflict')
  await page.getByLabel('动作名称 1', { exact: true }).fill('新查询名称')
  await page.getByRole('button', { name: '确认保存动作' }).click()
  await page.getByRole('button', { name: '封闭原操作' }).click()
  await page.getByRole('button', { name: '重新读取并保留草稿', exact: true }).click()
  await expect(page.getByLabel('动作名称 1', { exact: true })).toHaveValue('新查询名称')
  expect(f.writes).toHaveLength(1)
  await page.getByRole('button', { name: '确认保存动作' }).click()
  await expect(page.getByText('菜单动作已保存。', { exact: true })).toBeVisible()
  expect(f.writes[1]).toMatchObject({ expectedVersion: 'v2', expectedReferencesVersion: 'v2', expectedCatalogVersion: 'v2' })
  f.fault('referenced'); await page.getByRole('button', { name: '移除动作 1', exact: true }).click()
  await page.getByLabel('变更理由', { exact: true }).fill('Remove referenced definition')
  await page.getByRole('button', { name: '确认保存动作' }).click()
  await expect(page.getByText('动作仍被角色引用，未保存。请先处理角色授权，再核对原操作和当前版本。', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '确认保存动作' })).toBeDisabled()
  expect(f.writes[2].actions).toEqual([])
})
test('a new tab resolves a lost definition write and shared identity change defeats late reads', async ({ context, page }) => {
  const f = await fixture(context); await open(page); f.fault('lost')
  await page.getByRole('button', { name: '确认保存动作' }).click()
  await expect(page.getByRole('button', { name: '查询原操作' })).toBeVisible()
  const other = await context.newPage(); await other.goto('/__iam-menu-actions?mode=actions'); await other.reload()
  await other.getByRole('button', { name: '查询原操作' }).click()
  await expect(other.getByText('原操作已保存，不再重放。', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '查询原操作' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: '确认保存动作' })).toBeDisabled()
  expect(f.writes).toHaveLength(1)
  expect(await other.evaluate(() => JSON.stringify(localStorage))).not.toContain('Owned action definition')
  const release = f.delay(); await page.getByRole('button', { name: '重新读取', exact: true }).click()
  await other.evaluate(() => localStorage.setItem('cp6_auth_epoch', crypto.randomUUID()))
  release()
  await expect(page.getByText('登录身份已改变，请重新进入动作管理。', { exact: true })).toBeVisible()
  await expect(page.getByLabel('动作代码 1', { exact: true })).toHaveCount(0)
})
