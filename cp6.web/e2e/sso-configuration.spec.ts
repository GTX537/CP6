import { test, expect, type BrowserContext } from '@playwright/test'
test.beforeEach(async ({ page }) => { page.on('pageerror', error => console.log('PAGE_ERROR', error.message)) })

const config = { tenantId: '00000000-0000-0000-0000-000000000001', actorId: '00000000-0000-0000-0000-000000000002',
  configVersion: 'fixture-v1', policyVersion: 'none', admissionPolicy: null, authority: 'https://idp.example', clientId: 'fixture-client',
  scopes: 'openid email', emailClaim: 'email', enabled: false, enforced: false, autoProvision: false, defaultRoleId: null, hasClientSecret: true }
async function fixture(context: BrowserContext) {
  const writes: any[] = []; const resolutions: string[] = []; let lost = false; let closed = false; const mappingClosed = new Set<string>()
  await context.route('**/__sso-configuration-fixture', route => route.fulfill({ contentType: 'text/html', body: `<!doctype html><html lang="zh"><head><title>SSO configuration fixture</title></head><body><div id="app"></div><script type="module" src="/e2e/sso-configuration-fixture.ts">
    </script></body></html>` }))
  await context.route(url => url.pathname.startsWith('/api/'), async route => {
    const path = new URL(route.request().url()).pathname; const method = route.request().method()
    const send = (body: unknown) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) })
    if (path === '/api/sys/sso-config' && method === 'GET') return send(config)
    if (path === '/api/sys/sso-config' && method === 'PUT') {
      const input = route.request().postDataJSON(); writes.push(input)
      if (lost) return route.abort('connectionreset')
      return send({ operationId: input.operationId, state: 'COMMITTED', configVersion: 'fixture-v2', policyVersion: 'approved-v1' })
    }
    if (path.includes('/sso-config/operations/')) {
      resolutions.push(path); const segments = path.split('/'); const close = segments.at(-1) === 'close'; if (close) closed = true
      return send({ operationId: segments.at(close ? -2 : -1), state: closed ? 'CLOSED' : 'UNKNOWN' })
    }
    if (path.includes('/sso-config/identity-operations/')) {
      resolutions.push(path); const segments = path.split('/'), close = segments.at(-1) === 'close', id = segments.at(close ? -2 : -1)!
      if (close) mappingClosed.add(id)
      return send({ operationRef: id, state: id.startsWith('a') ? 'USER_PROVISIONED' : mappingClosed.has(id) ? 'CLOSED' : 'UNKNOWN' })
    }
    if (path.toLowerCase().includes('/role/')) return send([{ roleId: 17, roleName: 'Enabled member', enable: true }])
    return send({})
  })
  return { writes, resolutions, lose: () => { lost = true } }
}

test('actual approval form starts unapproved and sends explicit authority plus loaded versions once', async ({ context, page }) => {
  const f = await fixture(context); await page.goto('/__sso-configuration-fixture')
  await expect(page.getByText('尚未批准首次邮箱绑定或自动建号')).toBeVisible()
  await page.locator('.el-form-item').filter({ hasText: '本次操作' }).locator('.el-select').click()
  await page.getByRole('option', { name: '明确批准新的准入政策' }).click()
  const confirmation = page.getByRole('checkbox'); await expect(confirmation).not.toBeChecked()
  await page.getByPlaceholder('填写 IdP 令牌中的精确 issuer').fill('https://idp.example')
  await page.locator('textarea').fill('Owned browser approval fixture')
  await page.getByRole('button', { name: '保存', exact: true }).click()
  expect(f.writes).toHaveLength(0)
  await page.locator('.el-checkbox__label').click(); await expect(confirmation).toBeChecked()
  await page.getByRole('button', { name: '保存', exact: true }).click()
  await expect.poll(() => f.writes.length).toBe(1)
  expect(f.writes[0]).toMatchObject({ expectedConfigVersion: 'fixture-v1', expectedPolicyVersion: 'none', expectedPolicyRef: null,
    admission: { action: 'approve', issuerIsEmailAuthority: true, approvedIssuer: 'https://idp.example', allowInitialEmailLink: false, allowJit: false } })
  await expect(page.getByText('配置已提交。认证政策已更新，请重新登录后继续操作。')).toBeVisible()
  await expect(page.getByRole('button', { name: '保存', exact: true })).toBeDisabled()
})

test('identity support recovery preserves a committed account and closes only the queried unknown reference', async ({ context, page }) => {
  const f = await fixture(context); await page.goto('/__sso-configuration-fixture')
  const input = page.getByRole('textbox', { name: '身份映射原操作编号' })
  await input.fill('a'.repeat(64)); await page.getByRole('button', { name: '查询身份映射', exact: true }).click()
  await expect(page.getByText('原操作已创建账号，账号继续保留。用户可重新发起 SSO。')).toBeVisible()
  const close = page.getByRole('button', { name: '关闭这次未确认映射', exact: true }); await expect(close).toBeDisabled()
  await input.fill('b'.repeat(64)); await expect(close).toBeDisabled()
  await page.getByRole('button', { name: '查询身份映射', exact: true }).click()
  await expect(page.getByText('原操作结果尚未确认。可明确关闭原操作，或保留编号继续查询。')).toBeVisible()
  await close.click(); await expect(page.getByText('原操作已关闭，迟到请求不能继续写入。用户可重新发起 SSO。')).toBeVisible()
  expect(f.writes).toHaveLength(0); expect(f.resolutions.filter(x => x.endsWith('/close'))).toEqual(['/api/sys/sso-config/identity-operations/' + 'b'.repeat(64) + '/close'])
  await page.locator('.identity-recovery').screenshot({ path: (process.env.CP6_AUTH_BROWSER_RESULTS || 'e2e/results') + '/identity-recovery.png', animations: 'disabled' })
})

test('unknown save survives another tab and reload, queries and closes only the original operation', async ({ context, page }) => {
  const f = await fixture(context); f.lose(); await page.goto('/__sso-configuration-fixture')
  await page.locator('textarea').fill('Owned uncertain save fixture')
  await page.locator('input[type=password]').fill('synthetic-not-for-storage')
  await page.getByRole('button', { name: '保存', exact: true }).click()
  await expect(page.getByRole('button', { name: '查询原操作', exact: true })).toBeVisible()
  expect(f.writes).toHaveLength(1); const id = f.writes[0].operationId
  const other = await context.newPage(); await other.goto('/__sso-configuration-fixture')
  await expect(other.getByRole('button', { name: '保存', exact: true })).toBeDisabled()
  expect(await other.evaluate(() => JSON.stringify(localStorage))).not.toContain('synthetic-not-for-storage')
  await other.reload(); await other.getByRole('button', { name: '查询原操作', exact: true }).click()
  await expect(other.getByText('原操作结果仍未知，可以关闭原操作以阻止迟到保存。')).toBeVisible()
  await other.getByRole('button', { name: '关闭原操作后重试', exact: true }).click()
  await expect(other.getByText('原操作已关闭，可以重新填写并保存。')).toBeVisible()
  expect(f.writes).toHaveLength(1); expect(f.resolutions.every(x => x.includes(id))).toBe(true)
  await expect(other.getByRole('button', { name: '保存', exact: true })).toBeEnabled()
  if (process.env.CP6_SSO_BROWSER_SCREENSHOT) await other.screenshot({ path: process.env.CP6_SSO_BROWSER_SCREENSHOT, animations: 'disabled' })
})
