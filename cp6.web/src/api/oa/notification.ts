import http from '../http'
import type { NotificationItem, NotificationPage } from '../../types/oa/notification'

interface LegacyNotificationList {
  code: number
  message: string
  data: NotificationItem[]
  watermark?: string
}

export const notificationApi = {
  page: async (take = 10, signal?: AbortSignal): Promise<NotificationPage> =>
    (await http.get('/oa/v2/notifications', { params: { take }, signal }) as unknown as { data: NotificationPage }).data,

  count: async (signal?: AbortSignal): Promise<number> =>
    (await http.get('/oa/v2/notifications/unread-count', { signal }) as unknown as { data: { count: number } }).data.count,

  mark: (id: string, signal?: AbortSignal) =>
    http.post('/oa/v2/notifications/' + encodeURIComponent(id) + '/read', {}, { signal }),

  readThrough: (watermark: string, signal?: AbortSignal) =>
    http.post('/oa/v2/notifications/read-through', { watermark }, { signal }),

  openTask: async (task: string, signal?: AbortSignal): Promise<{ contextKind?: string }> =>
    (await http.get('/oa/v2/tasks/' + encodeURIComponent(task) + '/action-context', { signal }) as unknown as
      { data: { contextKind?: string } }).data,

  list: (unreadOnly?: boolean, page?: number, pageSize?: number): Promise<LegacyNotificationList> =>
    http.get('/oa/notification/list', { params: { unreadOnly, page, pageSize } }) as unknown as Promise<LegacyNotificationList>,

  unreadCount: () =>
    http.get<number>('/oa/notification/unread-count'),

  read: (id: string) =>
    http.post('/oa/notification/read', { id }),

  readAll: (watermark: string) =>
    http.post('/oa/notification/read-all', { watermark }),
}
