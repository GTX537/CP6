import http from './http'

export const userApi = {
  getList(params: { page: number; pageSize: number; keyword?: string }) {
    return http.get('/user', { params })
  },
  add(data: any) {
    return http.post('/user', data)
  },
  update(data: any) {
    return http.put('/user', data)
  },
  del(ids: string[]) {
    return http.delete('/user', { data: ids })
  }
}
