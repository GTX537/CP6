import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import test from 'node:test'

const { parse } = createRequire(new URL('../cp6.web/package.json', import.meta.url))('yaml')
const workflow = parse(readFileSync(new URL('../.github/workflows/client-contract.yml', import.meta.url), 'utf8'))
const steps = workflow.jobs['windows-and-web'].steps.filter(step => step['working-directory'] === 'cp6.web')
const phases = ['ci', 'type-check', 'test', 'build-only']

// 使用实际 YAML 的命令与步骤边界。npm 替身仅记录调用，并由本地原生进程给出退出码。
function runWeb(failure) {
  const calls = []
  for (const step of steps) {
    assert.ok(!step['continue-on-error'] && (!step.if || step.if === 'success()'))
    assert.equal(step.shell ?? 'pwsh', 'pwsh')
    const commands = step.run.trim().split(/\r?\n/)
    for (const command of commands) assert.match(command, /^npm [\w :=.-]+$/)
    const script = [
      "$ErrorActionPreference = 'Stop'",
      'function npm {',
      "  $phase = if ($args[0] -eq 'run') { $args[1] } else { $args[0] }",
      '  Write-Output $phase',
      '  $code = if ($phase -eq $env:CP6_WEB_TEST_FAILURE) { 7 } else { 0 }',
      '  & $env:CP6_WEB_TEST_NODE -e "process.exit($code)"',
      '}',
      ...commands,
      'if (Test-Path -LiteralPath variable:\\LASTEXITCODE) { exit $LASTEXITCODE }',
    ].join('\n')
    const child = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', script], {
      encoding: 'utf8', env: { ...process.env, CP6_WEB_TEST_FAILURE: failure, CP6_WEB_TEST_NODE: process.execPath }
    })
    if (child.error) throw child.error
    assert.notEqual(child.status, null, child.stderr)
    calls.push(...child.stdout.trim().split(/\r?\n/))
    if (child.status !== 0) return { status: child.status, calls }
  }
  return { status: 0, calls }
}

test('四项成功后才能完成 Web 验证', () => {
  assert.deepEqual(runWeb(''), { status: 0, calls: phases })
})

for (const [index, phase] of phases.entries()) {
  test(phase + ' 失败后保留退出码并停止后续命令', () => {
    assert.deepEqual(runWeb(phase), { status: 7, calls: phases.slice(0, index + 1) })
  })
}
