import http from '../http'
import type { SsoConfig, SsoConfigInput } from '@/types/sys/sso'

// S 类 #3 SSO（T9）：SSO 授权发起 + 租户配置管理。
export const ssoApi = {
  // 按租户构建 OIDC authorize URL（后端 discovery + PKCE + state），返 { authorizeUrl }。匿名。
  authorize(tenantCode: string, returnUrl?: string) {
    return http.get('/auth/sso/authorize', { params: { tenantCode, returnUrl } }) as Promise<{ authorizeUrl: string }>
  },
  // 查当前租户 SSO 配置投影（无密文）。
  getConfig() {
    return http.get('/sys/sso-config') as Promise<SsoConfig>
  },
  // upsert 当前租户 SSO 配置（clientSecret 留空=不改）。
  setConfig(input: SsoConfigInput) {
    return http.put('/sys/sso-config', input)
  }
}
