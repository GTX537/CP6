import http from './http'

export const roleApi = {
  getList(params: { page: number; pageSize: number; keyword?: string }) {
    return http.get('/role', { params })
  },
  getAll() {
    return http.get('/role/all')
  },
  add(data: any) {
    return http.post('/role', data)
  },
  update(data: any) {
    return http.put('/role', data)
  },
  del(ids: number[]) {
    return http.delete('/role', { data: ids })
  },
  getRoleMenus(roleId: number) {
    return http.get(`/role/${roleId}/menus`)
  },
  saveRoleMenus(roleId: number, menuIds: number[]) {
    return http.post(`/role/${roleId}/menus`, menuIds)
  }
}
