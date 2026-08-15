import { readFileSync } from 'node:fs'
import path from 'node:path'
import { expect, test, type Page } from '@playwright/test'

const versionId = '11111111-1111-1111-1111-111111111111'
const floorId = '22222222-2222-2222-2222-222222222222'
const rackId = '33333333-3333-3333-3333-333333333333'
const rackLevelId = '44444444-4444-4444-4444-444444444444'
const columnId = '55555555-5555-5555-5555-555555555555'
const secondColumnId = '66666666-5555-5555-5555-555555555555'
const cadSourceId = 'aaaaaaaa-2222-2222-2222-222222222222'
const cadParseJobId = 'aaaaaaaa-4444-4444-4444-444444444444'
const underlaySourceId = 'bbbbbbbb-2222-2222-2222-222222222222'
const underlayFileId = 'bbbbbbbb-3333-3333-3333-333333333333'
const excelCadMatchJobId = 'cccccccc-2222-2222-2222-222222222222'
const excelCadApplyJobId = 'cccccccc-3333-3333-3333-333333333333'
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

  const checklist = page.locator('[data-test="space-studio-checklist"]')
  await expect(checklist).toHaveJSProperty('open', true)
  await expect(checklist.getByRole('listitem')).toHaveCount(4)
  await expect(checklist.getByRole('listitem').nth(0)).toHaveAttribute(
    'aria-label',
    '导入来源 · 待完成',
  )
  await checklist.getByText('首次建模任务 · 4 步', { exact: true }).click()
  await expect(checklist).toHaveJSProperty('open', false)
  await checklist.getByText('首次建模任务 · 4 步', { exact: true }).click()
  await expect(checklist).toHaveJSProperty('open', true)
  expect(await checklist.locator('summary').evaluate(element =>
    element.getBoundingClientRect().height,
  )).toBeGreaterThanOrEqual(44)

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

  const canvas = page.locator('.canvas .konvajs-content')
  const bounds = await canvas.boundingBox()
  expect(bounds).not.toBeNull()
  await page.mouse.click(
    bounds!.x + 5_400 * 0.05,
    bounds!.y + bounds!.height - 2_600 * 0.05,
  )
  await expect(page.locator('.studio-statusbar')).toContainText('选择 1')

  await page.getByRole('button', { name: '3D', exact: true }).click()
  await expect(page.getByText('2D/3D 清单一致', { exact: true })).toBeVisible()
  await expect(page.getByText('2D 2 / 3D 2', { exact: true })).toBeVisible()
  await expect(page.locator('.studio-statusbar')).toContainText('选择 1')
  await captureEvidence(page, 'space-studio-1440x900.png')
  expect(errors).toEqual([])
})

test('restores the selected projection and 3D camera for the same floor', async ({ page }) => {
  const errors = collectPageErrors(page)
  await installSpaceStudioFixtures(page)
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)

  await page.getByRole('button', { name: '3D', exact: true }).click()
  await expect(page.getByText('2D/3D 清单一致', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '俯视', exact: true }).click()

  const storageKey = `cp6-space-studio-floor-view-v1:${versionId}:${floorId}`
  await expect.poll(() => page.evaluate((key) => {
    const serialized = sessionStorage.getItem(key)
    if (!serialized) return null
    const state = JSON.parse(serialized)
    return {
      schemaVersion: state.schemaVersion,
      projectionMode: state.projectionMode,
      hasCamera: Array.isArray(state.preview3d?.cameraPosition),
      hasTarget: Array.isArray(state.preview3d?.target),
    }
  }, storageKey)).toEqual({
    schemaVersion: 1,
    projectionMode: '3d',
    hasCamera: true,
    hasTarget: true,
  })

  await page.reload()
  await expect(page.getByRole('button', { name: '3D', exact: true })).toHaveClass(/active/)
  await expect(page.locator('[data-test="design-preview-3d-canvas"]')).toBeVisible()
  expect(errors).toEqual([])
})

test('controls and restores the underlay layer for the current floor', async ({ page }) => {
  const errors = collectPageErrors(page)
  await installSpaceStudioFixtures(page, [], { underlayAttached: true })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)

  await page.locator('.studio-modebar button').nth(2).click()
  const visible = page.getByLabel('显示底图', { exact: true })
  const opacity = page.getByLabel('底图透明度', { exact: true })
  const locked = page.getByLabel('锁定底图', { exact: true })
  await expect(visible).toBeChecked()
  await expect(opacity).toHaveValue('55')
  await expect(locked).toBeChecked()

  const underlayCanvas = page.locator('.canvas canvas').first()
  const visibleRaster = await underlayCanvas.evaluate((canvas) =>
    (canvas as HTMLCanvasElement).toDataURL(),
  )
  await visible.uncheck()
  await expect.poll(() => underlayCanvas.evaluate((canvas) =>
    (canvas as HTMLCanvasElement).toDataURL(),
  )).not.toBe(visibleRaster)
  const hiddenRaster = await underlayCanvas.evaluate((canvas) =>
    (canvas as HTMLCanvasElement).toDataURL(),
  )
  await opacity.fill('30')
  await visible.check()
  await expect.poll(() => underlayCanvas.evaluate((canvas) =>
    (canvas as HTMLCanvasElement).toDataURL(),
  )).not.toBe(hiddenRaster)
  await visible.uncheck()
  await locked.uncheck()
  await expect(page.locator('.underlay-layer-controls output')).toHaveText('30%')

  await page.locator('.studio-modebar button').first().click()
  await expect(page.getByRole('button', { name: '重新标定底图', exact: true }))
    .toBeEnabled()

  const storageKey = `cp6-space-studio-floor-view-v1:${versionId}:${floorId}`
  await expect.poll(() => page.evaluate((key) => {
    const serialized = sessionStorage.getItem(key)
    return serialized ? JSON.parse(serialized).underlay : null
  }, storageKey)).toEqual({
    visible: false,
    opacityPercent: 30,
    locked: false,
  })

  await page.reload()
  await page.locator('.studio-modebar button').nth(2).click()
  await expect(page.getByLabel('显示底图', { exact: true })).not.toBeChecked()
  await expect(page.getByLabel('底图透明度', { exact: true })).toHaveValue('30')
  await expect(page.getByLabel('锁定底图', { exact: true })).not.toBeChecked()
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

test('uploads and calibrates an image underlay without internal identifiers', async ({ page }) => {
  const errors = collectPageErrors(page)
  const methods: string[] = []
  const underlayBodies: Array<Record<string, any>> = []
  const underlayAttachBodies: Array<Record<string, any>> = []
  const underlayHistoryBodies: Array<Record<string, any>> = []
  await installSpaceStudioFixtures(page, methods, {
    underlayBodies,
    underlayAttachBodies,
    underlayHistoryBodies,
  })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)

  await page.locator('input[accept^=".pdf,.png"]').setInputFiles({
    name: 'warehouse-floor.png',
    mimeType: 'image/png',
    buffer: underlayPng(),
  })
  await expect(page.getByText('底图：待标定', { exact: true })).toBeVisible()

  await page.getByRole('button', { name: '标定底图', exact: true }).click()
  await expect(page.getByText('两点实距标定', { exact: true })).toBeVisible()
  await page.locator('[data-test="calibration-distance"] input').fill('8640')
  await page.locator('[data-test="calibration-origin-x"] input').fill('1000')
  await page.locator('[data-test="calibration-origin-y"] input').fill('2000')
  await page.locator('[data-test="calibration-rotation"] input').fill('90')
  await page.locator('[data-test="calibration-validation-x"] input').fill('-4300')
  await page.locator('[data-test="calibration-validation-y"] input').fill('2000')
  const stage = page.locator('.canvas .konvajs-content')
  const bounds = await stage.boundingBox()
  expect(bounds).not.toBeNull()
  const renderWidth = Math.min(
    bounds!.width * 0.8,
    bounds!.height * 0.8 * (1440 / 900),
  )
  const renderHeight = renderWidth * (900 / 1440)
  const renderTop = bounds!.height - renderHeight
  await stage.click({
    position: { x: renderWidth * 0.1, y: renderTop + renderHeight * 0.7 },
  })
  await stage.click({
    position: { x: renderWidth * 0.7, y: renderTop + renderHeight * 0.7 },
  })
  await stage.click({
    position: { x: renderWidth * 0.1, y: renderTop + renderHeight * 0.1 },
  })
  await expect(page.locator('.calibration-preview')).toContainText('验证误差: 100.00 mm / 允许 50.00 mm')
  await expect(page.getByText('验证未通过，请重新选点或检查坐标', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '验证并保存', exact: true })).toBeDisabled()

  await page.locator('[data-test="calibration-validation-x"] input').fill('-4400')
  await expect(page.locator('.calibration-preview')).toContainText('原点: (1000, 2000) mm')
  await expect(page.locator('.calibration-preview')).toContainText('旋转: 90.0000°')
  await expect(page.locator('.calibration-preview')).toContainText('验证误差: 0.00 mm / 允许 50.00 mm')
  await page.getByRole('button', { name: '验证并保存', exact: true }).click()

  await expect(page.getByText('底图：已标定', { exact: true })).toBeVisible()
  await page.getByRole('tab', { name: '批量', exact: true }).click()
  const tools = page.locator('[data-test="design-batch-tools"]')
  await tools.getByRole('button', { name: '撤销', exact: true }).click()
  await expect(page.getByText('已撤销：标定底图', { exact: true })).toBeVisible()
  await expect(page.getByText('底图：待标定', { exact: true })).toBeVisible()
  await tools.getByRole('button', { name: '重做', exact: true }).click()
  await expect(page.getByText('已重做：标定底图', { exact: true })).toBeVisible()
  await expect(page.getByText('底图：已标定', { exact: true })).toBeVisible()

  await page.getByRole('button', { name: '移除底图', exact: true }).click()
  await page.getByRole('button', { name: '确认', exact: true }).click()
  await expect(page.getByText('底图：未导入', { exact: true })).toBeVisible()
  await tools.getByRole('button', { name: '撤销', exact: true }).click()
  await expect(page.getByText('已撤销：移除底图', { exact: true })).toBeVisible()
  await expect(page.getByText('底图：已标定', { exact: true })).toBeVisible()
  expect(methods).toEqual(expect.arrayContaining([
    'POST underlay upload',
    'PUT underlay attach',
    'GET underlay content',
    'POST underlay calibration',
    'POST underlay history',
  ]))
  expect(underlayAttachBodies).toHaveLength(2)
  expect(underlayAttachBodies[0]).toMatchObject({
    sourceId: underlaySourceId,
    expectedFloorRevision: 7,
    expectedContentRevision: 7,
    clientInstanceId: expect.any(String),
    leaseId: ownedLease().leaseId,
    commandBatchId: expect.any(String),
  })
  expect(underlayAttachBodies[1]).toMatchObject({
    sourceId: null,
    expectedFloorRevision: 11,
    expectedContentRevision: 11,
  })
  expect(underlayBodies).toHaveLength(1)
  expect(underlayBodies[0]).toMatchObject({
    floorLogicalId: floorId,
    pageNumber: 1,
    pixelWidth: 1440,
    pixelHeight: 900,
    expectedFloorRevision: 8,
    expectedContentRevision: 8,
    clientInstanceId: expect.any(String),
    leaseId: ownedLease().leaseId,
    commandBatchId: expect.any(String),
    point1: { worldX: 1000, worldY: 2000 },
    point2: { worldX: 1000, worldY: 10640 },
    validationPoint: { worldX: -4400, worldY: 2000 },
  })
  expect(underlayHistoryBodies).toHaveLength(3)
  expect(underlayHistoryBodies[0]).toMatchObject({
    schemaVersion: 1,
    direction: 'Undo',
    expectedFloorRevision: 9,
    expectedContentRevision: 9,
    historySha256: 'c'.repeat(64),
  })
  expect(underlayHistoryBodies[1]).toMatchObject({
    direction: 'Redo',
    expectedFloorRevision: 10,
    expectedContentRevision: 10,
  })
  expect(underlayHistoryBodies[2]).toMatchObject({
    direction: 'Undo',
    expectedFloorRevision: 12,
    expectedContentRevision: 12,
  })
  expect(errors).toEqual([])
})

test('reviews and confirms the authoritative CAD plus Excel match in the studio', async ({ page }) => {
  const errors = collectPageErrors(page)
  const methods: string[] = []
  const matchBodies: Array<Record<string, any>> = []
  const matchHistoryBodies: Array<Record<string, any>> = []
  await installSpaceStudioFixtures(page, methods, {
    matchBodies,
    matchHistoryBodies,
  })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(`${studioUrl}?matchJobId=${excelCadMatchJobId}`)

  await expect(page.getByRole('heading', { name: 'Excel–CAD 权威匹配' })).toBeVisible()
  await expect(page.getByText('满足后续确认条件', { exact: true })).toBeVisible()
  const matchRow = page.locator('[data-test="match-row"]')
  await expect(matchRow).toContainText('R-001')
  await matchRow.click()
  await expect(page.locator('.studio-statusbar')).toContainText('选择 1')

  await page.locator('[data-test="confirm-match"]').click()
  await expect(page.locator('[data-test="confirmation-succeeded"]')).toBeVisible()
  expect(methods).toEqual(expect.arrayContaining([
    'GET Excel-CAD match',
    'POST Excel-CAD confirmation',
    'GET Excel-CAD confirmation',
  ]))
  expect(matchBodies).toEqual([{
    confirmed: true,
    artifactId: 'excel-cad-artifact-1',
    artifactPayloadSha256: 'f'.repeat(64),
    expectedContentRevision: 7,
    clientInstanceId: expect.any(String),
    leaseId: ownedLease().leaseId,
    expectedFloorRevision: 7,
  }])

  await page.getByRole('tab', { name: '批量', exact: true }).click()
  const tools = page.locator('[data-test="design-batch-tools"]')
  await tools.getByRole('button', { name: '撤销', exact: true }).click()
  await expect(page.getByText(
    '已撤销：合入 Excel–CAD 匹配（3 项）',
    { exact: true },
  )).toBeVisible()
  await tools.getByRole('button', { name: '重做', exact: true }).click()
  await expect(page.getByText(
    '已重做：合入 Excel–CAD 匹配（3 项）',
    { exact: true },
  )).toBeVisible()
  expect(matchHistoryBodies).toHaveLength(2)
  expect(matchHistoryBodies[0]).toMatchObject({
    schemaVersion: 2,
    direction: 'Undo',
    clientInstanceId: expect.any(String),
    leaseId: ownedLease().leaseId,
    expectedFloorRevision: 8,
    expectedContentRevision: 8,
    historySha256: 'e'.repeat(64),
  })
  expect(matchHistoryBodies[1]).toMatchObject({
    direction: 'Redo',
    expectedFloorRevision: 9,
    expectedContentRevision: 9,
  })
  expect(errors).toEqual([])
})

test('records confirmed CAD changes in the shared undo and redo stack', async ({ page }) => {
  const errors = collectPageErrors(page)
  const cadApplyBodies: Array<Record<string, any>> = []
  const editorBodies: Array<Record<string, any>> = []
  await installSpaceStudioFixtures(page, [], {
    cadReview: true,
    cadApplyHistory: true,
    cadApplyBodies,
    editorBodies,
  })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(`${studioUrl}?cadSourceId=${cadSourceId}&cadParseJobId=${cadParseJobId}`)

  await page.getByRole('button', { name: /^问题/ }).click()
  await expect(page.locator('[data-test="cad-changeset"]')).toBeVisible()
  await page.getByRole('button', { name: '确认并合入 1 项', exact: true }).click()
  await expect(page.getByText('已确认并原子合入 1 项 CAD 变更，可撤销', { exact: true }))
    .toBeVisible()
  expect(cadApplyBodies).toHaveLength(1)
  expect(cadApplyBodies[0]).toMatchObject({
    expectedFloorRevision: 7,
    expectedContentRevision: 7,
    changeIds: ['cad-change-column'],
  })

  await page.getByRole('tab', { name: '批量', exact: true }).click()
  const tools = page.locator('[data-test="design-batch-tools"]')
  await tools.getByRole('button', { name: '撤销', exact: true }).click()
  await expect(page.getByText('已撤销：合入 1 项 CAD 变更', { exact: true })).toBeVisible()
  expect(editorBodies[0]?.commands[0]).toMatchObject({
    type: 'UpdateProperties',
    targetLogicalId: columnId,
    updateProperties: { x: 5200 },
  })

  await tools.getByRole('button', { name: '重做', exact: true }).click()
  await expect(page.getByText('已重做：合入 1 项 CAD 变更', { exact: true })).toBeVisible()
  expect(editorBodies[1]?.commands[0]).toMatchObject({
    type: 'UpdateProperties',
    targetLogicalId: columnId,
    updateProperties: { x: 5500 },
  })
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

test('keyboard navigation locates problems and exposes accessible focus targets', async ({ page }) => {
  const errors = collectPageErrors(page)
  await installSpaceStudioFixtures(page, [], { cadReview: true })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(`${studioUrl}?cadSourceId=${cadSourceId}&cadParseJobId=${cadParseJobId}`)

  await expect(page.locator('.el-loading-mask')).toHaveCount(0)
  await expect(page.locator('.issues-command')).toContainText('(2)')
  await page.locator('.canvas').focus()
  await page.keyboard.press('g')
  const activeIssue = page.locator('[data-test="cad-review-item"].active')
  await expect(activeIssue).toContainText('CAD_BLOCKING_TEST')
  await expect(page.locator('.studio-statusbar')).toContainText('选择 1')

  await page.keyboard.press('g')
  await expect(activeIssue).toContainText('CAD_WARNING_TEST')
  const issueFontSize = await page.locator('.issue-action').first().evaluate((element) =>
    Number.parseFloat(getComputedStyle(element).fontSize),
  )
  expect(issueFontSize).toBeGreaterThanOrEqual(16)
  const issueHeights = await page.locator('[data-test="cad-review-item"]').evaluateAll(
    (elements) => elements.map((element) => element.getBoundingClientRect().height),
  )
  expect(issueHeights.every((height) => height >= 44)).toBe(true)

  const issuesTab = page.locator('#space-studio-tab-issues')
  await issuesTab.focus()
  await issuesTab.press('Home')
  const propertiesTab = page.locator('#space-studio-tab-properties')
  await expect(propertiesTab).toBeFocused()
  await expect(propertiesTab).toHaveAttribute('aria-selected', 'true')
  await propertiesTab.press('ArrowRight')
  const batchTab = page.locator('#space-studio-tab-batch')
  await expect(batchTab).toBeFocused()
  await expect(batchTab).toHaveAttribute('aria-selected', 'true')
  const focusOutline = await batchTab.evaluate((element) => getComputedStyle(element).outlineStyle)
  expect(focusOutline).not.toBe('none')

  await page.keyboard.press('Shift+/')
  await expect(page.locator('.el-message-box')).toContainText('G 定位下一个 Open 问题')
  expect(errors).toEqual([])
})

test('below 1280 switches to read-only 3D with version and issues only', async ({ page }) => {
  const errors = collectPageErrors(page)
  const methods: string[] = []
  await installSpaceStudioFixtures(page, methods, { cadReview: true })
  await page.setViewportSize({ width: 1024, height: 720 })
  await page.goto(`${studioUrl}?cadSourceId=${cadSourceId}&cadParseJobId=${cadParseJobId}`)

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
  await page.keyboard.press('g')
  await expect(page.locator('[data-test="cad-review-item"].active')).toContainText('CAD_BLOCKING_TEST')
  await expect(page.getByRole('button', { name: '3D', exact: true })).toHaveClass(/active/)
  expect(methods).not.toContain('POST lease')
  await captureEvidence(page, 'space-studio-1024x720.png')
  expect(errors).toEqual([])
})

test('creates, updates and explicitly deletes business layout through the leased chain', async ({ page }) => {
  const errors = collectPageErrors(page)
  const methods: string[] = []
  const layoutBodies: Array<Record<string, any>> = []
  await installSpaceStudioFixtures(page, methods, { layoutBodies })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)

  await page.locator('.studio-modebar button').filter({ hasText: '构件' }).click()
  await page.locator('[data-test="zone-code"]').fill('Z-A')
  await page.locator('[data-test="submit-layout"]').click()
  await expect(page.getByText('库区已创建并保存')).toBeVisible()

  await page.getByRole('tab', { name: '巷道', exact: true }).click()
  await page.locator('[data-test="aisle-code"]').fill('A-01')
  await page.locator('[data-test="submit-layout"]').click()
  await expect(page.getByText('巷道已创建并保存')).toBeVisible()

  await page.getByRole('tab', { name: '货架', exact: true }).click()
  await page.locator('[data-test="rack-code"]').fill('R-NEW')
  await expect(page.getByText('将生成 4 个库位', { exact: false })).toBeVisible()
  await page.locator('[data-test="submit-layout"]').click()
  await expect(page.getByText('货架已创建，并生成 4 个设计态库位')).toBeVisible()

  expect(methods.filter((value) => value === 'POST layout')).toHaveLength(3)
  expect(layoutBodies.map((body) => body.commands[0].type)).toEqual([
    'CreateZone',
    'CreateAisle',
    'CreateRack',
  ])
  for (const body of layoutBodies) {
    expect(body.leaseId).toBe(ownedLease().leaseId)
    expect(body.clientInstanceId).toBeTruthy()
  }
  expect(layoutBodies[0]!.expectedFloorRevision).toBe(7)
  expect(layoutBodies[1]!.expectedFloorRevision).toBe(8)
  expect(layoutBodies[2]!.expectedFloorRevision).toBe(9)

  await expect(page.locator('[data-test="layout-properties-panel"]')).toBeVisible()
  await page.locator('[data-test="layout-property-rack-code"]').fill('R-UPDATED')
  await page.locator('[data-test="save-layout-properties"]').click()
  await expect(page.getByText('业务构件修改已保存')).toBeVisible()
  expect(layoutBodies[3]!.commands[0].type).toBe('UpdateRack')
  expect(layoutBodies[3]!.commands[0].updateRack.rackCode).toBe('R-UPDATED')

  await page.locator('[data-test="remove-layout"]').click()
  await expect(page.getByText('确认级联删除货架')).toBeVisible()
  await page.getByRole('button', { name: '确认级联删除', exact: true }).click()
  await expect(page.getByText('货架及其子构件已在 Draft 中标记删除')).toBeVisible()
  expect(layoutBodies[4]!.commands[0]).toMatchObject({
    type: 'DeleteRack',
    deleteObject: { cascade: true },
  })
  expect(methods.filter((value) => value === 'POST layout')).toHaveLength(5)

  await page.getByRole('button', { name: '3D', exact: true }).click()
  await expect(page.getByText('2D/3D 清单一致', { exact: true })).toBeVisible()
  await expect(page.getByText('2D 4 / 3D 4', { exact: true })).toBeVisible()
  expect(errors).toEqual([])
})

test('creates the pallet and six static-equipment presets in the shared 2D/3D scene', async ({ page }) => {
  const errors = collectPageErrors(page)
  const editorBodies: Array<Record<string, any>> = []
  await installSpaceStudioFixtures(page, [], { editorBodies })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)

  await page.locator('.studio-modebar button').filter({ hasText: '构件' }).click()
  for (const label of ['托盘', '输送线', 'AGV', '叉车', '工作台', '电子秤', '充电站']) {
    await page.getByRole('button', { name: `创建${label}`, exact: true }).click()
    await expect(page.getByText(`${label} 已创建，可在属性面板继续调整`, { exact: true }))
      .toBeVisible()
  }

  expect(editorBodies).toHaveLength(7)
  const created = editorBodies.map(body => body.commands[0])
  expect(created.map(command => command.createElement.elementType)).toEqual([
    'Pallet',
    'Conveyor',
    'Device',
    'Device',
    'Workstation',
    'StaticEquipment',
    'StaticEquipment',
  ])
  expect(created.map(command => command.createElement.businessCode.split('-')[0])).toEqual([
    'PAL',
    'CONV',
    'AGV',
    'FORK',
    'BENCH',
    'SCALE',
    'CHG',
  ])
  expect(created.slice(1).map(command => command.createElement.attributes.find(
    (attribute: Record<string, string>) => attribute.key === 'equipmentKind',
  )?.value)).toEqual([
    'Conveyor',
    'Agv',
    'Forklift',
    'Workbench',
    'ElectronicScale',
    'ChargingStation',
  ])
  for (const body of editorBodies) {
    expect(body.leaseId).toBe(ownedLease().leaseId)
    expect(body.clientInstanceId).toBeTruthy()
    expect(body.expectedContentHash).toBeTruthy()
  }

  const tools = page.getByRole('toolbar', { name: 'Space Studio 编辑命令' })
  await tools.getByRole('button', { name: '撤销', exact: true }).click()
  await expect(page.getByText('已撤销：创建充电站', { exact: true })).toBeVisible()
  expect(editorBodies[7]!.commands[0]).toMatchObject({
    type: 'DeleteObject',
    targetLogicalId: created[6]!.targetLogicalId,
  })
  await tools.getByRole('button', { name: '重做', exact: true }).click()
  await expect(page.getByText('已重做：创建充电站', { exact: true })).toBeVisible()
  expect(editorBodies[8]!.commands[0]).toMatchObject({
    type: 'RestoreLogicalObject',
    targetLogicalId: created[6]!.targetLogicalId,
  })

  await page.getByRole('button', { name: '3D', exact: true }).click()
  await expect(page.getByText('2D/3D 清单一致', { exact: true })).toBeVisible()
  await expect(page.getByText('2D 9 / 3D 9', { exact: true })).toBeVisible()
  expect(errors).toEqual([])
})

test('drags selected rack and element through one leased reversible batch', async ({ page }) => {
  const errors = collectPageErrors(page)
  const editorBodies: Array<Record<string, any>> = []
  await installSpaceStudioFixtures(page, [], { editorBodies })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)
  await expect(page.locator('.studio-title-state').getByText('租约至', { exact: false })).toBeVisible()
  await expect(page.locator('.el-loading-mask')).toHaveCount(0)

  const canvas = page.locator('.canvas .konvajs-content')
  const bounds = await canvas.boundingBox()
  expect(bounds).not.toBeNull()
  const rackCenter = {
    x: bounds!.x + (1000 + 2400 / 2) * 0.05,
    y: bounds!.y + bounds!.height - (1200 + 1000 / 2) * 0.05,
  }
  await page.mouse.click(rackCenter.x, rackCenter.y)
  await page.keyboard.down('Control')
  await page.mouse.click(
    bounds!.x + (5200 + 400 / 2) * 0.05,
    bounds!.y + bounds!.height - (2400 + 400 / 2) * 0.05,
  )
  await page.keyboard.up('Control')
  await expect(page.locator('.studio-statusbar')).toContainText('选择 2')
  await page.mouse.move(rackCenter.x, rackCenter.y)
  await page.mouse.down()
  await page.waitForTimeout(50)
  await page.mouse.move(rackCenter.x + 50, rackCenter.y - 25, { steps: 5 })
  await page.waitForTimeout(50)
  await page.mouse.up()

  await expect.poll(() => editorBodies.length).toBe(1)
  await expect(page.getByText('对象位置已保存', { exact: true })).toBeVisible()
  expect(editorBodies[0]).toMatchObject({
    leaseId: ownedLease().leaseId,
    expectedFloorRevision: 7,
    expectedContentRevision: 7,
    expectedContentHash: 'd'.repeat(64),
    commands: [
      {
        type: 'MoveObject',
        targetLogicalId: rackId,
        moveObject: { x: 2000, y: 1700, z: 0 },
      },
      {
        type: 'MoveObject',
        targetLogicalId: columnId,
        moveObject: { x: 6200, y: 2900, z: 0 },
      },
    ],
  })
  expect(editorBodies[0]!.clientInstanceId).toBeTruthy()

  await page.getByRole('tab', { name: '批量', exact: true }).click()
  await page.locator('[data-test="design-batch-tools"]')
    .getByRole('button', { name: '撤销', exact: true })
    .click()
  await expect(page.getByText('已撤销：拖动 2 个对象', { exact: true })).toBeVisible()
  expect(editorBodies).toHaveLength(2)
  expect(editorBodies[1]).toMatchObject({
    leaseId: ownedLease().leaseId,
    expectedFloorRevision: 8,
    expectedContentRevision: 8,
    expectedContentHash: 'd'.repeat(64),
    commands: [
      {
        type: 'MoveObject',
        targetLogicalId: rackId,
        moveObject: { x: 1000, y: 1200, z: 0 },
      },
      {
        type: 'MoveObject',
        targetLogicalId: columnId,
        moveObject: { x: 5200, y: 2400, z: 0 },
      },
    ],
  })
  expect(errors).toEqual([])
})

test('retypes a CAD exception element and restores its semantic type through undo and redo', async ({ page }) => {
  const errors = collectPageErrors(page)
  const editorBodies: Array<Record<string, any>> = []
  await installSpaceStudioFixtures(page, [], { editorBodies })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)
  await expect(page.locator('.studio-title-state').getByText('租约至', { exact: false })).toBeVisible()
  await expect(page.locator('.el-loading-mask')).toHaveCount(0)

  const canvas = page.locator('.canvas .konvajs-content')
  const bounds = await canvas.boundingBox()
  expect(bounds).not.toBeNull()
  await page.mouse.click(
    bounds!.x + (1000 + 2400 / 2) * 0.05,
    bounds!.y + bounds!.height - (1200 + 1000 / 2) * 0.05,
  )
  await expect(page.locator('.studio-statusbar')).toContainText('选择 1')
  await page.mouse.click(
    bounds!.x + (5200 + 400 / 2) * 0.05,
    bounds!.y + bounds!.height - (2400 + 400 / 2) * 0.05,
  )

  const properties = page.locator('[data-test="design-element-properties"]')
  await expect(properties).toBeVisible()
  await properties.locator('[data-test="element-type"]').click()
  await page.getByRole('option', { name: 'Door', exact: true }).click()
  await properties.locator('[data-test="save-element"]').click()

  await expect(page.getByText('元素属性已保存', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(1)
  expect(editorBodies[0]).toMatchObject({
    leaseId: ownedLease().leaseId,
    expectedFloorRevision: 7,
    commands: [{
      type: 'UpdateProperties',
      targetLogicalId: columnId,
      updateProperties: { elementType: 'Door' },
    }],
  })

  await page.getByRole('tab', { name: '批量', exact: true }).click()
  const tools = page.locator('[data-test="design-batch-tools"]')
  await tools.getByRole('button', { name: '撤销', exact: true }).click()
  await expect(page.getByText('已撤销：修改通用元素属性', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(2)
  expect(editorBodies[1]!.commands[0].updateProperties.elementType).toBe('Column')

  await tools.getByRole('button', { name: '重做', exact: true }).click()
  await expect(page.getByText('已重做：修改通用元素属性', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(3)
  expect(editorBodies[2]!.commands[0].updateProperties.elementType).toBe('Door')
  expect(errors).toEqual([])
})

test('locks a saved CAD correction and compensates the lock through undo and redo', async ({ page }) => {
  const errors = collectPageErrors(page)
  const editorBodies: Array<Record<string, any>> = []
  await installSpaceStudioFixtures(page, [], { editorBodies })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)
  await expect(page.locator('.studio-title-state').getByText('租约至', { exact: false })).toBeVisible()
  await expect(page.locator('.el-loading-mask')).toHaveCount(0)

  const canvas = page.locator('.canvas .konvajs-content')
  const bounds = await canvas.boundingBox()
  expect(bounds).not.toBeNull()
  await page.mouse.click(
    bounds!.x + (1000 + 2400 / 2) * 0.05,
    bounds!.y + bounds!.height - (1200 + 1000 / 2) * 0.05,
  )
  await expect(page.locator('.studio-statusbar')).toContainText('选择 1')
  await page.mouse.click(
    bounds!.x + (5200 + 400 / 2) * 0.05,
    bounds!.y + bounds!.height - (2400 + 400 / 2) * 0.05,
  )

  const properties = page.locator('[data-test="design-element-properties"]')
  await expect(properties.locator('[data-test="manual-correction-lock-state"]'))
    .toContainText('尚未锁定')
  await properties.locator('[data-test="lock-manual-correction"]').click()

  await expect(page.getByText('人工校正已保存并锁定', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(1)
  expect(editorBodies[0]).toMatchObject({
    expectedFloorRevision: 7,
    expectedContentRevision: 7,
    commands: [{
      type: 'UpdateProperties',
      targetLogicalId: columnId,
      updateProperties: { manualCorrectionLocked: true },
    }],
  })
  await expect(properties.locator('[data-test="manual-correction-lock-state"]'))
    .toContainText('人工校正已锁定 v1')

  await page.getByRole('tab', { name: '批量', exact: true }).click()
  const tools = page.locator('[data-test="design-batch-tools"]')
  await tools.getByRole('button', { name: '撤销', exact: true }).click()
  await expect(page.getByText('已撤销：锁定人工校正', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(2)
  expect(editorBodies[1]!.commands[0].updateProperties.manualCorrectionLocked)
    .toBe(false)

  await tools.getByRole('button', { name: '重做', exact: true }).click()
  await expect(page.getByText('已重做：锁定人工校正', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(3)
  expect(editorBodies[2]!.commands[0].updateProperties.manualCorrectionLocked)
    .toBe(true)
  expect(errors).toEqual([])
})

test('merges CAD exception elements atomically and restores them through undo and redo', async ({ page }) => {
  const errors = collectPageErrors(page)
  const editorBodies: Array<Record<string, any>> = []
  await installSpaceStudioFixtures(page, [], { editorBodies, mergeEnabled: true })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)
  await expect(page.locator('.studio-title-state').getByText('租约至', { exact: false })).toBeVisible()
  await expect(page.locator('.el-loading-mask')).toHaveCount(0)

  const canvas = page.locator('.canvas .konvajs-content')
  const bounds = await canvas.boundingBox()
  expect(bounds).not.toBeNull()
  await page.mouse.click(
    bounds!.x + (5200 + 400 / 2) * 0.05,
    bounds!.y + bounds!.height - (2400 + 400 / 2) * 0.05,
  )
  await page.keyboard.down('Control')
  await page.mouse.click(
    bounds!.x + (6000 + 400 / 2) * 0.05,
    bounds!.y + bounds!.height - (2400 + 400 / 2) * 0.05,
  )
  await page.keyboard.up('Control')
  await expect(page.locator('.studio-statusbar')).toContainText('选择 2')

  await page.getByRole('tab', { name: '批量', exact: true }).click()
  const tools = page.locator('[data-test="design-batch-tools"]')
  await tools.locator('[data-test="merge-elements"]').click()
  await page.getByRole('button', { name: '确认合并', exact: true }).click()

  await expect(page.getByText('合并 2 个异常对象', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(1)
  expect(editorBodies[0]).toMatchObject({
    leaseId: ownedLease().leaseId,
    expectedFloorRevision: 7,
    commands: [
      { type: 'UpdateProperties', targetLogicalId: columnId },
      { type: 'DeleteObject', targetLogicalId: secondColumnId },
    ],
  })
  const mergedGeometry = JSON.parse(
    editorBodies[0]!.commands[0].updateProperties.geometryJson,
  )
  expect(mergedGeometry).toMatchObject({
    schemaVersion: 1,
    kind: 'group',
    parts: [
      { sourceLogicalId: columnId, x: 0, y: 0 },
      { sourceLogicalId: secondColumnId, x: 800, y: 0 },
    ],
  })

  await tools.getByRole('button', { name: '撤销', exact: true }).click()
  await expect(page.getByText('已撤销：合并 2 个异常对象', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(2)
  expect(editorBodies[1]!.commands.map((command: any) => command.type)).toEqual([
    'UpdateProperties',
    'RestoreLogicalObject',
  ])

  await tools.getByRole('button', { name: '重做', exact: true }).click()
  await expect(page.getByText('已重做：合并 2 个异常对象', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(3)
  expect(editorBodies[2]!.commands.map((command: any) => command.type)).toEqual([
    'UpdateProperties',
    'DeleteObject',
  ])
  expect(errors).toEqual([])
})

test('splits a CAD exception group with inherited properties and stable redo identities', async ({ page }) => {
  const errors = collectPageErrors(page)
  const editorBodies: Array<Record<string, any>> = []
  await installSpaceStudioFixtures(page, [], { editorBodies, splitEnabled: true })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)
  await expect(page.locator('.studio-title-state').getByText('租约至', { exact: false })).toBeVisible()
  await expect(page.locator('.el-loading-mask')).toHaveCount(0)

  const canvas = page.locator('.canvas .konvajs-content')
  const bounds = await canvas.boundingBox()
  expect(bounds).not.toBeNull()
  await page.mouse.click(
    bounds!.x + (5200 + 400 / 2) * 0.05,
    bounds!.y + bounds!.height - (2400 + 400 / 2) * 0.05,
  )
  await expect(page.locator('.studio-statusbar')).toContainText('选择 1')

  await page.getByRole('tab', { name: '批量', exact: true }).click()
  const tools = page.locator('[data-test="design-batch-tools"]')
  await tools.locator('[data-test="split-element"]').click()
  await page.getByRole('button', { name: '确认拆分', exact: true }).click()

  await expect(page.getByText('拆分 2 个异常部件', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(1)
  expect(editorBodies[0]!.commands.map((command: any) => command.type)).toEqual([
    'UpdateProperties',
    'CreateElement',
  ])
  expect(editorBodies[0]!.commands[0]).toMatchObject({
    targetLogicalId: columnId,
    updateProperties: {
      x: 5200,
      y: 2400,
      width: 400,
      businessCode: 'COL-01',
    },
  })
  const created = editorBodies[0]!.commands[1]
  expect(created.targetLogicalId).not.toBe(columnId)
  expect(created.targetLogicalId).not.toBe(secondColumnId)
  expect(created.createElement).toMatchObject({
    elementType: 'Column',
    x: 6000,
    y: 2400,
    width: 400,
    businessCode: 'COL-01',
    attributes: [],
  })
  expect(JSON.parse(created.createElement.geometryJson)).toMatchObject({
    schemaVersion: 1,
    kind: 'box',
  })

  await tools.getByRole('button', { name: '撤销', exact: true }).click()
  await expect(page.getByText('已撤销：拆分 2 个异常部件', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(2)
  expect(editorBodies[1]!.commands.map((command: any) => command.type)).toEqual([
    'UpdateProperties',
    'DeleteObject',
  ])
  expect(editorBodies[1]!.commands[1].targetLogicalId).toBe(created.targetLogicalId)

  await tools.getByRole('button', { name: '重做', exact: true }).click()
  await expect(page.getByText('已重做：拆分 2 个异常部件', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(3)
  expect(editorBodies[2]!.commands.map((command: any) => command.type)).toEqual([
    'UpdateProperties',
    'RestoreLogicalObject',
  ])
  expect(editorBodies[2]!.commands[1].targetLogicalId).toBe(created.targetLogicalId)
  expect(editorBodies.flatMap((body) => body.commands)
    .filter((command: any) => command.type === 'CreateElement')).toHaveLength(1)
  expect(errors).toEqual([])
})

test('redraws a CAD exception on canvas without changing its identity', async ({ page }) => {
  const errors = collectPageErrors(page)
  const editorBodies: Array<Record<string, any>> = []
  await installSpaceStudioFixtures(page, [], { editorBodies })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)
  await expect(page.locator('.studio-title-state').getByText('租约至', { exact: false })).toBeVisible()
  await expect(page.locator('.el-loading-mask')).toHaveCount(0)

  const canvas = page.locator('.canvas .konvajs-content')
  const bounds = await canvas.boundingBox()
  expect(bounds).not.toBeNull()
  const clickWorld = async (x: number, y: number) => page.mouse.click(
    bounds!.x + x * 0.05,
    bounds!.y + bounds!.height - y * 0.05,
  )
  await clickWorld(5_400, 2_600)
  await expect(page.locator('.studio-statusbar')).toContainText('选择 1')

  await page.locator('[data-testid="space-redraw-tool"]').click()
  await expect(page.locator('.studio-title-state')).toContainText('未保存重画')
  await clickWorld(4_800, 2_200)
  await clickWorld(5_600, 2_200)
  await clickWorld(5_700, 2_900)
  await clickWorld(4_900, 3_000)
  await expect(page.locator('.studio-statusbar')).toContainText('重画 4/100 点')
  expect(editorBodies).toHaveLength(0)

  await page.getByRole('button', { name: '3D', exact: true }).click()
  await expect(page.getByRole('button', { name: '3D', exact: true })).toHaveClass(/active/)
  await expect(page.locator('.studio-title-state')).toContainText('未保存重画 · 4 个顶点')
  await expect(page.locator('.studio-statusbar')).toContainText('选择 1')
  await expect(page.locator('[data-testid="space-redraw-complete"]')).toBeDisabled()
  expect(editorBodies).toHaveLength(0)

  await page.getByRole('button', { name: '2D', exact: true }).click()
  await expect(page.locator('.studio-statusbar')).toContainText('重画 4/100 点')
  await expect(page.locator('[data-testid="space-redraw-complete"]')).toBeEnabled()

  await page.locator('[data-testid="space-redraw-complete"]').click()
  await expect(page.getByText('确认重画', { exact: true })).toBeVisible()
  expect(editorBodies).toHaveLength(0)
  await page.getByRole('button', { name: '确认保存', exact: true }).click()

  await expect(page.getByText('重画异常对象', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(1)
  expect(editorBodies[0]).toMatchObject({
    leaseId: ownedLease().leaseId,
    expectedFloorRevision: 7,
    commands: [{
      type: 'UpdateProperties',
      targetLogicalId: columnId,
      updateProperties: {
        elementType: 'Column',
        x: 4_800,
        y: 2_200,
        z: 0,
        rotationZ: 0,
        width: 900,
        height: 3_000,
        depth: 800,
        businessCode: 'COL-01',
      },
    }],
  })
  expect(JSON.parse(
    editorBodies[0]!.commands[0].updateProperties.geometryJson,
  )).toEqual({
    schemaVersion: 1,
    kind: 'polygon',
    outer: [
      { x: 0, y: 0 },
      { x: 800, y: 0 },
      { x: 900, y: 700 },
      { x: 100, y: 800 },
    ],
    holes: [],
    height: 3_000,
  })

  const toolbar = page.locator('.studio-commandbar')
  await toolbar.getByRole('button', { name: '撤销', exact: true }).click()
  await expect(page.getByText('已撤销：重画异常对象', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(2)
  expect(editorBodies[1]!.commands[0]).toMatchObject({
    type: 'UpdateProperties',
    targetLogicalId: columnId,
    updateProperties: {
      geometryJson: '{"schemaVersion":1,"kind":"box","width":400,"height":3000,"depth":400}',
      x: 5_200,
      y: 2_400,
      width: 400,
      depth: 400,
    },
  })

  await toolbar.getByRole('button', { name: '重做', exact: true }).click()
  await expect(page.getByText('已重做：重画异常对象', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(3)
  expect(editorBodies[2]!.commands[0]).toMatchObject({
    type: 'UpdateProperties',
    targetLogicalId: columnId,
  })
  expect(JSON.parse(editorBodies[2]!.commands[0].updateProperties.geometryJson).kind)
    .toBe('polygon')
  expect(editorBodies.flatMap((body) => body.commands)
    .every((command: any) => command.targetLogicalId === columnId)).toBe(true)
  expect(errors).toEqual([])
})

test('copies a Draft element only after confirmation and reuses stable redo identity', async ({ page }) => {
  const errors = collectPageErrors(page)
  const editorBodies: Array<Record<string, any>> = []
  await installSpaceStudioFixtures(page, [], { editorBodies })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)
  await expect(page.locator('.studio-title-state').getByText('租约至', { exact: false })).toBeVisible()
  await expect(page.locator('.el-loading-mask')).toHaveCount(0)

  const canvas = page.locator('.canvas .konvajs-content')
  const bounds = await canvas.boundingBox()
  expect(bounds).not.toBeNull()
  await page.mouse.click(
    bounds!.x + (5_200 + 200) * 0.05,
    bounds!.y + bounds!.height - (2_400 + 200) * 0.05,
  )
  await expect(page.locator('.studio-statusbar')).toContainText('选择 1')

  await page.getByRole('tab', { name: '批量', exact: true }).click()
  const tools = page.locator('[data-test="design-batch-tools"]')
  await tools.locator('[data-test="copy-objects"]').click()
  await expect(page.getByText('确认复制对象', { exact: true })).toBeVisible()
  expect(editorBodies).toHaveLength(0)
  await page.getByRole('button', { name: '确认复制', exact: true }).click()

  await expect(page.getByText('已复制 1 个草稿对象', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(1)
  const created = editorBodies[0]!.commands[0]
  expect(created).toMatchObject({
    type: 'CreateElement',
    createElement: {
      elementType: 'Column',
      x: 6_100,
      y: 2_400,
      z: 0,
      width: 400,
      height: 3_000,
      depth: 400,
      attributes: [],
    },
  })
  expect(created.targetLogicalId).not.toBe(columnId)
  expect(created.createElement).not.toHaveProperty('businessCode')
  expect(created.createElement).not.toHaveProperty('linkedEntityType')
  expect(created.createElement).not.toHaveProperty('sourceId')

  await tools.getByRole('button', { name: '撤销', exact: true }).click()
  await expect(page.getByText('已撤销：复制 1 个对象', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(2)
  expect(editorBodies[1]!.commands).toEqual(expect.arrayContaining([expect.objectContaining({
    type: 'DeleteObject',
    targetLogicalId: created.targetLogicalId,
  })]))

  await tools.getByRole('button', { name: '重做', exact: true }).click()
  await expect(page.getByText('已重做：复制 1 个对象', { exact: true })).toBeVisible()
  await expect.poll(() => editorBodies.length).toBe(3)
  expect(editorBodies[2]!.commands).toEqual(expect.arrayContaining([expect.objectContaining({
    type: 'RestoreLogicalObject',
    targetLogicalId: created.targetLogicalId,
  })]))
  expect(editorBodies.flatMap((body) => body.commands)
    .filter((command: any) => command.type === 'CreateElement')).toHaveLength(1)
  expect(errors).toEqual([])
})

test('previews protected location codes and explicitly applies the frozen proposal', async ({ page }) => {
  const errors = collectPageErrors(page)
  const methods: string[] = []
  const codingBodies: Array<Record<string, any>> = []
  await installSpaceStudioFixtures(page, methods, {
    codingEnabled: true,
    codingBodies,
  })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)

  await page.locator('.inspector-tabs [role="tab"]').nth(1).click()
  await page.locator('[data-test="preview-location-codes"]').click()
  await expect(page.getByText('将修改 1', { exact: true })).toBeVisible()
  await expect(page.getByText('已绑定 WMS', { exact: false })).toBeVisible()
  await expect(page.locator('[data-test="apply-location-codes"]')).toBeDisabled()

  await page.locator('[data-test="confirm-location-codes"]').check()
  await page.locator('[data-test="apply-location-codes"]').click()
  await expect(page.getByText('已原子写入 1 个设计态库位编码')).toBeVisible()

  expect(methods).toContain('POST coding preview')
  expect(methods).toContain('POST coding apply')
  expect(codingBodies).toHaveLength(1)
  expect(codingBodies[0]).toMatchObject({
    leaseId: ownedLease().leaseId,
    mode: 'fill-empty',
    expectedFloorRevision: 7,
    expectedContentRevision: 7,
    proposalHash: 'a'.repeat(64),
  })
  expect(codingBodies[0]!.commandBatchId).toBeTruthy()
  expect(errors).toEqual([])
})

for (const cadSample of [
  {
    format: 'DWG',
    fileName: 'warehouse.dwg',
    mimeType: 'application/vnd.autocad.dwg',
    content: Buffer.from('AC1027-CP6-DWG-FIXTURE'),
  },
  {
    format: 'DXF',
    fileName: 'warehouse.dxf',
    mimeType: 'application/vnd.autocad.dxf',
    content: Buffer.from('0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n'),
  },
]) {
test(`uploads ${cadSample.format} and requires a server preview plus two confirmations before parse start`, async ({ page }) => {
  const errors = collectPageErrors(page)
  const methods: string[] = []
  const cadBodies: Array<Record<string, any>> = []
  await installSpaceStudioFixtures(page, methods, { cadBodies })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(studioUrl)

  await page.locator('input[accept^=".dwg,.dxf"]').setInputFiles({
    name: cadSample.fileName,
    mimeType: cadSample.mimeType,
    buffer: cadSample.content,
  })
  await expect(page.getByRole('heading', { name: '确认楼层、单位、坐标与映射' })).toBeVisible()
  await page.getByLabel('来源单位').selectOption('Millimeter')
  await page.getByLabel('映射 Profile').selectOption('cad-profile-1:1')
  await page.getByRole('button', { name: '生成语义预览' }).click()
  await expect(page.getByText('WALL · H:WALL-1')).toBeVisible()
  await expect(page.getByRole('button', { name: '确认并启动解析' })).toBeDisabled()

  await page.locator('.confirmation input').nth(0).check()
  await page.locator('.confirmation input').nth(1).check()
  await page.getByRole('button', { name: '确认并启动解析' }).click()

  await expect(page.getByText('CAD 解析已启动', { exact: false })).toBeVisible()
  expect(methods).toContain('POST cad upload')
  expect(methods).toContain('POST cad preparation')
  expect(methods).toContain('POST cad parse')
  expect(cadBodies[0]).toMatchObject({
    floorLogicalId: floorId,
    confirmedUnit: 'Millimeter',
    mappingProfileId: 'cad-profile-1',
    mappingProfileVersion: 1,
  })
  expect(cadBodies[1]).toMatchObject({
    preparationId: 'cad-preparation-1',
    coordinateTransformSha256: 'a'.repeat(64),
    mappingDefinitionSha256: 'b'.repeat(64),
    mappingPreviewSha256: 'c'.repeat(64),
  })
  expect(errors).toEqual([])
})
}

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
  options: {
    leaseHeld?: boolean
    layoutBodies?: Array<Record<string, any>>
    editorBodies?: Array<Record<string, any>>
    codingEnabled?: boolean
    codingBodies?: Array<Record<string, any>>
    cadBodies?: Array<Record<string, any>>
    cadReview?: boolean
    cadApplyHistory?: boolean
    cadApplyBodies?: Array<Record<string, any>>
    underlayBodies?: Array<Record<string, any>>
    underlayAttachBodies?: Array<Record<string, any>>
    underlayHistoryBodies?: Array<Record<string, any>>
    matchBodies?: Array<Record<string, any>>
    matchHistoryBodies?: Array<Record<string, any>>
    mergeEnabled?: boolean
    splitEnabled?: boolean
    underlayAttached?: boolean
  } = {},
) {
  await page.addInitScript(() => {
    localStorage.setItem('cp6_authed', '1')
    localStorage.setItem('lang', 'zh-CN')
    localStorage.setItem('nickName', 'Space Modeler')
  })

  const scene: any = sceneFixture()
  if (options.underlayAttached) {
    scene.floor.underlaySourceId = underlaySourceId
    scene.floor.underlayCalibrationId = 'bbbbbbbb-4444-4444-4444-444444444444'
    scene.floor.underlayScale = 10
    scene.floor.underlayOffsetX = 0
    scene.floor.underlayOffsetY = 0
    scene.floor.underlayRotationZ = 0
  }
  if (options.mergeEnabled) {
    scene.elements.push({
      revision: revision(secondColumnId),
      floorLogicalId: floorId,
      elementType: 'Column',
      geometryJson: '{"schemaVersion":1,"kind":"box","width":400,"height":3000,"depth":400}',
      x: 6000,
      y: 2400,
      z: 0,
      rotationZ: 0,
      width: 400,
      height: 3000,
      depth: 400,
      businessCode: 'COL-01',
    })
  }
  if (options.splitEnabled) {
    scene.elements[0].geometryJson = JSON.stringify({
      schemaVersion: 1,
      kind: 'group',
      parts: [
        {
          sourceLogicalId: columnId,
          x: 0,
          y: 0,
          z: 0,
          rotationZ: 0,
          width: 400,
          height: 3000,
          depth: 400,
          geometry: {
            schemaVersion: 1,
            kind: 'box',
            width: 400,
            height: 3000,
            depth: 400,
          },
        },
        {
          sourceLogicalId: secondColumnId,
          x: 800,
          y: 0,
          z: 0,
          rotationZ: 0,
          width: 400,
          height: 3000,
          depth: 400,
          geometry: {
            schemaVersion: 1,
            kind: 'box',
            width: 400,
            height: 3000,
            depth: 400,
          },
        },
      ],
    })
    scene.elements[0].width = 1200
  }
  if (options.codingEnabled) {
    scene.zones.push({
      revision: revision('99999999-1111-1111-1111-111111111111'),
      floorLogicalId: floorId,
      zoneCode: 'Z-A',
      name: '存储区 A',
      zoneType: 1,
      polygonJson: JSON.stringify({
        schemaVersion: 1,
        points: [[0, 0], [5000, 0], [5000, 5000], [0, 5000]],
      }),
    })
    scene.racks[0].zoneLogicalId = '99999999-1111-1111-1111-111111111111'
    scene.locations[0].locationCode = undefined
    scene.locations[1].locationCode = 'WMS-001'
    scene.locations[1].codeOrigin = 'Adopted'
    scene.locations[1].externalBindingState = 'Bound'
  }
  const underlaySnapshots = new Map<string, {
    before: Record<string, any>
    after: Record<string, any>
  }>()
  const underlaySnapshot = () => ({
    sourceId: scene.floor.underlaySourceId,
    calibrationId: scene.floor.underlayCalibrationId,
    scale: scene.floor.underlayScale,
    offsetX: scene.floor.underlayOffsetX,
    offsetY: scene.floor.underlayOffsetY,
    rotationZ: scene.floor.underlayRotationZ,
  })
  const restoreUnderlay = (snapshot: Record<string, any>) => {
    scene.floor.underlaySourceId = snapshot.sourceId
    scene.floor.underlayCalibrationId = snapshot.calibrationId
    scene.floor.underlayScale = snapshot.scale
    scene.floor.underlayOffsetX = snapshot.offsetX
    scene.floor.underlayOffsetY = snapshot.offsetY
    scene.floor.underlayRotationZ = snapshot.rotationZ
  }
  await page.route((url) => url.pathname.startsWith('/api/'), async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const scenePath = `/api/space/design/v1/versions/${versionId}/floors/${floorId}/scene`
    const leasePath = `/api/space/design/v1/versions/${versionId}/floors/${floorId}/lease`
    const layoutPath = `/api/space/design/v1/versions/${versionId}/floors/${floorId}/layout-commands`
    const editorPath = `/api/space/design/v1/versions/${versionId}/floors/${floorId}/commands`
    const codingPreviewPath = `/api/space/design/v1/versions/${versionId}/floors/${floorId}/location-codes:preview`
    const codingApplyPath = `/api/space/design/v1/versions/${versionId}/floors/${floorId}/location-codes:apply`
    const cadUploadPath = `/api/space/design/v1/versions/${versionId}/cad-sources`
    const cadStatusPath = `/api/space/design/v1/versions/${versionId}/sources/${cadSourceId}/cad-preparations/status`
    const cadProfilesPath = `/api/space/design/v1/versions/${versionId}/cad-mapping-profiles`
    const cadPreparationPath = `/api/space/design/v1/versions/${versionId}/sources/${cadSourceId}/cad-preparations:preview`
    const cadParsePath = `/api/space/design/v1/versions/${versionId}/sources/${cadSourceId}/cad-parses`
    const cadCapabilityPath = `/api/space/design/v1/sites/${scene.siteId}/cad-capability`
    const underlayUploadPath = `/api/space/design/v1/versions/${versionId}/underlay-sources`
    const underlayAttachPath = `/api/space/design/v1/versions/${versionId}/floors/${floorId}/underlay`
    const underlayHistoryPath = `${underlayAttachPath}:compensate`
    const underlayContentPath = `/api/space/design/v1/versions/${versionId}/sources/${underlaySourceId}/content`
    const underlayCalibrationPath = `/api/space/design/v1/versions/${versionId}/sources/${underlaySourceId}/underlay-calibration`
    const excelCadMatchPath = `/api/space/design/v1/versions/${versionId}/excel-cad-matches/${excelCadMatchJobId}`

    if (url.pathname === scenePath) {
      await route.fulfill({ json: scene })
      return
    }
    if (url.pathname === underlayUploadPath && request.method() === 'POST') {
      methods.push('POST underlay upload')
      await route.fulfill({
        status: 201,
        json: {
          file: { id: underlayFileId, state: 'Clean' },
          source: { id: underlaySourceId, state: 'Ready' },
        },
      })
      return
    }
    if (url.pathname === underlayAttachPath && request.method() === 'PUT') {
      methods.push('PUT underlay attach')
      const body = request.postDataJSON() as Record<string, any>
      options.underlayAttachBodies?.push(body)
      const before = underlaySnapshot()
      scene.floor.underlaySourceId = body.sourceId ?? undefined
      scene.floor.underlayCalibrationId = undefined
      scene.floor.underlayScale = undefined
      scene.floor.underlayOffsetX = 0
      scene.floor.underlayOffsetY = 0
      scene.floor.underlayRotationZ = 0
      scene.floor.revisionNumber++
      scene.contentRevision++
      underlaySnapshots.set(body.commandBatchId, {
        before,
        after: underlaySnapshot(),
      })
      await route.fulfill({ json: {
        floor: scene.floor,
        versionContentRevision: scene.contentRevision,
        history: {
          schemaVersion: 1,
          originalCommandBatchId: body.commandBatchId,
          operationType: 'UnderlaySet',
          historySha256: 'c'.repeat(64),
        },
        idempotentReplay: false,
      } })
      return
    }
    if (url.pathname === underlayHistoryPath && request.method() === 'POST') {
      methods.push('POST underlay history')
      const body = request.postDataJSON() as Record<string, any>
      options.underlayHistoryBodies?.push(body)
      const snapshots = underlaySnapshots.get(body.originalCommandBatchId)
      if (!snapshots) {
        await route.fulfill({ status: 422, json: { code: 'SPACE_UNDERLAY_HISTORY_INVALID' } })
        return
      }
      restoreUnderlay(body.direction === 'Undo' ? snapshots.before : snapshots.after)
      scene.floor.revisionNumber++
      scene.contentRevision++
      await route.fulfill({ json: {
        schemaVersion: 1,
        originalCommandBatchId: body.originalCommandBatchId,
        commandBatchId: body.commandBatchId,
        direction: body.direction,
        historySha256: body.historySha256,
        floor: scene.floor,
        versionContentRevision: scene.contentRevision,
        idempotentReplay: false,
      } })
      return
    }
    if (url.pathname === underlayContentPath) {
      methods.push('GET underlay content')
      await route.fulfill({
        contentType: 'image/png',
        body: underlayPng(),
      })
      return
    }
    if (url.pathname === underlayCalibrationPath && request.method() === 'POST') {
      methods.push('POST underlay calibration')
      const body = request.postDataJSON() as Record<string, any>
      options.underlayBodies?.push(body)
      const pixelDx = body.point2.pixelX - body.point1.pixelX
      const pixelDy = body.point1.pixelY - body.point2.pixelY
      const worldDx = body.point2.worldX - body.point1.worldX
      const worldDy = body.point2.worldY - body.point1.worldY
      const millimetersPerPixel = Math.hypot(worldDx, worldDy) / Math.hypot(pixelDx, pixelDy)
      const rotationRadians = Math.atan2(worldDy, worldDx) - Math.atan2(pixelDy, pixelDx)
      const rotationZ = ((rotationRadians * 180 / Math.PI) % 360 + 360) % 360
      const cosine = Math.cos(rotationRadians)
      const sine = Math.sin(rotationRadians)
      const localX = body.point1.pixelX
      const localY = body.pixelHeight - body.point1.pixelY
      const offsetX = Math.round(body.point1.worldX - millimetersPerPixel * (
        cosine * localX - sine * localY
      ))
      const offsetY = Math.round(body.point1.worldY - millimetersPerPixel * (
        sine * localX + cosine * localY
      ))
      const validationLocalX = body.validationPoint.pixelX
      const validationLocalY = body.pixelHeight - body.validationPoint.pixelY
      const validationX = offsetX + millimetersPerPixel * (
        cosine * validationLocalX - sine * validationLocalY
      )
      const validationY = offsetY + millimetersPerPixel * (
        sine * validationLocalX + cosine * validationLocalY
      )
      const validationErrorMillimeters = Math.hypot(
        validationX - body.validationPoint.worldX,
        validationY - body.validationPoint.worldY,
      )
      const errorThresholdMillimeters = Math.max(50, Math.hypot(worldDx, worldDy) * 0.002)
      if (validationErrorMillimeters > errorThresholdMillimeters) {
        await route.fulfill({
          status: 422,
          json: { code: 'SPACE_UNDERLAY_CALIBRATION_INVALID' },
        })
        return
      }
      const before = underlaySnapshot()
      scene.floor.revisionNumber++
      scene.contentRevision++
      scene.floor.underlayCalibrationId = 'bbbbbbbb-4444-4444-4444-444444444444'
      scene.floor.underlayScale = millimetersPerPixel
      scene.floor.underlayOffsetX = offsetX
      scene.floor.underlayOffsetY = offsetY
      scene.floor.underlayRotationZ = rotationZ
      underlaySnapshots.set(body.commandBatchId, {
        before,
        after: underlaySnapshot(),
      })
      await route.fulfill({
        json: {
          floor: scene.floor,
          calibration: {
            id: scene.floor.underlayCalibrationId,
            modelVersionId: versionId,
            floorLogicalId: floorId,
            sourceId: underlaySourceId,
            pageNumber: 1,
            pixelWidth: 1440,
            pixelHeight: 900,
            point1: body.point1,
            point2: body.point2,
            validationPoint: body.validationPoint,
            millimetersPerPixel,
            offsetX,
            offsetY,
            rotationZ,
            validationErrorMillimeters,
            errorThresholdMillimeters,
          },
          versionContentRevision: scene.contentRevision,
          history: {
            schemaVersion: 1,
            originalCommandBatchId: body.commandBatchId,
            operationType: 'UnderlayCalibrate',
            historySha256: 'c'.repeat(64),
          },
          idempotentReplay: false,
        },
      })
      return
    }
    if (url.pathname === excelCadMatchPath && request.method() === 'GET') {
      methods.push('GET Excel-CAD match')
      await route.fulfill({ json: excelCadMatchFixture() })
      return
    }
    if (url.pathname === `${excelCadMatchPath}/confirmations` && request.method() === 'POST') {
      methods.push('POST Excel-CAD confirmation')
      options.matchBodies?.push(request.postDataJSON() as Record<string, any>)
      await route.fulfill({
        status: 202,
        json: {
          matchJobId: excelCadMatchJobId,
          applyJobId: excelCadApplyJobId,
          jobStatus: 'Queued',
        },
      })
      return
    }
    if (url.pathname === `${excelCadMatchPath}/confirmations/${excelCadApplyJobId}`) {
      methods.push('GET Excel-CAD confirmation')
      scene.floor.revisionNumber = 8
      scene.contentRevision = 8
      await route.fulfill({
        json: {
          matchJobId: excelCadMatchJobId,
          applyJobId: excelCadApplyJobId,
          commandBatchId: 'cccccccc-4444-4444-4444-444444444444',
          jobStatus: 'Succeeded',
          expectedContentRevision: 7,
          result: {
            schemaVersion: 2,
            historySha256: 'e'.repeat(64),
            historyCommandCount: 3,
          },
        },
      })
      return
    }
    if (url.pathname ===
        `${excelCadMatchPath}/confirmations/${excelCadApplyJobId}:compensate`
      && request.method() === 'POST') {
      methods.push('POST Excel-CAD compensation')
      const body = request.postDataJSON() as Record<string, any>
      options.matchHistoryBodies?.push(body)
      scene.floor.revisionNumber += 1
      scene.contentRevision += 1
      scene.racks[0].revision.lifecycleState =
        body.direction === 'Undo' ? 'Disabled' : 'Active'
      await route.fulfill({
        json: {
          schemaVersion: 2,
          matchJobId: excelCadMatchJobId,
          applyJobId: excelCadApplyJobId,
          commandBatchId: body.commandBatchId,
          direction: body.direction,
          historySha256: body.historySha256,
          historyCommandCount: 3,
          floorRevision: scene.floor.revisionNumber,
          versionContentRevision: scene.contentRevision,
          idempotentReplay: false,
        },
      })
      return
    }
    if (url.pathname === cadUploadPath && request.method() === 'POST') {
      methods.push('POST cad upload')
      await route.fulfill({
        status: 202,
        json: {
          source: { id: cadSourceId, state: 'Scanning', sha256: 'd'.repeat(64) },
          scanJobId: 'aaaaaaaa-3333-3333-3333-333333333333',
        },
      })
      return
    }
    if (url.pathname === cadCapabilityPath) {
      await route.fulfill({
        json: {
          siteId: scene.siteId,
          configurationRevision: 3,
          canPrepareCad: true,
          cadGaReady: true,
          primary: {
            providerKey: 'primary',
            displayName: 'Primary CAD',
            role: 'Primary',
            deploymentMode: 'OnPremisesIsolatedWorker',
            dataBoundary: 'SiteLocal',
            approvalEvidenceReference: 'evidence-primary',
            secretReferenceConfigured: false,
            validFromUtc: '2026-01-01T00:00:00Z',
            expiresAtUtc: '2027-01-01T00:00:00Z',
            supportsDwg: true,
            supportsDxf: true,
            licensingApproved: true,
            securityApproved: true,
            dataRegionApproved: true,
            deletionRetentionApproved: true,
            qualificationScore: 92,
            qualificationRubricVersion: 'cad-ga-v1',
            goldenDatasetSha256: 'd'.repeat(64),
            frozenEnvironmentSha256: 'e'.repeat(64),
            qualificationEvidenceReference: 'evidence-qualification-primary',
            qualified: true,
            runtimeAvailable: true,
            currentlyValid: true,
          },
          backup: {
            providerKey: 'backup',
            displayName: 'Backup CAD',
            role: 'Backup',
            deploymentMode: 'ApprovedCloudService',
            dataBoundary: 'CustomerApprovedCloudRegion',
            approvalEvidenceReference: 'evidence-backup',
            secretReferenceConfigured: true,
            validFromUtc: '2026-01-01T00:00:00Z',
            expiresAtUtc: '2027-01-01T00:00:00Z',
            supportsDwg: true,
            supportsDxf: true,
            licensingApproved: true,
            securityApproved: true,
            dataRegionApproved: true,
            deletionRetentionApproved: true,
            qualificationScore: 86,
            qualificationRubricVersion: 'cad-ga-v1',
            goldenDatasetSha256: 'd'.repeat(64),
            frozenEnvironmentSha256: 'e'.repeat(64),
            qualificationEvidenceReference: 'evidence-qualification-backup',
            qualified: true,
            runtimeAvailable: true,
            currentlyValid: true,
          },
          blockingCodes: [],
          evaluatedAtUtc: '2026-08-14T00:00:00Z',
        },
      })
      return
    }
    if (url.pathname === cadStatusPath) {
      await route.fulfill({
        json: {
          sourceId: cadSourceId,
          sourceState: 'Ready',
          fileState: 'Clean',
          readyForPreparation: true,
        },
      })
      return
    }
    if (url.pathname === cadProfilesPath) {
      await route.fulfill({
        json: [{
          profileId: 'cad-profile-1',
          version: 1,
          name: 'CP6 warehouse',
          scope: 'System',
          definitionSha256: 'b'.repeat(64),
          ruleCount: 8,
        }],
      })
      return
    }
    if (url.pathname === cadPreparationPath && request.method() === 'POST') {
      methods.push('POST cad preparation')
      options.cadBodies?.push(request.postDataJSON() as Record<string, any>)
      await route.fulfill({
        json: {
          preparationId: 'cad-preparation-1',
          expiresAtUtc: '2030-01-01T02:00:00Z',
          baseContentRevision: scene.contentRevision,
          baseContentHash: scene.contentHash,
          readyForParsing: true,
          coordinateAnalysis: {
            suggestedUnit: 'Millimeter',
            suggestedScaleToMillimeters: 1,
            isSuggestedExtentPlausible: true,
            issues: [],
          },
          coordinateMetadata: {
            confirmedUnit: 'Millimeter',
            confirmedScaleToMillimeters: 1,
          },
          inventorySummary: {
            layerCount: 1,
            blockCount: 0,
            entityCount: 1,
            supportedEntityCount: 1,
            unsupportedEntityCount: 0,
          },
          mappingProfile: {
            profileId: 'cad-profile-1',
            version: 1,
            name: 'CP6 warehouse',
            scope: 'System',
            definitionSha256: 'b'.repeat(64),
            ruleCount: 8,
          },
          mappingPreview: {
            summary: {
              mappedLayerCount: 1,
              unmappedLayerCount: 0,
              conflictLayerCount: 0,
              mappedBlockCount: 0,
              unmappedBlockCount: 0,
              blockingCount: 0,
              warningCount: 0,
            },
          },
          semanticPreview: {
            items: [{
              previewObjectId: 'wall-1',
              target: 'Wall',
              confidence: 0.95,
              disposition: 'AutoAccepted',
              isConfirmable: true,
              source: { sourceRef: 'H:WALL-1', layerId: 'WALL' },
            }],
            summary: {
              autoAcceptedCount: 1,
              candidateCount: 0,
              rejectedCount: 0,
              blockingCount: 0,
              warningCount: 0,
            },
          },
          startRequest: {
            preparationId: 'cad-preparation-1',
            floorLogicalId: floorId,
            confirmedUnit: 'Millimeter',
            confirmedScaleToMillimeters: 1,
            coordinateMetadataJson: '{}',
            coordinateTransformSha256: 'a'.repeat(64),
            mappingProfileId: 'cad-profile-1',
            mappingProfileVersion: 1,
            mappingDefinitionSha256: 'b'.repeat(64),
            mappingPreviewSha256: 'c'.repeat(64),
          },
        },
      })
      return
    }
    if (url.pathname === cadParsePath && request.method() === 'POST') {
      methods.push('POST cad parse')
      options.cadBodies?.push(request.postDataJSON() as Record<string, any>)
      await route.fulfill({
        status: 202,
        json: { jobId: 'aaaaaaaa-4444-4444-4444-444444444444', status: 'Queued' },
      })
      return
    }
    if (url.pathname === `${cadParsePath}/${cadParseJobId}`) {
      await route.fulfill({
        json: {
          jobId: cadParseJobId,
          status: options.cadReview ? 'Succeeded' : 'Failed',
          sourceState: 'Ready',
          lastErrorCode: options.cadReview ? undefined : 'TEST_TERMINAL',
          artifacts: [],
        },
      })
      return
    }
    if (url.pathname === `${cadParsePath}/${cadParseJobId}/review-workspace`) {
      await route.fulfill({
        json: cadReviewWorkspaceFixture(options.cadApplyHistory),
      })
      return
    }
    if (url.pathname === `${cadParsePath}/${cadParseJobId}/review-workspace:apply`
      && request.method() === 'POST') {
      const body = request.postDataJSON() as Record<string, any>
      options.cadApplyBodies?.push(body)
      const element = scene.elements.find(
        (item: any) => item.revision.logicalId === columnId,
      )
      const properties = (x: number) => ({
        elementType: element.elementType,
        geometryJson: element.geometryJson,
        x,
        y: element.y,
        z: element.z,
        rotationZ: element.rotationZ,
        width: element.width,
        height: element.height,
        depth: element.depth,
        businessCode: element.businessCode,
        linkedEntityType: element.linkedEntityType,
        linkedLogicalId: element.linkedLogicalId,
        attributes: [],
      })
      element.x = 5500
      scene.floor.revisionNumber += 1
      scene.contentRevision += 1
      await route.fulfill({
        json: {
          commandBatchId: body.commandBatchId,
          floorRevision: scene.floor.revisionNumber,
          versionContentRevision: scene.contentRevision,
          appliedChangeCount: 1,
          workspaceSha256: 'e'.repeat(64),
          idempotentReplay: false,
          undoCommands: [{
            type: 'UpdateProperties',
            targetLogicalId: columnId,
            updateProperties: properties(5200),
          }],
          redoCommands: [{
            type: 'UpdateProperties',
            targetLogicalId: columnId,
            updateProperties: properties(5500),
          }],
        },
      })
      return
    }
    if (url.pathname === layoutPath && request.method() === 'POST') {
      methods.push('POST layout')
      const body = request.postDataJSON() as Record<string, any>
      options.layoutBodies?.push(body)
      const command = body.commands[0]
      const revisionValue = revision(command.targetLogicalId)
      const affectedZones: any[] = []
      const affectedAisles: any[] = []
      const affectedRacks: any[] = []
      const affectedRackLevels: any[] = []
      const affectedLocations: any[] = []
      if (command.type === 'CreateZone') {
        const created = {
          revision: revisionValue,
          floorLogicalId: floorId,
          ...command.createZone,
        }
        scene.zones.push(created)
        affectedZones.push(created)
      }
      if (command.type === 'CreateAisle') {
        const created = { revision: revisionValue, ...command.createAisle }
        scene.aisles.push(created)
        affectedAisles.push(created)
      }
      if (command.type === 'CreateRack') {
        const created = {
          revision: revisionValue,
          floorLogicalId: floorId,
          ...command.createRack,
        }
        scene.racks.push(created)
        affectedRacks.push(created)
        for (const level of command.createRack.levels) {
          const createdLevel = {
            revision: revision(crypto.randomUUID()),
            rackLogicalId: command.targetLogicalId,
            ...level,
          }
          scene.rackLevels.push(createdLevel)
          affectedRackLevels.push(createdLevel)
          for (let column = 1; column <= level.binCount; column += 1) {
            for (let depth = 1; depth <= level.depthCount; depth += 1) {
              affectedLocations.push({
                revision: revision(crypto.randomUUID()),
                rackLogicalId: command.targetLogicalId,
                levelNo: level.levelNo,
                columnNo: column,
                depthNo: depth,
              })
            }
          }
        }
      }
      if (command.type === 'UpdateRack') {
        const updated = scene.racks.find((rack: any) => rack.revision.logicalId === command.targetLogicalId)
        Object.assign(updated, command.updateRack)
        affectedRacks.push(updated)
        scene.rackLevels = scene.rackLevels.filter((level: any) => level.rackLogicalId !== command.targetLogicalId)
        for (const level of command.updateRack.levels) {
          const updatedLevel = {
            revision: revision(crypto.randomUUID()),
            rackLogicalId: command.targetLogicalId,
            ...level,
          }
          scene.rackLevels.push(updatedLevel)
          affectedRackLevels.push(updatedLevel)
        }
      }
      if (command.type === 'DeleteRack') {
        const removed = scene.racks.find((rack: any) => rack.revision.logicalId === command.targetLogicalId)
        removed.revision.lifecycleState = 'RemoveRequested'
        affectedRacks.push(removed)
        for (const level of scene.rackLevels.filter((item: any) => item.rackLogicalId === command.targetLogicalId)) {
          level.revision.lifecycleState = 'RemoveRequested'
          affectedRackLevels.push(level)
        }
      }
      scene.floor.revisionNumber += 1
      scene.contentRevision += 1
      await route.fulfill({
        json: {
          commandBatchId: body.commandBatchId,
          floorRevision: scene.floor.revisionNumber,
          versionContentRevision: scene.contentRevision,
          appliedCommands: [{
            commandId: command.commandId,
            type: command.type,
            targetLogicalId: command.targetLogicalId,
          }],
          affectedZones,
          affectedAisles,
          affectedRacks,
          affectedRackLevels,
          affectedLocations,
          idempotentReplay: false,
        },
      })
      return
    }
    if (url.pathname === editorPath && request.method() === 'POST') {
      const body = request.postDataJSON() as Record<string, any>
      options.editorBodies?.push(body)
      const affectedElements: any[] = []
      const affectedRacks: any[] = []
      for (const command of body.commands) {
        if (command.type === 'UpdateProperties') {
          const element = scene.elements.find(
            (item: any) => item.revision.logicalId === command.targetLogicalId,
          )
          const {
            attributes: _attributes,
            manualCorrectionLocked,
            ...properties
          } = command.updateProperties
          Object.assign(element, properties)
          if (manualCorrectionLocked !== undefined) {
            element.isManualCorrectionLocked = manualCorrectionLocked
            if (manualCorrectionLocked) {
              element.userCorrectionVersion =
                (element.userCorrectionVersion ?? 0) + 1
            }
          } else if (element.isManualCorrectionLocked) {
            element.userCorrectionVersion =
              (element.userCorrectionVersion ?? 0) + 1
          }
          affectedElements.push(element)
          continue
        }
        if (command.type === 'CreateElement') {
          const payload = command.createElement
          const element = {
            revision: revision(command.targetLogicalId),
            floorLogicalId: floorId,
            elementType: payload.elementType,
            geometryJson: payload.geometryJson,
            x: payload.x,
            y: payload.y,
            z: payload.z,
            rotationZ: payload.rotationZ,
            width: payload.width,
            height: payload.height,
            depth: payload.depth,
            businessCode: payload.businessCode,
            parentLogicalId: payload.parentLogicalId,
            linkedEntityType: payload.linkedEntityType,
            linkedLogicalId: payload.linkedLogicalId,
          }
          scene.elements.push(element)
          affectedElements.push(element)
          continue
        }
        if (command.type === 'DeleteObject' || command.type === 'RestoreLogicalObject') {
          const element = scene.elements.find(
            (item: any) => item.revision.logicalId === command.targetLogicalId,
          )
          element.revision.lifecycleState = command.type === 'DeleteObject'
            ? 'RemoveRequested'
            : 'Active'
          affectedElements.push(element)
          continue
        }
        if (command.type !== 'MoveObject') continue
        const rack = scene.racks.find(
          (item: any) => item.revision.logicalId === command.targetLogicalId,
        )
        const element = scene.elements.find(
          (item: any) => item.revision.logicalId === command.targetLogicalId,
        )
        const target = rack ?? element
        Object.assign(target, command.moveObject)
        if (rack) affectedRacks.push(rack)
        if (element) affectedElements.push(element)
      }
      scene.floor.revisionNumber += 1
      scene.contentRevision += 1
      await route.fulfill({
        json: {
          commandBatchId: body.commandBatchId,
          floorRevision: scene.floor.revisionNumber,
          versionContentRevision: scene.contentRevision,
          appliedCommands: body.commands.map((command: any) => ({
            commandId: command.commandId,
            type: command.type,
            targetLogicalId: command.targetLogicalId,
          })),
          affectedElements,
          affectedObjects: affectedElements.map((element) => ({
            targetLogicalId: element.revision.logicalId,
            element,
            attributes: [],
          })),
          affectedRacks,
          affectedRackLevels: [],
          affectedLocations: [],
          idempotentReplay: false,
        },
      })
      return
    }
    if (url.pathname === codingPreviewPath && request.method() === 'POST') {
      methods.push('POST coding preview')
      const body = request.postDataJSON() as Record<string, any>
      await route.fulfill({
        json: {
          schemaVersion: 1,
          modelVersionId: versionId,
          floorLogicalId: floorId,
          mode: body.mode,
          scopeZoneLogicalId: body.scopeZoneLogicalId,
          baseFloorRevision: scene.floor.revisionNumber,
          baseContentRevision: scene.contentRevision,
          proposalHash: 'a'.repeat(64),
          ruleSetHash: 'b'.repeat(64),
          changedCount: 1,
          unchangedCount: 0,
          protectedCount: 1,
          rules: [{
            ruleId: '99999999-2222-2222-2222-222222222222',
            ruleName: '仓库默认编码',
            scopeType: 0,
            ruleHash: 'c'.repeat(64),
          }],
          items: [
            {
              locationLogicalId: scene.locations[0].revision.logicalId,
              rackLogicalId: rackId,
              rackCode: 'R-001',
              columnNo: 1,
              levelNo: 1,
              depthNo: 1,
              proposedCode: 'Z-A-R-001-01-01-01',
              decision: 'modify',
              reason: 'fill-empty',
              ruleId: '99999999-2222-2222-2222-222222222222',
            },
            {
              locationLogicalId: scene.locations[1].revision.logicalId,
              rackLogicalId: rackId,
              rackCode: 'R-001',
              columnNo: 2,
              levelNo: 1,
              depthNo: 1,
              currentCode: 'WMS-001',
              proposedCode: 'WMS-001',
              decision: 'protected',
              reason: 'wms-bound',
              ruleId: '99999999-2222-2222-2222-222222222222',
            },
          ],
        },
      })
      return
    }
    if (url.pathname === codingApplyPath && request.method() === 'POST') {
      methods.push('POST coding apply')
      const body = request.postDataJSON() as Record<string, any>
      options.codingBodies?.push(body)
      scene.locations[0].locationCode = 'Z-A-R-001-01-01-01'
      scene.floor.revisionNumber += 1
      scene.contentRevision += 1
      await route.fulfill({
        json: {
          commandBatchId: body.commandBatchId,
          floorRevision: scene.floor.revisionNumber,
          versionContentRevision: scene.contentRevision,
          proposalHash: body.proposalHash,
          appliedCount: 1,
          appliedItems: [],
          idempotentReplay: false,
        },
      })
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
    contentHash: 'd'.repeat(64),
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
    locations: [{
      revision: revision('aaaaaaaa-1111-1111-1111-111111111111'),
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
      externalBindingState: 'Unbound',
    }, {
      revision: revision('aaaaaaaa-2222-2222-2222-222222222222'),
      floorLogicalId: floorId,
      rackLogicalId: rackId,
      locationCode: 'R-001-L01-C002-D01',
      columnNo: 2,
      levelNo: 1,
      depthNo: 1,
      width: 1200,
      height: 1200,
      depth: 1000,
      codeOrigin: 'Generated',
      externalBindingState: 'Unbound',
    }],
    elements: [{
      revision: {
        ...revision(columnId),
        sourceId: cadSourceId,
        sourceRef: 'H:COLUMN-001',
      },
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
      isManualCorrectionLocked: false,
      userCorrectionVersion: 0,
    }],
    elementAttributes: [],
    locationExternalBindings: [],
    designAttributes: [],
  }
}

function underlayPng(): Buffer {
  return readFileSync(path.resolve(
    process.cwd(),
    '../docs/space/reports/e08-s05-performance-browser-hardware.png',
  ))
}

function excelCadMatchFixture() {
  return {
    jobId: excelCadMatchJobId,
    modelVersionId: versionId,
    jobStatus: 'Succeeded',
    processorVersion: 'space-excel-cad-match-v1',
    excelSourceId: 'cccccccc-5555-5555-5555-555555555555',
    preflightJobId: 'cccccccc-6666-6666-6666-666666666666',
    cadSourceId,
    cadParseJobId,
    floorLogicalId: floorId,
    expectedContentRevision: 7,
    artifactId: 'excel-cad-artifact-1',
    artifactPayloadSha256: 'f'.repeat(64),
    fileSha256: 'e'.repeat(64),
    canConfirm: true,
    summary: {
      excelRackRowCount: 1,
      newCount: 0,
      updateCount: 1,
      unchangedCount: 0,
      unmatchedCount: 0,
      conflictCount: 0,
      errorCount: 0,
      locatableCount: 1,
    },
    totalRowCount: 1,
    returnedRowCount: 1,
    rows: [{
      excelRowId: 'excel-row-1',
      sourceSheet: 'Racks',
      rowNumber: 2,
      values: { rackCode: 'R-001' },
      disposition: 'Update',
      cadPreviewObjectId: 'cad-rack-1',
      editorLogicalId: rackId,
      matchedSourceRef: 'H:RACK-001',
      cadConfidence: 0.98,
      cadConfidenceBand: 'High',
      keyEvidence: [{
        kind: 'EditorRackCode',
        value: 'R-001',
        candidateId: rackId,
      }],
      differenceFields: ['name'],
      errorCodes: [],
      location: {
        kind: 'Entity',
        floorLogicalId: floorId,
        sourceRef: 'H:RACK-001',
        anchor: { x: 2200, y: 1700, z: 0 },
        bounds: { minX: 1000, minY: 1200, maxX: 3400, maxY: 2200 },
        suggestedPaddingMillimeters: 500,
        canFocusCanvas: true,
      },
      matchEvidenceSha256: 'd'.repeat(64),
    }],
  }
}

function cadReviewWorkspaceFixture(includeChanges = false) {
  return {
    schemaVersion: 1,
    isReadOnlyWorkspace: true,
    tenantId: '77777777-8888-9999-aaaa-bbbbbbbbbbbb',
    modelVersionId: versionId,
    floorLogicalId: floorId,
    floorCode: 'F1',
    diagnosticIndexSha256: 'a'.repeat(64),
    editorContentRevision: 7,
    editorSnapshotSha256: 'b'.repeat(64),
    sourceId: cadSourceId,
    cadParseJobId,
    items: [{
      reviewItemId: 'issue-blocking',
      trackingKey: 'blocking:rack',
      kind: 'MappingDiagnostic',
      severity: 'Blocking',
      status: 'Open',
      code: 'CAD_BLOCKING_TEST',
      relatedCodes: [],
      suggestedActionCode: 'REVIEW_RACK',
      sourceRef: 'H:RACK-001',
      targetLogicalId: rackId,
      rackCode: 'R-001',
      location: {
        kind: 'Entity',
        floorLogicalId: floorId,
        sourceRef: 'H:RACK-001',
        bounds: { minX: 800, minY: 1000, maxX: 3600, maxY: 2400 },
        suggestedPaddingMillimeters: 500,
        canFocusCanvas: true,
      },
      upstreamEvidenceSha256: 'c'.repeat(64),
    }, {
      reviewItemId: 'issue-warning',
      trackingKey: 'warning:column',
      kind: 'SemanticDiagnostic',
      severity: 'Warning',
      status: 'Open',
      code: 'CAD_WARNING_TEST',
      relatedCodes: [],
      suggestedActionCode: 'REVIEW_COLUMN',
      sourceRef: 'H:COLUMN-001',
      targetLogicalId: columnId,
      location: {
        kind: 'Entity',
        floorLogicalId: floorId,
        sourceRef: 'H:COLUMN-001',
        anchor: { x: 5200, y: 2400, z: 0 },
        suggestedPaddingMillimeters: 500,
        canFocusCanvas: true,
      },
      upstreamEvidenceSha256: 'd'.repeat(64),
    }],
    summary: {
      totalCount: 2,
      openCount: 2,
      resolvedCount: 0,
      openInfoCount: 0,
      openWarningCount: 1,
      openBlockingCount: 1,
      locatableCount: 2,
      unlocatableCount: 0,
      cadDiagnosticCount: 2,
      proposalReviewCount: 0,
      excelReviewCount: 0,
    },
    workspaceSha256: 'e'.repeat(64),
    ...(includeChanges
      ? {
          changes: [{
            changeId: 'cad-change-column',
            kind: 'Modify',
            logicalId: columnId,
            sourceRef: 'H:COLUMN-001',
            previewObjectId: 'preview-column',
            objectType: 'Column',
            confidence: 0.99,
            isSelected: true,
            canApply: true,
            isManualCorrectionLocked: false,
            userCorrectionVersion: 0,
            beforeBounds: {
              minX: 5200,
              minY: 2400,
              minZ: 0,
              maxX: 5600,
              maxY: 2800,
              maxZ: 3000,
            },
            afterBounds: {
              minX: 5500,
              minY: 2400,
              minZ: 0,
              maxX: 5900,
              maxY: 2800,
              maxZ: 3000,
            },
          }],
          changeSummary: {
            totalCount: 1,
            addCount: 0,
            modifyCount: 1,
            deleteCount: 0,
            conflictCount: 0,
            lowConfidenceCount: 0,
            unrecognizedCount: 0,
            selectedCount: 1,
            applyEligibleCount: 1,
          },
          semanticPreviewSha256: '9'.repeat(64),
          changesetSha256: 'f'.repeat(64),
        }
      : {}),
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
