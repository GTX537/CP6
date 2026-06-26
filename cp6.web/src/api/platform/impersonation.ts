import http from '@/api/http'
import type { ImpersonationStartResult, ImpersonationEndResult } from '@/types/platform/platform'

// 多租户合规 #5（块② heart）impersonation API。
// start：以目标用户身份签 imp access（后端仅写 cp6_at，refresh 不动 → 隐式切出窗口）；返目标菜单。
// end：重签平台超管令牌（拉黑 imp jti），返平台超管自身菜单。
export const impersonationApi = {
  start(body: { tenantId: string; userId?: string | null; reason?: string | null }): Promise<ImpersonationStartResult> {
    return http.post('/platform/impersonation/start', body) as unknown as Promise<ImpersonationStartResult>
  },
  end(): Promise<ImpersonationEndResult> {
    return http.post('/platform/impersonation/end') as unknown as Promise<ImpersonationEndResult>
  }
}
