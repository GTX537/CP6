import { defineStore } from 'pinia'
import { ref } from 'vue'
import { rolePermApi } from '@/api/sys/rolePerm'

/**
 * PUB 章02/04 前端权限 store。
 * actionKeys = 当前用户全部操作键（"menuKey:action"），供 v-permission 判定。
 * 注意：前端隐藏只是 UX，真正强校验在后端 [RequirePermission]（双保）。
 */
export const usePermissionStore = defineStore('permission', () => {
  const actionKeys = ref<Set<string>>(new Set())
  const loaded = ref(false)

  async function loadMyActions() {
    try {
      const keys = await rolePermApi.myActions()
      actionKeys.value = new Set(keys)
      loaded.value = true
    } catch {
      // 未登录/失败：保持空集，不阻塞页面
    }
  }

  function has(key: string): boolean {
    return actionKeys.value.has(key)
  }

  function reset() {
    actionKeys.value = new Set()
    loaded.value = false
  }

  return { actionKeys, loaded, loadMyActions, has, reset }
})
