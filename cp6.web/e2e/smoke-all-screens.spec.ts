import { test, expect, type Page } from '@playwright/test'

/**
 * 全画面スモークテスト
 *
 * 登录态下，遍历后端菜单（localStorage.menus）里所有带 routePath 的画面，
 * 逐一打开并断言"外壳 + 主内容区"正常渲染、未被踢回登录、无白屏。
 * 模块重构（views 按 WMS/ERP/MES/PMS 分目录）后用来兜底回归：
 * 任何 import 路径错误 / 路由失配 / 渲染崩溃都会在这里暴露。
 *
 * 用 expect.soft 收集所有失败，单个坏画面不会掩盖其它画面的结果。
 */

type MenuItem = { id: number; routePath?: string; menuName?: string }

function escapeRegExp(s: string) {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

async function assertScreenRenders(page: Page, path: string, label: string) {
  await page.goto(path, { waitUntil: 'domcontentloaded' })

  // 未被守卫踢回登录页
  expect.soft(page.url(), `${label} (${path}) 被踢回登录页`).not.toContain('/login')

  // 外壳
  await expect
    .soft(page.locator('.layout-aside').first(), `${label} 侧边栏未渲染`)
    .toBeVisible({ timeout: 12_000 })

  // 主内容区有实际元素
  const content = page
    .locator('.layout-main')
    .first()
    .locator(
      '.el-table, .el-form, .el-card, .el-tabs, .el-descriptions, .el-empty, ' +
        '.el-row, .page-header, h1, h2, h3, .el-button, canvas'
    )
  await expect
    .soft(content.first(), `${label} (${path}) 主内容区白屏`)
    .toBeVisible({ timeout: 15_000 })
}

test('全画面スモーク：菜单内の全画面が登录态で正常表示される', async ({ page }) => {
  // 70+ 画面を 1 テストで順次巡回するため、既定 60s では足りない。
  // 1 画面 ≒ 1〜2s × 70 画面 ＋ soft 待ち分の余裕を見て 4 分に拡張。
  test.setTimeout(240_000)

  // メニュー一覧（routePath 付き）を取得
  await page.goto('/')
  const menus = await page.evaluate(() => {
    try {
      return JSON.parse(localStorage.getItem('menus') || '[]')
    } catch {
      return []
    }
  })

  const routes = (menus as MenuItem[])
    .filter(m => m.routePath && m.routePath.startsWith('/'))
    // 独立大屏（Control Tower）は重いので別管理、ここでは除外
    .filter(m => m.routePath !== '/mes/control-tower')

  expect(routes.length, '菜单应包含可路由画面').toBeGreaterThan(10)

  for (const m of routes) {
    await test.step(`${m.menuName ?? ''} ${m.routePath}`, async () => {
      await assertScreenRenders(page, m.routePath!, m.menuName ?? m.routePath!)
    })
  }
})
