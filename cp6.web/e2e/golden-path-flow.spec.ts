import { test, expect, type APIRequestContext } from '@playwright/test'

/**
 * CP6 完整业务流程 E2E（实写版 / 真实写入）
 *
 * 与 golden-path.spec.ts（画面走查）不同，本用例真正"跑"完整流程：
 * 通过后端 API 实际下单、发行製造指図、入出庫，产生真实单号，并断言模块间自动联动：
 *
 *   ① ERP  受注作成            → 自動で WMS 出荷指示が生成される（Order → WMS hook）
 *   ② MES  製造指図 作成・発行 → 自動で WMS 材料出庫指示が生成される（WorkOrder → WMS hook）
 *   ③ WMS  入庫 → 出庫指示 → 確定 → 引当 → 出庫確定（梱包NO採番 + 在庫減算）
 *
 * 前提：后端(5177)/前端(5173) 运行中，已 seed デモマスタ
 *      （取引先 ASAHI、倉庫 DW01、ロケーション DEMO-RAW-A-01、材料 GLUE-A）。
 * 每次运行用时间戳生成唯一製品CD/ロットCD，可重复执行不冲突。
 */

const API = 'http://localhost:5177/api'
const RUN = Date.now().toString().slice(-7)
const PRODUCT_CD = `E2EP${RUN}`
const LOT_NO = `E2EL${RUN}`
const CUSTOMER_CD = 'ASAHI'
const WAREHOUSE_CD = 'DW01'
const LOCATION_CD = 'DEMO-RAW-A-01'
const MATERIAL_CD = 'GLUE-A'

/** code===0 を前提に data を返す共通呼び出し */
async function call(
  request: APIRequestContext,
  token: string,
  method: 'get' | 'post',
  path: string,
  body?: unknown
): Promise<any> {
  const res = await request[method](`${API}${path}`, {
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    ...(body !== undefined ? { data: body } : {})
  })
  const text = await res.text()
  let json: any
  try {
    json = JSON.parse(text)
  } catch {
    throw new Error(`${method.toUpperCase()} ${path} は JSON 以外を返した (status=${res.status()}): ${text.slice(0, 200)}`)
  }
  expect(json.code, `${method.toUpperCase()} ${path} → code=0 期待 / 実際: ${JSON.stringify(json).slice(0, 200)}`).toBe(0)
  return json.data
}

/** 受注NO / 指図NO に紐づく出荷指示が自動生成されるまで数回リトライ */
async function findOutbound(
  request: APIRequestContext,
  token: string,
  predicate: (o: any) => boolean
): Promise<any | null> {
  for (let i = 0; i < 5; i++) {
    const list = (await call(request, token, 'get', '/wms/outbound-order?page=1&pageSize=100')) as any[]
    const hit = (list ?? []).find(predicate)
    if (hit) return hit
    await new Promise(r => setTimeout(r, 600))
  }
  return null
}

test('ERP → MES → WMS 完整业务流程（実書き込み + 跨模块自動連携）', async ({ request }) => {
  // ───── ログイン（実行時に新鮮な token を取得）─────
  const token: string = await test.step('ログイン', async () => {
    const res = await request.post(`${API}/auth/login`, {
      headers: { 'Content-Type': 'application/json' },
      data: { userName: 'admin', password: '123456' }
    })
    expect(res.ok()).toBeTruthy()
    const t = (await res.json()).token
    expect(t, 'login token').toBeTruthy()
    return t
  })

  // ─────────────────────── ① ERP：受注作成 → 出荷指示 自動生成 ───────────────────────
  let webOrderNo = ''
  await test.step('ERP 受注作成 → WMS 出荷指示が自動生成される', async () => {
    const order = await call(request, token, 'post', '/orders', {
      customerCd: CUSTOMER_CD,
      orderType: '01',
      orderDate: '2026-05-30',
      quantity: 1000,
      details: [
        {
          webOrderDetailNo: 1,
          productCd: PRODUCT_CD,
          quantity: 1000,
          customerDeliveryDate: '2026-06-15'
        }
      ]
    })
    webOrderNo = order.webOrderNo
    expect(webOrderNo, '受注NO が採番される').toBeTruthy()

    // 跨模块 hook：受注 → 出荷指示（OutboundType=2）
    const ship = await findOutbound(request, token, o => o.webOrderNo === webOrderNo)
    expect(ship, `受注 ${webOrderNo} から出荷指示が自動生成される`).not.toBeNull()
    expect(ship.outboundType, '出荷指示は OutboundType=2').toBe(2)
  })

  // ─────────────────────── ② MES：製造指図 作成・発行 → 材料出庫 自動生成 ───────────────────────
  let workOrderNo = ''
  await test.step('MES 製造指図 作成・発行 → WMS 材料出庫が自動生成される', async () => {
    const wo = await call(request, token, 'post', '/mes/work-orders', {
      productCd: PRODUCT_CD,
      productName: 'E2E自動テスト製品',
      customerCd: CUSTOMER_CD,
      productionQty: 1000,
      priority: 1,
      planStartDate: '2026-05-30',
      planEndDate: '2026-06-05',
      processes: [{ processCd: 'P10', taskCd: 'PRINT', processName: '印刷', sortOrder: 1 }],
      materials: [{ processCd: 'P10', materialCd: MATERIAL_CD, planQty: 3, unit: 'KG', sortOrder: 1 }]
    })
    workOrderNo = wo.workOrderNo
    expect(workOrderNo, '製造指図NO が採番される').toBeTruthy()

    // 発行（status → 2）。末尾で WMS 材料出庫 hook が発火する
    await call(request, token, 'post', `/mes/work-orders/${workOrderNo}/issue`)
    const issued = await call(request, token, 'get', `/mes/work-orders/${workOrderNo}`)
    expect(issued.status, '発行後の指図ステータス=2').toBe(2)

    // 跨模块 hook：製造指図 → 材料出庫（OutboundType=1）
    const matOut = await findOutbound(request, token, o => o.workOrderNo === workOrderNo)
    expect(matOut, `製造指図 ${workOrderNo} から材料出庫が自動生成される`).not.toBeNull()
    expect(matOut.outboundType, '材料出庫は OutboundType=1').toBe(1)
  })

  // ─────────────────────── ③ WMS：入庫 → 出庫指示 → 確定 → 引当 → 出庫確定 ───────────────────────
  await test.step('WMS 入庫 → 出庫指示 → 確定 → 引当 → 出庫確定（梱包採番 + 在庫減算）', async () => {
    // 入庫（IN）：完成品 5000 を DW01 / DEMO-RAW-A-01 に上架
    const inTxn = await call(request, token, 'post', '/wms/stock/apply', {
      txnType: 'IN',
      warehouseCd: WAREHOUSE_CD,
      locationCd: LOCATION_CD,
      productCd: PRODUCT_CD,
      lotNo: LOT_NO,
      qty: 5000,
      unitCd: 'EA',
      relatedType: 'E2E',
      remark: 'E2E 入庫'
    })
    expect(inTxn.txnNo, '入庫トランザクションNO').toBeTruthy()

    const stockBefore = await getStockQty(request, token, PRODUCT_CD)
    expect(stockBefore.physical, '入庫後の物理在庫=5000').toBe(5000)

    // 出庫指示（出荷区分 OutboundType=2）作成
    const outbound = await call(request, token, 'post', '/wms/outbound-order', {
      outboundType: 2,
      warehouseCd: WAREHOUSE_CD,
      customerCd: CUSTOMER_CD,
      priority: 1,
      remarks: 'E2E 出庫',
      details: [{ lineNo: 1, productCd: PRODUCT_CD, requiredQty: 1000, unitCd: 'EA' }]
    })
    const outboundNo = outbound.outboundNo
    expect(outboundNo, '出庫指示NO が採番される').toBeTruthy()

    // 確定 → 引当（FIFO + 期限優先）→ 出庫確定
    await call(request, token, 'post', `/wms/outbound-order/${outboundNo}/confirm`)
    await call(request, token, 'post', `/wms/outbound-order/${outboundNo}/allocate`)
    const shipped = await call(request, token, 'post', `/wms/outbound-order/${outboundNo}/ship`, {
      caseQty: 10,
      carrierCd: 'YAMATO',
      trackingNo: `E2E-TRACK-${RUN}`
    })
    expect(shipped.packageNo, '出荷区分の出庫確定で梱包NO が採番される').toBeTruthy()

    // 在庫が 1000 減って 4000 になっている
    const stockAfter = await getStockQty(request, token, PRODUCT_CD)
    expect(stockAfter.physical, '出庫確定後の物理在庫=4000').toBe(4000)
  })
})

/** 製品CD の在庫（physical / available）を取得 */
async function getStockQty(request: APIRequestContext, token: string, productCd: string) {
  const data = await call(request, token, 'get', `/wms/stock?productCd=${productCd}&page=1&pageSize=10`)
  const items: any[] = data.items ?? []
  const physical = items.reduce((s, it) => s + Number(it.physicalQty || 0), 0)
  const available = items.reduce((s, it) => s + Number(it.availableQty || 0), 0)
  return { physical, available, items }
}
