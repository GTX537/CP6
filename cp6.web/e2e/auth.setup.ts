import { test as setup, expect } from '@playwright/test'
import fs from 'node:fs'
import path from 'node:path'

const authFile = 'e2e/.auth/admin.json'

/**
 * 登录一次并保存会话（token + 菜单 + 用户信息）到 storageState。
 * 后续业务用例复用此会话，跳过登录步骤。
 *
 * 账号：admin / 123456
 */
setup('authenticate as admin', async ({ page }) => {
  await page.goto('/login')

  // 第一个文本框 = 用户名；密码框 type=password
  const userInput = page.locator('input').first()
  await userInput.fill('admin')
  await page.locator('input[type="password"]').fill('123456')

  await page.locator('.login-button').click()

  // 登录成功后离开 /login（router.push('/') 有 700ms 过渡）
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })

  // 确认 token 已写入 localStorage
  const token = await page.evaluate(() => localStorage.getItem('token'))
  expect(token, 'login should store a token').toBeTruthy()

  // 确保目录存在再保存会话
  fs.mkdirSync(path.dirname(authFile), { recursive: true })
  await page.context().storageState({ path: authFile })
})
