import http from './http'

export const menuApi = {
  getAll() {
    return http.get('/menu')
  },
  add(data: any) {
    return http.post('/menu', data)
  },
  update(data: any) {
    return http.put('/menu', data)
  },
  del(ids: number[]) {
    return http.delete('/menu', { data: ids })
  }
}
