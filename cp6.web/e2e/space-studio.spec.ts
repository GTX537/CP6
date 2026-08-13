import path from 'node:path'
import { expect, test, type Page } from '@playwright/test'

const versionId = '11111111-1111-1111-1111-111111111111'
const floorId = '22222222-2222-2222-2222-222222222222'
const rackId = '33333333-3333-3333-3333-333333333333'
const rackLevelId = '44444444-4444-4444-4444-444444444444'
const columnId = '55555555-5555-5555-5555-555555555555'
const studioUrl = `/space/design/${versionId}/floors/${floorId}/underlay`

test('1440x900 provides the full editor and a consistent local 3D preview', async ({ page }) => {
  const errors = collectPageErrors(page)
  await installSpaceStudioFixtures(page)
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)

  await expect(page.getByText('Space Studio', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '2D', exact: true })).toHaveClass(/active/)
  await expect(page.locator('.studio-context-pane h2')).toHaveText('来源')
  await expect(page.locator('.studio-title-state').getByText('租约至', { exact: false })).toBeVisible()
  await expect(page.getByRole('button', { name: '校验并发布', exact: true })).toBeEnabled()
  await expect(page.locator('.el-loading-mask')).toHaveCount(0)

  const columns = await page.locator('.workspace').evaluate((element) =>
    getComputedStyle(element).gridTemplateColumns,
  )
  expect(columns).toMatch(/^296px .* 324px$/)
  const commandTargetHeights = await page.locator('.studio-commandbar button').evaluateAll(
    (elements) => elements
      .filter((element) => getComputedStyle(element).display !== 'none')
      .map((element) => element.getBoundingClientRect().height),
  )
  expect(commandTargetHeights.every((height) => height >= 44)).toBe(true)

  await page.getByRole('button', { name: '3D', exact: true }).click()
  await expect(page.getByText('2D/3D 清单一致', { exact: true })).toBeVisible()
  await expect(page.getByText('2D 2 / 3D 2', { exact: true })).toBeVisible()
  await captureEvidence(page, 'space-studio-1440x900.png')
  expect(errors).toEqual([])
})

test('1280x720 remains a complete editing surface', async ({ page }) => {
  const errors = collectPageErrors(page)
  await installSpaceStudioFixtures(page)
  await page.setViewportSize({ width: 1280, height: 720 })
  await page.goto(studioUrl)

  await expect(page.getByRole('button', { name: '2D', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '3D', exact: true })).toBeVisible()
  await expect(page.locator('.studio-context-pane')).toBeVisible()
  await expect(page.getByRole('button', { name: '校验并发布', exact: true })).toBeEnabled()
  await expect(page.getByText('窄屏只读', { exact: true })).toHaveCount(0)
  await expect(page.locator('.el-loading-mask')).toHaveCount(0)
  await captureEvidence(page, 'space-studio-1280x720.png')
  expect(errors).toEqual([])
})

test('held lease exposes wait and audited takeover recovery', async ({ page }) => {
  await installSpaceStudioFixtures(page, [], { leaseHeld: true })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)

  await expect(page.getByText('当前楼层正由其他会话编辑')).toBeVisible()
  await expect(page.getByRole('button', { name: '刷新并等待' })).toBeVisible()
  await expect(page.getByRole('button', { name: '申请接管' })).toBeVisible()
})

test('keyboard shortcuts never hijack focused inputs', async ({ page }) => {
  await installSpaceStudioFixtures(page, [], { leaseHeld: true })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)
  await page.getByRole('button', { name: '申请接管' }).click()
  const input = page.locator('.el-message-box input')
  await input.fill('恢复旧标签页')
  await input.press('Control+A')
  await input.press('v')
  await expect(input).toHaveValue('v')
})

test('below 1280 switches to read-only 3D with version and issues only', async ({ page }) => {
  const errors = collectPageErrors(page)
  const methods: string[] = []
  await installSpaceStudioFixtures(page, methods)
  await page.setViewportSize({ width: 1024, height: 720 })
  await page.goto(studioUrl)

  await expect(page.getByText('Draft · r7', { exact: false })).toBeVisible()
  await expect(page.locator('.studio-title-state').getByText('窄屏只读', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '2D', exact: true })).toBeHidden()
  await expect(page.getByRole('button', { name: '3D', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '3D', exact: true })).toHaveClass(/active/)
  await expect(page.locator('.issues-command')).toBeVisible()
  await expect(page.getByRole('button', { name: '校验并发布', exact: true })).toBeHidden()
  await expect(page.getByRole('tab', { name: '问题', exact: true })).toBeVisible()
  await expect(page.getByRole('tab', { name: '属性', exact: true })).toBeHidden()
  await expect(page.getByText('2D/3D 清单一致', { exact: true })).toBeVisible()
  expect(methods).not.toContain('POST lease')
  await captureEvidence(page, 'space-studio-1024x720.png')
  expect(errors).toEqual([])
})

function collectPageErrors(page: Page): string[] {
  const errors: string[] = []
  page.on('pageerror', (error) => errors.push(error.message))
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text())
  })
  return errors
}

async function captureEvidence(page: Page, fileName: string): Promise<void> {
  const directory = process.env.CP6_VISUAL_EVIDENCE_DIR
  if (!directory) return
  await page.screenshot({ path: path.join(directory, fileName), fullPage: true })
}

async function installSpaceStudioFixtures(
  page: Page,
  methods: string[] = [],
  options: { leaseHeld?: boolean } = {},
) {
  await page.addInitScript(() => {
    localStorage.setItem('cp6_authed', '1')
    localStorage.setItem('lang', 'zh-CN')
    localStorage.setItem('nickName', 'Space Modeler')
  })

  await page.route((url) => url.pathname.startsWith('/api/'), async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const scenePath = `/api/space/design/v1/versions/${versionId}/floors/${floorId}/scene`
    const leasePath = `/api/space/design/v1/versions/${versionId}/floors/${floorId}/lease`

    if (url.pathname === scenePath) {
      await route.fulfill({ json: sceneFixture() })
      return
    }
    if (url.pathname === leasePath && request.method() === 'GET') {
      methods.push('GET lease')
      await route.fulfill({ json: options.leaseHeld ? heldLease() : availableLease() })
      return
    }
    if (url.pathname === leasePath && request.method() === 'POST') {
      methods.push('POST lease')
      if (options.leaseHeld) {
        await route.fulfill({
          status: 409,
          json: { code: 'SPACE_EDIT_LEASE_HELD', detail: 'held' },
        })
        return
      }
      await route.fulfill({ json: ownedLease() })
      return
    }
    if (url.pathname.startsWith(`${leasePath}/`) || url.pathname.endsWith('/lease:takeover')) {
      await route.fulfill({ json: ownedLease() })
      return
    }
    if (url.pathname.endsWith('/wms-adoption/locations')) {
      await route.fulfill({ json: { items: [], nextCursor: null } })
      return
    }
    if (url.pathname === '/api/pub/role-perm/my-actions') {
      await route.fulfill({ json: { data: ['space:model:edit', 'space:model:lease:takeover'] } })
      return
    }
    await route.fulfill({ json: {} })
  })
}

function heldLease() {
  return {
    modelVersionId: versionId,
    floorLogicalId: floorId,
    ownerUserId: '99999999-9999-9999-9999-999999999999',
    holderDisplayName: '另一位编辑者',
    expiresAtUtc: '2030-01-01T00:01:30Z',
    isAvailable: false,
    isOwnedByCurrentActor: false,
  }
}

function availableLease() {
  return {
    modelVersionId: versionId,
    floorLogicalId: floorId,
    isAvailable: true,
    isOwnedByCurrentActor: false,
  }
}

function ownedLease() {
  return {
    modelVersionId: versionId,
    floorLogicalId: floorId,
    leaseId: '66666666-6666-6666-6666-666666666666',
    ownerUserId: '77777777-7777-7777-7777-777777777777',
    clientInstanceId: 'space-studio-playwright',
    expiresAtUtc: '2030-01-01T00:01:30Z',
    lastRenewedAtUtc: '2030-01-01T00:00:00Z',
    isAvailable: false,
    isOwnedByCurrentActor: true,
    rowVersion: 'AAAAAAAAB9E=',
  }
}

function sceneFixture() {
  return {
    schemaVersion: 1,
    authority: 'DesignRevision',
    runtimeOverlayIncluded: false,
    modelVersionId: versionId,
    siteId: '88888888-8888-8888-8888-888888888888',
    versionStatus: 'Draft',
    contentRevision: 7,
    contentHash: 'space-studio-playwright-fixture',
    floor: {
      revision: revision(floorId),
      siteLogicalId: '88888888-8888-8888-8888-888888888888',
      level: 1,
      floorCode: 'F1',
      name: 'Main floor',
      elevation: 0,
      height: 6000,
      coordinateSystem: 'RH_Z_UP_MM',
      revisionNumber: 7,
    },
    zones: [],
    aisles: [],
    racks: [{
      revision: revision(rackId),
      floorLogicalId: floorId,
      rackCode: 'R-001',
      name: 'Rack 001',
      rackType: 'Selective',
      x: 1000,
      y: 1200,
      z: 0,
      rotationZ: 0,
      width: 2400,
      depth: 1000,
      height: 3000,
    }],
    rackLevels: [{
      revision: revision(rackLevelId),
      rackLogicalId: rackId,
      levelNo: 1,
      bottomZ: 0,
      clearHeight: 1200,
      binCount: 2,
      depthCount: 1,
      cellWidth: 1200,
      cellDepth: 1000,
      beamHeight: 100,
      maxLoad: 1000,
    }],
    locations: [],
    elements: [{
      revision: revision(columnId),
      floorLogicalId: floorId,
      elementType: 'Column',
      geometryJson: '{"schemaVersion":1,"kind":"box","width":400,"height":3000,"depth":400}',
      x: 5200,
      y: 2400,
      z: 0,
      rotationZ: 0,
      width: 400,
      height: 3000,
      depth: 400,
      businessCode: 'COL-01',
    }],
    elementAttributes: [],
    locationExternalBindings: [],
    designAttributes: [],
  }
}

function revision(logicalId: string) {
  return {
    revisionId: crypto.randomUUID(),
    logicalId,
    lifecycleState: 'Active',
    rowVersion: 'AAAAAAAAB9E=',
  }
}
