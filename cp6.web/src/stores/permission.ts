import { defineStore } from 'pinia'
import { ref } from 'vue'
import { rolePermApi } from '@/api/sys/rolePerm'
import { identityStamp, requireIdentity } from '@/auth/identity'

/**
 * PUB 章02/04 前端权限 store。
 * actionKeys = 当前用户全部操作键（"menuKey:action"），供 v-permission 判定。
 * 注意：前端隐藏只是 UX，真正强校验在后端 [RequirePermission]（双保）。
 */
export const usePermissionStore = defineStore('permission', () => {
  const actionKeys = ref<Set<string>>(new Set())
  const loaded = ref(false)
  let generation = 0
  let inFlight: Promise<void> | undefined

  function loadMyActions(): Promise<void> {
    if (inFlight) return inFlight
    const expected = generation, stamp = identityStamp()
    const request = (async () => {
      try {
        const keys = await rolePermApi.myActions()
        if (expected !== generation) return
        requireIdentity(stamp)
        actionKeys.value = new Set(keys)
        loaded.value = true
      } catch {
        // A failed initial read stays unconfirmed. A later navigation can retry.
      }
    })()
    inFlight = request
    void request.finally(() => { if (inFlight === request) inFlight = undefined })
    return request
  }

  function has(key: string): boolean {
    return actionKeys.value.has(key)
  }

  function reset() {
    generation++
    inFlight = undefined
    actionKeys.value = new Set()
    loaded.value = false
  }

  return { actionKeys, loaded, loadMyActions, has, reset }
})
