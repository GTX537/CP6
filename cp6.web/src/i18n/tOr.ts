import { useI18n } from 'vue-i18n'

/**
 * i18n 回退助手：把可能是 i18n key 的字符串安全译出。
 *  - key 已注册 → 返回译文
 *  - 未注册且给了 fallback → 返回 fallback
 *  - 未注册且无 fallback → 返回 key 本身（不出现空白/裸告警）
 *
 * 典型用途：后端错误码（E-SPACE-3xx 等）——注册了词条即本地化，未注册则原样透出。
 */
export interface I18nLike {
  te: (key: string) => boolean
  t: (key: string) => string
}

/** 纯函数形态（可脱离组件测试）：显式传入 vue-i18n composer（或任意含 te/t 的对象）。 */
export function tOr(i18n: I18nLike, key: string, fallback?: string): string {
  if (!key) return fallback ?? key
  return i18n.te(key) ? i18n.t(key) : (fallback ?? key)
}

/** 组合式形态：组件 setup 内 `const tr = useTOr()`，调用 `tr(key, fallback?)`。 */
export function useTOr() {
  const i18n = useI18n()
  return (key: string, fallback?: string): string => tOr(i18n as I18nLike, key, fallback)
}
