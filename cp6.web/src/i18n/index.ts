import { createI18n } from 'vue-i18n'
import axios from 'axios'

// 语言选项列表（i18n 优化 P5：仅 dev 构建追加伪本地化 QA 语言）
export const langOptions = [
  { label: '简体中文', value: 'zh-CN' },
  { label: '繁體中文', value: 'zh-TW' },
  { label: 'English', value: 'en' },
  { label: '日本語', value: 'ja' },
  { label: '한국어', value: 'ko' },
  ...(import.meta.env.DEV ? [{ label: '🔣 Pseudo (QA)', value: 'pseudo' }] : []),
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
  'zh-CN': { short: { year: 'numeric', month: '2-digit', day: '2-digit' }, long: { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3 }, time: { hour: '2-digit', minute: '2-digit', second: '2-digit' } },
  'zh-TW': { short: { year: 'numeric', month: '2-digit', day: '2-digit' }, long: { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3 }, time: { hour: '2-digit', minute: '2-digit', second: '2-digit' } },
  en: { short: { year: 'numeric', month: 'short', day: 'numeric' }, long: { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3 }, time: { hour: '2-digit', minute: '2-digit', second: '2-digit' } },
  ja: { short: { year: 'numeric', month: '2-digit', day: '2-digit' }, long: { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3 }, time: { hour: '2-digit', minute: '2-digit', second: '2-digit' } },
  ko: { short: { year: 'numeric', month: '2-digit', day: '2-digit' }, long: { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3 }, time: { hour: '2-digit', minute: '2-digit', second: '2-digit' } },
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
const loadingPacks = new Map<string, Promise<void>>()
let manifestVersion: string | null = null

const languageHttp = axios.create({
  baseURL: '/api',
  timeout: 10000,
  withCredentials: true,
})

function loadOnce(key: string, loader: () => Promise<void>): Promise<void> {
  const existing = loadingPacks.get(key)
  if (existing) return existing

  const pending = loader().finally(() => loadingPacks.delete(key))
  loadingPacks.set(key, pending)
  return pending
}

async function getLanguageResource<T>(url: string): Promise<T> {
  const response = await languageHttp.get<T>(url)
  return response.data
}

async function fetchManifestVersion(): Promise<string> {
  if (manifestVersion !== null) return manifestVersion
  let v = ''
  try {
    const m: any = await getLanguageResource('/lang/manifest')
    v = m?.version || ''
  } catch {
    v = ''
  }
  manifestVersion = v
  return v
}

const I18N_STORAGE_PREFIX = 'cp6_i18n_pack_'

/** 合并一批扁平词条到某语言（不覆盖语言切换状态）。 */
function merge(lang: string, flat: Record<string, string>) {
  const existing = (i18n.global.getLocaleMessage(lang) as any) || {}
  i18n.global.setLocaleMessage(lang, { ...existing, ...flat })
  try {
    const raw = localStorage.getItem(`${I18N_STORAGE_PREFIX}${lang}`)
    const cached = raw ? JSON.parse(raw) : {}
    localStorage.setItem(`${I18N_STORAGE_PREFIX}${lang}`, JSON.stringify({ ...cached, ...flat }))
  } catch {
    // 忽略 localStorage 异常
  }
}

/** 从 localStorage 恢复已缓存的语言包到 i18n 实例（同步执行，0ms 阻塞） */
export function hydrateCachedLanguagePacks(targetLang?: string) {
  const lang = targetLang || localStorage.getItem('lang') || 'ja'
  const langs = [lang, ...(fallbackChain[lang] || [])]
  for (const l of langs) {
    try {
      const raw = localStorage.getItem(`${I18N_STORAGE_PREFIX}${l}`)
      if (raw) {
        const flat = JSON.parse(raw)
        if (flat && typeof flat === 'object') {
          const existing = (i18n.global.getLocaleMessage(l) as any) || {}
          i18n.global.setLocaleMessage(l, { ...existing, ...flat })
          loadedPacks.add(`${l}:_core`)
        }
      }
    } catch {
      // 忽略解析异常
    }
  }
}

// 模块初始化时立即尝试同步恢复已有缓存
hydrateCachedLanguagePacks()

/** 兜底：实时拉某语言全量包（懒加载失败 / 无路由 ns 映射时用，保证不出现裸 key）。 */
function loadFull(lang: string): Promise<void> {
  if (loadedPacks.has(`${lang}:_full`)) return Promise.resolve()
  return loadOnce(`${lang}:_full`, async () => {
    try {
      const flat: any = await getLanguageResource(`/lang/${lang}`)
      merge(lang, flat)
      loadedPacks.add(`${lang}:_full`)
      loadedPacks.add(`${lang}:_core`)
    } catch (e) {
      console.warn(`[i18n] loadFull failed: ${lang}`, e)
    }
  })
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
    const flat: any = await getLanguageResource(`/lang/published/${version}/${lang}`)
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
    const flat: any = await getLanguageResource(`/lang/${lang}/ns/_core`)
    merge(lang, flat)
    loadedPacks.add(`${lang}:_core`)
  } catch (e) {
    console.error(`[i18n] loadCore failed: ${lang}, fallback to full`, e)
    await loadFull(lang)
  }
}

/** live 模式：加载某语言的某大模块命名空间；失败兜底全量。 */
function loadNamespace(lang: string, ns: string): Promise<void> {
  if (loadedPacks.has(`${lang}:${ns}`) || loadedPacks.has(`${lang}:_full`)) return Promise.resolve()
  return loadOnce(`${lang}:${ns}`, async () => {
    try {
      const flat: any = await getLanguageResource(`/lang/${lang}/ns/${ns}`)
      merge(lang, flat)
      loadedPacks.add(`${lang}:${ns}`)
    } catch (e) {
      console.warn(`[i18n] loadNamespace failed: ${lang}/${ns}, fallback to full`, e)
      await loadFull(lang)
    }
  })
}

// ── i18n 优化 P5：伪本地化质量门（dev-only QA 语言）─────────────────
// 把 en 文案重音化 + 加括号 + 加长 ~40%。用途：①未翻译/硬编码文案不会变形 → 一眼裸露；
// ②加长文本撑出布局，揪文本溢出。{占位} 不变形。
const PSEUDO_MAP: Record<string, string> = {
  a: 'å', b: 'ƀ', c: 'ç', d: 'ð', e: 'é', f: 'ƒ', g: 'ĝ', h: 'ĥ', i: 'í', j: 'ĵ', k: 'ķ', l: 'ļ', m: 'ɱ',
  n: 'ñ', o: 'ö', p: 'þ', q: 'ǫ', r: 'ŕ', s: 'š', t: 'ţ', u: 'ü', v: 'ṽ', w: 'ŵ', x: 'ẋ', y: 'ý', z: 'ž',
  A: 'Å', B: 'Ɓ', C: 'Ç', D: 'Ð', E: 'É', F: 'Ƒ', G: 'Ĝ', H: 'Ĥ', I: 'Í', J: 'Ĵ', K: 'Ķ', L: 'Ļ', M: 'Ɱ',
  N: 'Ñ', O: 'Ö', P: 'Þ', Q: 'Ǫ', R: 'Ŕ', S: 'Š', T: 'Ţ', U: 'Ü', V: 'Ṽ', W: 'Ŵ', X: 'Ẋ', Y: 'Ý', Z: 'Ž',
}
function pseudoize(v: string): string {
  let out = ''
  let i = 0
  while (i < v.length) {
    const ch = v.charAt(i)
    if (ch === '{') {
      const end = v.indexOf('}', i)
      if (end >= 0) { out += v.slice(i, end + 1); i = end + 1; continue } // 保留 {占位}
    }
    out += PSEUDO_MAP[ch] ?? ch
    i++
  }
  const base = out.replace(/\{[^}]*\}/g, '')
  const pad = '·'.repeat(Math.max(1, Math.ceil(base.length * 0.4)))
  return `⟦${out}${pad}⟧`
}
async function loadPseudo() {
  if (loadedPacks.has('pseudo:_full')) return
  try {
    // 直接拉「原始」en 字典（值为字符串）。不能用 getLocaleMessage('en')——vue-i18n 会把消息编译成对象。
    const flat: any = await getLanguageResource('/lang/en')
    const pseudo: Record<string, string> = {}
    for (const k in flat) {
      const v = flat[k]
      pseudo[k] = typeof v === 'string' ? pseudoize(v) : v
    }
    i18n.global.setLocaleMessage('pseudo', pseudo)
    loadedPacks.add('pseudo:_full')
  } catch (e) {
    console.error('[i18n] loadPseudo failed', e)
  }
}

/** 加载某语言的基础包（按模式）。 */
async function loadBase(lang: string) {
  if (lang === 'pseudo') await loadPseudo()
  else if (MODE === 'publish') await loadPublished(lang)
  else await loadCore(lang)
}

/** 路由守卫调用：确保目标路径所需命名空间已就绪（含当前语言 + 回退链语言）。 */
export async function ensureNamespacesForPath(path: string) {
  const lang = (i18n.global.locale as any).value as string
  if (lang === 'pseudo') return // 伪本地化整包已含全部
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

/** 后台预取路由所需翻译，不延迟页面导航。 */
export function prefetchNamespacesForPath(path: string): void {
  void ensureNamespacesForPath(path).catch((e) => {
    console.warn(`[i18n] route prefetch failed: ${path}`, e)
  })
}

// 切换语言：载新语言基础包 + 已访问过的大模块 ns + 回退链基础包。
export async function changeLang(langCode: string) {
  // i18n 优化 P5：伪本地化是整包(en 底)，无需 ns/回退链加载。
  if (langCode === 'pseudo') {
    await loadPseudo()
    i18n.global.locale.value = 'pseudo'
    localStorage.setItem('lang', 'pseudo')
    return
  }
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
