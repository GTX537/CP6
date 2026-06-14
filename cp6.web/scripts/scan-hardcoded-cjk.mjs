// 一次性终扫：找未被 t()/$t()/tt() 包裹、且非注释的硬编码 CJK（前端 .vue/.ts）。
// 思路：先抹掉注释与 t('…')/$t("…")/tt('…') 调用的 key，再看剩余 CJK。
// 启发式（非 AST），可能少量误报；用于人工复核收尾。
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve, join } from 'node:path'

const __dirname = dirname(fileURLToPath(import.meta.url))
const SRC = resolve(__dirname, '../src')
const CJK = /[぀-ヿ｡-ﾟ一-鿿]/
const KANA = /[぀-ヿ｡-ﾟ]/

function walk(dir, acc = []) {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name)
    const st = statSync(p)
    if (st.isDirectory()) walk(p, acc)
    else if (/\.(vue|ts)$/.test(name) && !/keys\.generated\./.test(name)) acc.push(p)
  }
  return acc
}

const hits = []
for (const f of walk(SRC)) {
  let raw = readFileSync(f, 'utf-8')
  // 全局剥离块注释 /* … */（含 /** JSDoc */）、HTML 注释、<style> 整块（保留行号占位）
  const keepLines = (m) => m.replace(/[^\n]/g, ' ')
  raw = raw.replace(/\/\*[\s\S]*?\*\//g, keepLines)
  raw = raw.replace(/<!--[\s\S]*?-->/g, keepLines)
  raw = raw.replace(/<style[\s\S]*?<\/style>/gi, keepLines)
  const lines = raw.split(/\r?\n/)
  for (let i = 0; i < lines.length; i++) {
    let line = lines[i]
    if (!CJK.test(line)) continue
    let s = line
    // 抹掉 t()/$t()/tt() 调用里的 key 字面量
    s = s.replace(/\$?t?t\(\s*(['"`])(?:\\.|(?!\1).)*\1/g, 't(__)')
    // 行注释 //… 与 JSDoc 续行（以 * 开头）
    s = s.replace(/\/\/.*$/, '')
    if (/^\s*\*/.test(s)) continue
    // console.*（开发日志，保留不译）
    s = s.replace(/console\.\w+\([\s\S]*?\)/g, '')
    if (!CJK.test(s)) continue
    // 标注：=== '…' / === "…" 多为状态码数据值比较（非 UI），单列提示
    const isCodeCompare = /[=!]==?\s*(['"])[^'"]*[぀-ヿ｡-ﾟ一-鿿]/.test(s)
    hits.push({
      f: f.slice(SRC.length + 1).replace(/\\/g, '/'),
      n: i + 1,
      jp: KANA.test(s),
      code: isCodeCompare,
      text: line.trim().slice(0, 120),
    })
  }
}

hits.sort((a, b) => a.f.localeCompare(b.f) || a.n - b.n)
const byFile = new Map()
for (const h of hits) {
  if (!byFile.has(h.f)) byFile.set(h.f, [])
  byFile.get(h.f).push(h)
}
for (const [f, hs] of byFile) {
  console.log(`\n${f}  (${hs.length})`)
  for (const h of hs) console.log(`  ${h.n}${h.jp ? ' [JP]' : ' [CN]'}${h.code ? ' [code=?]' : ''}  ${h.text}`)
}
console.log(`\n=== 合计 ${hits.length} 行，分布 ${byFile.size} 文件 ===`)
