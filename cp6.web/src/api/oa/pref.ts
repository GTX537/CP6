import http from '../http'
import type { NotificationPreferenceCommand, NotificationPreferenceView } from '../../types/oa/notification'

export const prefApi = {
  notificationPreferences: async (): Promise<NotificationPreferenceView> =>
    (await http.get('/oa/v2/notification-preferences') as unknown as { data: NotificationPreferenceView }).data,

  saveNotificationPreferences: async (command: NotificationPreferenceCommand) =>
    (await http.put('/oa/v2/notification-preferences', command) as unknown as
      { data: { commandId: string; result: NotificationPreferenceView } }).data,

  notificationPreferenceOriginal: async (id: string) =>
    (await http.get('/oa/v2/notification-preferences/commands/' + encodeURIComponent(id)) as unknown as
      { data: { commandId: string; result: NotificationPreferenceView } }).data,

  get:  ()                     => http.get('/oa/pref/get'),
  save: (prefsJson: string)    => http.post('/oa/pref/save', { prefsJson }),
  /** 服务端顶层键合并写（保他键；值 null=删键恢复默认） */
  saveMerge: (partialJson: string) => http.post('/oa/pref/save', { prefsJson: partialJson, merge: true }),
  /** 通知矩阵元数据（类型轴 + 通道支持标志） */
  notifyMatrix: () => http.get('/oa/pref/notify-matrix'),
}
