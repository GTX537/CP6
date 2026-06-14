/**
 * i18n 优化 P2：locale 感知的格式化工具（数字 / 日期 / 百分比 / 多币种货币）。
 *
 * 用法：
 * - 组件 setup 内（随 locale 切换自动重渲染）：const { formatCurrency } = useFormat()
 * - 非 setup 处（如 el-table :formatter 回调、工具函数）：import { formatCurrency } from '@/utils/format'
 *
 * 设计：数字/日期走 vue-i18n 的 n()/d()（格式定义在 i18n/index.ts 的 numberFormats/datetimeFormats）；
 * 货币因币种动态（多币种 ERP），用 Intl.NumberFormat 按 locale+currency 即时格式化。
 * 时区：当前按用户浏览器本地时区显示；跨区按 user tz 显示留 P3/P4。
 */
import i18n from '@/i18n'
import { useI18n } from 'vue-i18n'

export type DateFormatKey = 'short' | 'long' | 'time'
export type NumberFormatKey = 'decimal' | 'integer' | 'percent'

type DateInput = Date | string | number | null | undefined
type NumberInput = number | string | null | undefined

function toDate(v: DateInput): Date | null {
  if (v === null || v === undefined || v === '') return null
  const d = v instanceof Date ? v : new Date(v)
  return isNaN(d.getTime()) ? null : d
}

function toNumber(v: NumberInput): number | null {
  if (v === null || v === undefined || v === '') return null
  const n = typeof v === 'number' ? v : Number(v)
  return isNaN(n) ? null : n
}

function currentLocale(): string {
  const loc = (i18n.global.locale as unknown as { value?: string }).value
  return loc || 'ja'
}

// ── 标准函数（用全局 i18n，可在任意位置调用）─────────────────────────────
export function formatDate(v: DateInput, fmt: DateFormatKey = 'short'): string {
  const d = toDate(v)
  return d ? (i18n.global as any).d(d, fmt) : ''
}

export function formatDateTime(v: DateInput): string {
  return formatDate(v, 'long')
}

export function formatNumber(v: NumberInput, fmt: NumberFormatKey = 'decimal'): string {
  const n = toNumber(v)
  return n === null ? '' : (i18n.global as any).n(n, fmt)
}

/** 入参为比率（0.15 → 15%）。已是百分数请先 /100。 */
export function formatPercent(v: NumberInput): string {
  const n = toNumber(v)
  return n === null ? '' : (i18n.global as any).n(n, 'percent')
}

/**
 * 数量：locale 感知的「最多 maxFrac 位小数、无尾随零」（替代散落各处的
 * Number(n).toLocaleString('ja-JP', {maximumFractionDigits: N})）。空值返回 ''。
 */
export function formatQty(v: NumberInput, maxFrac = 4): string {
  const n = toNumber(v)
  if (n === null) return ''
  return new Intl.NumberFormat(currentLocale(), { maximumFractionDigits: maxFrac }).format(n)
}

/** 多币种：currency 为 ISO 4217 码（JPY/CNY/USD/...），默认 JPY。 */
export function formatCurrency(v: NumberInput, currency = 'JPY'): string {
  const n = toNumber(v)
  if (n === null) return ''
  return new Intl.NumberFormat(currentLocale(), { style: 'currency', currency }).format(n)
}

// ── 组合式（组件内，随 locale 切换自动重渲染）────────────────────────────
export function useFormat() {
  const { n, d, locale } = useI18n()
  return {
    formatDate: (v: DateInput, fmt: DateFormatKey = 'short') => {
      const dt = toDate(v)
      return dt ? d(dt, fmt) : ''
    },
    formatDateTime: (v: DateInput) => {
      const dt = toDate(v)
      return dt ? d(dt, 'long') : ''
    },
    formatNumber: (v: NumberInput, fmt: NumberFormatKey = 'decimal') => {
      const num = toNumber(v)
      return num === null ? '' : n(num, fmt)
    },
    formatPercent: (v: NumberInput) => {
      const num = toNumber(v)
      return num === null ? '' : n(num, 'percent')
    },
    formatCurrency: (v: NumberInput, currency = 'JPY') => {
      const num = toNumber(v)
      if (num === null) return ''
      return new Intl.NumberFormat(locale.value, { style: 'currency', currency }).format(num)
    },
  }
}
