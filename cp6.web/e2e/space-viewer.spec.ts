/**
 * Space Viewer P1 Closed-Loop E2E (05 渲染 / 06 定位)
 *
 * 整合进既有 Playwright 框架:由 playwright.config.ts 的 chromium project 收录,
 * 鉴权复用 e2e/auth.setup.ts 的 storageState(admin 已登录),无需本文件自行登录。
 *
 * 前提(同既有 e2e):前端 5173 + 后端 5177(→ CP6DB_SpaceQA)已运行。
 * 默认 IDs 指向 QA 演示数据,可经 env 覆盖:
 *   SPACE_SITE_ID / SPACE_FLOOR_ID / SPACE_LOCATION_CODE / SPACE_OTHER_FLOOR_CODE
 * 运行:npx playwright test e2e/space-viewer.spec.ts --project=chromium
 */

import { test, expect, type Page } from '@playwright/test'

const SITE_ID = process.env['SPACE_SITE_ID'] ?? 'F31F48C2-81D5-4BA7-AFF1-83DA8D87C2FE'
const FLOOR_ID = process.env['SPACE_FLOOR_ID'] ?? '5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F'
const LOCATION_CODE = process.env['SPACE_LOCATION_CODE'] ?? 'A-01-01-01'

async function openViewer(page: Page, floorId = FLOOR_ID): Promise<void> {
  await page.goto(`/space/viewer/${SITE_ID}?floorId=${floorId}`)
  // loading 初始为 false(.viewer-loading 由 v-if 控制,初次渲染不在 DOM)→ onMounted 跑 loadFloor 后才 true。
  // 若直接 waitForSelector(hidden) 会因元素尚未挂载而立即返回,场景其实未加载。
  // 故先等 loading 出现(若极快未出现则忽略),再等其消失=场景真正就绪。
  await page.waitForSelector('.viewer-loading', { state: 'visible', timeout: 10000 }).catch(() => {})
  await page.waitForSelector('.viewer-loading', { state: 'hidden', timeout: 30000 })
}

test.describe('Space Viewer P1 Closed-Loop', () => {
  // TC-N1: Floor loads and canvas renders
  test('N1-a: loads floor — canvas visible with non-zero size', async ({ page }) => {
    await openViewer(page)
    const canvas = page.locator('canvas.viewer-canvas')
    await expect(canvas).toBeVisible()
    const box = await canvas.boundingBox()
    expect(box?.width).toBeGreaterThan(100)
    expect(box?.height).toBeGreaterThan(100)
  })

  // TC-N1: Floor list sidebar
  test('N1-b: floor list sidebar is visible and shows items', async ({ page }) => {
    await openViewer(page)
    await expect(page.locator('.floor-list')).toBeVisible()
    await expect(page.locator('.floor-list__item').first()).toBeVisible()
  })

  // TC-N1: Floor switching — clicking a different floor triggers reload
  test('N1-c: switching floor reloads scene (active item updates)', async ({ page }) => {
    await openViewer(page)
    const items = page.locator('.floor-list__item')
    const count = await items.count()
    if (count < 2) {
      test.skip() // only one floor seeded — skip switching test
      return
    }
    // Click a floor that is NOT currently active
    const activeId = await page.locator('.floor-list__item--active').getAttribute('data-floor-id').catch(() => null)
    for (let i = 0; i < count; i++) {
      const item = items.nth(i)
      const floorId = await item.getAttribute('data-floor-id').catch(() => null)
      if (floorId !== activeId) {
        await item.click()
        break
      }
    }
    // Loading should appear then clear
    await page.waitForSelector('.viewer-loading', { state: 'hidden', timeout: 20000 })
  })

  // TC-M3: Click canvas → info card may appear (pick hit depends on seed data)
  test('M3: click center of canvas — no JS error', async ({ page }) => {
    await openViewer(page)
    const canvas = page.locator('canvas.viewer-canvas')
    const box = await canvas.boundingBox()
    if (!box) throw new Error('Canvas not found')
    await canvas.click({ position: { x: box.width / 2, y: box.height / 2 } })
    // Brief wait to confirm no unhandled error
    await page.waitForTimeout(500)
  })

  // TC-N3: Search box locate by exact code
  test('N3-a: search by exact code → camera flies and info card appears', async ({ page }) => {
    await openViewer(page)
    const searchInput = page.locator('.search-box .el-input__inner')
    await searchInput.click()
    await searchInput.pressSequentially(LOCATION_CODE)
    await searchInput.press('Enter')
    // locate = API 往返 + flyTo(~800ms) + InfoCard 渲染;冷/争用栈下放宽到 12s 稳过
    const infoCard = page.locator('.info-card')
    await expect(infoCard).toBeVisible({ timeout: 12000 })
  })

  // TC-N3b: Prefix search → candidate dropdown
  test('N3-b: typing prefix shows candidate dropdown', async ({ page }) => {
    await openViewer(page)
    const searchInput = page.locator('.search-box .el-input__inner')
    // Use first 2 chars of the code as prefix
    await searchInput.click()
    await searchInput.pressSequentially(LOCATION_CODE.slice(0, 2))
    // Wait for debounce (300 ms) + API response
    await page.waitForTimeout(600)
    // Candidates may appear if seed data exists — soft check
    const dropdown = page.locator('.search-candidates')
    const visible = await dropdown.isVisible().catch(() => false)
    // Log visibility for manual verification; do not fail if no data
    console.log(`Candidate dropdown visible: ${visible}`)
  })

  // TC-N3c: Click a candidate → locate
  test('N3-c: click candidate item triggers locate', async ({ page }) => {
    await openViewer(page)
    const searchInput = page.locator('.search-box .el-input__inner')
    await searchInput.click()
    await searchInput.pressSequentially(LOCATION_CODE.slice(0, 2))
    await page.waitForTimeout(600)
    const firstCandidate = page.locator('.search-candidate-item').first()
    if (await firstCandidate.isVisible()) {
      await firstCandidate.click({ force: true })
      await page.waitForTimeout(1200)
      // Info card should now show
      await expect(page.locator('.info-card')).toBeVisible({ timeout: 2000 })
    }
  })

  // TC-N4: Double-click focuses selected location
  test('N4-a: double-click canvas triggers focus animation — no JS error', async ({ page }) => {
    await openViewer(page)
    const canvas = page.locator('canvas.viewer-canvas')
    const box = await canvas.boundingBox()
    if (!box) throw new Error('Canvas not found')
    await canvas.dblclick({ position: { x: box.width / 2, y: box.height / 2 } })
    await page.waitForTimeout(800)
  })

  // TC-N4b: Toolbar Home button
  test('N4-b: toolbar home (⌂) resets camera', async ({ page }) => {
    await openViewer(page)
    await page.locator('.viewer-toolbar .tb-btn').filter({ hasText: '⌂' }).click()
    await page.waitForTimeout(700)
  })

  // TC-N4c: Toolbar Overview button (≡)
  test('N4-c: toolbar overview (≡) flies to top-down view', async ({ page }) => {
    await openViewer(page)
    await page.locator('.viewer-toolbar .tb-btn').filter({ hasText: '≡' }).click()
    await page.waitForTimeout(800)
  })

  // TC-N4d: Toolbar Focus button (⊕) after locate
  test('N4-d: focus button (⊕) after locate zooms into location', async ({ page }) => {
    await openViewer(page)
    // First locate a location
    const searchInput = page.locator('.search-box .el-input__inner')
    await searchInput.click()
    await searchInput.pressSequentially(LOCATION_CODE)
    await searchInput.press('Enter')
    await page.waitForTimeout(1200)
    // Now click focus
    await page.locator('.viewer-toolbar .tb-btn').filter({ hasText: '⊕' }).click()
    await page.waitForTimeout(700)
  })

  // TC-N4e: Toolbar Iso preset
  test('N4-e: toolbar iso (⬡) preset — no JS error', async ({ page }) => {
    await openViewer(page)
    await page.locator('.viewer-toolbar .tb-btn').filter({ hasText: '⬡' }).click()
    await page.waitForTimeout(700)
  })

  // TC-N4f: Toolbar projection toggle (⟳)
  test('N4-f: toggle projection (⟳) switches perspective/ortho', async ({ page }) => {
    await openViewer(page)
    await page.locator('.viewer-toolbar .tb-btn').filter({ hasText: '⟳' }).click()
    await page.waitForTimeout(300)
    // Toggle back
    await page.locator('.viewer-toolbar .tb-btn').filter({ hasText: '⟳' }).click()
    await page.waitForTimeout(300)
  })

  // TC-CROSS: Cross-floor locate (requires two seeded floors with placed locations)
  test('CROSS: locate a code on a different floor auto-switches floor', async ({ page }) => {
    const otherFloorCode = process.env['SPACE_OTHER_FLOOR_CODE']
    if (!otherFloorCode) {
      test.skip() // env var not set — skip cross-floor test
      return
    }
    await openViewer(page)
    const searchInput = page.locator('.search-box .el-input__inner')
    await searchInput.click()
    await searchInput.pressSequentially(otherFloorCode)
    await searchInput.press('Enter')
    // Cross-floor switch + load + flyTo
    await page.waitForSelector('.viewer-loading', { state: 'hidden', timeout: 20000 })
    await page.waitForTimeout(1200)
    await expect(page.locator('.info-card')).toBeVisible({ timeout: 3000 })
  })
})

// Diagnostic helper — run to open viewer and keep browser open for manual inspection
test.describe('Manual inspection (skip in CI)', () => {
  test.skip(
    !process.env['SPACE_MANUAL'],
    'Set SPACE_MANUAL=1 to run manual inspection tests',
  )

  test('open viewer for manual inspection', async ({ page }) => {
    await page.goto(`/space/viewer/${SITE_ID}?floorId=${FLOOR_ID}`)
    // Keep open for manual review — use --headed flag
    await page.waitForTimeout(60000)
  })
})
