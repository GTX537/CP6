import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, expect, it, vi } from 'vitest'
import { usePermissionStore } from './permission'
import { AUTH_EPOCH } from '@/auth/identity'
const api = vi.hoisted(() => ({ myActions: vi.fn() }))
vi.mock('@/api/sys/rolePerm', () => ({ rolePermApi: api }))
beforeEach(() => { setActivePinia(createPinia()); localStorage.clear(); vi.clearAllMocks() })

it('does not restore the previous identity permissions when a read completes after reset', async () => {
  let finish!: (keys: string[]) => void
  api.myActions.mockReturnValue(new Promise(resolve => { finish = resolve }))
  const permissions = usePermissionStore(); const pending = permissions.loadMyActions()
  permissions.reset(); finish(['user:edit']); await pending
  expect(permissions.has('user:edit')).toBe(false)
  expect(permissions.loaded).toBe(false)
})

it('shares one current permission receipt across concurrent navigation requests', async () => {
  let finish!: (keys: string[]) => void
  api.myActions.mockReturnValue(new Promise<string[]>(resolve => { finish = resolve }))
  const permissions = usePermissionStore()
  const first = permissions.loadMyActions(); const second = permissions.loadMyActions()
  try { expect(api.myActions).toHaveBeenCalledTimes(1) }
  finally { finish(['erp-product:query']); await Promise.all([first, second]) }
  expect(permissions.loaded).toBe(true); expect(permissions.has('erp-product:query')).toBe(true)
})

it('does not let an older request ending clear the next identity request', async () => {
  let oldDone!: (keys: string[]) => void; let newDone!: (keys: string[]) => void
  api.myActions.mockReturnValueOnce(new Promise<string[]>(resolve => { oldDone = resolve }))
    .mockReturnValueOnce(new Promise<string[]>(resolve => { newDone = resolve }))
  const permission = usePermissionStore(); const old = permission.loadMyActions()
  permission.reset(); const next = permission.loadMyActions()
  oldDone(['old:edit']); await old
  const shared = permission.loadMyActions(); expect(api.myActions).toHaveBeenCalledTimes(2)
  newDone(['new:query']); await Promise.all([next, shared])
  expect(permission.has('old:edit')).toBe(false); expect(permission.has('new:query')).toBe(true)
})

it('rejects an obsolete identity receipt even before the root boundary can reset the store', async () => {
  let finish!: (keys: string[]) => void
  api.myActions.mockReturnValue(new Promise<string[]>(resolve => { finish = resolve }))
  const permission = usePermissionStore(); const pending = permission.loadMyActions()
  localStorage.setItem(AUTH_EPOCH, 'replacement-epoch'); finish(['old:edit']); await pending
  expect(permission.loaded).toBe(false); expect(permission.has('old:edit')).toBe(false)
})
