import http from '../http'

export const langApi = {
  getList(params: { page: number; pageSize: number; keyword?: string }) {
    return http.get('/lang', { params })
  },
  add(data: any) {
    return http.post('/lang', data)
  },
  update(data: any) {
    return http.put('/lang', data)
  },
  del(ids: number[]) {
    return http.delete('/lang', { data: ids })
  },
  // i18n 优化 P4 发布模式
  publish() {
    return http.post('/lang/publish')
  },
  rollback(version: string) {
    return http.post('/lang/publish/rollback', { version })
  },
  manifest() {
    return http.get('/lang/manifest')
  },
}
