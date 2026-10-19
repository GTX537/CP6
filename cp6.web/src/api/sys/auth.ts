import axios from 'axios'
import http from '../http'
import { browserLogin, logoutAuthentication, refreshAuthentication, changePasswordAuthentication } from '@/auth/browserAuthentication'
import { AUTH_EPOCH } from '@/auth/identity'

// S 类认证加固 T9：认证端点（token 已改 httpOnly Cookie，不再走 body）
export const authApi = {
  // 登录：成功后端 Set-Cookie 写 cp6_at/cp6_rt/cp6_csrf，返回用户信息（不含 token）
  login(data: { userName: string; password: string; tenantCode?: string }) {
    return browserLogin(data)
  },
  // 登出：后端清三 Cookie + 黑名单当前 access jti
  logout() {
    return logoutAuthentication()
  },
  // 刷新：读 cp6_rt Cookie（无 body），后端轮换并重写三 Cookie
  refresh() {
    return refreshAuthentication(localStorage.getItem(AUTH_EPOCH) || '')
  },
  // 改密：成功后后端吊销所有 refresh，需重新登录
  changePassword(data: { currentPassword: string; newPassword: string }) {
    return changePasswordAuthentication(data)
  },
  // #3 SSO（T9）：登录态画像（落地屏拿菜单/用户信息；同站 XHR 携 cp6_at Cookie）
  profile(options?: { passive?: boolean }) {
    // The login page may be anonymous. Its same-origin cookie probe must not
    // refresh, redirect, or toast on 401 through the protected-request client.
    if (options?.passive) {
      return axios.get('/api/auth/profile', { withCredentials: true, timeout: 10000 })
        .then(response => response.data)
    }
    return http.get('/auth/profile')
  }
}
