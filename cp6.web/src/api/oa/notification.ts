import http from '../http'
import type { NotificationItem } from '../../types/oa/notification'

export const notificationApi = {
  list: (unreadOnly?: boolean, page?: number, pageSize?: number) =>
    http.get<NotificationItem[]>('/oa/notification/list', { params: { unreadOnly, page, pageSize } }),

  unreadCount: () =>
    http.get<number>('/oa/notification/unread-count'),

  read: (id: string) =>
    http.post('/oa/notification/read', { id }),

  readAll: () =>
    http.post('/oa/notification/read-all'),
}
