import fs from 'node:fs'
import path from 'node:path'
import process from 'node:process'
import { chromium } from '@playwright/test'

const candidates = [
  process.env.SPACE_PERFORMANCE_BROWSER_PATH,
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
].filter(Boolean)
const executablePath = candidates.find((candidate) => fs.existsSync(candidate))
if (!executablePath) {
  console.error('SPACE_PERFORMANCE_BROWSER_UNAVAILABLE: Chrome or Edge was not found.')
  process.exit(2)
}

const targetUrl = process.env.SPACE_PERFORMANCE_URL
  ?? 'http://127.0.0.1:4175/space-performance.html'
const screenshotPath = path.resolve(
  process.env.SPACE_PERFORMANCE_SCREENSHOT
    ?? '../docs/space/reports/e08-s05-performance-browser-hardware.png',
)
const consoleErrors = []
let browser

try {
  browser = await chromium.launch({
    executablePath,
    headless: false,
    args: [
      '--window-position=-32000,-32000',
      '--window-size=1440,900',
      '--disable-background-timer-throttling',
      '--disable-backgrounding-occluded-windows',
      '--disable-renderer-backgrounding',
      '--no-first-run',
      '--no-default-browser-check',
      '--use-angle=d3d11',
    ],
  })
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } })
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text())
  })
  await page.goto(targetUrl, { waitUntil: 'load', timeout: 30_000 })
  await page.waitForFunction(() => {
    const status = document.querySelector('#status')?.getAttribute('data-status')
    return status === 'pass' || status === 'fail' || status === 'error'
  }, undefined, { timeout: 30_000 })

  const evidence = await page.evaluate(() => {
    const canvas = document.querySelector('#space-canvas')
    const gl = canvas?.getContext('webgl2') ?? canvas?.getContext('webgl')
    const extension = gl?.getExtension('WEBGL_debug_renderer_info')
    const renderer = extension
      ? gl.getParameter(extension.UNMASKED_RENDERER_WEBGL)
      : gl?.getParameter(gl.RENDERER)
    const vendor = extension
      ? gl.getParameter(extension.UNMASKED_VENDOR_WEBGL)
      : gl?.getParameter(gl.VENDOR)
    return {
      result: window.__SPACE_PERFORMANCE_RESULT__,
      webgl: {
        renderer,
        vendor,
        version: gl?.getParameter(gl.VERSION),
      },
    }
  })
  await page.screenshot({ path: screenshotPath, fullPage: true })
  const softwareRenderer = /swiftshader|software/i.test(evidence.webgl.renderer ?? '')
  const output = {
    ...evidence,
    executablePath,
    targetUrl,
    screenshotPath,
    softwareRenderer,
    consoleErrors,
  }
  console.info(`SPACE_E08_S05_HARDWARE_RESULT=${JSON.stringify(output)}`)

  if (softwareRenderer) {
    console.error('SPACE_PERFORMANCE_GPU_UNVERIFIED: browser fell back to software rendering.')
    process.exitCode = 2
  } else if (evidence.result?.status !== 'PASS' || consoleErrors.length > 0) {
    process.exitCode = 1
  }
} catch (error) {
  console.error(error)
  process.exitCode = 1
} finally {
  await browser?.close()
}
