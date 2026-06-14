/**
 * i18n 优化 P2：从 key 快照生成 TS 类型，给 t() 找回类型安全。
 * 输出 src/i18n/keys.generated.ts：导出 MessageKey 联合类型 + 类型安全包装 tt()。
 *
 * 非强制：不全局覆盖 vue-i18n 的 t（避免破坏海量动态 t(变量) 调用与含特殊字符的日文 key）。
 * 想要编译期校验 key 的地方，opt-in 用 tt('xxx')；打错 key 即 TS 报错。
 */
import { readFileSync, writeFileSync, existsSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const __dirname = dirname(fileURLToPath(import.meta.url))
const SNAP = resolve(__dirname, '../src/i18n/keys.generated.json')
const OUT = resolve(__dirname, '../src/i18n/keys.generated.ts')

if (!existsSync(SNAP)) {
  console.error(`[i18n:types] 缺少 key 快照 ${SNAP}，请先 npm run i18n:pull`)
  process.exit(1)
}
const keys = JSON.parse(readFileSync(SNAP, 'utf-8'))

// 每个 key 转成 TS 字符串字面量（转义反斜杠/单引号/换行/回车，防 key 含特殊字符破坏生成的 TS）
const esc = (k) => String(k).replace(/\\/g, '\\\\').replace(/'/g, "\\'").replace(/\r/g, '\\r').replace(/\n/g, '\\n')
const union = keys.map((k) => `  | '${esc(k)}'`).join('\n')

const banner = `// ⚠️ 自动生成，请勿手改。来源：npm run i18n:gen-types（读取 keys.generated.json）。\n// i18n 优化 P2：key 类型安全（opt-in，用 tt() 触发编译期校验）。\n`

const body = `${banner}
import i18n from '@/i18n'
import { useI18n } from 'vue-i18n'

/** DB(Sys_Lang) 中现存的全部词条 key（${keys.length} 个）。 */
export type MessageKey =
${union || "  | never"}

/** 类型安全翻译（全局 i18n）：打错 key 编译期即报错。动态 key 仍用普通 t()。 */
export function tt(key: MessageKey, ...args: unknown[]): string {
  return (i18n.global as any).t(key, ...args)
}

/** 组件内类型安全翻译（随 locale 响应式）。 */
export function useTypedT() {
  const { t } = useI18n()
  return { tt: (key: MessageKey, ...args: unknown[]) => (t as any)(key, ...args) }
}
`

writeFileSync(OUT, body, 'utf-8')
console.log(`[i18n:types] ${keys.length} keys -> ${OUT}`)
