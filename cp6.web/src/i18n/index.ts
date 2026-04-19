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

// 将扁平的 { "login.title": "xxx" } 转成嵌套的 { login: { title: "xxx" } }
function flatToNested(flat: Record<string, string>): Record<string, any> {
  const result: Record<string, any> = {}
  for (const [key, value] of Object.entries(flat)) {
    const parts = key.split('.')
    let current: Record<string, any> = result
    for (let i = 0; i < parts.length - 1; i++) {
      if (!current[parts[i]!]) current[parts[i]!] = {}
      current = current[parts[i]!]
    }
    const lastKey = parts[parts.length - 1]!
    current[lastKey] = value
  }
  return result
}

const i18n = createI18n({
  legacy: false,
  locale: localStorage.getItem('lang') || 'zh-CN',
  fallbackLocale: 'zh-CN',
  messages: {},
})

// 从API加载指定语言的翻译
export async function loadLang(langCode: string) {
  try {
    const flat: any = await http.get(`/lang/${langCode}`)
    const nested = flatToNested(flat)
    i18n.global.setLocaleMessage(langCode, nested)
  } catch {
    console.warn(`Failed to load language: ${langCode}`)
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
  const lang = localStorage.getItem('lang') || 'zh-CN'
  await loadLang(lang)
}

export default i18n
