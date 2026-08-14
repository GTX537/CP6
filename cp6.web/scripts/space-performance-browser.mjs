import crypto from 'node:crypto'
import { execFileSync } from 'node:child_process'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import process from 'node:process'
import { chromium } from '@playwright/test'
import {
  FORMAL_SPACE_PERFORMANCE_BUDGETS,
  aggregateEvidence,
} from './space-performance-evidence.mjs'

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
const diagnostic = process.env.SPACE_PERFORMANCE_DIAGNOSTIC === '1'
const runCount = Number.parseInt(
  process.env.SPACE_PERFORMANCE_RUNS ?? String(FORMAL_SPACE_PERFORMANCE_BUDGETS.minimumColdRuns),
  10,
)
if (!Number.isInteger(runCount) || runCount < 1) {
  console.error('SPACE_PERFORMANCE_RUNS must be a positive integer.')
  process.exit(2)
}
if (!diagnostic && runCount < FORMAL_SPACE_PERFORMANCE_BUDGETS.minimumColdRuns) {
  console.error(`Formal evidence requires at least ${FORMAL_SPACE_PERFORMANCE_BUDGETS.minimumColdRuns} cold runs.`)
  process.exit(2)
}

const viewport = {
  width: Number.parseInt(process.env.SPACE_PERFORMANCE_VIEWPORT_WIDTH ?? '1920', 10),
  height: Number.parseInt(process.env.SPACE_PERFORMANCE_VIEWPORT_HEIGHT ?? '1080', 10),
}
const screenshotPath = path.resolve(
  process.env.SPACE_PERFORMANCE_SCREENSHOT
    ?? '../artifacts/space-performance/space-viewer-ga.png',
)
const evidencePath = path.resolve(
  process.env.SPACE_PERFORMANCE_EVIDENCE
    ?? '../artifacts/space-performance/space-viewer-ga.json',
)
const requiredGpuPattern = new RegExp(
  process.env.SPACE_PERFORMANCE_REQUIRED_GPU ?? 'Iris.*Xe',
  'i',
)

function commandOutput(command, args, fallback = null) {
  try {
    return execFileSync(command, args, { encoding: 'utf8', windowsHide: true }).trim()
  } catch {
    return fallback
  }
}

function gitIsClean() {
  try {
    execFileSync('git', ['diff', '--quiet'], { stdio: 'ignore', windowsHide: true })
    execFileSync('git', ['diff', '--cached', '--quiet'], { stdio: 'ignore', windowsHide: true })
    return true
  } catch {
    return false
  }
}

function sha256File(filePath) {
  return crypto.createHash('sha256').update(fs.readFileSync(filePath)).digest('hex').toUpperCase()
}

function graphicsAdapters() {
  if (process.platform !== 'win32') return []
  const output = commandOutput('powershell.exe', [
    '-NoProfile',
    '-Command',
    'Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,AdapterRAM | ConvertTo-Json -Compress',
  ], '[]')
  try {
    const parsed = JSON.parse(output)
    return Array.isArray(parsed) ? parsed : [parsed]
  } catch {
    return []
  }
}

async function runOnce(browser, phase, index, captureScreenshot = false) {
  const context = await browser.newContext({
    viewport,
    deviceScaleFactor: 1,
  })
  const page = await context.newPage()
  const consoleErrors = []
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text())
  })
  page.on('pageerror', (error) => consoleErrors.push(error.message))

  try {
    await page.goto(targetUrl, { waitUntil: 'load', timeout: 45_000 })
    await page.waitForFunction(() => {
      const status = document.querySelector('#status')?.getAttribute('data-status')
      return status === 'pass' || status === 'fail' || status === 'error'
    }, undefined, { timeout: 45_000 })

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
    if (captureScreenshot) {
      fs.mkdirSync(path.dirname(screenshotPath), { recursive: true })
      await page.screenshot({ path: screenshotPath, fullPage: true })
    }
    const softwareRenderer = /swiftshader|software/i.test(evidence.webgl.renderer ?? '')
    console.info(
      `SPACE_PERFORMANCE_PROGRESS phase=${phase} run=${index} status=${evidence.result?.status ?? 'ERROR'} renderer=${evidence.webgl.renderer ?? 'unknown'}`,
    )
    return {
      phase,
      index,
      ...evidence,
      softwareRenderer,
      consoleErrors,
    }
  } catch (error) {
    consoleErrors.push(error instanceof Error ? error.message : String(error))
    return {
      phase,
      index,
      result: null,
      webgl: {},
      softwareRenderer: false,
      consoleErrors,
    }
  } finally {
    await context.close()
  }
}

let browser
const startedAtUtc = new Date().toISOString()
const commitSha = commandOutput('git', ['rev-parse', 'HEAD'], 'unknown')
const trackedWorktreeCleanAtStart = gitIsClean()

try {
  browser = await chromium.launch({
    executablePath,
    headless: false,
    args: [
      '--window-position=-32000,-32000',
      `--window-size=${viewport.width},${viewport.height}`,
      '--disable-background-timer-throttling',
      '--disable-backgrounding-occluded-windows',
      '--disable-renderer-backgrounding',
      '--disable-extensions',
      '--no-first-run',
      '--no-default-browser-check',
      '--use-angle=d3d11',
    ],
  })
  const browserVersion = browser.version()
  const warmup = await runOnce(browser, 'warmup', 0)
  const runs = []
  for (let index = 1; index <= runCount; index++) {
    runs.push(await runOnce(browser, 'cold', index, index === runCount))
  }

  const effectiveBudgets = diagnostic
    ? { ...FORMAL_SPACE_PERFORMANCE_BUDGETS, minimumColdRuns: runCount }
    : FORMAL_SPACE_PERFORMANCE_BUDGETS
  const aggregate = aggregateEvidence(runs, effectiveBudgets)
  aggregate.checks.requiredGpu = runs.every((run) => requiredGpuPattern.test(run.webgl?.renderer ?? ''))
  aggregate.checks.warmupCompleted = (
    warmup.result?.status === 'PASS'
    && !warmup.softwareRenderer
    && /WebGL\s*2/i.test(warmup.webgl?.version ?? '')
    && warmup.consoleErrors.length === 0
  )
  aggregate.checks.cleanTrackedWorktree = diagnostic || trackedWorktreeCleanAtStart
  aggregate.status = Object.values(aggregate.checks).every(Boolean) ? 'PASS' : 'FAIL'

  const evidence = {
    schemaVersion: 1,
    classification: diagnostic ? 'DIAGNOSTIC' : 'FORMAL_GA',
    status: aggregate.status,
    startedAtUtc,
    finishedAtUtc: new Date().toISOString(),
    command: 'npm run benchmark:space-browser',
    targetUrl,
    viewport,
    executablePath,
    browserVersion,
    requiredGpuPattern: requiredGpuPattern.source,
    screenshotPath,
    evidencePath,
    git: {
      commitSha,
      trackedWorktreeCleanAtStart,
    },
    environment: {
      platform: process.platform,
      osRelease: os.release(),
      osVersion: os.version(),
      architecture: os.arch(),
      cpu: os.cpus()[0]?.model ?? 'unknown',
      logicalCpuCount: os.cpus().length,
      totalMemoryBytes: os.totalmem(),
      nodeVersion: process.version,
      graphicsAdapters: graphicsAdapters(),
    },
    inputs: {
      datasetVersion: runs[0]?.result?.datasetVersion ?? warmup.result?.datasetVersion ?? null,
      files: [
        'src/space-viewer/performance/standardWarehouse.ts',
        'src/space-viewer/performance/budgets.ts',
        'src/space-viewer/performance/browserBenchmark.ts',
        'scripts/space-performance-evidence.mjs',
        'scripts/space-performance-browser.mjs',
      ].map((relativePath) => ({
        relativePath,
        sha256: sha256File(path.resolve(relativePath)),
      })),
    },
    budgets: effectiveBudgets,
    warmup,
    aggregate,
    runs,
  }
  fs.mkdirSync(path.dirname(evidencePath), { recursive: true })
  fs.writeFileSync(evidencePath, `${JSON.stringify(evidence, null, 2)}\n`, 'utf8')
  const evidenceSha256 = sha256File(evidencePath)
  console.info(`SPACE_PERFORMANCE_RESULT status=${evidence.status} classification=${evidence.classification} runs=${runCount}`)
  console.info(`SPACE_PERFORMANCE_EVIDENCE path=${evidencePath} sha256=${evidenceSha256}`)
  console.info(`SPACE_PERFORMANCE_SUMMARY=${JSON.stringify(aggregate)}`)

  if (evidence.status !== 'PASS') process.exitCode = 1
} catch (error) {
  console.error(error)
  process.exitCode = 1
} finally {
  await browser?.close()
}
