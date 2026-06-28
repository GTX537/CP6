/**
 * OA act-as 态机（per-tab sessionStorage 隔离）。
 * T10：仅存 { userId, userName }，无 token 重签/jti（比 platform.ts 更轻量）。
 */
const KEY = 'cp6_oa_acting_as'

export interface ActingAs {
  userId: string
  userName: string
}

export function setActingAs(a: ActingAs): void {
  sessionStorage.setItem(KEY, JSON.stringify(a))
}

export function getActingAs(): ActingAs | null {
  const s = sessionStorage.getItem(KEY)
  if (!s) return null
  try { return JSON.parse(s) as ActingAs } catch { return null }
}

export function clearActingAs(): void {
  sessionStorage.removeItem(KEY)
}
