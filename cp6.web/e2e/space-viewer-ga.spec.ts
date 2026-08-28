import path from 'node:path'
import { expect, test, type Page } from '@playwright/test'

const siteId = '88888888-8888-8888-8888-888888888888'
const versionId = '11111111-1111-1111-1111-111111111111'
const floorId = '22222222-2222-2222-2222-222222222222'
const rackId = '33333333-3333-3333-3333-333333333333'
const rackLevelId = '44444444-4444-4444-4444-444444444444'
const locationId = '55555555-5555-5555-5555-555555555555'
const zoneId = '66666666-6666-6666-6666-666666666666'
const viewerUrl = `/space/viewer/${siteId}?floorId=${floorId}`

test('1440x900 loads only the Current Published Design Revision', async ({ page }) => {
  const errors = collectPageErrors(page)
  const requests: string[] = []
  await installViewerFixtures(page, requests)
  await page.setViewportSize({ width: 1440, height: 900 })
  await openViewer(page)

  const canvas = page.getByRole('region', { name: '仓库三维视图' })
  await expect(canvas).toBeVisible()
  await expect(page.getByRole('navigation', { name: '楼层' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Main floor' })).toHaveAttribute(
    'aria-current',
    'page',
  )
  expect(requests.filter(request => request.endsWith('/published-scene'))).toHaveLength(1)
  expect(requests.some(request => /\/versions\/[^/]+\/floors\/[^/]+\/scene/.test(request)))
    .toBe(false)
  expect(requests.some(request => /\/space\/floors\/[^/]+\/scene/.test(request)))
    .toBe(false)
  await captureEvidence(page, 'space-viewer-ga-1440x900.png')
  expect(errors).toEqual([])
})

test('1280x720 keeps the Viewer controls and canvas inside the viewport', async ({ page }) => {
  const errors = collectPageErrors(page)
  await installViewerFixtures(page)
  await page.setViewportSize({ width: 1280, height: 720 })
  await openViewer(page)

  const layout = await page.evaluate(() => ({
    innerWidth: window.innerWidth,
    innerHeight: window.innerHeight,
    scrollWidth: document.documentElement.scrollWidth,
    scrollHeight: document.documentElement.scrollHeight,
  }))
  expect(layout.scrollWidth).toBeLessThanOrEqual(layout.innerWidth)
  expect(layout.scrollHeight).toBeLessThanOrEqual(layout.innerHeight)
  await expect(page.getByRole('toolbar', { name: '三维视图工具栏' })).toBeVisible()
  await expect(page.getByRole('region', { name: '仓库三维视图' })).toBeVisible()
  await captureEvidence(page, 'space-viewer-ga-1280x720.png')
  expect(errors).toEqual([])
})

test('keyboard users can operate the canvas, floor list, and toolbar', async ({ page }) => {
  const errors = collectPageErrors(page)
  await installViewerFixtures(page)
  await page.setViewportSize({ width: 1440, height: 900 })
  await openViewer(page)

  await page.evaluate(() => {
    ;(window as typeof window & { __handledViewerKeys?: string[] }).__handledViewerKeys = []
    document.addEventListener('keydown', (event) => {
      if (event.defaultPrevented) {
        ;(window as typeof window & { __handledViewerKeys?: string[] })
          .__handledViewerKeys?.push(event.key.toLowerCase())
      }
    })
  })
  const canvas = page.getByRole('region', { name: '仓库三维视图' })
  await canvas.focus()
  await expect(canvas).toBeFocused()
  for (const key of ['1', '2', '3', 'Home', 'o', 'f', 'p']) await canvas.press(key)
  await expect.poll(() => page.evaluate(() =>
    (window as typeof window & { __handledViewerKeys?: string[] }).__handledViewerKeys,
  )).toEqual(['1', '2', '3', 'home', 'o', 'f', 'p'])

  const dispatch = page.getByRole('button', { name: '人员调度建议' })
  await dispatch.focus()
  await dispatch.press('Enter')
  await expect(dispatch).toHaveAttribute('aria-pressed', 'true')
  await dispatch.press('Space')
  await expect(dispatch).toHaveAttribute('aria-pressed', 'false')

  const floor = page.getByRole('button', { name: 'Main floor' })
  await floor.focus()
  await expect(floor).toBeFocused()
  expect(errors).toEqual([])
})

test('critical Viewer controls expose an accessibility tree and 4.5:1 contrast', async ({ page }) => {
  const errors = collectPageErrors(page)
  await installViewerFixtures(page)
  await page.setViewportSize({ width: 1440, height: 900 })
  await openViewer(page)

  const session = await page.context().newCDPSession(page)
  const tree = await session.send('Accessibility.getFullAXTree')
  const accessibleNames = tree.nodes
    .map(node => node.name?.value)
    .filter((value): value is string => typeof value === 'string')
  for (const name of [
    '仓库三维视图',
    '三维视图工具栏',
    '俯视',
    '等轴',
    '正视',
    '复位',
    '整层概览',
    '聚焦选中',
    '切换投影',
    'Main floor',
  ]) {
    expect(accessibleNames).toContain(name)
  }

  const controls = page.locator('.viewer-toolbar .tb-btn, .floor-list__item')
  const ratios: Array<{ name: string; ratio: number }> = []
  for (let index = 0; index < await controls.count(); index += 1) {
    const control = controls.nth(index)
    ratios.push({
      name: await control.getAttribute('aria-label') ?? await control.innerText(),
      ratio: await control.evaluate(contrastRatio),
    })
  }
  expect(ratios.length).toBeGreaterThanOrEqual(12)
  expect(Math.min(...ratios.map(result => result.ratio))).toBeGreaterThanOrEqual(4.5)
  expect(errors).toEqual([])
})

function collectPageErrors(page: Page): string[] {
  const errors: string[] = []
  page.on('pageerror', error => errors.push(error.message))
  page.on('console', message => {
    if (message.type() === 'error') errors.push(message.text())
  })
  return errors
}

async function openViewer(page: Page): Promise<void> {
  await page.goto(viewerUrl)
  await expect(page.locator('.viewer-error')).toHaveCount(0)
  await expect(page.locator('.viewer-loading')).toHaveCount(0)
  await expect(page.getByRole('region', { name: '仓库三维视图' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Main floor' })).toHaveAttribute(
    'aria-current',
    'page',
  )
}

async function captureEvidence(page: Page, fileName: string): Promise<void> {
  const directory = process.env.CP6_VISUAL_EVIDENCE_DIR
  if (!directory) return
  await page.screenshot({ path: path.join(directory, fileName), fullPage: true })
}

async function installViewerFixtures(page: Page, requests: string[] = []): Promise<void> {
  await page.addInitScript(() => {
    localStorage.setItem('cp6_authed', '1')
    localStorage.setItem('lang', 'zh-CN')
    localStorage.setItem('nickName', 'BUBAO.GAO')
  })
  await page.route(url => url.pathname.startsWith('/api/'), async route => {
    const url = new URL(route.request().url())
    requests.push(url.pathname)
    if (url.pathname === `/api/space/design/v1/sites/${siteId}/published-scene`) {
      await route.fulfill({ json: publishedSnapshot() })
      return
    }
    if (url.pathname === `/api/space/design/v1/sites/${siteId}/runtime/inventory`) {
      await route.fulfill({ json: inventorySnapshot() })
      return
    }
    if (url.pathname === '/api/pub/role-perm/my-actions') {
      await route.fulfill({ json: { data: [] } })
      return
    }
    await route.fulfill({ json: {} })
  })
}

function publishedSnapshot() {
  const contentHash = 'd'.repeat(64)
  return {
    schemaVersion: 1,
    authority: 'DesignRevision',
    runtimeOverlayIncluded: false,
    siteId,
    publishedVersionId: versionId,
    contentRevision: 7,
    contentHash,
    floors: [{
      schemaVersion: 1,
      authority: 'DesignRevision',
      runtimeOverlayIncluded: false,
      modelVersionId: versionId,
      siteId,
      versionStatus: 'Published',
      contentRevision: 7,
      contentHash,
      floor: {
        revision: revision(floorId),
        siteLogicalId: siteId,
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
        zoneLogicalId: zoneId,
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
        binCount: 1,
        depthCount: 1,
        cellWidth: 1200,
        cellDepth: 1000,
        beamHeight: 100,
        maxLoad: 1000,
      }],
      locations: [{
        revision: revision(locationId),
        floorLogicalId: floorId,
        rackLogicalId: rackId,
        locationCode: 'R-001-L01-C001-D01',
        columnNo: 1,
        levelNo: 1,
        depthNo: 1,
        width: 1200,
        height: 1200,
        depth: 1000,
        codeOrigin: 'Generated',
        externalBindingState: 'Bound',
      }],
      elements: [],
      elementAttributes: [],
      locationExternalBindings: [],
      designAttributes: [],
    }],
  }
}

function inventorySnapshot() {
  return {
    siteId,
    publishedVersionId: versionId,
    warehouseCode: 'CONTROLLED-WH',
    source: {
      kind: 'Simulated',
      adapterId: 'PLAYWRIGHT-FIXTURE',
      dataSourceId: 'PLAYWRIGHT-FIXTURE',
      observedAtUtc: '2026-08-28T12:00:00Z',
      receivedAtUtc: '2026-08-28T12:00:01Z',
      delayMilliseconds: 1000,
      clockSkewMilliseconds: 0,
      isSimulated: true,
      isAvailable: true,
    },
    items: [],
  }
}

function revision(logicalId: string) {
  return {
    revisionId: `${logicalId.slice(0, 8)}-aaaa-bbbb-cccc-111111111111`,
    logicalId,
    lifecycleState: 'Active',
    rowVersion: 'AAAAAAAAB9E=',
  }
}

function contrastRatio(element: Element): number {
  const parse = (value: string): [number, number, number, number] => {
    const match = value.match(/[\d.]+/g)?.map(Number) ?? [0, 0, 0]
    return [match[0] ?? 0, match[1] ?? 0, match[2] ?? 0, match[3] ?? 1]
  }
  const composite = (
    foreground: [number, number, number, number],
    background: [number, number, number, number],
  ): [number, number, number, number] => {
    const alpha = foreground[3] + background[3] * (1 - foreground[3])
    if (alpha === 0) return [0, 0, 0, 0]
    return [
      (foreground[0] * foreground[3] + background[0] * background[3] * (1 - foreground[3])) / alpha,
      (foreground[1] * foreground[3] + background[1] * background[3] * (1 - foreground[3])) / alpha,
      (foreground[2] * foreground[3] + background[2] * background[3] * (1 - foreground[3])) / alpha,
      alpha,
    ]
  }
  let background: [number, number, number, number] = [255, 255, 255, 1]
  const layers: Array<[number, number, number, number]> = []
  for (let current: Element | null = element; current; current = current.parentElement) {
    layers.push(parse(getComputedStyle(current).backgroundColor))
  }
  for (const layer of layers.reverse()) background = composite(layer, background)
  const foreground = composite(parse(getComputedStyle(element).color), background)
  const luminance = (color: [number, number, number, number]) => {
    const channels = color.slice(0, 3).map(channel => {
      const normalized = channel / 255
      return normalized <= 0.04045
        ? normalized / 12.92
        : ((normalized + 0.055) / 1.055) ** 2.4
    })
    return 0.2126 * channels[0]! + 0.7152 * channels[1]! + 0.0722 * channels[2]!
  }
  const foregroundLuminance = luminance(foreground)
  const backgroundLuminance = luminance(background)
  return (Math.max(foregroundLuminance, backgroundLuminance) + 0.05)
    / (Math.min(foregroundLuminance, backgroundLuminance) + 0.05)
}
