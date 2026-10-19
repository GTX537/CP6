import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, expect, it, vi } from 'vitest'
import { usePermissionStore } from './permission'
const api = vi.hoisted(() => ({ myActions: vi.fn() }))
vi.mock('@/api/sys/rolePerm', () => ({ rolePermApi: api }))
beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

it('does not restore the previous identity permissions when a read completes after reset', async () => {
  let finish!: (keys: string[]) => void
  api.myActions.mockReturnValue(new Promise(resolve => { finish = resolve }))
  const permissions = usePermissionStore(); const pending = permissions.loadMyActions()
  permissions.reset(); finish(['user:edit']); await pending
  expect(permissions.has('user:edit')).toBe(false)
  expect(permissions.loaded).toBe(false)
})
