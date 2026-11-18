import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import test from 'node:test'
import { auditWorkflows, runWorkflowAudit } from './check-workflows.mjs'

function fixture(t) {
  const temp = path.resolve(os.tmpdir())
  const root = mkdtempSync(path.join(temp, 'cp6-trigger-test-'))
  t.after(() => {
    assert.equal(path.dirname(root), temp)
    rmSync(root, { recursive: true, force: true })
  })
  const git = (...args) => execFileSync('git', ['-c', 'user.name=Fixture',
    '-c', 'user.email=fixture@example.invalid', '-c', 'commit.gpgsign=false', ...args], { cwd: root })
  git('init', '-q')
  mkdirSync(path.join(root, '.github/workflows'), { recursive: true })
  const workflow = (name, on) => writeFileSync(path.join(root, '.github/workflows', name),
    'on: ' + on + '\njobs:\n  check:\n    uses: example/repo/.github/workflows/check.yml@v1\n')
  function commit() {
    git('add', '--', '.')
    git('commit', '-qm', 'fixture')
  }
  return { root, git, workflow, commit }
}

test('报告准确覆盖两侧提交、依赖、增加和删除，默认只打印', t => {
  const { root, git, workflow, commit } = fixture(t)
  workflow('old.yml', 'workflow_dispatch')
  commit()
  git('update-ref', 'refs/remotes/origin/main', 'HEAD')
  assert.equal(auditWorkflows(root).workflow_changes, false)

  rmSync(path.join(root, '.github/workflows/old.yml'))
  workflow('new.yaml', '[workflow_call, workflow_dispatch]')
  writeFileSync(path.join(root, 'input.txt'), 'changed')
  commit()
  const report = auditWorkflows(root)
  assert.equal(report.workflow_changes, true)
  assert.deepEqual(report.changed_workflows, ['.github/workflows/new.yaml', '.github/workflows/old.yml'])
  assert.ok(report.changed_paths.includes('input.txt'))
  assert.deepEqual(report.entries.map(entry => entry.ref), ['HEAD', 'origin/main'])
  assert.equal(report.entries[0].dependencies[0].job, 'check')

  let stdout = ''
  const capture = t.mock.method(process.stdout, 'write', chunk => { stdout += chunk; return true })
  runWorkflowAudit(root, [])
  capture.mock.restore()
  assert.deepEqual(JSON.parse(stdout).changed_workflows, report.changed_workflows)
  assert.equal(existsSync(path.join(root, 'docs/development/evidence')), false)
  runWorkflowAudit(root, ['report.json'])
  assert.equal(JSON.parse(readFileSync(path.join(root, 'report.json'))).base, report.base)
})

test('未提交的工作流、候选分支与受保护分支上的自动事件都阻止推送', t => {
  const { root, git, workflow, commit } = fixture(t)
  workflow('release.yml', '{push: {tags: ["v*.*.*"]}}')
  commit()
  git('update-ref', 'refs/remotes/origin/main', 'HEAD')
  assert.equal(auditWorkflows(root).entries.length, 2)
  workflow('pr.yml', 'pull_request_target')
  assert.throws(() => auditWorkflows(root), /先提交/)
  commit()
  assert.throws(() => auditWorkflows(root), /HEAD:.*pr.yml/)
  git('update-ref', 'refs/remotes/origin/main', 'HEAD')
  workflow('pr.yml', 'workflow_dispatch')
  commit()
  assert.throws(() => auditWorkflows(root), /origin\/main:.*pr.yml/)
})
