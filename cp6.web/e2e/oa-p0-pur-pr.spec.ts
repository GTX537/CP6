import { test, expect, type Page } from '@playwright/test'

async function ensurePrSequence(page: Page) {
  const listResponse = await page.request.get('/api/pub/seq', {
    params: { page: 1, pageSize: 10, keyword: 'PR' },
  })
  expect(listResponse.ok(), `PR sequence lookup failed with HTTP ${listResponse.status()}`).toBeTruthy()
  const list = await listResponse.json() as { rows: Array<{ bizKey: string }> }
  if (list.rows.some(row => row.bizKey === 'PR')) return

  const addResponse = await page.request.post('/api/pub/seq', {
    data: {
      bizKey: 'PR',
      prefix: 'PR',
      dateFormat: 'yyyyMMdd',
      seqLength: 4,
      resetCycle: 1,
      currentValue: 0,
    },
  })
  expect(addResponse.ok(), `PR sequence setup failed with HTTP ${addResponse.status()}`).toBeTruthy()
}

async function createPr(page: Page, suffix: string) {
  await ensurePrSequence(page)
  const response = await page.request.post('/api/pur/pr', {
    data: {
      requestDate: '2026-07-23',
      remarks: `OA P0 E2E ${suffix}`,
      lines: [{
        itemId: `E2E-${suffix}`,
        qty: 2,
        unitCd: 'EA',
        requiredDate: '2026-08-01',
        estPrice: 100,
        suggestSupplierId: null,
      }],
    },
  })
  expect(response.ok()).toBeTruthy()
  const body = await response.json()
  expect(body.code).toBe(0)
  return body.data.prNo as string
}

async function openAndSubmit(page: Page, prNo: string) {
  await page.goto(`/pur/pr?prNo=${encodeURIComponent(prNo)}`)
  await expect(page).toHaveURL(new RegExp(`/pur/pr\\?prNo=${prNo}`))
  await expect(page.getByText(prNo).first()).toBeVisible()
  await page.getByTestId('approval-submit').click()
  await expect(page.getByTestId('approval-status')).toHaveAttribute('data-approval-status', 'running')
}

async function openFromInbox(page: Page, prNo: string) {
  await page.goto('/oa/inbox')
  await page.getByTestId('inbox-folder-pending').click()
  const detailRoute = `/pur/pr?prNo=${prNo}`
  const marker = page.locator(
    `[data-testid="inbox-review-row"][data-detail-route="${detailRoute}"]`,
  )
  const row = marker.locator('xpath=ancestor::tr')
  await expect(row).toBeVisible()
  await row.click()
  await expect(page).toHaveURL(new RegExp(`/pur/pr\\?prNo=${prNo}`))
}

test('P0_AC_P07/P08/P09 PUR_PR submit → inbox deep link → approve callback', async ({ page }) => {
  const prNo = await createPr(page, `APP-${Date.now()}`)
  await openAndSubmit(page, prNo)
  await openFromInbox(page, prNo)
  await page.getByTestId('approval-approve').click()
  await expect(page.getByTestId('approval-status')).toHaveAttribute('data-approval-status', 'approved')

  const response = await page.request.get(`/api/pur/pr/${prNo}`)
  expect((await response.json()).data.status).toBe(2)
})

test('P0_AC_P10 PUR_PR reject returns business state to Draft and keeps timeline comment', async ({ page }) => {
  const prNo = await createPr(page, `REJ-${Date.now()}`)
  await openAndSubmit(page, prNo)
  await openFromInbox(page, prNo)
  await page.getByTestId('approval-comment').fill('OA P0 rejection evidence')
  await page.getByTestId('approval-reject').click()
  await expect(page.getByTestId('approval-status')).toHaveAttribute('data-approval-status', 'rejected')
  await expect(page.getByText('OA P0 rejection evidence')).toBeVisible()

  const response = await page.request.get(`/api/pur/pr/${prNo}`)
  expect((await response.json()).data.status).toBe(0)
})
