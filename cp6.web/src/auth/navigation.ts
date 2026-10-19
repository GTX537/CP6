import router, { addDynamicRoutes } from '@/router'
import { usePlatformStore } from '@/stores/platform'
import { resumeOidcReturn } from '@/views/oidcReturn'
import { authenticationSnapshot, requireCurrentProfile } from './browserAuthentication'
import { AuthenticationChangedError } from './identity'

export function navigateAuthentication(profile: any): void {
  if (profile.twoFactorRequired) {
    const current = authenticationSnapshot()
    if (current.phase !== 'pending') throw new AuthenticationChangedError()
    void router.replace(current.purpose === '2fa_enroll' ? '/sys/2fa-enroll' : '/sys/2fa-challenge')
    return
  }
  requireCurrentProfile(profile)
  usePlatformStore().refreshFlag()
  addDynamicRoutes(profile.menus || [])
  if (!profile.mustChangePassword && resumeOidcReturn()) return
  void router.replace(profile.mustChangePassword ? '/sys/change-password' : '/')
}
