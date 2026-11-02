import { expect } from '@playwright/test'
import path from 'node:path'

export async function runNativeRuntime(page, origin, output, steps) {
  await page.goto(origin + '/oa/flow-admin')
  await page.getByRole('tab', { name: '表单版本启停', exact: true }).click()
  let activation = page.getByRole('region', { name: '准确版本启停' })
  await activation.getByRole('button', { name: '读取表单', exact: true }).click()
  await activation.getByRole('button', { name: 'Browser accepted form · personal', exact: true }).click()
  await activation.getByLabel('发布版本').selectOption({ label: '版本 2 · Browser accepted form' })
  const formVersionId = await activation.getByLabel('发布版本').inputValue()
  await activation.getByLabel('启停原因').fill('Browser chooses exact new FORM publication')
  await activation.getByRole('button', { name: '启用所选版本', exact: true }).click()
  await expect(activation.getByRole('status')).toContainText('确认')
  steps.push('FORM exact new publication activated')

  await page.goto(origin + '/oa/designer/window')
  await page.getByRole('button', { name: '新草稿工作区', exact: true }).click()
  const designer = page.getByRole('region', { name: '流程草稿工作区' })
  await designer.locator('.workbench > aside').getByRole('button', { name: /已发布/ }).click()
  const publication = designer.getByRole('region', { name: '发布与版本' })
  await publication.getByRole('button', { name: '读取历史版本', exact: true }).click()
  await publication.getByRole('button', { name: '从此版本创建下一草稿', exact: true }).click()
  await designer.getByLabel('流程名称', { exact: true }).fill('Browser accepted flow')
  await designer.getByRole('button', { name: '保存草稿', exact: true }).click()
  await publication.getByRole('button', { name: '预览发布', exact: true }).click()
  await expect(publication).toContainText(formVersionId)
  await publication.getByLabel('发布说明').fill('Actual browser pins the selected FORM publication')
  await publication.getByRole('button', { name: '确认发布此版本', exact: true }).click()
  await expect(publication.getByRole('status')).toContainText('发布已确认')
  steps.push('FLOW next draft, save, exact dependency preview and publication')

  await page.goto(origin + '/oa/flow-admin')
  await page.getByRole('tab', { name: '准确版本启停', exact: true }).click()
  activation = page.getByRole('region', { name: '准确版本启停' })
  await activation.getByRole('button', { name: '读取流程', exact: true }).click()
  await activation.getByRole('button', { name: 'Browser accepted flow · native-inbox', exact: true }).click()
  await activation.getByLabel('发布版本').selectOption({ label: '版本 2 · Browser accepted flow' })
  const flowVersionId = await activation.getByLabel('发布版本').inputValue()
  await activation.getByLabel('启停原因').fill('Browser adopts exact bound FLOW publication')
  await activation.getByRole('button', { name: '启用所选版本', exact: true }).click()
  await expect(activation.getByRole('status')).toContainText('确认')
  steps.push('FLOW exact new publication activated with unique FORM binding')

  await page.goto(origin + '/oa/form-catalog')
  await page.getByRole('button', { name: '填写 Browser accepted form', exact: true }).click()
  const editor = page.locator('.personal-editor')
  await expect(editor.getByRole('heading', { name: 'Browser accepted form · v2', exact: true })).toBeVisible({ timeout: 30000 })
  await editor.getByPlaceholder('草稿标题（保存草稿时使用）').fill('Browser submitted record')
  await editor.locator('.field > input').fill('Browser exact submitted value')
  // The server executes the real command. Drop only its completed HTTP response;
  // the page must recover the original receipt instead of creating a second draft.
  let dropped = false
  await page.route('**/api/oa/v2/forms/personal/drafts', async route => {
    const response = await route.fetch()
    expect(response.status()).toBe(200)
    dropped = true
    await route.abort('failed')
  }, { times: 1 })
  await editor.getByRole('button', { name: '保存草稿', exact: true }).click()
  await expect(editor.locator('.pending')).toBeVisible()
  await editor.getByRole('button', { name: '查询原结果', exact: true }).click()
  await expect(editor.locator('.pending')).toHaveCount(0)
  expect(dropped).toBe(true)
  await expect(editor.locator('.field > input')).toHaveValue('Browser exact submitted value')
  steps.push('directory initiation, actual committed draft response loss and original receipt recovery')
  const response = page.waitForResponse(r => /\/api\/oa\/v2\/drafts\/[^/]+\/submit$/.test(new URL(r.url()).pathname) && r.request().method() === 'POST')
  await editor.getByRole('button', { name: '提交', exact: true }).click()
  const submitted = await response
  expect(submitted.status()).toBe(200)
  const submission = (await submitted.json()).data.result
  await expect(editor.getByRole('heading', { name: '表单已提交', exact: true })).toBeVisible()
  await expect(editor.getByRole('status')).toContainText('已发起审批')
  steps.push('saved draft submits through real Owner and creates exact pinned native instance')
  await page.screenshot({ path: path.join(output, 'form-submitted.png'), fullPage: true })
  return { formVersionId, flowVersionId, ...submission }
}
