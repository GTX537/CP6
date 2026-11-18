import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const repository = fileURLToPath(new URL('../', import.meta.url))
const { parse } = createRequire(new URL('../cp6.web/package.json', import.meta.url))('yaml')
const workflowFile = /^\.github\/workflows\/[^/]+\.ya?ml$/
const splitPaths = output => output.split('\0').filter(Boolean)

function inspectWorkflow(source, file) {
  const workflow = parse(source)
  const events = workflow?.on
  let names
  if (Array.isArray(events)) names = events
  else if (typeof events === 'string') names = [events]
  else names = Object.keys(events ?? {})
  const safe = names.length > 0 && names.every(name => {
    if (name === 'workflow_dispatch' || name === 'workflow_call') return true
    const push = events?.push
    return name === 'push' && Array.isArray(push?.tags) && push.tags.length > 0 &&
      !Object.hasOwn(push, 'branches') && !Object.hasOwn(push, 'branches-ignore')
  })
  if (!safe) throw new Error('工作流存在普通推送、PR 或其他未经确认的自动触发事件：' + file)

  const dependencies = []
  for (const [job, definition] of Object.entries(workflow.jobs)) {
    if (definition.uses) dependencies.push({ job, uses: definition.uses })
    for (const step of definition.steps ?? []) {
      if (step.uses) dependencies.push({ job, uses: step.uses })
    }
  }
  return { sha256: createHash('sha256').update(source).digest('hex'), events, dependencies,
    branch_push_and_pr_safe: true }
}

export function auditWorkflows(root) {
  const git = (...args) => execFileSync('git', ['-c', 'core.longpaths=true', ...args], {
    cwd: root, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024
  })
  const dirty = git('status', '--porcelain', '--untracked-files=all', '--', '.github/workflows')
  if (dirty) throw new Error('请先提交或处理工作流目录的改动，再核对候选提交。')

  const candidate = git('rev-parse', 'HEAD').trim()
  const base = git('rev-parse', 'origin/main').trim()
  const paths = splitPaths(git('diff', '--no-renames', '--name-only', '-z', base, candidate))
  const changedWorkflows = paths.filter(file => workflowFile.test(file))
  const parsed = new Map()
  const entries = []
  for (const [ref, commit] of [['HEAD', candidate], ['origin/main', base]]) {
    for (const row of splitPaths(git('ls-tree', '-rz', commit, '--', '.github/workflows'))) {
      const tab = row.indexOf('\t')
      const [, type, oid] = row.slice(0, tab).split(' ')
      const file = row.slice(tab + 1)
      if (type !== 'blob' || !workflowFile.test(file)) continue
      if (!parsed.has(oid)) parsed.set(oid, inspectWorkflow(git('cat-file', 'blob', oid), ref + ':' + file))
      entries.push({ ref, commit, path: file, ...parsed.get(oid) })
    }
  }
  return { observed_at: new Date().toISOString(), scope: '普通分支推送和 PR 的静态触发检查',
    candidate, base, workflow_changes: changedWorkflows.length > 0, changed_workflows: changedWorkflows,
    changed_paths: paths, entries }
}

export function runWorkflowAudit(root = repository, args = process.argv.slice(2)) {
  if (args.length > 1) throw new Error('用法：node scripts/check-workflows.mjs [报告路径]')
  const report = auditWorkflows(root)
  if (args.length) {
    writeFileSync(path.resolve(root, args[0]), JSON.stringify(report, null, 2) + '\n')
  } else {
    const { candidate, base, workflow_changes, changed_workflows, changed_paths } = report
    process.stdout.write(JSON.stringify({ candidate, base, workflow_changes, changed_workflows, changed_paths }, null, 2) + '\n')
  }
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  runWorkflowAudit()
}
