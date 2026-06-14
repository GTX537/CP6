/**
 * i18n 优化 P2：从运行中的后端 API 拉取全部词条 key 快照，写入 src/i18n/keys.generated.json。
 *
 * key 的唯一事实源是 DB(Sys_Lang)，散落在后端 C# seed，静态解析脆弱；故从 API 拉取最可靠。
 * 该快照供 i18n-check-keys（CI 缺 key 校验）与 i18n-gen-types（类型生成）离线使用，CI 无需 DB/后端。
 *
 * 用法：先起后端 dev server，再 `npm run i18n:pull`（可选 API_BASE 环境变量覆盖地址）。
 */
import { writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const BASE = process.env.API_BASE || 'http://localhost:5177'
const LANG = process.env.API_LANG || 'ja' // 任意语言的 key 集合相同，取源语言 ja
const __dirname = dirname(fileURLToPath(import.meta.url))
const OUT = resolve(__dirname, '../src/i18n/keys.generated.json')

const url = `${BASE}/api/lang/${LANG}`
try {
  const res = await fetch(url)
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  const dict = await res.json()
  const keys = Object.keys(dict).sort()
  writeFileSync(OUT, JSON.stringify(keys, null, 0) + '\n', 'utf-8')
  console.log(`[i18n:pull] ${keys.length} keys -> ${OUT}`)
} catch (e) {
  console.error(`[i18n:pull] FAILED fetching ${url}: ${e.message}`)
  console.error('[i18n:pull] 请先启动后端 dev server（默认 http://localhost:5177），或设 API_BASE。')
  process.exit(1)
}
