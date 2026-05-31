import { test, expect, type Page } from '@playwright/test'

/**
 * CP6 ゴールデンパス E2E（ERP → MES → WMS 跨模块完整流程）
 *
 * 设计说明：
 *  - 业务主链（取引先→見積→御見積→製品→受注→製造指図→製造実績→品質検査→倉庫→在庫→出庫）
 *    的每一张画面按"业务先后顺序"在真实浏览器里依次打开，断言其登录态下正常渲染（外壳 + 内容区）。
 *  - 跨模块"接缝"（受注→製造指図的产品/客先继承、在庫数据）通过后端 API 只读断言来验证数据确实被打通，
 *    避免对 Element Plus 多步向导做脆弱的逐字段填写。
 *  - 复用 setup 阶段保存的登录会话（storageState），无需重复登录。
 */

const API = 'http://localhost:5177/api'

/** 打开一张业务画面并断言其在登录态下正常渲染 */
async function openScreen(page: Page, path: string, label: string) {
  await test.step(`画面表示: ${label} (${path})`, async () => {
    await page.goto(path, { waitUntil: 'domcontentloaded' })

    // 1) 不能被路由守卫踢回登录页
    await expect(page, `${label} 应保持在 ${path}，未被踢回登录页`).toHaveURL(
      new RegExp(escapeRegExp(path) + '(\\?.*)?$')
    )

    // 2) Layout 外壳（侧边栏 + 主内容区）已渲染
    await expect(page.locator('.layout-aside').first()).toBeVisible()
    const main = page.locator('.layout-main').first()
    await expect(main).toBeVisible()

    // 3) 主内容区里有实际页面元素（表格 / 表单 / 卡片 / 标签页 / 空状态 等任一）
    const content = main.locator(
      '.el-table, .el-form, .el-card, .el-tabs, .el-descriptions, .el-empty, ' +
        '.el-row, .page-header, h1, h2, h3, .el-button'
    )
    await expect(
      content.first(),
      `${label} 主内容区应渲染出页面元素`
    ).toBeVisible({ timeout: 15_000 })
  })
}

function escapeRegExp(s: string) {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

test.describe.serial('CP6 ゴールデンパス: ERP → MES → WMS', () => {
  // ─────────────────────────── ERP（販売・製品） ───────────────────────────
  test('ERP 段：取引先 → 見積 → 御見積 → 製品 → 受注 画面が順番に開く', async ({ page }) => {
    await openScreen(page, '/business-partner-list', '取引先マスタ 一覧')
    await openScreen(page, '/business-partner', '取引先マスタ 登録')
    await openScreen(page, '/estimate-calc-list', '見積計算書 照会')
    await openScreen(page, '/estimate-calc', '見積計算書 登録')
    await openScreen(page, '/quotation-list', '御見積書 一覧')
    await openScreen(page, '/quotation', '御見積書 登録')
    await openScreen(page, '/product-list', '製品マスタ 一覧')
    await openScreen(page, '/product', '製品マスタ 登録')
    await openScreen(page, '/order-list', '受注一覧照会')
    await openScreen(page, '/order', '受注入力')
  })

  // ─────────────────────────── MES（製造執行） ───────────────────────────
  test('MES 段：製造指図 → 製造実績 → 品質検査 → 計画ボード 画面が開く', async ({ page }) => {
    await openScreen(page, '/mes/work-order', '製造指図 入力')
    await openScreen(page, '/mes/work-order-list', '製造指図 一覧')
    await openScreen(page, '/mes/production-result', '製造実績 入力')
    await openScreen(page, '/mes/production-result-list', '製造実績 一覧')
    await openScreen(page, '/mes/quality-inspection', '品質検査 入力')
    await openScreen(page, '/mes/quality-inspection-list', '品質検査 一覧')
    await openScreen(page, '/mes/planning-board', '生産計画ボード')
    await openScreen(page, '/mes/dashboard', 'MES ダッシュボード')
  })

  // ─────────────────────────── WMS（倉庫管理） ───────────────────────────
  test('WMS 段：倉庫 → 在庫 → 入庫 → 出庫 → 棚卸 画面が開く', async ({ page }) => {
    await openScreen(page, '/wms/warehouse', '倉庫マスタ')
    await openScreen(page, '/wms/location', 'ロケーション管理')
    await openScreen(page, '/wms/stock', '在庫照会')
    await openScreen(page, '/wms/inbound-order-list', '入庫予定 一覧')
    await openScreen(page, '/wms/inbound-receipt', '入庫実績 入力')
    await openScreen(page, '/wms/outbound-order-list', '出庫指示 一覧')
    await openScreen(page, '/wms/stock-take-list', '棚卸 一覧')
    await openScreen(page, '/wms/dashboard', 'WMS ダッシュボード')
  })

  // ──────────────────── 跨模块接缝：后端 API 只读断言 ────────────────────
  test('跨模块データ接続：ERP マスタ / MES 製造指図 / WMS 在庫 が API で繋がっている', async ({ page }) => {
    // 登录态下从 localStorage 取当前 token
    await page.goto('/')
    const token = await page.evaluate(() => localStorage.getItem('token'))
    expect(token, 'storageState 应包含有效 token').toBeTruthy()
    const headers = { Authorization: `Bearer ${token}` }

    // ERP: 取引先マスタに登録データが存在する（販売・製品の起点）
    await test.step('ERP 取引先マスタにデータが存在', async () => {
      const res = await page.request.get(`${API}/business-partners/list?page=1&pageSize=5`, { headers })
      expect(res.ok()).toBeTruthy()
      const body = await res.json()
      expect(body.code).toBe(0)
      expect(Array.isArray(body.data?.rows)).toBeTruthy()
      expect(body.data.rows.length, '取引先が 1 件以上').toBeGreaterThan(0)
    })

    // MES: 製造指図が存在し、製品 CD / 客先 CD が継承されている（受注→製品→製造指図の接続証跡）
    await test.step('MES 製造指図に製品・客先が継承されている', async () => {
      const res = await page.request.get(`${API}/mes/work-orders?pageIndex=1&pageSize=5`, { headers })
      expect(res.ok()).toBeTruthy()
      const body = await res.json()
      expect(body.code).toBe(0)
      const items = body.data?.items ?? []
      expect(items.length, '製造指図が 1 件以上').toBeGreaterThan(0)
      const wo = items[0]
      expect(wo.productCd, '製造指図に製品 CD が継承されている').toBeTruthy()
      expect(wo.customerCd, '製造指図に客先 CD が継承されている').toBeTruthy()
    })

    // WMS: 在庫データが存在し、利用可能数が計算されている（入出庫トランザクションの結果）
    await test.step('WMS 在庫が計算済みで存在', async () => {
      const res = await page.request.get(`${API}/wms/stock?page=1&pageSize=5`, { headers })
      expect(res.ok()).toBeTruthy()
      const body = await res.json()
      expect(body.code).toBe(0)
      const items = body.data?.items ?? []
      expect(items.length, '在庫が 1 件以上').toBeGreaterThan(0)
      const stk = items[0]
      expect(stk.productCd, '在庫に製品 CD が紐づく').toBeTruthy()
      expect(typeof stk.availableQty, '利用可能数が数値').toBe('number')
    })
  })
})
