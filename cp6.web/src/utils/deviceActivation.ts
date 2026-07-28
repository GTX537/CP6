export type DeviceActivationPlatform = 'Android' | 'Windows'
export type ScannerHidTerminator = 'Enter' | 'Tab' | 'None'

export interface DeviceActivationPayloadInput {
  server: string
  tenant: string
  token: string
  platform: DeviceActivationPlatform
  scanPrefix?: string
  scanSuffix?: string
  scanTerminator?: ScannerHidTerminator
  scanDuplicateMs?: number
}

export function buildDeviceActivationPayload(input: DeviceActivationPayloadInput) {
  const query = new URLSearchParams({
    server: input.server.trim(),
    tenant: input.tenant.trim(),
    token: input.token,
  })

  if (input.platform === 'Android') {
    if (input.scanPrefix) query.set('scanPrefix', input.scanPrefix)
    if (input.scanSuffix) query.set('scanSuffix', input.scanSuffix)
    query.set('scanTerminator', input.scanTerminator ?? 'Enter')
    query.set('scanDuplicateMs', String(input.scanDuplicateMs ?? 750))
  }

  return `cp6-activate://device?${query}`
}
