// @vitest-environment jsdom
import { describe, it, expect, beforeEach } from 'vitest'
import { setActingAs, getActingAs, clearActingAs } from './oaActingAs'

describe('oaActingAs', () => {
  beforeEach(() => sessionStorage.clear())

  it('set/get/clear roundtrip', () => {
    expect(getActingAs()).toBeNull()
    setActingAs({ userId: 'u1', userName: 'X 经理' })
    expect(getActingAs()?.userId).toBe('u1')
    expect(getActingAs()?.userName).toBe('X 经理')
    clearActingAs()
    expect(getActingAs()).toBeNull()
  })

  it('get returns null for malformed JSON', () => {
    sessionStorage.setItem('cp6_oa_acting_as', 'not-json')
    expect(getActingAs()).toBeNull()
  })
})
