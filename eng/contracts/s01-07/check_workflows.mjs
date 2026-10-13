// Read-only pre-push audit of candidate and the freshly fetched protected main.
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'
import { readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..')
const require = createRequire(path.join(root, 'cp6.web/package.json'))
const { parse } = require('yaml')
const git = (...args) => execFileSync('git', ['-c', 'core.longpaths=true', ...args], { cwd: root })
const sha = bytes => createHash('sha256').update(bytes).digest('hex')
const entries = []
for (const ref of ['HEAD', 'origin/main']) {
  const files = git('ls-tree', '-r', '--name-only', ref, '--', '.github/workflows').toString().trim().split('\n').filter(Boolean)
  for (const file of files) {
    const bytes = git('show', `${ref}:${file}`)
    const doc = parse(bytes.toString())
    const events = doc.on
    const keys = typeof events === 'object' && events !== null ? Object.keys(events) : [events]
    const safe = keys.every(event => event === 'workflow_dispatch' || event === 'workflow_call' ||
      (event === 'push' && Array.isArray(events.push?.tags) && !events.push.branches && !events.push['branches-ignore']))
    if (!safe) throw new Error(`Automatic branch/PR trigger: ${ref}:${file}`)
    if (ref === 'HEAD' && sha(readFileSync(path.join(root, file))) !== sha(bytes)) {
      // Worktree CRLF is normal here; compare normalized contents without changing source.
      if (readFileSync(path.join(root, file), 'utf8').replace(/\r\n/g, '\n') !== bytes.toString().replace(/\r\n/g, '\n')) throw new Error(`Working workflow differs: ${file}`)
    }
    const dependencies = []
    for (const [jobName, job] of Object.entries(doc.jobs ?? {})) {
      if (job.uses) dependencies.push({ job: jobName, uses: job.uses })
      for (const step of job.steps ?? []) if (step.uses) dependencies.push({ job: jobName, uses: step.uses })
    }
    entries.push({ ref, commit: git('rev-parse', ref).toString().trim(), path: file, sha256: sha(bytes), events, dependencies, branch_push_and_pr_safe: safe })
  }
}
const report = { observed_at: new Date().toISOString(), scope: 'Plain task branch push / protected main PR; no tag push or workflow dispatch authorized', entries, workflow_changes: false, triggered: [], rerun: [], cancelled: [], actions_policy: 'Local validation only. R2 protected tag publishing gate preserved.' }
const destination = path.resolve(root, process.argv[2] ?? 'docs/development/evidence/S01-07/workflows.json')
writeFileSync(destination, JSON.stringify(report, null, 2) + '\n')
console.log(`PASS: ${entries.length} candidate/protected-main workflows; only manual/call or tag-only events`)
