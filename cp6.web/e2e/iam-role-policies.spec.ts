import { expect, test, type BrowserContext, type Page } from '@playwright/test'
import type { IamPolicyEditor } from '../src/api/sys/iamPolicy'
async function fixture(context: BrowserContext) {
  const base: IamPolicyEditor = {
    tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002',
    mode: 'actions', roleId: null, version: 'v1', referencesVersion: 'refs1',
    roles: [{ roleId: 72, roleName: '角色甲', enable: true }, { roleId: 73, roleName: '角色乙', enable: true }],
    catalog: { version: 'cat1',
      menus: [{ menuId: 1, menuName: '订单目录', menuKey: null, parentId: null, enable: true }, { menuId: 2, menuName: '订单页面', menuKey: 'order', parentId: 1, enable: true }],
      actions: [{ menuId: 2, actionCode: 'read', actionName: '查询订单', sort: 0 }],
      scopes: [{ key: 'order', name: '订单', supports: [1, 4, 5], default: 1 }],
      departments: [{ id: 'd1', deptName: '正常部门', available: true }, { id: 'd2', deptName: '停用部门', available: false }],
      fields: [{ key: 'order', fields: [{ name: 'Amount', label: '金额' }, { name: 'Name', label: '名称' }] }, { key: 'other', fields: [{ name: 'Cost', label: '成本' }] }] },
    function: { menuIds: [], actions: [] }, dataScopes: [], fieldPerms: [{ resourceKey: 'order', fieldName: 'Amount', access: 3 }], warnings: []
  }
  let mode = 'normal', delayedRole: number | null = null, delay: Promise<void> | null = null
  const writes: any[] = [], operations = new Map<string, any>()
  await context.route('**/__iam-policies-fixture?*', route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html lang="zh"><body><div id="app"></div><script type="module" src="/e2e/iam-role-policies-fixture.ts"></script></body></html>' }))
  await context.route(url => url.pathname.startsWith('/api/'), async route => {
    const url = new URL(route.request().url()), method = route.request().method()
    const send = (data: unknown, status = 200) => route.fulfill({ contentType: 'application/json', status, body: JSON.stringify(data) })
    if (url.pathname.endsWith('/editor')) {
      const roleId = url.searchParams.has('roleId') ? Number(url.searchParams.get('roleId')) : null
      const snapshot = { ...structuredClone(base), mode: url.pathname.split('/').at(-2), roleId }
      if (roleId === delayedRole && delay) await delay
      return send(snapshot)
    }
    if (method === 'PUT') {
      const input = route.request().postDataJSON(); writes.push({ ...input, path: url.pathname })
      if (mode === 'conflict') { mode = 'normal'; base.version = 'v2'; base.referencesVersion = 'refs2'; base.catalog.version = 'cat2'; return send({ code: 'IAM_REFERENCES_CHANGED' }, 412) }
      const result = { operationId: input.operationId, kind: `role-policy-${url.pathname.split('/').at(-2)}`, state: 'COMMITTED' }
      operations.set(input.operationId, result)
      if (mode === 'lost') return route.abort('connectionreset')
      return send(result)
    }
    if (url.pathname.includes('/operations/')) {
      const close = url.pathname.endsWith('/close'), id = url.pathname.split('/').at(close ? -2 : -1)!
      const result = operations.get(id) || { operationId: id, kind: url.searchParams.get('kind'), state: close ? 'CLOSED' : 'UNKNOWN' }
      if (close) operations.set(id, result)
      return send(result)
    }
    return send({})
  })
  return { writes, mode: (value: string) => { mode = value }, delay: (role: number) => { delayedRole = role; let release!: () => void; delay = new Promise<void>(resolve => { release = resolve }); return release } }
}
async function open(page: Page, mode: string) {
  await page.goto(`/__iam-policies-fixture?mode=${mode}`)
  await page.getByLabel('角色', { exact: true }).selectOption('72')
  await page.getByLabel('变更理由', { exact: true }).fill('Owned policy change')
}
test('menu navigation parents accompany selected child and another tab resolves one lost write', async ({ context, page }) => {
  const f = await fixture(context); f.mode('lost'); await open(page, 'legacy')
  await page.getByLabel('订单页面', { exact: true }).check()
  await expect(page.getByLabel('订单目录', { exact: true })).toBeChecked()
  await page.getByLabel('查询订单', { exact: true }).check()
  await page.getByRole('button', { name: '确认保存' }).click()
  await expect(page.getByRole('button', { name: '查询原操作' })).toBeVisible()
  expect(f.writes[0]).toMatchObject({ menuIds: [2, 1], actions: [{ menuId: 2, actionCode: 'read' }] })
  const other = await context.newPage(); await other.goto('/__iam-policies-fixture?mode=fields'); await other.reload()
  await expect(other.getByLabel('角色', { exact: true })).toBeDisabled()
  await other.getByRole('button', { name: '查询原操作' }).click()
  await expect(other.getByText('原操作已保存，不再重放。', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '查询原操作' })).toHaveCount(0)
  expect(f.writes).toHaveLength(1)
  expect(await other.evaluate(() => JSON.stringify(localStorage))).not.toContain('Owned policy change')
})
test('data scope CAS keeps complete draft and disabled departments cannot be chosen', async ({ context, page }) => {
  const f = await fixture(context); f.mode('conflict'); await open(page, 'scopes')
  await page.getByLabel('订单范围', { exact: true }).selectOption('4')
  expect(await page.getByRole('option', { name: '停用部门（不可用）' }).evaluate(element => (element as HTMLOptionElement).disabled)).toBe(true)
  await page.getByLabel('订单自定义部门', { exact: true }).selectOption('d1')
  await page.getByLabel('订单自定义部门', { exact: true }).press('End')
  await expect(page.getByLabel('订单自定义部门', { exact: true })).toHaveValues(['d1'])
  await page.getByRole('button', { name: '确认保存' }).click()
  await page.getByRole('button', { name: '封闭原操作' }).click()
  await page.getByRole('button', { name: '重新读取并保留草稿' }).click()
  await expect(page.getByLabel('订单范围', { exact: true })).toHaveValue('4')
  await expect(page.getByLabel('订单自定义部门', { exact: true })).toHaveValues(['d1'])
  expect(f.writes).toHaveLength(1)
  await page.getByRole('button', { name: '确认保存' }).click()
  await expect(page.getByText('授权配置已保存。', { exact: true })).toBeVisible()
  expect(f.writes[1]).toMatchObject({ expectedVersion: 'v2', expectedReferencesVersion: 'refs2', expectedCatalogVersion: 'cat2', scopes: [{ resourceKey: 'order', scopeType: 4, customDeptIds: ['d1'] }] })
  expect(f.writes[1].operationId).not.toBe(f.writes[0].operationId)
})
test('field default reset sends the complete chosen resource and switching resource drops the old draft', async ({ context, page }) => {
  const f = await fixture(context); await open(page, 'fields')
  await page.getByLabel('金额访问级', { exact: true }).selectOption('1')
  await page.getByRole('button', { name: '确认保存' }).click()
  await expect(page.getByText('授权配置已保存。', { exact: true })).toBeVisible()
  expect(f.writes[0]).toMatchObject({ resourceKey: 'order', fields: [{ resourceKey: 'order', fieldName: 'Amount', access: 1 }, { resourceKey: 'order', fieldName: 'Name', access: 1 }] })
  await page.getByLabel('资源', { exact: true }).selectOption('other')
  await page.getByLabel('成本访问级', { exact: true }).selectOption('2')
  await page.getByLabel('变更理由', { exact: true }).fill('Other resource')
  await page.getByRole('button', { name: '确认保存' }).click()
  await expect.poll(() => f.writes.length).toBe(2)
  expect(f.writes[1]).toMatchObject({ resourceKey: 'other', fields: [{ resourceKey: 'other', fieldName: 'Cost', access: 2 }] })
})
test('late role response cannot overwrite a newer role or resurrect data after identity change', async ({ context, page }) => {
  const f = await fixture(context); await open(page, 'fields')
  const release = f.delay(72)
  await page.getByRole('button', { name: '重新读取', exact: true }).click()
  await page.getByLabel('角色', { exact: true }).selectOption('73')
  await expect(page.getByLabel('变更理由', { exact: true })).toBeVisible()
  release(); await expect(page.getByLabel('角色', { exact: true })).toHaveValue('73')
  const releaseAgain = f.delay(73)
  await page.getByRole('button', { name: '重新读取', exact: true }).click()
  const other = await context.newPage(); await other.goto('/__iam-policies-fixture?mode=actions')
  await other.evaluate(() => localStorage.setItem('cp6_auth_epoch', crypto.randomUUID()))
  await expect(page.getByText('登录身份已改变，请重新进入授权管理。')).toBeVisible()
  releaseAgain(); await expect(page.getByLabel('金额访问级', { exact: true })).toHaveCount(0)
  expect(f.writes).toHaveLength(0)
})
