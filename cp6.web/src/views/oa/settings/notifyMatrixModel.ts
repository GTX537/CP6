/** 通知矩阵纯函数（wfs-inbox-ux §2.3）。解析语义逐位镜像后端 NotifyMatrix.IsEnabled。 */
export interface NotifyMatrixRow {
  typeKey: string
  typeValue: number
  inAppSupported: boolean
  emailSupported: boolean
}

export type MatrixState = Record<string, { inApp: boolean; email: boolean }>

/** 新类型键 → 遗留扁平键 映射（仅既有四类型有遗留形态；镜像后端 LegacyKeyMap）。 */
const LEGACY_KEY: Record<string, string> = {
  todoCreated: 'todo',
  flowApproved: 'approved',
  flowRejected: 'rejected',
  timeout: 'timeout',
}

export function buildMatrixState(prefsJson: string, rows: NotifyMatrixRow[]): MatrixState {
  let notify: Record<string, unknown> = {}
  try {
    const parsed = JSON.parse(prefsJson || '{}')
    if (parsed && typeof parsed.notify === 'object' && parsed.notify !== null) notify = parsed.notify
  } catch {
    notify = {} // 畸形 → 全默认 true
  }
  const state: MatrixState = {}
  for (const r of rows) {
    const cell = notify[r.typeKey]
    if (cell && typeof cell === 'object') {
      const c = cell as Record<string, unknown>
      state[r.typeKey] = { inApp: c.inApp !== false, email: c.email !== false }
    } else {
      const legacyKey = LEGACY_KEY[r.typeKey]
      if (legacyKey) {
        // 遗留扁平回落：事件键 false → 双关；全局 email false → 仅邮件关
        const eventOn = notify[legacyKey] !== false
        const emailOn = notify['email'] !== false
        state[r.typeKey] = { inApp: eventOn, email: eventOn && emailOn }
      } else {
        // 新类型无遗留形态：逐位镜像后端 IsEnabled（LegacyKeyMap 缺失 → 双通道无条件默认开）
        state[r.typeKey] = { inApp: true, email: true }
      }
    }
  }
  return state
}

/** 序列化为顶层 notify patch（配 prefApi.saveMerge，服务端合并保他键）。 */
export function toNotifyPatch(state: MatrixState): string {
  const notify: Record<string, { inApp: boolean; email: boolean }> = {}
  for (const [k, v] of Object.entries(state)) notify[k] = { inApp: v.inApp, email: v.email }
  return JSON.stringify({ notify })
}
