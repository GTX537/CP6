import http from '@/api/http'
import type {
  IApplySpaceElementCommandBatchResponse,
  ISpaceElementAttributeWriteDto,
  ISpaceSceneElementDto,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import type { EditorCommandInput } from '@/modules/space-design/commands/editorBatchCommands'

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

export interface EditorCommandEnvelope {
  schemaVersion: number
  commandBatchId: string
  clientInstanceId: string
  leaseId: string
  expectedFloorRevision: number
  commands: Array<EditorCommandInput & { commandId: string }>
}

export const designElementsApi = {
  update(
    versionId: string,
    floorLogicalId: string,
    expectedFloorRevision: number,
    clientInstanceId: string,
    leaseId: string,
    element: ISpaceSceneElementDto,
    payload: ElementPropertiesPayload,
  ) {
    const targetLogicalId = requireLogicalId(element)
    return designElementsApi.apply(
      versionId,
      floorLogicalId,
      expectedFloorRevision,
      clientInstanceId,
      leaseId,
      [
        {
          type: 'UpdateProperties',
          targetLogicalId,
          updateProperties: payload,
        },
      ],
    )
  },

  remove(
    versionId: string,
    floorLogicalId: string,
    expectedFloorRevision: number,
    clientInstanceId: string,
    leaseId: string,
    element: ISpaceSceneElementDto,
  ) {
    const targetLogicalId = requireLogicalId(element)
    return designElementsApi.apply(
      versionId,
      floorLogicalId,
      expectedFloorRevision,
      clientInstanceId,
      leaseId,
      [
        {
          type: 'DeleteObject',
          targetLogicalId,
        },
      ],
    )
  },

  apply(
    versionId: string,
    floorLogicalId: string,
    expectedFloorRevision: number,
    clientInstanceId: string,
    leaseId: string,
    commands: readonly EditorCommandInput[],
  ) {
    const envelope = designElementsApi.createEnvelope(
      expectedFloorRevision,
      clientInstanceId,
      leaseId,
      commands,
    )
    return designElementsApi.sendEnvelope(versionId, floorLogicalId, envelope)
  },

  createEnvelope(
    expectedFloorRevision: number,
    clientInstanceId: string,
    leaseId: string,
    commands: readonly EditorCommandInput[],
  ): EditorCommandEnvelope {
    return {
      schemaVersion: 1,
      commandBatchId: crypto.randomUUID(),
      clientInstanceId,
      leaseId,
      expectedFloorRevision,
      commands: commands.map((command) => ({
        ...command,
        commandId: crypto.randomUUID(),
      })),
    }
  },

  sendEnvelope(
    versionId: string,
    floorLogicalId: string,
    envelope: EditorCommandEnvelope,
  ) {
    return apply(versionId, floorLogicalId, envelope)
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
