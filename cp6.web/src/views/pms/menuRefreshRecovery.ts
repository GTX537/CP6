import { ref } from 'vue'

type ReadState = 'idle' | 'loading' | 'ready' | 'failed' | 'pending'

// A recovery owns only a read, never the command that preceded it.
export function createMenuReadRecovery(isCurrent: () => boolean) {
  const state = ref<ReadState>('idle')
  let generation = 0
  return {
    state,
    invalidate() { generation++; state.value = 'pending' },
    async run<T>(read: () => Promise<T>, apply: (value: T) => boolean | void) {
      if (!isCurrent()) return
      const request = ++generation
      state.value = 'loading'
      const current = () => isCurrent() && request === generation
      try {
        const value = await read()
        if (!current()) return
        state.value = apply(value) === false ? 'pending' : 'ready'
      } catch {
        if (current()) state.value = 'failed'
      }
    },
  }
}

interface NavigationMenu {
  id: number
  menuName: string
  routePath: string | null
  icon: string | null
  parentId: number | null
  orderNo: number
}

// Profile menus use id (the management API uses menuId). An absent/malformed
// collection must not erase the current navigation by becoming an empty array.
export function readNavigationMenus(profile: unknown): NavigationMenu[] {
  if (!profile || typeof profile !== 'object' || !('menus' in profile) || !Array.isArray(profile.menus)) {
    throw new Error('Invalid navigation profile')
  }
  const ids = new Map<number, number | null>()
  const nullableString = (value: unknown) => value === null || typeof value === 'string'
  for (const item of profile.menus) {
    if (!item || typeof item !== 'object' || !Number.isFinite(item.id) || typeof item.menuName !== 'string'
      || !nullableString(item.routePath) || !nullableString(item.icon) || !Number.isFinite(item.orderNo)
      || !(item.parentId === null || Number.isFinite(item.parentId)) || ids.has(item.id)) {
      throw new Error('Invalid navigation menu')
    }
    ids.set(item.id, item.parentId)
  }
  // Missing parents are allowed by the existing role projection; cycles are not.
  for (const id of ids.keys()) {
    const ancestors = new Set<number>()
    let parent: number | null | undefined = id
    while (parent !== null && parent !== undefined) {
      if (ancestors.has(parent)) throw new Error('Cyclic navigation menu')
      ancestors.add(parent)
      parent = ids.get(parent)
    }
  }
  return profile.menus
}
