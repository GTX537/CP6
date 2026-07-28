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

type CapturedMutation = {
  path: string
  body: Record<string, unknown>
}

type ApiFixtureOptions = {
  failFirstSerialMutation?: boolean
}

async function installApiFixtures(
  page: Page,
  activationRequests: ActivationRequest[],
  capturedMutations: CapturedMutation[] = [],
  options: ApiFixtureOptions = {},
) {
  await page.addInitScript(() => {
    // Chromium may surface this delivery warning while Element Plus resizes
    // tables inside animated dialogs. It is not an application exception.
    window.addEventListener('error', (event) => {
      if (event.message.startsWith('ResizeObserver loop')) {
        event.stopImmediatePropagation()
        event.preventDefault()
      }
    }, true)
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

  await page.route(url => url.pathname.startsWith('/api/'), async (route) => {
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

    if (pathname === '/api/v2/wms/serials' && request.method() === 'POST') {
      capturedMutations.push({
        path: pathname,
        body: request.postDataJSON() as Record<string, unknown>,
      })
      const serialAttempts = capturedMutations.filter(item => item.path === pathname).length
      if (options.failFirstSerialMutation && serialAttempts === 1) {
        await route.fulfill({
          status: 504,
          json: { message: 'Command outcome unknown' },
        })
        return
      }
      await route.fulfill({
        json: {
          operationId: 'serial-operation',
          txnType: 'MOVE',
          productCd: 'P-01',
          serialCount: 1,
          stockTxnNos: ['TXN-01'],
          serials: [],
        },
      })
      return
    }

    if (pathname === '/api/v2/wms/serials') {
      await route.fulfill({
        json: {
          ...emptyPage,
          items: [{
            serialNo: 'SERIAL-01',
            productCd: 'P-01',
            warehouseCd: 'PILOT-WH',
            locationCd: 'SOURCE-A',
            lotNo: 'LOT-01',
            lpnNo: 'PALLET-01',
            status: 'InStock',
            rowVersion: 'serial-v1',
          }],
        },
      })
      return
    }

    if (pathname === '/api/v2/wms/lpns' && request.method() === 'POST') {
      capturedMutations.push({
        path: pathname,
        body: request.postDataJSON() as Record<string, unknown>,
      })
      await route.fulfill({
        json: {
          lpnNo: 'PALLET-02',
          containerType: 'PALLET',
          warehouseCd: 'PILOT-WH',
          locationCd: 'SOURCE-A',
          status: 'Open',
          contents: [],
          childLpns: [],
          rowVersion: 'lpn-v2',
        },
      })
      return
    }

    if (pathname.startsWith('/api/v2/wms/lpns/') && request.method() === 'POST') {
      capturedMutations.push({
        path: pathname,
        body: request.postDataJSON() as Record<string, unknown>,
      })
      await route.fulfill({
        json: {
          lpnNo: 'PALLET-01',
          containerType: 'PALLET',
          warehouseCd: 'PILOT-WH',
          locationCd: 'SOURCE-A',
          status: 'Open',
          contents: [],
          childLpns: [],
          rowVersion: 'lpn-v2',
        },
      })
      return
    }

    if (pathname === '/api/v2/wms/lpns') {
      await route.fulfill({
        json: {
          ...emptyPage,
          items: [{
            lpnNo: 'PALLET-01',
            containerType: 'PALLET',
            warehouseCd: 'PILOT-WH',
            locationCd: 'SOURCE-A',
            parentLpnNo: null,
            status: 'Open',
            contents: [],
            childLpns: [],
            rowVersion: 'lpn-v1',
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

test('submits validated typed serial and LPN commands', async ({ page }) => {
  const capturedMutations: CapturedMutation[] = []
  await installApiFixtures(page, [], capturedMutations, {
    failFirstSerialMutation: true,
  })

  await page.goto('/wms/mobile-task')
  await page.getByRole('button', { name: 'Production console' }).click()
  const productionDialog = page.getByRole('dialog', { name: 'WMS production console' })

  await productionDialog.getByRole('tab', { name: 'Serials' }).click()
  await expect(productionDialog.getByText('SERIAL-01')).toBeVisible()
  await productionDialog.getByRole('button', { name: 'Post lifecycle' }).click()

  const serialDialog = page.getByRole('dialog', { name: 'Post serial lifecycle' })
  await serialDialog.getByRole('button', { name: 'Commit transaction' }).click()
  await expect(page.getByText('Product is required')).toBeVisible()
  await formInput(serialDialog, 'Product').fill('P-01')
  await formInput(serialDialog, 'Warehouse').fill('PILOT-WH')
  await formInput(serialDialog, 'Source location').fill('SOURCE-A')
  await formInput(serialDialog, 'Target location').fill('TARGET-B')
  await serialDialog.locator('.el-form-item', { hasText: 'Serial numbers' })
    .locator('textarea').fill('SERIAL-01')
  await serialDialog.getByRole('button', { name: 'Commit transaction' }).click()

  await expect.poll(() => capturedMutations.length).toBe(1)
  await expect(page.getByText('Command result was not confirmed', { exact: false })).toBeVisible()
  await expect(serialDialog).toBeVisible()
  await serialDialog.getByRole('button', { name: 'Commit transaction' }).click()
  await expect.poll(() => capturedMutations.length).toBe(2)
  expect(capturedMutations[0]?.path).toBe('/api/v2/wms/serials')
  expect(capturedMutations[0]?.body).toMatchObject({
    operationId: expect.any(String),
    txnType: 'MOVE',
    productCd: 'P-01',
    serialNos: ['SERIAL-01'],
    warehouseCd: 'PILOT-WH',
    fromLocationCd: 'SOURCE-A',
    toLocationCd: 'TARGET-B',
  })
  expect(capturedMutations[1]?.body.operationId).toBe(capturedMutations[0]?.body.operationId)

  await productionDialog.getByRole('tab', { name: 'LPNs' }).click()
  await expect(productionDialog.getByLabel('LPNs').getByText('PALLET-01')).toBeVisible()
  await productionDialog.getByRole('button', { name: 'Create LPN' }).click()

  const createDialog = page.getByRole('dialog', { name: 'Create logistics unit' })
  await createDialog.getByRole('button', { name: 'Create LPN' }).click()
  await expect(page.getByText('LPN is required')).toBeVisible()
  await formInput(createDialog, 'LPN').fill('PALLET-02')
  await formInput(createDialog, 'Container type').fill('PALLET')
  await formInput(createDialog, 'Warehouse').fill('PILOT-WH')
  await formInput(createDialog, 'Location').fill('SOURCE-A')
  await createDialog.getByRole('button', { name: 'Create LPN' }).click()

  await expect.poll(() => capturedMutations.length).toBe(3)
  expect(capturedMutations[2]).toMatchObject({
    path: '/api/v2/wms/lpns',
    body: {
      operationId: expect.any(String),
      lpnNo: 'PALLET-02',
      containerType: 'PALLET',
      warehouseCd: 'PILOT-WH',
      locationCd: 'SOURCE-A',
    },
  })

  await productionDialog.getByRole('button', { name: 'Split', exact: true }).first().click()
  const splitDialog = page.getByRole('dialog', { name: 'SPLIT PALLET-01' })
  await formInput(splitDialog, 'Target LPN').fill('PALLET-03')
  await formInput(splitDialog, 'Target container').fill('PALLET')
  await splitDialog.getByRole('button', { name: 'Confirm split' }).click()
  await expect(page.getByText('Split requires a child LPN or serial number')).toBeVisible()
  await splitDialog.locator('.el-form-item', { hasText: 'Serial numbers' })
    .locator('textarea').fill('SERIAL-01')
  await splitDialog.getByRole('button', { name: 'Confirm split' }).click()

  await expect.poll(() => capturedMutations.length).toBe(4)
  expect(capturedMutations[3]).toMatchObject({
    path: '/api/v2/wms/lpns/PALLET-01/split',
    body: {
      operationId: expect.any(String),
      rowVersion: 'lpn-v1',
      targetLpnNo: 'PALLET-03',
      targetContainerType: 'PALLET',
      serialNos: ['SERIAL-01'],
      childLpns: [],
    },
  })
})
