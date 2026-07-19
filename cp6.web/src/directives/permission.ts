import type { Directive } from 'vue'
import { watch, type WatchStopHandle } from 'vue'
import { usePermissionStore } from '@/stores/permission'

const stopHandles = new WeakMap<HTMLElement, WatchStopHandle>()

function observePermission(el: HTMLElement, key?: string) {
  stopHandles.get(el)?.()
  stopHandles.delete(el)
  if (!key) return

  const store = usePermissionStore()
  if (store.loaded) {
    if (!store.has(key)) el.remove()
    return
  }

  const stop = watch(() => store.loaded, (loaded) => {
    if (!loaded) return
    stop()
    stopHandles.delete(el)
    if (!store.has(key)) el.remove()
  })
  stopHandles.set(el, stop)
}

/**
 * v-permission="'order:export'" —— 无该操作权则移除元素。
 * 注意：仅 UX 层；后端 [RequirePermission] 才是强校验。
 * store 未加载完成（loaded=false）时 fail-open（保留元素），避免首屏误删。
 */
export const permission: Directive<HTMLElement, string> = {
  mounted(el, binding) {
    observePermission(el, binding.value)
  },
  updated(el, binding) {
    if (binding.value !== binding.oldValue) observePermission(el, binding.value)
  },
  unmounted(el) {
    stopHandles.get(el)?.()
    stopHandles.delete(el)
  },
}
