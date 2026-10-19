import { defineStore } from 'pinia'
import { ref } from 'vue'
import { IMPERSONATION } from '@/auth/identity'

/**
 * 多租户合规 #5（T9）平台区 store。
 * R8 态机设计：
 *  - isPlatformAdmin 用 localStorage（cp6_isPlatformAdmin）：登录时写入，跨标签共享。
 *  - impersonating 随共享 Cookie 身份同步到所有标签页；不能声称每标签隔离。
 * 前端隐藏/横幅只是 UX，真闸门在后端 [RequirePlatformAdmin]。
 */
export interface ImpersonationState {
  tenantName: string
  userName: string
  expiresAt: number
}

export const usePlatformStore = defineStore('platform', () => {
  const isPlatformAdmin = ref(localStorage.getItem('cp6_isPlatformAdmin') === '1')
  const impersonating = ref<ImpersonationState | null>(
    JSON.parse(localStorage.getItem(IMPERSONATION) || 'null')
  )

  function setImpersonation(data: ImpersonationState) {
    impersonating.value = data
    localStorage.setItem(IMPERSONATION, JSON.stringify(data))
  }

  function clearImpersonation() {
    impersonating.value = null
    localStorage.removeItem(IMPERSONATION)
  }

  /** 登录/登出后从 localStorage 重新同步标志（LoginView 写入后调用）。 */
  function refreshFlag() {
    isPlatformAdmin.value = localStorage.getItem('cp6_isPlatformAdmin') === '1'
    impersonating.value = JSON.parse(localStorage.getItem(IMPERSONATION) || 'null')
  }

  return { isPlatformAdmin, impersonating, setImpersonation, clearImpersonation, refreshFlag }
})
