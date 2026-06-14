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

// i18n 优化 P2：日期/时间格式（5 locale）。日期在用户本地时区显示（Intl 默认）；
// 跨区按用户时区显示留 P3/P4（届时存 UTC、按 user tz 格式化）。
const datetimeFormats = {
  'zh-CN': {
    short: { year: 'numeric', month: '2-digit', day: '2-digit' },
    long: { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' },
    time: { hour: '2-digit', minute: '2-digit', second: '2-digit' },
  },
  'zh-TW': {
    short: { year: 'numeric', month: '2-digit', day: '2-digit' },
    long: { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' },
    time: { hour: '2-digit', minute: '2-digit', second: '2-digit' },
  },
  en: {
    short: { year: 'numeric', month: 'short', day: 'numeric' },
    long: { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' },
    time: { hour: '2-digit', minute: '2-digit', second: '2-digit' },
  },
  ja: {
    short: { year: 'numeric', month: '2-digit', day: '2-digit' },
    long: { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' },
    time: { hour: '2-digit', minute: '2-digit', second: '2-digit' },
  },
  ko: {
    short: { year: 'numeric', month: '2-digit', day: '2-digit' },
    long: { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' },
    time: { hour: '2-digit', minute: '2-digit', second: '2-digit' },
  },
} as const

// i18n 优化 P2：数字格式（decimal/integer/percent）。货币因币种动态，由 utils/format.ts 的
// formatCurrency 用 Intl 按 locale+currency 处理，不在此固定。
const numberFormats = {
  'zh-CN': {
    decimal: { style: 'decimal', minimumFractionDigits: 2, maximumFractionDigits: 2 },
    integer: { style: 'decimal', maximumFractionDigits: 0 },
    percent: { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 2 },
  },
  'zh-TW': {
    decimal: { style: 'decimal', minimumFractionDigits: 2, maximumFractionDigits: 2 },
    integer: { style: 'decimal', maximumFractionDigits: 0 },
    percent: { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 2 },
  },
  en: {
    decimal: { style: 'decimal', minimumFractionDigits: 2, maximumFractionDigits: 2 },
    integer: { style: 'decimal', maximumFractionDigits: 0 },
    percent: { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 2 },
  },
  ja: {
    decimal: { style: 'decimal', minimumFractionDigits: 2, maximumFractionDigits: 2 },
    integer: { style: 'decimal', maximumFractionDigits: 0 },
    percent: { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 2 },
  },
  ko: {
    decimal: { style: 'decimal', minimumFractionDigits: 2, maximumFractionDigits: 2 },
    integer: { style: 'decimal', maximumFractionDigits: 0 },
    percent: { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 2 },
  },
} as const

// flatJson:true により、{"a.b.c":"v"} のキーをドット区切りのまま検索できる。
// シード SQL に "wms.pack.title" と "wms.pack.title.packages" のような
// 葉と枝が衝突するキーが含まれており、嵌套化（flatToNested）すると
// 「Cannot create property X on string」で失敗していたためフラットに切替。
const i18n = createI18n({
  legacy: false,
  locale: localStorage.getItem('lang') || 'ja',
  fallbackLocale: { 'zh-CN': ['zh-TW', 'ja'], 'zh-TW': ['zh-CN', 'ja'], en: ['ja'], ko: ['en', 'ja'], default: ['ja'] },
  flatJson: true,
  // i18n 优化 P2：缺失 key 不在生产刷屏；开发态收集后可上报（缺失监控见 missing handler）。
  missingWarn: false,
  fallbackWarn: false,
  datetimeFormats: datetimeFormats as any,
  numberFormats: numberFormats as any,
  messages: {},
})

// 从API加载指定语言的翻译
export async function loadLang(langCode: string) {
  try {
    const flat: any = await http.get(`/lang/${langCode}`)
    i18n.global.setLocaleMessage(langCode, flat)
  } catch (e) {
    console.error(`[loadLang] FAILED to load language: ${langCode}`, e)
  }
}

// 切换语言：同时预加载回退链语言，保证缺失 key 能逐级回退到有值的语言。
export async function changeLang(langCode: string) {
  await loadLang(langCode)
  await Promise.all((fallbackChain[langCode] || []).map((fb) => ensureLang(fb)))
  i18n.global.locale.value = langCode
  localStorage.setItem('lang', langCode)
}

// 仅在该语言尚未加载时拉取（避免回退链重复请求）。
async function ensureLang(langCode: string) {
  const loaded = (i18n.global.availableLocales as string[]).includes(langCode)
  if (!loaded) await loadLang(langCode)
}

// 初始化：加载当前语言 + 其回退链。
export async function initI18n() {
  const lang = localStorage.getItem('lang') || 'ja'
  await loadLang(lang)
  await Promise.all((fallbackChain[lang] || []).map((fb) => ensureLang(fb)))
}

export default i18n
