import { describe, expect, it } from 'vitest'
import { safeOidcReturn } from './oidcReturn'

describe('OIDC login continuation', () => {
  it('permits only a local authorization endpoint', () => {
    expect(safeOidcReturn('/connect/authorize?client_id=CP6.Web&state=abc')).toBe('/connect/authorize?client_id=CP6.Web&state=abc')
    for (const value of ['https://evil.example/connect/authorize?', '//evil.example', '/\\evil.example', '/api/auth/logout', '/connect/authorize.evil?x=1', '/connect/authorize?x=1#evil', '/connect/authorize?x=\n']) {
      expect(safeOidcReturn(value)).toBeNull()
    }
  })
})
