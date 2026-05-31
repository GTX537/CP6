import { test, expect, type APIRequestContext } from '@playwright/test'

/**
 * CP6 ERP 前段 完整业务线 E2E（実写版 / 真实写入）
 *
 * 对应演示脚本 Step1〜Step6：从「客户资料」一路走到「报价・产品・受注・改单价」。
 * 每步都真正调用后端 API 写库，并断言模块间的真实数据衔接：
 *
 *   Step1 取引先(客户+请款)  → bpCd 採番（作为下游 customerCd，全链真实相连）
 *   Step2 見積計算書         → calculate 引擎算价 + 登録（QtnDiv=20 決定見積）
 *   Step3 御見積書           → 関連計算書 → 確定登録（MasterConfirmFlg=9，校验 QtnDiv=20）
 *   Step4 製品マスタ         → by-quotation/by-estimate-calc 引入 → 登録（productCd 採番）
 *   Step5 受注               → 用 productCd 下单（+ 自動 WMS 出荷指示 Hook）
 *   Step6 単価訂正           → batch 改価 → ApprovalStatus=1 + POWER EGG WF 起票
 *
 * 前提：后端(5177) 运行中。每次用时间戳生成唯一编码，可重复执行不冲突。
 */

const API = 'http://localhost:5177/api'
const RUN = Date.now().toString().slice(-7)

const BP_CD = `E2EBP${RUN}`          // 新規取引先（= 下游 customerCd）
const PROJECT_NO = `E2EPJ${RUN}`     // 案件NO（見積→报价 关联键）
const BASE_CD = '0001'               // 拠点CD（无 FK 校验，dummy 即可）
const STAFF_CD = 'admin'             // 担当CD（dummy）

/** code===0 を前提に data を返す共通呼び出し */
async function call(
  request: APIRequestContext,
  token: string,
  method: 'get' | 'post' | 'put',
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
  expect(json.code, `${method.toUpperCase()} ${path} → code=0 期待 / 実際: ${JSON.stringify(json).slice(0, 300)}`).toBe(0)
  return json.data
}

/** 受注NO に紐づく出荷指示が自動生成されるまで数回リトライ（best-effort Hook） */
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

test('ERP 前段 完整业务线（取引先 → 見積 → 御見積 → 製品 → 受注 → 単価訂正）', async ({ request }) => {
  // ───── ログイン ─────
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

  // ─────────────────── Step1：取引先マスタ（客户 + 请款）───────────────────
  await test.step('Step1 取引先作成（得意先FLG + 請求先FLG）', async () => {
    const bp = await call(request, token, 'post', '/business-partners', {
      bpCd: BP_CD,
      bpName: `E2E自動テスト得意先 ${RUN}`,
      baseCd: BASE_CD,
      // 得意先(客户)属性 → 売掛先/営業担当/業務担当 必須（非空即可）
      customerFlg: true,
      accountsReceivableCd: BP_CD,
      salesStaffCd: STAFF_CD,
      businessStaffCd: STAFF_CD,
      // 請求先(请款)属性 → 入金先 必須
      billingFlg: true,
      receiptCd: BP_CD
    })
    expect(bp?.bpCd, '取引先CD が採番/保存される').toBe(BP_CD)
    expect(bp?.customerFlg, '得意先FLG=ON').toBeTruthy()
  })

  // ─────────────────── Step2：見積計算書（算价 + 登録）───────────────────
  let qtnCalcNo = ''
  await test.step('Step2 見積計算書 calculate + 登録（QtnDiv=20 決定見積）', async () => {
    // ① 計算引擎：面積 × 原紙単価 × 段成率（主数据缺失时走 fallback，仍返回正价）
    const calcDto = {
      qtnBaseCd: BASE_CD,
      orderBaseCd: BASE_CD,
      staffCd: STAFF_CD,
      customerCd: BP_CD,
      projectNoParent: PROJECT_NO,
      customerProductName1: 'E2E 包装箱',
      orderQty: 1000,
      sheetFlute: 'A',
      paperCdF: 'K5',
      sheetDimW: 400,
      sheetDimF: 300,
      estimateQtys: [1000, 2000, null, null, null, null, null, null],
      processes: [{ seqNo: 1, processCd: 'P10', processName: '印刷' }]
    }
    const calc = await call(request, token, 'post', '/estimate-calcs/calculate', calcDto)
    expect(calc?.estimateUnitPrice, '見積単価が算出される').toBeGreaterThan(0)

    // ② 登録（确定単価 + QtnDiv=20。Quotation 確定登録の前提）
    const created = await call(request, token, 'post', '/estimate-calcs', {
      ...calcDto,
      qtnDiv: '20',
      estimateUnitPrice: calc.estimateUnitPrice,
      confirmedUnitPrice: calc.confirmedUnitPrice,
      standardUnitPrice: calc.standardUnitPrice,
      estimateSqm: calc.estimateSqm,
      unit: 'EA'
    })
    qtnCalcNo = created.qtnCalcNo
    expect(qtnCalcNo, '見積計算書NO が採番される').toBeTruthy()
    expect(created.qtnDiv, 'QtnDiv=20（決定見積）').toBe('20')
  })

  // ─────────────────── Step3：御見積書（関連 → 確定登録）───────────────────
  let qtnNo = ''
  await test.step('Step3 御見積書 作成 → 確定登録（MasterConfirmFlg=9）', async () => {
    // 案件NO で関連見積計算書 候选を取得（§2.6.1 联动），我们的計算書应在候选里
    const candidates = (await call(
      request, token, 'get',
      `/quotations/calcs?customerCd=${BP_CD}&projectNoParent=${PROJECT_NO}`
    )) as any[]
    expect.soft(
      (candidates ?? []).some(c => c.qtnCalcNo === qtnCalcNo),
      '案件NOで自分の見積計算書が関連候选に出る'
    ).toBeTruthy()

    // 御見積書 作成（関連計算書 + 打印用明细）
    const q = await call(request, token, 'post', '/quotations', {
      baseCd: BASE_CD,
      staffCd: STAFF_CD,
      customerCd: BP_CD,
      customerName: `E2E自動テスト得意先 ${RUN}`,
      projectNoParent: PROJECT_NO,
      calcs: [{ qtnCalcNo }],
      details: [
        { detailNo: 1, itemName1: 'E2E 包装箱', quantity: 1000, unitPrice: 50, unit: 'EA', qtnCalcNo }
      ]
    })
    qtnNo = q.qtnNo
    expect(qtnNo, '御見積書NO が採番される').toBeTruthy()

    // 確定登録（勾选の見積計算書 QtnDiv が 20 でないと拒否される）
    const confirmed = await call(request, token, 'post', `/quotations/${qtnNo}/confirm`, {
      qtnCalcNos: [qtnCalcNo]
    })
    expect(confirmed.masterConfirmFlg, '確定後 MasterConfirmFlg=9').toBe(9)
  })

  // ─────────────────── Step4：製品マスタ（报价引入 → 登録）───────────────────
  let productCd = ''
  await test.step('Step4 製品マスタ 报价引入 → 登録', async () => {
    // 报价NO / 計算書NO による引入（画面の「从报价导入」相当）
    await call(request, token, 'get', `/products/by-quotation/${qtnNo}`)
    await call(request, token, 'get', `/products/by-estimate-calc/${qtnCalcNo}`)

    const product = await call(request, token, 'post', '/products', {
      members: [
        {
          rowNo: 1,
          quotationNo: qtnNo,
          estimateCalcNo: qtnCalcNo,
          projectNoParent: PROJECT_NO,
          customerProductName1: 'E2E 包装箱'
        }
      ],
      basicInfo: {
        customerCd: BP_CD,
        customerName: `E2E自動テスト得意先 ${RUN}`,
        cpItemName1: 'E2E 包装箱',
        customerItemName1: 'E2E 包装箱',
        orderType: '01',
        sheetFlute: 'A',
        paperCdF: 'K5',
        sheetDimW: 400,
        sheetDimF: 300,
        qtyUnit: 'EA'
      }
    })
    productCd = product.productCd
    expect(productCd, '製品CD が採番される').toBeTruthy()
    // EstimateCalcNo あり → 見積WF 承認待ち（Status=0）
    expect(product.status, '見積連携あり製品は Status=0（承認待ち）').toBe(0)
  })

  // ─────────────────── Step5：受注（用 productCd 下单）───────────────────
  let webOrderNo = ''
  await test.step('Step5 受注作成（製品CD 使用 + WMS 出荷指示 Hook）', async () => {
    const order = await call(request, token, 'post', '/orders', {
      customerCd: BP_CD,
      orderType: '01',
      orderDate: '2026-05-30',
      quantity: 1000,
      salesPriceDiv: '1', // 個別単価採用
      details: [
        {
          webOrderDetailNo: 1,
          productCd,
          quantity: 1000,
          salesPriceDiv: '1',
          individualUnitPrice: 100,
          customerDeliveryDate: '2026-06-15'
        }
      ]
    })
    webOrderNo = order.webOrderNo
    expect(webOrderNo, '受注NO が採番される').toBeTruthy()

    // ERP→WMS 接缝（best-effort）：受注 → 出荷指示。新規客户のマスタ依存で揺れ得るため soft 断言
    const ship = await findOutbound(request, token, o => o.webOrderNo === webOrderNo)
    expect.soft(ship, `受注 ${webOrderNo} から出荷指示が自動生成される（Hook）`).not.toBeNull()
  })

  // ─────────────────── Step6：単価訂正（改価 → 審批起票）───────────────────
  await test.step('Step6 単価訂正 batch → ApprovalStatus=1 + POWER EGG WF 起票', async () => {
    const result = await call(request, token, 'put', '/orders/price-correction/batch', {
      items: [
        {
          webOrderNo,
          webOrderDetailNo: 1,
          individualUnitPriceAfter: 120, // 100 → 120：単価変更あり
          specialPriceFlg: '1',
          priceChangeReason: 'E2E 単価改定テスト'
        }
      ]
    })
    expect(result.updatedCount, '1 件更新される').toBe(1)
    expect(result.wfRequestedCount, '単価変更により WF 起票 1 件').toBe(1)
    expect((result.conflictedKeys ?? []).length, '競合なし').toBe(0)

    // 受注明细の承認状況が「承認依頼中(1)」に遷移
    const fresh = await call(request, token, 'get', `/orders/${webOrderNo}`)
    const detail = (fresh.details ?? []).find((d: any) => d.webOrderDetailNo === 1)
    expect(detail?.approvalStatus, '改価後 ApprovalStatus=1（承認依頼中）').toBe(1)
    expect(Number(detail?.individualUnitPrice), '個別単価=120 に更新').toBe(120)
  })
})
