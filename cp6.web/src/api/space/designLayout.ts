import http from '@/api/http'
import type {
  IApplySpaceLayoutCommandBatchRequest,
  IApplySpaceLayoutCommandBatchResponse,
  ISpaceCreateLayoutAisleDto,
  ISpaceCreateLayoutRackDto,
  ISpaceCreateLayoutZoneDto,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const root = '/space/design/v1'

export interface LayoutCommandEnvelope
  extends Omit<IApplySpaceLayoutCommandBatchRequest, 'commands'> {
  commands: LayoutCommandInput[]
}

export interface LayoutCommandInput {
  commandId: string
  type: string
  targetLogicalId: string
  createZone?: ISpaceCreateLayoutZoneDto
  createAisle?: ISpaceCreateLayoutAisleDto
  createRack?: ISpaceCreateLayoutRackDto
}

export const designLayoutApi = {
  apply(
    versionId: string,
    floorLogicalId: string,
    expectedFloorRevision: number,
    expectedContentRevision: number,
    clientInstanceId: string,
    leaseId: string,
    commands: readonly LayoutCommandInput[],
  ) {
    const envelope = designLayoutApi.createEnvelope(
      expectedFloorRevision,
      expectedContentRevision,
      clientInstanceId,
      leaseId,
      commands,
    )
    return designLayoutApi.sendEnvelope(
      versionId,
      floorLogicalId,
      envelope,
    )
  },

  createEnvelope(
    expectedFloorRevision: number,
    expectedContentRevision: number,
    clientInstanceId: string,
    leaseId: string,
    commands: readonly LayoutCommandInput[],
  ): LayoutCommandEnvelope {
    return {
      schemaVersion: 1,
      commandBatchId: crypto.randomUUID(),
      clientInstanceId,
      leaseId,
      expectedFloorRevision,
      expectedContentRevision,
      commands: commands.map((command) => ({
        ...command,
        commandId: command.commandId || crypto.randomUUID(),
      })),
    }
  },

  sendEnvelope(
    versionId: string,
    floorLogicalId: string,
    envelope: LayoutCommandEnvelope,
  ) {
    return http.post<unknown, IApplySpaceLayoutCommandBatchResponse>(
      `${root}/versions/${versionId}/floors/${floorLogicalId}/layout-commands`,
      envelope,
    )
  },
}
