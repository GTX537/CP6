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

// flatJson:true により、{"a.b.c":"v"} のキーをドット区切りのまま検索できる。
// シード SQL に "wms.pack.title" と "wms.pack.title.packages" のような
// 葉と枝が衝突するキーが含まれており、嵌套化（flatToNested）すると
// 「Cannot create property X on string」で失敗していたためフラットに切替。
const i18n = createI18n({
  legacy: false,
  locale: localStorage.getItem('lang') || 'ja',
  fallbackLocale: 'ja',
  flatJson: true,
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

// 切换语言
export async function changeLang(langCode: string) {
  await loadLang(langCode)
  i18n.global.locale.value = langCode
  localStorage.setItem('lang', langCode)
}

// 初始化：加载当前语言
export async function initI18n() {
  const lang = localStorage.getItem('lang') || 'ja'
  await loadLang(lang)
}

export default i18n
