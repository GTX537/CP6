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
  // el-input 的 v-model 不被 Playwright .fill() 的 value 设置触发 → 用 pressSequentially
  // 逐字符输入(逐次 input 事件)使 Element Plus 表单模型真正更新,否则 el-form 校验视为空值挡提交。
  const userInput = page.locator('input').first()
  await userInput.click()
  await userInput.pressSequentially('admin')
  const pwInput = page.locator('input[type="password"]')
  await pwInput.click()
  await pwInput.pressSequentially('123456')

  await page.locator('.login-button').click()

  // 登录成功后离开 /login（router.push('/') 有 700ms 过渡）
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })

  // token 由后端 Set-Cookie(httpOnly cp6_at)持有,storageState 会捕获该 cookie;
  // 前端仅在 localStorage 存非敏感登录态标志 cp6_authed='1',用它做登录成功 sanity。
  const authed = await page.evaluate(() => localStorage.getItem('cp6_authed'))
  expect(authed, 'login should set cp6_authed flag').toBe('1')

  // 确保目录存在再保存会话
  fs.mkdirSync(path.dirname(authFile), { recursive: true })
  await page.context().storageState({ path: authFile })
})
