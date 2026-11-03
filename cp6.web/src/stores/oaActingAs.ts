/**
 * OA act-as 态机（per-tab sessionStorage 隔离）。
 * T10：仅存 { userId, userName }，无 token 重签/jti（比 platform.ts 更轻量）。
 */
const KEY = 'cp6_oa_acting_as'
export const ACTING_AS_EVENT = 'cp6-oa-acting-as-changed'

export interface ActingAs {
  userId: string
  userName: string
}

export function setActingAs(a: ActingAs): void {
  sessionStorage.setItem(KEY, JSON.stringify(a))
  window.dispatchEvent(new Event(ACTING_AS_EVENT))
}

export function getActingAs(): ActingAs | null {
  const s = sessionStorage.getItem(KEY)
  if (!s) return null
  try { return JSON.parse(s) as ActingAs } catch { return null }
}

export function clearActingAs(): void {
  sessionStorage.removeItem(KEY)
  window.dispatchEvent(new Event(ACTING_AS_EVENT))
}
