import { test, expect, type APIRequestContext } from '@playwright/test'

/**
 * CP6 ERP 全画面 機能テスト（実写版 / 真实写入）
 *
 * erp-front-flow.spec.ts（主链 BP→受注→単価訂正）の補完。
 * 本用例は「ERP 各画面の主要 API を一通り叩く」横展開テスト：
 * 主链の次要端点（一覧/CSV/copy/lookup/各種チェック）＋
 * 製品周辺マスタ 3 種（FSC チェックシート / シート単価 / 版型・木型）を
 * 実際に呼んで code=0 と業務的な戻り値を断言する。
 *
 *   共通 Master   /master/bases・staffs・generic-codes・lookup（Popup 基盤）
 *   取引先        check-exists / list / CSV
 *   見積計算書    list / get / copy
 *   御見積書      list / get / calcs 候选
 *   製品          next-seq / list / check-wip / CSV
 *   受注          next-seq / credit-check / lead-time / list / CSV
 *   FSC(PA100)    formats / list / issue（実発番）/ download
 *   シート単価(PA130) list / batch-update（UPSERT 実書き込み）
 *   版型木型(PA140/150) next-seq / create / get / revise(Rev↑) / history / list / CSV
 *
 * 前提：后端(5177) 運行中。前段链路を毎回自前で作るので、単独実行可・反復実行可。
 */

const API = 'http://localhost:5177/api'
const RUN = Date.now().toString().slice(-7)

const BP_CD = `E2EM${RUN}`
const PROJECT_NO = `E2EMP${RUN}`
const BASE_CD = '0001'
const STAFF_CD = 'admin'

/** code===0 を前提に data を返す共通呼び出し（JSON API 用） */
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

/** File（CSV/Excel/帳票）系：JSON ではなくバイナリ。HTTP 200 だけ確認する */
async function callFile(
  request: APIRequestContext,
  token: string,
  method: 'get' | 'post',
  path: string,
  body?: unknown
): Promise<void> {
  const res = await request[method](`${API}${path}`, {
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    ...(body !== undefined ? { data: body } : {})
  })
  expect(res.ok(), `${method.toUpperCase()} ${path} → HTTP 200 期待 / 実際 status=${res.status()}`).toBeTruthy()
}

test('ERP 全画面 機能テスト（次要端点 + 製品周辺マスタ FSC/シート単価/版型木型）', async ({ request }) => {
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

  // ───── 前段链路（後続マスタテスト用の実データを作る）─────
  let qtnCalcNo = ''
  let qtnNo = ''
  await test.step('前提：取引先 → 見積計算書(QtnDiv=20) → 御見積書 確定', async () => {
    await call(request, token, 'post', '/business-partners', {
      bpCd: BP_CD,
      bpName: `E2Eマスタ得意先 ${RUN}`,
      baseCd: BASE_CD,
      customerFlg: true,
      accountsReceivableCd: BP_CD,
      salesStaffCd: STAFF_CD,
      businessStaffCd: STAFF_CD,
      billingFlg: true,
      receiptCd: BP_CD
    })

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

    const q = await call(request, token, 'post', '/quotations', {
      baseCd: BASE_CD,
      staffCd: STAFF_CD,
      customerCd: BP_CD,
      customerName: `E2Eマスタ得意先 ${RUN}`,
      projectNoParent: PROJECT_NO,
      calcs: [{ qtnCalcNo }],
      details: [{ detailNo: 1, itemName1: 'E2E 包装箱', quantity: 1000, unitPrice: 50, unit: 'EA', qtnCalcNo }]
    })
    qtnNo = q.qtnNo
    await call(request, token, 'post', `/quotations/${qtnNo}/confirm`, { qtnCalcNos: [qtnCalcNo] })

    expect(qtnCalcNo && qtnNo, '前段データ（見積計算書NO・御見積書NO）が揃う').toBeTruthy()
  })

  // ─────────────────── 共通 Master（MSBBPACOM）───────────────────
  await test.step('共通 Master：bases / staffs / generic-codes / lookup', async () => {
    const bases = await call(request, token, 'get', '/master/bases')
    expect(Array.isArray(bases), '据点下拉は配列').toBeTruthy()

    const staffs = await call(request, token, 'get', `/master/staffs?baseCd=${BASE_CD}`)
    expect(Array.isArray(staffs), '担当者下拉は配列').toBeTruthy()

    const codes = await call(request, token, 'get', '/master/generic-codes?groupCode=OrderType')
    expect(Array.isArray(codes), '受注区分コード組は配列').toBeTruthy()

    const cust = await call(request, token, 'get', `/master/lookup/customer?keyword=${BP_CD}&page=1&pageSize=20`)
    expect(cust, '得意先ルックアップが結果オブジェクトを返す').toBeTruthy()

    const prod = await call(request, token, 'get', '/master/lookup/product?page=1&pageSize=20')
    expect(prod, '製品ルックアップが結果オブジェクトを返す').toBeTruthy()
  })

  // ─────────────────── 取引先マスタ（PA110/120）───────────────────
  await test.step('取引先：check-exists / list / CSV', async () => {
    const exists = await call(request, token, 'get', `/business-partners/check-exists/${BP_CD}`)
    // {exists:true} か bool を想定。truthy で十分
    expect(JSON.stringify(exists), '作成済 BP は存在判定が真').toContain('true')

    const list = await call(request, token, 'get', `/business-partners/list?bpCd=${BP_CD}&page=1&pageSize=20`)
    expect(list, '取引先一覧が返る').toBeTruthy()

    await callFile(request, token, 'get', `/business-partners/export-csv?bpCd=${BP_CD}`)
  })

  // ─────────────────── 見積計算書（PA010/020）───────────────────
  await test.step('見積計算書：list / get / copy', async () => {
    const list = await call(request, token, 'get', '/estimate-calcs?page=1&pageSize=20')
    expect(list, '見積計算書一覧が返る').toBeTruthy()

    const one = await call(request, token, 'get', `/estimate-calcs/${qtnCalcNo}`)
    expect(one?.qtnCalcNo, '単件取得で自分の計算書が取れる').toBe(qtnCalcNo)

    const copied = await call(request, token, 'post', `/estimate-calcs/${qtnCalcNo}/copy`)
    expect(copied?.qtnCalcNo, 'コピー新規で別NOが採番される').toBeTruthy()
    expect(copied.qtnCalcNo).not.toBe(qtnCalcNo)
  })

  // ─────────────────── 御見積書（PA030/040）───────────────────
  await test.step('御見積書：list / get / calcs 候选', async () => {
    const list = await call(request, token, 'get', '/quotations?page=1&pageSize=20')
    expect(list, '御見積書一覧が返る').toBeTruthy()

    const one = await call(request, token, 'get', `/quotations/${qtnNo}`)
    expect(one?.qtnNo, '単件取得で自分の御見積書が取れる').toBe(qtnNo)

    const cands = (await call(
      request, token, 'get',
      `/quotations/calcs?customerCd=${BP_CD}&projectNoParent=${PROJECT_NO}`
    )) as any[]
    expect(Array.isArray(cands), '関連見積計算書候选は配列').toBeTruthy()
  })

  // ─────────────────── 製品マスタ（PA050/060）───────────────────
  let productCd = ''
  await test.step('製品：next-seq / 登録 / list / check-wip / CSV', async () => {
    const seq = await call(request, token, 'get', '/products/next-seq')
    expect(JSON.stringify(seq), '製品採番が返る').toBeTruthy()

    const product = await call(request, token, 'post', '/products', {
      members: [{ rowNo: 1, quotationNo: qtnNo, estimateCalcNo: qtnCalcNo, projectNoParent: PROJECT_NO, customerProductName1: 'E2E 包装箱' }],
      basicInfo: {
        customerCd: BP_CD,
        customerName: `E2Eマスタ得意先 ${RUN}`,
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

    const list = await call(request, token, 'get', '/products?page=1&pageSize=20')
    expect(list, '製品一覧が返る').toBeTruthy()

    const wip = await call(request, token, 'get', `/products/check-wip/${productCd}`)
    // mcframe7 連携無し → Level=0 を想定（NoOpWipCheckService）
    expect(JSON.stringify(wip), '仕掛チェックが結果を返す').toBeTruthy()

    await callFile(request, token, 'get', '/products/export.csv?page=1&pageSize=20')
  })

  // ─────────────────── 受注（PA070/080）次要端点 ───────────────────
  await test.step('受注：next-seq / credit-check / lead-time / list / CSV', async () => {
    const seq = await call(request, token, 'get', '/orders/next-seq')
    expect(JSON.stringify(seq), '受注採番が返る').toBeTruthy()

    const credit = await call(request, token, 'get', `/orders/credit-check?customerCd=${BP_CD}&amount=100000`)
    expect(credit, '与信チェックが結果を返す').toBeTruthy()

    const lt = await call(request, token, 'post', '/orders/lead-time', {
      shipDateTime: '2026-06-15 10:00',
      leadTimeDays: [1, 2, 3],
      wgCd: ''
    })
    expect(lt, '加工予定日計算が結果を返す').toBeTruthy()

    const list = await call(request, token, 'get', '/orders/list?page=1&pageSize=20')
    expect(list?.rows !== undefined, '受注一覧 rows が返る').toBeTruthy()

    await callFile(request, token, 'get', '/orders/list/export.csv?page=1&pageSize=20')
  })

  // ─────────────────── FSC 製品化チェックシート（PA100）───────────────────
  await test.step('FSC：formats / list / issue（実発番）/ download', async () => {
    const formats = (await call(request, token, 'get', '/fsc-checklists/formats')) as any[]
    expect((formats ?? []).length, '出力フォーマットが 1 件以上（未定義時フォールバック）').toBeGreaterThan(0)
    const formatName: string = formats[0].name

    const list = await call(request, token, 'get', `/fsc-checklists/list?baseCd=${BASE_CD}&page=1&pageSize=20`)
    expect(list, 'FSC 一覧が返る').toBeTruthy()

    const issued = await call(request, token, 'post', '/fsc-checklists/issue', {
      formatName,
      targets: [{ qtnNo, qtnCalcNo, customerCd: BP_CD, staffCd: STAFF_CD }]
    })
    expect(issued?.issuedCount, '自分の見積計算書 1 件で FSC が発番される').toBeGreaterThan(0)
    const fscNo: string = issued.items[0].fscManagementNo
    expect(fscNo, 'FSC 管理NO が採番される').toBeTruthy()

    await callFile(request, token, 'get', `/fsc-checklists/download/${fscNo}`)
  })

  // ─────────────────── シート単価マスタ（PA130）───────────────────
  await test.step('シート単価：list / batch-update（UPSERT 実書き込み）', async () => {
    const baseDate = '2026-05-30'
    const list = await call(request, token, 'get', `/sheet-unit-prices/list?baseDate=${baseDate}&baseCd=${BASE_CD}&importDiv=1`)
    expect(Array.isArray(list), 'シート単価一覧は配列').toBeTruthy()

    // 一意な複合キーで 1 行 UPSERT（Selected=true の行のみ処理される）
    const updated = await call(request, token, 'put', '/sheet-unit-prices/batch-update', {
      importDiv: 1,
      rows: [
        {
          rowNo: 1,
          selected: true,
          revisionDate: `${baseDate}T00:00:00`,
          baseCd: BASE_CD,
          customerCd: BP_CD,
          sheetFlute: 'A',
          paperCdF: `K${RUN.slice(-3)}`,
          printCdF: '',
          embossCdF: '',
          paperCdC: '',
          printCdC: '',
          embossCdC: '',
          paperCdB: '',
          printCdB: '',
          embossCdB: '',
          unitPrice: 12.5
        }
      ]
    })
    expect(updated?.updated, '選択 1 行が UPSERT される').toBe(1)
  })

  // ─────────────────── 版型・木型マスタ（PA140/150）───────────────────
  await test.step('版型木型：next-seq / create / get / revise / history / list / CSV', async () => {
    const seq = await call(request, token, 'get', '/plate-molds/next-seq')
    expect(JSON.stringify(seq), '版型木型採番が返る').toBeTruthy()

    const dto = {
      baseCd: BASE_CD,
      decisionEstimateNo: qtnCalcNo,
      vrsnName: `E2E版型 ${RUN}`,
      customerCd: BP_CD,
      supplierCd: BP_CD,
      representativeProductCd: productCd,
      stDate: '2026-05-30T00:00:00',
      endDate: '2030-12-31T00:00:00',
      mfgQty: 1,
      processCd: 'P10'
    }
    const created = await call(request, token, 'post', '/plate-molds', dto)
    const wdPtnNo: string = created.wdPtnNo
    expect(wdPtnNo, '版型・木型NO が採番される').toBeTruthy()
    expect(created.wdRev, '初版 Rev=1').toBe(1)

    const one = await call(request, token, 'get', `/plate-molds/${wdPtnNo}`)
    expect(one?.wdPtnNo, '単件取得で自分の版型が取れる').toBe(wdPtnNo)

    const revised = await call(request, token, 'put', `/plate-molds/${wdPtnNo}/revise`, {
      ...dto,
      stDate: '2031-01-01T00:00:00',
      endDate: '2035-12-31T00:00:00',
      vrsnName: `E2E版型改定 ${RUN}`
    })
    expect(revised?.wdRev, '改定で Rev=2 に上がる').toBe(2)

    const history = (await call(request, token, 'get', `/plate-molds/${wdPtnNo}/history`)) as any[]
    expect((history ?? []).length, '改定履歴が 2 件以上').toBeGreaterThanOrEqual(2)

    const list = await call(request, token, 'get', '/plate-molds/list?page=1&pageSize=20')
    expect(list?.rows !== undefined, '版型木型一覧 rows が返る').toBeTruthy()

    await callFile(request, token, 'get', '/plate-molds/list/export.csv?page=1&pageSize=20')
  })
})
