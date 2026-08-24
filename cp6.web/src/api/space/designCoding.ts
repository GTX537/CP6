import http from '@/api/http'
import type {
  IApplySpaceLocationCodesRequest,
  IApplySpaceLocationCodesResponse,
  IPreviewSpaceLocationCodesRequest,
  IPreviewSpaceLocationCodesResponse,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const root = '/space/design/v1'

export type LocationCodingEnvelope = IApplySpaceLocationCodesRequest

export const designCodingApi = {
  preview(
    versionId: string,
    floorLogicalId: string,
    request: IPreviewSpaceLocationCodesRequest,
  ) {
    return http.post<unknown, IPreviewSpaceLocationCodesResponse>(
      `${root}/versions/${versionId}/floors/${floorLogicalId}/location-codes:preview`,
      request,
    )
  },

  createEnvelope(
    preview: IPreviewSpaceLocationCodesResponse,
    clientInstanceId: string,
    leaseId: string,
  ): LocationCodingEnvelope {
    return {
      schemaVersion: 1,
      commandBatchId: crypto.randomUUID(),
      clientInstanceId,
      leaseId,
      mode: preview.mode,
      scopeZoneLogicalId: preview.scopeZoneLogicalId,
      expectedFloorRevision: preview.baseFloorRevision,
      expectedContentRevision: preview.baseContentRevision,
      proposalHash: preview.proposalHash,
    }
  },

  apply(
    versionId: string,
    floorLogicalId: string,
    envelope: LocationCodingEnvelope,
  ) {
    return http.post<unknown, IApplySpaceLocationCodesResponse>(
      `${root}/versions/${versionId}/floors/${floorLogicalId}/location-codes:apply`,
      envelope,
    )
  },
}
