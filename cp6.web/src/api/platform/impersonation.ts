import { switchBrowserIdentity } from '@/auth/browserAuthentication'
import type { ImpersonationStartResult, ImpersonationEndResult } from '@/types/platform/platform'

// 多租户合规 #5（块② heart）impersonation API。
// start/end 改变共享 Cookie；所有标签页经认证协调器同步身份与菜单。
// end：重签平台超管令牌（拉黑 imp jti），返平台超管自身菜单。
export const impersonationApi = {
  start(body: { tenantId: string; userId?: string | null; reason?: string | null }): Promise<ImpersonationStartResult> {
    return switchBrowserIdentity<ImpersonationStartResult>('start', body)
  },
  end(): Promise<ImpersonationEndResult> {
    return switchBrowserIdentity<ImpersonationEndResult>('end')
  }
}
