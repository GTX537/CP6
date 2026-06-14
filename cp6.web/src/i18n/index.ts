import { createI18n } from 'vue-i18n'
import http from '@/api/http'

// 语言选项列表
export const langOptions = [
  { label: '简体中文', value: 'zh-CN' },
  { label: '繁體中文', value: 'zh-TW' },
  { label: 'English', value: 'en' },
  { label: '日本語', value: 'ja' },
  { label: '한국어', value: 'ko' },
]

// i18n 优化 P2：显式回退链（缺失逐级回退，而非一律回 ja）。
export const fallbackChain: Record<string, string[]> = {
  'zh-CN': ['zh-TW', 'ja'],
  'zh-TW': ['zh-CN', 'ja'],
  en: ['ja'],
  ko: ['en', 'ja'],
  ja: [],
}

// i18n 优化 P4：交付模式。'live'(默认)=实时读 API；'publish'=读版本化静态包(不可变长缓存)。
const MODE = (import.meta.env.VITE_I18N_MODE as string) || 'live'

// i18n 优化 P4：按路由懒加载的大模块命名空间（须与后端 LangColumn.LazyNamespaces 一致）。
// 其余 key（含无点日文 key、nav/menu/table/login/dict 等）归 _core，启动即载。
const LAZY_NAMESPACES = ['wms', 'sales', 'erp', 'mes']

/** 路由路径 → 需要的大模块命名空间。返回空数组表示 _core 已覆盖。 */
export function namespacesForPath(path: string): string[] {
  if (path.startsWith('/wms')) return ['wms']
  if (path.startsWith('/mes')) return ['mes']
  if (
    path.startsWith('/erp') ||
    /^\/(order|product|quotation|estimate-calc|business-partner|fsc-checklist|sheet-unit-price|plate-mold)/.test(path)
  )
    return ['sales', 'erp']
  return []
}

const datetimeFormats = {
  'zh-CN': { short: { year: 'numeric', month: '2-digit', day: '2-digit' }, long: { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }, time: { hour: '2-digit', minute: '2-digit', second: '2-digit' } },
  'zh-TW': { short: { year: 'numeric', month: '2-digit', day: '2-digit' }, long: { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }, time: { hour: '2-digit', minute: '2-digit', second: '2-digit' } },
  en: { short: { year: 'numeric', month: 'short', day: 'numeric' }, long: { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }, time: { hour: '2-digit', minute: '2-digit', second: '2-digit' } },
  ja: { short: { year: 'numeric', month: '2-digit', day: '2-digit' }, long: { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }, time: { hour: '2-digit', minute: '2-digit', second: '2-digit' } },
  ko: { short: { year: 'numeric', month: '2-digit', day: '2-digit' }, long: { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }, time: { hour: '2-digit', minute: '2-digit', second: '2-digit' } },
} as const

const numberFormats = {
  'zh-CN': { decimal: { style: 'decimal', minimumFractionDigits: 2, maximumFractionDigits: 2 }, integer: { style: 'decimal', maximumFractionDigits: 0 }, percent: { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 2 } },
  'zh-TW': { decimal: { style: 'decimal', minimumFractionDigits: 2, maximumFractionDigits: 2 }, integer: { style: 'decimal', maximumFractionDigits: 0 }, percent: { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 2 } },
  en: { decimal: { style: 'decimal', minimumFractionDigits: 2, maximumFractionDigits: 2 }, integer: { style: 'decimal', maximumFractionDigits: 0 }, percent: { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 2 } },
  ja: { decimal: { style: 'decimal', minimumFractionDigits: 2, maximumFractionDigits: 2 }, integer: { style: 'decimal', maximumFractionDigits: 0 }, percent: { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 2 } },
  ko: { decimal: { style: 'decimal', minimumFractionDigits: 2, maximumFractionDigits: 2 }, integer: { style: 'decimal', maximumFractionDigits: 0 }, percent: { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 2 } },
} as const

// flatJson:true により、{"a.b.c":"v"} のキーをドット区切りのまま検索できる。
const i18n = createI18n({
  legacy: false,
  locale: localStorage.getItem('lang') || 'ja',
  fallbackLocale: { 'zh-CN': ['zh-TW', 'ja'], 'zh-TW': ['zh-CN', 'ja'], en: ['ja'], ko: ['en', 'ja'], default: ['ja'] },
  flatJson: true,
  missingWarn: false,
  fallbackWarn: false,
  datetimeFormats: datetimeFormats as any,
  numberFormats: numberFormats as any,
  messages: {},
})

// ── 加载状态跟踪 ────────────────────────────────────────────────
const loadedPacks = new Set<string>() // `${lang}:${ns}` / `${lang}:_core` / `${lang}:_full`
const neededNamespaces = new Set<string>() // 本会话访问过的大模块 ns（换语言时按此重载）
let manifestVersion: string | null = null

async function fetchManifestVersion(): Promise<string> {
  if (manifestVersion !== null) return manifestVersion
  let v = ''
  try {
    const m: any = await http.get('/lang/manifest')
    v = m?.version || ''
  } catch {
    v = ''
  }
  manifestVersion = v
  return v
}

/** 合并一批扁平词条到某语言（不覆盖语言切换状态）。 */
function merge(lang: string, flat: Record<string, string>) {
  const existing = (i18n.global.getLocaleMessage(lang) as any) || {}
  i18n.global.setLocaleMessage(lang, { ...existing, ...flat })
}

/** 兜底：实时拉某语言全量包（懒加载失败 / 无路由 ns 映射时用，保证不出现裸 key）。 */
async function loadFull(lang: string) {
  if (loadedPacks.has(`${lang}:_full`)) return
  try {
    const flat: any = await http.get(`/lang/${lang}`)
    merge(lang, flat)
    loadedPacks.add(`${lang}:_full`)
    loadedPacks.add(`${lang}:_core`)
  } catch (e) {
    console.error(`[i18n] loadFull failed: ${lang}`, e)
  }
}

/** publish 模式：读版本化静态包（不可变长缓存）；无已发布版本则回退实时全量。 */
async function loadPublished(lang: string) {
  if (loadedPacks.has(`${lang}:_full`)) return
  const version = await fetchManifestVersion()
  if (!version) {
    await loadFull(lang)
    return
  }
  try {
    const flat: any = await http.get(`/lang/published/${version}/${lang}`)
    merge(lang, flat)
    loadedPacks.add(`${lang}:_full`)
    loadedPacks.add(`${lang}:_core`)
  } catch (e) {
    console.error(`[i18n] loadPublished failed: ${lang}@${version}, fallback to full`, e)
    await loadFull(lang)
  }
}

/** live 模式：加载某语言的 _core 包（小，启动即载）。 */
async function loadCore(lang: string) {
  if (loadedPacks.has(`${lang}:_core`) || loadedPacks.has(`${lang}:_full`)) return
  try {
    const flat: any = await http.get(`/lang/${lang}/ns/_core`)
    merge(lang, flat)
    loadedPacks.add(`${lang}:_core`)
  } catch (e) {
    console.error(`[i18n] loadCore failed: ${lang}, fallback to full`, e)
    await loadFull(lang)
  }
}

/** live 模式：加载某语言的某大模块命名空间；失败兜底全量。 */
async function loadNamespace(lang: string, ns: string) {
  if (loadedPacks.has(`${lang}:${ns}`) || loadedPacks.has(`${lang}:_full`)) return
  try {
    const flat: any = await http.get(`/lang/${lang}/ns/${ns}`)
    merge(lang, flat)
    loadedPacks.add(`${lang}:${ns}`)
  } catch (e) {
    console.error(`[i18n] loadNamespace failed: ${lang}/${ns}, fallback to full`, e)
    await loadFull(lang)
  }
}

/** 加载某语言的基础包（按模式）。 */
async function loadBase(lang: string) {
  if (MODE === 'publish') await loadPublished(lang)
  else await loadCore(lang)
}

/** 路由守卫调用：确保目标路径所需命名空间已就绪（含当前语言 + 回退链语言）。 */
export async function ensureNamespacesForPath(path: string) {
  const lang = (i18n.global.locale as any).value as string
  const nsList = namespacesForPath(path)
  if (MODE === 'publish' || nsList.length === 0) {
    // publish 模式整包已含全部；无映射的路由 _core 已覆盖。无需额外加载。
    return
  }
  const langs = [lang, ...(fallbackChain[lang] || [])]
  await Promise.all(
    nsList.flatMap((ns) => {
      neededNamespaces.add(ns)
      return langs.map((l) => loadNamespace(l, ns))
    }),
  )
}

// 切换语言：载新语言基础包 + 已访问过的大模块 ns + 回退链基础包。
export async function changeLang(langCode: string) {
  const langs = [langCode, ...(fallbackChain[langCode] || [])]
  await Promise.all(langs.map((l) => loadBase(l)))
  await Promise.all(
    [...neededNamespaces].flatMap((ns) => langs.map((l) => loadNamespace(l, ns))),
  )
  i18n.global.locale.value = langCode
  localStorage.setItem('lang', langCode)
}

// 初始化：载当前语言 + 回退链的基础包（live=_core 小包、publish=版本化全量）。
export async function initI18n() {
  const lang = localStorage.getItem('lang') || 'ja'
  const langs = [lang, ...(fallbackChain[lang] || [])]
  await Promise.all(langs.map((l) => loadBase(l)))
}

// 兼容旧调用：按需加载某语言基础包。
export async function loadLang(langCode: string) {
  await loadBase(langCode)
}

export default i18n
