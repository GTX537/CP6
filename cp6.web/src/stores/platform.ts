import { defineStore } from 'pinia'
import { ref } from 'vue'

/**
 * 多租户合规 #5（T9）平台区 store。
 * R8 态机设计：
 *  - isPlatformAdmin 用 localStorage（cp6_isPlatformAdmin）：登录时写入，跨标签共享。
 *  - impersonating 用 sessionStorage（cp6_impersonating）：**每标签隔离**——
 *    在某标签内 impersonation 期间另开新标签不受影响（R8 双标签隔离对抗）。
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
    JSON.parse(sessionStorage.getItem('cp6_impersonating') || 'null')
  )

  function setImpersonation(data: ImpersonationState) {
    impersonating.value = data
    sessionStorage.setItem('cp6_impersonating', JSON.stringify(data))
  }

  function clearImpersonation() {
    impersonating.value = null
    sessionStorage.removeItem('cp6_impersonating')
  }

  /** 登录/登出后从 localStorage 重新同步标志（LoginView 写入后调用）。 */
  function refreshFlag() {
    isPlatformAdmin.value = localStorage.getItem('cp6_isPlatformAdmin') === '1'
  }

  return { isPlatformAdmin, impersonating, setImpersonation, clearImpersonation, refreshFlag }
})
