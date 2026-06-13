import type { Directive } from 'vue'
import { usePermissionStore } from '@/stores/permission'

/**
 * v-permission="'order:export'" —— 无该操作权则移除元素。
 * 注意：仅 UX 层；后端 [RequirePermission] 才是强校验。
 * store 未加载完成（loaded=false）时 fail-open（保留元素），避免首屏误删。
 */
export const permission: Directive<HTMLElement, string> = {
  mounted(el, binding) {
    const store = usePermissionStore()
    const key = binding.value
    if (store.loaded && key && !store.has(key)) {
      el.parentNode?.removeChild(el)
    }
  }
}
