import { expect, test, type BrowserContext, type Page } from '@playwright/test'

const date = '2026-11-17'
const guid = (value: number) => 'a0000000-0000-4000-8000-' + String(value).padStart(12, '0')
const token = guid(1), basis = guid(2), document = guid(3), plan = guid(4), rate = guid(5), locator = guid(6)
const workOrder = guid(7), costCenter = guid(8), operation = guid(9), policy = guid(10), rateBasis = guid(11)
const version = '9007199254740993'
const body = { total: '272.73000000', currency: 'CNY', targetQuantity: '4.12345678', outputUnit: 'EA', lines: [] }
const allActions = ['fin-cost:view', 'fin-cost:basis-view', 'fin-cost:basis-use', 'fin-cost:collect', 'fin-cost:rate-draft',
  'fin-cost:rate-release', 'fin-cost:basis-submit', 'mes-process-cost-rate:query', 'mes-process-cost-rate:edit']

async function fixture(context: BrowserContext, { commitOnUnknown = true, actions = allActions } = {}) {
  const freezes: unknown[] = []
  const drafts: unknown[] = []
  const releases: unknown[] = []
  const bases: unknown[] = []
  const queried: string[] = []
  let current: string | null = null
  let committed: unknown
  let savedRate = false, released = false
  const expiresAt = new Date(Date.now() + 600_000).toISOString()

  await context.route('**/__manufacturing-cost-fixture', route => route.fulfill({
    contentType: 'text/html',
    body: '<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"></head><body><div id="app"></div><script type="module" src="/e2e/manufacturing-cost-fixture.ts"></script></body></html>',
  }))
  await context.route(url => url.pathname.startsWith('/api/'), async route => {
    const url = new URL(route.request().url()), path = url.pathname
    const send = (data: unknown, status = 200) => route.fulfill({
      contentType: 'application/json', status, body: JSON.stringify(data),
    })
    if (path === '/api/pub/role-perm/my-actions') return send({ code: 0, data: actions })
    if (path === '/api/fin/cost') return send({ code: 0, data: [] })
    if (path === '/api/fin/cost/v1/options') {
      const scope = { environmentId: 'fixture', tenantId: guid(20), legalEntityKey: '模拟法人', ledgerKey: '制造账套', mode: 'REAL' }
      const contexts = [{ token, legalEntityLabel: scope.legalEntityKey, ledgerLabel: scope.ledgerKey }]
      return url.searchParams.has('ledgerKey')
        ? send({ contexts, contextToken: token, scope, businessDate: date, currencies: ['CNY'], availability: 'AVAILABLE' })
        : send({ contexts, contextToken: null, scope: null, currencies: [], availability: 'UNPROVEN' })
    }
    if (route.request().headers()['x-finance-context'] !== token) return send({ message: 'FINANCE_CONTEXT_NOT_FOUND' }, 404)
    const prefix = '/api/fin/cost/owner-v1'
    if (path === prefix + '/work-order-maintenance') return send({
      workOrderId: workOrder, workOrderNo: 'WO-695', productCd: 'PRINT-695', outputUnit: 'EA', completedQuantity: '4.12345678',
      businessDate: date, currency: 'CNY', policyVersionId: policy, sourceIssue: null, basisInputsAvailable: true,
      materials: [{ id: guid(30), materialCd: 'PAPER-01', materialName: '纸张', unit: 'KG' }],
      steps: [{ id: guid(31), processCd: 'PRINT', taskCd: 'P1', wgCd: 'WC-1' }],
      basisVersions: [{ versionId: basis, sequence: '1', state: 'PUBLISHED', revision: 3,
        selected: true, from: date, toExclusive: null, madeByCurrentUser: false },
      ...(bases.length ? [{ versionId: guid(32), sequence: '2', state: 'SUBMITTED', revision: 1,
        selected: false, from: date, toExclusive: null, madeByCurrentUser: true }] : [])],
      document: { id: document, version: current },
    })
    if (path === prefix + '/standard-bases') {
      bases.push(route.request().postDataJSON())
      return send({ versionId: guid(32), sequence: 2, state: 'SUBMITTED', revision: 1 }, 201)
    }
    if (path === prefix + '/standard-bases/' + basis || path === prefix + '/standard-bases/' + guid(32))
      return send({ body, policyVersionId: policy })
    if (path === prefix + '/standard-documents/' + document + '/collection-plans') {
      const input = route.request().postDataJSON()
      expect(input).toMatchObject({ expectedVersion: null, standardBasisVersionId: basis })
      return send({ kind: 'COMMITTED', commandId: input.commandId, receiptId: guid(40), operation: 'PREPARE', documentId: document, planId: plan, expiresAt }, 201)
    }
    if (path === prefix + '/standard-documents/' + document + '/freeze') {
      const input = route.request().postDataJSON()
      expect(input).toMatchObject({ planId: plan, expectedVersion: null })
      freezes.push(input)
      if (commitOnUnknown || freezes.length > 1) {
        current = version
        committed = { kind: 'COMMITTED', commandId: input.commandId, receiptId: guid(41), operation: 'FREEZE',
          documentId: document, version, planId: plan, replayed: false }
      }
      if (freezes.length === 1) return send({ kind: 'OUTCOME_UNKNOWN', commandId: input.commandId }, 202)
      return send(committed)
    }
    if (path.startsWith(prefix + '/standard-document-commands/')) {
      const id = path.split('/').at(-1)!
      queried.push(id)
      return committed ? send({ kind: 'FOUND', reply: committed })
        : send({ kind: 'NOT_OBSERVED', commandId: id, provesPermanentNoEffect: false })
    }
    if (path === prefix + '/standard-documents/' + document || path === prefix + '/standard-documents/' + document + '/versions/' + version)
      return send({ documentId: document, version: current, state: current ? 'READY' : 'DRAFT', body: current ? body : undefined })

    if (path === prefix + '/rate-maintenance') {
      const centerLocator = { rootId: costCenter, versionId: guid(12), opaqueToken: locator }
      return send({ currency: 'CNY', businessDate: date, truncated: false,
        centers: [{ code: '印刷中心', name: '印刷', locator: centerLocator, operationId: operation },
          { code: '其他工序中心', name: '其他', locator: { rootId: guid(22), versionId: guid(23), opaqueToken: guid(24) }, operationId: guid(25) }],
        choices: [{ id: rateBasis, policyVersionId: policy, workCenterId: guid(14), operationId: operation,
          wgCd: 'WC-1', productCd: 'PRINT-695', processCd: 'PRINT', taskCd: 'P1', zeroAllowed: false }],
        versions: [{ rateId: rate, revision: released ? 4 : 3, state: released ? 'RELEASED' : 'APPROVED', from: date, toExclusive: null,
          wgCd: 'WC-1', productCd: 'PRINT-695', processCd: 'PRINT', taskCd: 'P1', laborRate: '100.00000000',
          overheadRate: '45.12345678', madeByCurrentUser: false },
        ...(savedRate ? [{ rateId: guid(16), revision: 1, state: 'DRAFT', from: date, toExclusive: null,
          wgCd: 'WC-1', productCd: 'PRINT-695', processCd: 'PRINT', taskCd: 'P1', laborRate: '12345678901234567890.12345678',
          overheadRate: '1.23456789', madeByCurrentUser: true }] : [])] })
    }
    if (path === prefix + '/rates') {
      drafts.push(route.request().postDataJSON())
      savedRate = true
      return send({ rateId: guid(16), revision: 1, state: 'DRAFT' }, 201)
    }
    if (path === prefix + '/rates/' + rate + '/release-preview') {
      expect(route.request().postDataJSON()).toEqual({ expectedRevision: 3 })
      return send({ token: guid(42), rateId: rate, before: [], after: [{ rateId: rate, from: date, toExclusive: null }] })
    }
    if (path === prefix + '/rates/' + rate + '/publish') {
      releases.push(route.request().postDataJSON())
      released = true
      return send({ rateId: rate, revision: 4, state: 'RELEASED' })
    }
    return send({ message: 'UNEXPECTED_FIXTURE_REQUEST' }, 404)
  })
  return { freezes, drafts, releases, queried, bases }
}

async function openWorkspace(page: Page) {
  await page.goto('/__manufacturing-cost-fixture')
  await page.getByRole('button', { name: '制造成本维护' }).click()
  await page.getByText('请选择法人和账套', { exact: true }).click()
  await page.getByRole('option', { name: '模拟法人 / 制造账套' }).click()
}

async function openOrder(page: Page) {
  await page.getByLabel('工单号', { exact: true }).fill('WO-695')
  await page.getByRole('button', { name: '查询工单' }).click()
  await expect(page.getByText('272.73000000 CNY')).toBeVisible()
}

async function freeze(page: Page) {
  await openOrder(page)
  await page.getByLabel('成本单操作原因').fill('批准整单标准成本')
  await page.getByRole('button', { name: '生成归集计划' }).click()
  await page.getByRole('button', { name: '确认冻结' }).click()
  await expect(page.getByRole('button', { name: '回查原命令' })).toBeVisible()
}

test('recovers the original frozen document after reload and reads the byte-exact historical version', async ({ context, page }) => {
  const records = await fixture(context)
  await openWorkspace(page)
  await freeze(page)
  await openWorkspace(page)
  await page.getByRole('button', { name: '回查原命令' }).click()
  await expect(page.getByText('已取得原命令回执。')).toBeVisible()
  expect(records.freezes).toHaveLength(1)
  expect(records.queried).toEqual([(records.freezes[0] as { commandId: string }).commandId])
  await openOrder(page)
  await page.getByLabel('成本单历史版本').fill(version)
  await page.getByRole('button', { name: '查看历史版本' }).click()
  await expect(page.getByRole('heading', { name: '标准成本单第 ' + version + ' 版' })).toBeVisible()
})

test('resends precisely the original command only after a not-observed response and explicit retry', async ({ context, page }) => {
  const records = await fixture(context, { commitOnUnknown: false })
  await openWorkspace(page)
  await freeze(page)
  await expect(page.getByRole('button', { name: '按原命令重试' })).toHaveCount(0)
  await page.getByRole('button', { name: '回查原命令' }).click()
  await page.getByRole('button', { name: '按原命令重试' }).click()
  await expect(page.getByRole('heading', { name: '标准成本单第 ' + version + ' 版' })).toBeVisible()
  expect(records.freezes).toHaveLength(2)
  expect(records.freezes[1]).toEqual(records.freezes[0])
})

test('registers a successor using the selected work order inputs and exact quantities', async ({ context, page }) => {
  const records = await fixture(context)
  await openWorkspace(page)
  await openOrder(page)
  await page.getByRole('button', { name: '登记标准依据' }).click()
  const dialog = page.getByRole('dialog', { name: '登记标准依据' })
  await expect(dialog.getByLabel('整单目标数量')).toHaveValue('4.12345678')
  await dialog.getByLabel('标准用量 PAPER-01').fill('1.00000001')
  await dialog.getByLabel('人工工时 PRINT').fill('2.00000002')
  await dialog.getByLabel('机时 PRINT').fill('3.00000003')
  await dialog.getByLabel('登记原因').fill('核准下期标准耗用')
  await dialog.getByRole('button', { name: '提交依据' }).click()
  await expect(page.getByRole('heading', { name: '第 2 版标准依据' })).toBeVisible()
  expect(records.bases).toHaveLength(1)
  expect(records.bases[0]).toMatchObject({ workOrderId: workOrder, previousVersionId: basis, targetQuantity: '4.12345678',
    policyVersionId: policy, outputUnit: 'EA',
    materials: [{ materialOccurrenceId: guid(30), allowedQuantity: '1.00000001', unit: 'KG' }],
    times: [{ workStepId: guid(31), laborHours: '2.00000002', machineHours: '3.00000003' }] })
})

test('scopes center choices to the operation, preserves decimal rates and publishes the selected preview', async ({ context, page }) => {
  const records = await fixture(context)
  await openWorkspace(page)
  await page.getByRole('tab', { name: '工序费率' }).click()
  await page.getByRole('button', { name: '新建费率版本' }).click()
  const dialog = page.getByRole('dialog', { name: '新建费率版本' })
  await dialog.getByRole('combobox', { name: '工作中心、工序和批准依据' }).press('ArrowDown')
  await page.getByRole('option', { name: /WC-1.*PRINT-695/ }).click()
  await dialog.getByRole('combobox', { name: '成本中心版本' }).press('ArrowDown')
  await expect(page.getByRole('option', { name: /其他工序中心/ })).toHaveCount(0)
  await page.getByRole('option', { name: '印刷中心 · 印刷' }).click()
  await dialog.getByLabel('人工费率 / 人时').fill('12345678901234567890.12345678')
  await dialog.getByLabel('制造费率 / 机时').fill('1.23456789')
  await dialog.getByRole('button', { name: '保存草稿' }).click()
  await expect(page.getByText('已保存费率草稿。')).toBeVisible()
  expect(records.drafts[0]).toMatchObject({ laborRate: '12345678901234567890.12345678',
    overheadRate: '1.23456789', costCenter: { versionId: guid(12), opaqueToken: locator } })
  await page.getByRole('button', { name: '发布预览' }).click()
  const preview = page.getByRole('dialog', { name: '核对费率发布前后的生效区间' })
  await expect(preview.getByText('本次版本')).toBeVisible()
  await preview.getByRole('button', { name: '确认发布' }).click()
  await expect(page.getByText('费率版本已发布。')).toBeVisible()
  expect(records.releases).toEqual([{ previewToken: guid(42) }])
})

test('readable versions stay visible while ungranted document and rate actions are absent', async ({ context, page }) => {
  const records = await fixture(context, { actions: ['fin-cost:view', 'fin-cost:basis-view', 'mes-process-cost-rate:query'] })
  await openWorkspace(page)
  await openOrder(page)
  await expect(page.getByRole('button', { name: '生成归集计划' })).toHaveCount(0)
  await page.getByRole('tab', { name: '工序费率' }).click()
  await expect(page.getByText('45.12345678')).toBeVisible()
  await expect(page.getByRole('button', { name: '新建费率版本' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: '发布预览' })).toHaveCount(0)
  expect(records.freezes).toHaveLength(0)
  expect(records.releases).toHaveLength(0)
})
