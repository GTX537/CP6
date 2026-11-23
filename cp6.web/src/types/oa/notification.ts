/**
 * WfNotificationType — 对齐后端 WfNotificationType 常量
 * SignalR 事件名："WfNotification"
 * SignalR 只通知刷新，持久收件由当前本人列表读取。
 */
export const NotificationType = {
  TodoCreated:  1,
  FlowApproved: 2,
  FlowRejected: 3,
  Timeout:      4,
  BranchPruned: 5,
  FormSubmitted: 6,
  TaskHandled: 7,
} as const

export type NotificationType = typeof NotificationType[keyof typeof NotificationType]

export interface NotificationItem {
  id:          string
  type:        NotificationType
  title:       string
  body:        string
  instanceId?: string
  taskId?:     string
  flowKey?:    string
  inAppDelivery?: string | null
  emailDelivery?: string | null
  readAt?: string | null
  isRead:      boolean
  createDate:  string
}

export interface NotificationPage {
  items: NotificationItem[]
  asOf: string
  watermark: string
  nextCursor: string | null
}

export interface NotificationPreferenceRow {
  typeKey: string
  typeValue: number
  inAppSupported: boolean
  emailSupported: boolean
}

export type NotificationPreferenceCells = Record<string, { inApp: boolean; email: boolean }>

export interface NotificationPreferenceView {
  version: string
  supportVersion: string
  readState: 'Valid' | 'LegacyInvalidDefaulted'
  support: NotificationPreferenceRow[]
  effectivePrefs: NotificationPreferenceCells
}

export interface NotificationPreferenceCommand {
  commandId: string
  expectedVersion: string
  body: { expectedPreferenceVersion: string; patchMode: 'TOP_LEVEL_MERGE'; patch: { notify: NotificationPreferenceCells | null } }
}
