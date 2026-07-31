import http from '../http'
import type {
  IAttachSpaceUnderlayResponse,
  ISaveSpaceUnderlayCalibrationResponse,
  ISpaceUnderlayCalibrationDto,
  ISpaceDesignSceneDto,
  ISpaceFileDto,
  IUploadSpaceUnderlayResponse,
  SpaceSourceType,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const root = '/space/design/v1'

export interface SaveUnderlayCalibrationPayload {
  floorLogicalId: string
  pageNumber: number
  pixelWidth: number
  pixelHeight: number
  point1: UnderlayCalibrationPointPayload
  point2: UnderlayCalibrationPointPayload
  validationPoint: UnderlayCalibrationPointPayload
  expectedFloorRevision: number
}

interface UnderlayCalibrationPointPayload {
  pixelX: number
  pixelY: number
  worldX: number
  worldY: number
}

export const designUnderlayApi = {
  getScene(versionId: string, floorLogicalId: string) {
    return http.get<unknown, ISpaceDesignSceneDto>(
      `${root}/versions/${versionId}/floors/${floorLogicalId}/scene`,
    )
  },

  upload(versionId: string, file: File, sourceType: SpaceSourceType) {
    const form = new FormData()
    form.append('file', file, file.name)
    form.append('sourceType', sourceType.toString())
    return http.post<unknown, IUploadSpaceUnderlayResponse>(
      `${root}/versions/${versionId}/underlay-sources`,
      form,
      {
        timeout: 120_000,
      },
    )
  },

  getFile(versionId: string, fileId: string) {
    return http.get<unknown, ISpaceFileDto>(
      `${root}/versions/${versionId}/files/${fileId}`,
    )
  },

  getContent(versionId: string, sourceId: string) {
    return http.get<unknown, Blob>(
      `${root}/versions/${versionId}/sources/${sourceId}/content`,
      {
        responseType: 'blob',
        headers: {
          Accept: 'application/pdf,image/png,image/jpeg',
        },
      },
    )
  },

  attach(
    versionId: string,
    floorLogicalId: string,
    sourceId: string,
    expectedFloorRevision: number,
  ) {
    return http.put<unknown, IAttachSpaceUnderlayResponse>(
      `${root}/versions/${versionId}/floors/${floorLogicalId}/underlay`,
      {
        sourceId,
        expectedFloorRevision,
      },
      {
        headers: {
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    )
  },

  getCalibration(
    versionId: string,
    sourceId: string,
    floorLogicalId: string,
  ) {
    return http.get<unknown, ISpaceUnderlayCalibrationDto>(
      `${root}/versions/${versionId}/sources/${sourceId}/underlay-calibration`,
      {
        params: { floorLogicalId },
      },
    )
  },

  calibrate(
    versionId: string,
    sourceId: string,
    request: SaveUnderlayCalibrationPayload,
  ) {
    return http.post<unknown, ISaveSpaceUnderlayCalibrationResponse>(
      `${root}/versions/${versionId}/sources/${sourceId}/underlay-calibration`,
      request,
      {
        headers: {
          'Idempotency-Key': crypto.randomUUID(),
        },
      },
    )
  },
}
