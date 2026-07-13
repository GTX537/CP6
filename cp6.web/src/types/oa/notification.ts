/**
 * WfNotificationType — 对齐后端 WfNotificationType 常量
 * SignalR 事件名："WfNotification"
 * SignalR payload: { type: number, userId: string, instanceId?: string, taskId?: string, flowKey?: string }
 */
export const NotificationType = {
  TodoCreated:  1,
  FlowApproved: 2,
  FlowRejected: 3,
  Timeout:      4,
  BranchPruned: 5,
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
  isRead:      boolean
  createDate:  string
}
