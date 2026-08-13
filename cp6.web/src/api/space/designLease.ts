import http from '@/api/http'

const root = '/space/design/v1'

export interface SpaceEditLease {
  modelVersionId: string
  floorLogicalId: string
  leaseId?: string
  ownerUserId?: string
  holderDisplayName?: string
  clientInstanceId?: string
  acquiredAtUtc?: string
  expiresAtUtc?: string
  lastRenewedAtUtc?: string
  isAvailable: boolean
  isOwnedByCurrentActor: boolean
  rowVersion?: string
}

function url(versionId: string, floorLogicalId: string) {
  return `${root}/versions/${versionId}/floors/${floorLogicalId}/lease`
}

export const designLeaseApi = {
  get(versionId: string, floorLogicalId: string) {
    return http.get<unknown, SpaceEditLease>(url(versionId, floorLogicalId))
  },

  acquire(versionId: string, floorLogicalId: string, clientInstanceId: string) {
    return http.post<unknown, SpaceEditLease>(url(versionId, floorLogicalId), {
      clientInstanceId,
    })
  },

  renew(
    versionId: string,
    floorLogicalId: string,
    leaseId: string,
    clientInstanceId: string,
  ) {
    return http.post<unknown, SpaceEditLease>(
      `${url(versionId, floorLogicalId)}/${leaseId}:renew`,
      { clientInstanceId },
    )
  },

  release(
    versionId: string,
    floorLogicalId: string,
    leaseId: string,
    clientInstanceId: string,
  ) {
    return http.post<unknown, SpaceEditLease>(
      `${url(versionId, floorLogicalId)}/${leaseId}:release`,
      { clientInstanceId },
    )
  },

  takeover(
    versionId: string,
    floorLogicalId: string,
    clientInstanceId: string,
    reason: string,
  ) {
    return http.post<unknown, SpaceEditLease>(
      `${root}/versions/${versionId}/floors/${floorLogicalId}/lease:takeover`,
      { clientInstanceId, reason },
    )
  },
}
