import http from '../http'
import type {
  ICreateSpaceFloorRequest,
  ICreateSpaceFloorResponse,
  ICreateSpaceVersionResponse,
  ISpaceModelDto,
  ISpaceSceneFloorDto,
  ISpaceVersionDto,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const root = '/space/design/v1'

export const designProjectApi = {
  getModel(siteId: string) {
    return http.get<unknown, ISpaceModelDto>(
      `${root}/sites/${encodeURIComponent(siteId)}/model`,
    )
  },

  getVersion(versionId: string) {
    return http.get<unknown, ISpaceVersionDto>(
      `${root}/versions/${encodeURIComponent(versionId)}`,
    )
  },

  getFloors(versionId: string) {
    return http.get<unknown, ISpaceSceneFloorDto[]>(
      `${root}/versions/${encodeURIComponent(versionId)}/floors`,
    )
  },

  createBlankVersion(
    siteId: string,
    name: string,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, ICreateSpaceVersionResponse>(
      `${root}/sites/${encodeURIComponent(siteId)}/versions`,
      {
        name,
        basedOnVersionId: null,
        createMode: 'Blank',
      },
      {
        headers: { 'Idempotency-Key': idempotencyKey },
      },
    )
  },

  createFloor(
    versionId: string,
    request: ICreateSpaceFloorRequest,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, ICreateSpaceFloorResponse>(
      `${root}/versions/${encodeURIComponent(versionId)}/floors`,
      request,
      {
        headers: { 'Idempotency-Key': idempotencyKey },
      },
    )
  },
}
