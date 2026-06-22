import http from '../http'

// S 类认证加固 T9：认证端点（token 已改 httpOnly Cookie，不再走 body）
export const authApi = {
  // 登录：成功后端 Set-Cookie 写 cp6_at/cp6_rt/cp6_csrf，返回用户信息（不含 token）
  login(data: { userName: string; password: string; tenantCode?: string }) {
    return http.post('/auth/login', data)
  },
  // 登出：后端清三 Cookie + 黑名单当前 access jti
  logout() {
    return http.post('/auth/logout')
  },
  // 刷新：读 cp6_rt Cookie（无 body），后端轮换并重写三 Cookie
  refresh() {
    return http.post('/auth/refresh')
  },
  // 改密：成功后后端吊销所有 refresh，需重新登录
  changePassword(data: { currentPassword: string; newPassword: string }) {
    return http.post('/auth/change-password', data)
  }
}
