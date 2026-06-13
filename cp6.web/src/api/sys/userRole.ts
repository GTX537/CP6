import http from '../http'

// 后端 UserRoleController 返回 {code,message,data} 信封；http 拦截器已返回 body。
export const userRoleApi = {
  async get(userId: string): Promise<{ roleIds: number[]; primaryRoleId: number | null }> {
    const res: any = await http.get(`/pub/user-role/${userId}`)
    return res.data ?? { roleIds: [], primaryRoleId: null }
  },
  save(userId: string, roleIds: number[], primaryRoleId: number | null) {
    return http.put(`/pub/user-role/${userId}`, { roleIds, primaryRoleId })
  },
  migrate() {
    return http.post('/pub/user-role/migrate')
  },
}
