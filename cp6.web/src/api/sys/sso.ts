import http from '../http'
import { browserSso } from '@/auth/browserAuthentication'
import type { SsoConfig, SsoConfigInput, SsoConfigOperation } from '@/types/sys/sso'

// S 类 #3 SSO（T9）：SSO 授权发起 + 租户配置管理。
export const ssoApi = {
  // 按租户构建 OIDC authorize URL（后端 discovery + PKCE + state），返 { authorizeUrl }。匿名。
  authorize(tenantCode: string, returnUrl?: string) {
    return browserSso(tenantCode, returnUrl)
  },
  // 查当前租户 SSO 配置投影（无密文）。
  getConfig() {
    return http.get('/sys/sso-config') as Promise<SsoConfig>
  },
  // upsert 当前租户 SSO 配置（clientSecret 留空=不改）。
  setConfig(input: SsoConfigInput) {
    return http.put('/sys/sso-config', input) as Promise<SsoConfigOperation>
  },
  getOperation(id: string) {
    return http.get(`/sys/sso-config/operations/${encodeURIComponent(id)}`) as Promise<SsoConfigOperation>
  },
  closeOperation(id: string) {
    return http.post(`/sys/sso-config/operations/${encodeURIComponent(id)}/close`) as Promise<SsoConfigOperation>
  }
}
