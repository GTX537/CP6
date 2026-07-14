// 年历页纯逻辑（无 Vue 依赖，可单测）：日期例外态映射 + Cp 色调（零硬编码色）。
import type { Tone } from '@/components/base/CpTag.vue'
import type { WorkCalendarDay } from '@/api/oa/workCalendar'

/** 某日显示态：
 *  - makeup   补班（例外 isWorkday=true）——周末却上班，醒目
 *  - closed   假日（例外 isWorkday=false）——平日却休 / 法定假日
 *  - weekend  默认休（周六日，无例外）
 *  - normal   默认工作日（周一~五，无例外，无标签）
 */
export type DayKind = 'makeup' | 'closed' | 'weekend' | 'normal'

export interface DayState {
  kind: DayKind
  note?: string | null
}

/** yyyy-MM-dd（本地日历键，避开 toISOString 的 UTC 偏移）。 */
export function ymd(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}

/** 例外行数组 → date(yyyy-MM-dd) → 例外 的查表。 */
export function toExceptionMap(items: WorkCalendarDay[]): Map<string, WorkCalendarDay> {
  const m = new Map<string, WorkCalendarDay>()
  for (const it of items) m.set(it.date.slice(0, 10), it)
  return m
}

/** 反转态映射：给定日期 + 例外表 → 显示态。例外优先，否则周末=休/平日=工。 */
export function stateForDate(dayKey: string, ex: Map<string, WorkCalendarDay>): DayState {
  const hit = ex.get(dayKey)
  if (hit) return { kind: hit.isWorkday ? 'makeup' : 'closed', note: hit.note }
  const dow = new Date(dayKey + 'T00:00:00').getDay()   // 0=日 6=六
  return { kind: dow === 0 || dow === 6 ? 'weekend' : 'normal' }
}

/** DayKind → CpTag Tone（零硬编码色；normal 无标签故映射 muted 兜底不渲染）。 */
export function dayTone(kind: DayKind): Tone {
  switch (kind) {
    case 'makeup':  return 'warn'    // 补班：警示色（周末上班的例外）
    case 'closed':  return 'info'    // 假日：信息色
    case 'weekend': return 'muted'   // 默认周末休：弱化
    default:        return 'muted'   // normal：不渲染标签
  }
}

/** normal（默认工作日）不渲染状态标签；其余三态渲染。 */
export function hasTag(kind: DayKind): boolean {
  return kind !== 'normal'
}
