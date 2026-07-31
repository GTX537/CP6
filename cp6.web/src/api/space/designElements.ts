import http from '@/api/http'
import type {
  IApplySpaceElementCommandBatchResponse,
  ISpaceElementAttributeWriteDto,
  ISpaceSceneElementDto,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const root = '/space/design/v1'

export interface ElementPropertiesPayload {
  geometryJson: string
  x: number
  y: number
  z: number
  rotationZ: number
  width: number
  height: number
  depth: number
  businessCode?: string
  linkedEntityType?: string
  linkedLogicalId?: string
  attributes: ISpaceElementAttributeWriteDto[]
}

export const designElementsApi = {
  update(
    versionId: string,
    floorLogicalId: string,
    expectedFloorRevision: number,
    clientInstanceId: string,
    element: ISpaceSceneElementDto,
    payload: ElementPropertiesPayload,
  ) {
    const targetLogicalId = requireLogicalId(element)
    return apply(versionId, floorLogicalId, {
      schemaVersion: 1,
      commandBatchId: crypto.randomUUID(),
      clientInstanceId,
      expectedFloorRevision,
      commands: [
        {
          commandId: crypto.randomUUID(),
          type: 'UpdateProperties',
          targetLogicalId,
          updateProperties: payload,
        },
      ],
    })
  },

  remove(
    versionId: string,
    floorLogicalId: string,
    expectedFloorRevision: number,
    clientInstanceId: string,
    element: ISpaceSceneElementDto,
  ) {
    const targetLogicalId = requireLogicalId(element)
    return apply(versionId, floorLogicalId, {
      schemaVersion: 1,
      commandBatchId: crypto.randomUUID(),
      clientInstanceId,
      expectedFloorRevision,
      commands: [
        {
          commandId: crypto.randomUUID(),
          type: 'DeleteObject',
          targetLogicalId,
        },
      ],
    })
  },
}

function requireLogicalId(element: ISpaceSceneElementDto) {
  const logicalId = element.revision?.logicalId
  if (!logicalId) {
    throw new Error('The selected element has no logical identity.')
  }
  return logicalId
}

function apply(
  versionId: string,
  floorLogicalId: string,
  request: object,
) {
  return http.post<unknown, IApplySpaceElementCommandBatchResponse>(
    `${root}/versions/${versionId}/floors/${floorLogicalId}/commands`,
    request,
  )
}
