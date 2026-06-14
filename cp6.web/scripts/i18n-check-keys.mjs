/**
 * i18n 优化 P2/⑥：CI 缺 key 校验（前端 + 后端）。
 * - 前端：扫 cp6.web/src 的 t('…') / $t('…')。
 * - 后端(⑥)：扫 CP6.WebApi / CP6.Core 的 _localizer["…"] / localizer["…"] / BizException("…")。
 * 比对 src/i18n/keys.generated.json 快照；引用了但快照缺失 → 报告并非零退出（CI gate）。
 *
 * 仅校验“字面量 key”；动态调用无法静态判定，跳过并计数。
 * 快照由 `npm run i18n:pull` 生成（需后端运行）。CI 用提交的快照，无需 DB。
 */
import { readFileSync, existsSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve, join } from 'node:path'
import { readdirSync, statSync } from 'node:fs'

const __dirname = dirname(fileURLToPath(import.meta.url))
const SRC = resolve(__dirname, '../src')
const SNAP = resolve(__dirname, '../src/i18n/keys.generated.json')

if (!existsSync(SNAP)) {
  console.error(`[i18n:check] 缺少 key 快照 ${SNAP}，请先 npm run i18n:pull`)
  process.exit(1)
}
const known = new Set(JSON.parse(readFileSync(SNAP, 'utf-8')))

function walk(dir, acc = []) {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name)
    const st = statSync(p)
    if (st.isDirectory()) walk(p, acc)
    else if (/\.(vue|ts)$/.test(name)) acc.push(p)
  }
  return acc
}

// 匹配 t('x') / $t("x") / t('x', …)；仅捕获单/双引号字面量 key
const RE = /(?<![\w$])\$?t\(\s*(['"])((?:\\.|(?!\1).)*)\1/g
const files = walk(SRC)
const missing = new Map() // key -> [files]
let dynamicCount = 0
let refCount = 0

for (const f of files) {
  const code = readFileSync(f, 'utf-8')
  // 粗略统计动态调用（t( 后接非引号）
  for (const m of code.matchAll(/(?<![\w$])\$?t\(\s*[^'")\s]/g)) dynamicCount++
  for (const m of code.matchAll(RE)) {
    const key = m[2]
    // 跳过动态拼接：t('前缀.' + 变量) —— 引号后紧跟 +，或 key 以 . 结尾（前缀片段）
    const after = code.slice(m.index + m[0].length, m.index + m[0].length + 8)
    if (/^\s*\+/.test(after) || key.endsWith('.')) { dynamicCount++; continue }
    refCount++
    if (!known.has(key)) {
      const rel = f.slice(SRC.length + 1).replace(/\\/g, '/')
      if (!missing.has(key)) missing.set(key, new Set())
      missing.get(key).add(rel)
    }
  }
}

console.log(`[i18n:check] 前端：扫 ${files.length} 文件；字面量 t() 引用 ${refCount}；动态 t(变量) 约 ${dynamicCount}（跳过）；快照 key ${known.size}`)

// ── ⑥ 后端 C# key 校验 ──────────────────────────────────────────
const REPO = resolve(__dirname, '../..')
function walkCs(dir, acc = []) {
  let entries
  try { entries = readdirSync(dir) } catch { return acc }
  for (const name of entries) {
    if (name === 'bin' || name === 'obj' || name === 'Migrations') continue
    const p = join(dir, name)
    let st
    try { st = statSync(p) } catch { continue }
    if (st.isDirectory()) walkCs(p, acc)
    else if (name.endsWith('.cs')) acc.push(p)
  }
  return acc
}
// _localizer["KEY"] / localizer["KEY"] / Localizer["KEY"] (控制器基类) / BizException("KEY") —— 仅字面量
const RE_LOC = /localizer\s*\[\s*"((?:\\.|[^"])*)"\s*\]/gi
const RE_BIZ = /BizException\(\s*"((?:\\.|[^"])*)"/g
let csFileCount = 0
let csRefCount = 0
for (const root of ['CP6.WebApi', 'CP6.Core']) {
  for (const f of walkCs(join(REPO, root))) {
    csFileCount++
    const code = readFileSync(f, 'utf-8')
    for (const re of [RE_LOC, RE_BIZ]) {
      for (const m of code.matchAll(re)) {
        const key = m[1]
        csRefCount++
        if (!known.has(key)) {
          const rel = (root + '/' + f.slice(join(REPO, root).length + 1)).replace(/\\/g, '/')
          if (!missing.has(key)) missing.set(key, new Set())
          missing.get(key).add(rel)
        }
      }
    }
  }
}
console.log(`[i18n:check] 后端：扫 ${csFileCount} 个 .cs；_localizer/BizException 字面量引用 ${csRefCount}`)

if (missing.size === 0) {
  console.log('[i18n:check] ✅ 无缺失 key')
  process.exit(0)
}

console.error(`[i18n:check] ❌ ${missing.size} 个 key 在代码中引用但 DB 快照缺失：`)
for (const [key, fileset] of [...missing].sort((a, b) => a[0].localeCompare(b[0]))) {
  console.error(`  - ${JSON.stringify(key)}  ← ${[...fileset].slice(0, 3).join(', ')}${fileset.size > 3 ? ` (+${fileset.size - 3})` : ''}`)
}
console.error('[i18n:check] 修复：在后端 seed 补这些 key（5 语）后重新 npm run i18n:pull，或修正代码里的 key 拼写。')
process.exit(1)
