import { expect, test, type Locator, type Page } from '@playwright/test'

const permissions = [
  'wms-mobile:view',
  'wms-mobile:add',
  'wms-mobile:assign',
  'wms-mobile:pause',
  'wms-mobile:cancel',
  'wms-mobile:analytics',
  'wms-mobile:barcode-manage',
  'wms-mobile:serial-manage',
  'wms-mobile:lpn-manage',
  'wms-mobile:label-print',
  'wms-mobile:label-manage',
  'wms-mobile:device-manage',
  'pub-data-scope:query',
  'pub-data-scope:edit',
]

const emptyPage = {
  items: [],
  total: 0,
  page: 1,
  pageSize: 100,
}

type ActivationRequest = {
  platform: string
  deviceMode: string
  warehouseCd?: string
  areaCd?: string
  scanPrefix?: string
  scanSuffix?: string
  scanTerminator?: string
  scanDuplicateMs?: number
}

async function installApiFixtures(page: Page, activationRequests: ActivationRequest[]) {
  await page.addInitScript(() => {
    localStorage.setItem('cp6_authed', '1')
    localStorage.setItem('lang', 'en')
    localStorage.setItem('nickName', 'WMS Admin')
    localStorage.setItem('menus', JSON.stringify([{
      id: 461,
      menuName: 'Mobile Task',
      routePath: '/wms/mobile-task',
      parentId: null,
      orderNo: 1,
    }]))
  })

  await page.routeWebSocket('**/hubs/wms**', socket => {
    socket.onMessage((message) => {
      if (typeof message === 'string' && message.includes('"protocol"'))
        socket.send('{}\u001e')
    })
  })
  await page.route('**/hubs/wms/negotiate**', route =>
    route.fulfill({
      json: {
        negotiateVersion: 1,
        connectionId: 'wms-e2e-connection',
        connectionToken: 'wms-e2e-token',
        availableTransports: [{
          transport: 'WebSockets',
          transferFormats: ['Text'],
        }],
      },
    }))

  await page.route('http://localhost:5173/api/**', async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const { pathname } = url

    if (pathname.startsWith('/api/lang/')) {
      await route.fulfill({
        json: {
          'app.title': 'CP6',
          'layout.logout': 'Log out',
          'wms.mobile.title': 'Mobile tasks',
        },
      })
      return
    }

    if (pathname === '/api/pub/role-perm/my-actions') {
      await route.fulfill({ json: { data: permissions } })
      return
    }

    if (pathname === '/api/oa/notifications/unread-count') {
      await route.fulfill({ json: { data: { count: 0 } } })
      return
    }

    if (pathname === '/api/v2/wms/tasks') {
      await route.fulfill({ json: { ...emptyPage, pageSize: 50 } })
      return
    }

    if (pathname === '/api/v2/admin/wms-features') {
      await route.fulfill({
        json: [{
          warehouseCd: 'PILOT-WH',
          productionMoveEnabled: true,
          serialLpnEnabled: true,
          scanRetentionDays: 180,
          rowVersion: 'rollout-v1',
        }],
      })
      return
    }

    if (pathname === '/api/v2/admin/client-devices' && request.method() === 'POST') {
      activationRequests.push(request.postDataJSON() as ActivationRequest)
      await route.fulfill({
        json: {
          activationToken: 'one-time-token-123',
          expiresAt: '2030-01-01T00:10:00Z',
          platform: 'Android',
          deviceMode: 'Shared',
          warehouseCd: 'PILOT-WH',
          areaCd: 'PICK-A',
        },
      })
      return
    }

    if (pathname === '/api/v2/admin/client-devices') {
      await route.fulfill({
        json: {
          ...emptyPage,
          items: [{
            deviceId: 'RF-01',
            deviceMode: 'Shared',
            platform: 'Android',
            status: 'Active',
            warehouseCd: 'PILOT-WH',
            areaCd: 'PICK-A',
            currentUser: 'picker01',
            lastSeenAt: '2030-01-01T00:00:00Z',
            rowVersion: 'device-v1',
          }],
        },
      })
      return
    }

    await route.fulfill({ json: { data: [] } })
  })
}

function formInput(dialog: Locator, label: string) {
  return dialog.locator('.el-form-item', { hasText: label }).locator('input').first()
}

test('provisions Android scanner settings in a one-time activation QR', async ({ page }) => {
  const activationRequests: ActivationRequest[] = []
  const pageErrors: string[] = []
  const consoleErrors: string[] = []
  page.on('pageerror', error => pageErrors.push(error.message))
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text())
  })
  await installApiFixtures(page, activationRequests)

  await page.goto('/wms/mobile-task')
  await expect(page.getByRole('heading', { name: 'Mobile tasks' })).toBeVisible()

  await page.getByRole('button', { name: 'Production console' }).click()
  const productionDialog = page.getByRole('dialog', { name: 'WMS production console' })
  await expect(productionDialog).toBeVisible()
  await expect(productionDialog.getByText('PILOT-WH')).toBeVisible()

  await productionDialog.getByRole('tab', { name: 'Devices' }).click()
  await expect(productionDialog.getByText('RF-01')).toBeVisible()
  await productionDialog.getByRole('button', { name: 'Create activation QR' }).click()

  const activationDialog = page.getByRole('dialog', { name: 'Device activation' })
  await expect(activationDialog.getByText('Scanner provisioning')).toBeVisible()
  await formInput(activationDialog, 'Server URL').fill('https://wms.example.test/api')
  await formInput(activationDialog, 'Tenant').fill('CP6')
  await formInput(activationDialog, 'Warehouse').fill('PILOT-WH')
  await formInput(activationDialog, 'Area').fill('PICK-A')
  await formInput(activationDialog, 'HID prefix').fill(']C1')
  await formInput(activationDialog, 'HID suffix').fill('~')
  await formInput(activationDialog, 'Duplicate window').fill('900')

  await activationDialog.locator('.el-form-item', { hasText: 'HID terminator' }).locator('.el-select').click()
  await page.getByRole('option', { name: 'Tab', exact: true }).click()
  await activationDialog.getByRole('button', { name: 'Generate one-time QR' }).click()

  await expect.poll(() => activationRequests.length).toBe(1)
  expect(activationRequests[0]).toEqual({
    platform: 'Android',
    deviceMode: 'Shared',
    warehouseCd: 'PILOT-WH',
    areaCd: 'PICK-A',
  })

  const encodedPayload = await activationDialog.locator('.activation-qr code').innerText()
  const payload = new URL(encodedPayload)
  expect(payload.protocol).toBe('cp6-activate:')
  expect(payload.hostname).toBe('device')
  expect(payload.searchParams.get('server')).toBe('https://wms.example.test/api')
  expect(payload.searchParams.get('tenant')).toBe('CP6')
  expect(payload.searchParams.get('token')).toBe('one-time-token-123')
  expect(payload.searchParams.get('scanPrefix')).toBe(']C1')
  expect(payload.searchParams.get('scanSuffix')).toBe('~')
  expect(payload.searchParams.get('scanTerminator')).toBe('Tab')
  expect(payload.searchParams.get('scanDuplicateMs')).toBe('900')
  await expect(activationDialog.getByAltText('Device activation QR')).toHaveAttribute('src', /^data:image\/png;base64,/)
  expect(pageErrors).toEqual([])
  expect(consoleErrors).toEqual([])
})

test('hides scanner provisioning for Windows activations', async ({ page }) => {
  const activationRequests: ActivationRequest[] = []
  await installApiFixtures(page, activationRequests)

  await page.goto('/wms/mobile-task')
  await page.getByRole('button', { name: 'Production console' }).click()
  const productionDialog = page.getByRole('dialog', { name: 'WMS production console' })
  await productionDialog.getByRole('tab', { name: 'Devices' }).click()
  await productionDialog.getByRole('button', { name: 'Create activation QR' }).click()

  const activationDialog = page.getByRole('dialog', { name: 'Device activation' })
  await activationDialog.locator('.el-form-item', { hasText: 'Platform' }).locator('.el-select').click()
  await page.getByRole('option', { name: 'Windows', exact: true }).click()
  await expect(activationDialog.getByText('Scanner provisioning')).toBeHidden()
})
