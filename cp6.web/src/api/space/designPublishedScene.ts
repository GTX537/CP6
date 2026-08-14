import http from '@/api/http'
import type {
  ISpaceDesignSceneDto,
  ISpacePublishedViewerSceneDto,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import type { FloorVO } from '@/types/space/scene'

const root = '/space/design/v1'

export const designPublishedSceneApi = {
  get(siteId: string) {
    return http.get<unknown, ISpacePublishedViewerSceneDto>(
      `${root}/sites/${encodeURIComponent(siteId)}/published-scene`,
    )
  },
}

export interface PublishedSceneIndex {
  scenes: ReadonlyMap<string, ISpaceDesignSceneDto>
  floors: FloorVO[]
}

export function indexPublishedViewerScene(
  snapshot: ISpacePublishedViewerSceneDto,
  requestedSiteId: string,
): PublishedSceneIndex {
  if (
    snapshot.schemaVersion !== 1
    || snapshot.authority !== 'DesignRevision'
    || snapshot.runtimeOverlayIncluded !== false
    || snapshot.siteId !== requestedSiteId
    || !snapshot.publishedVersionId
  ) {
    throw new Error('Published viewer scene authority is invalid.')
  }

  const scenes = new Map<string, ISpaceDesignSceneDto>()
  for (const scene of snapshot.floors ?? []) {
    if (
      scene.modelVersionId !== snapshot.publishedVersionId
      || scene.siteId !== requestedSiteId
      || scene.versionStatus !== 'Published'
      || scene.contentRevision !== snapshot.contentRevision
      || scene.contentHash !== snapshot.contentHash
      || scene.runtimeOverlayIncluded !== false
    ) {
      throw new Error('Published viewer floor authority is invalid.')
    }
    const floorId = publishedFloorId(scene)
    if (scenes.has(floorId)) {
      throw new Error(`Published viewer floor ${floorId} is duplicated.`)
    }
    scenes.set(floorId, scene)
  }
  const floors = [...scenes.values()]
    .map(toPublishedFloorView)
    .sort((left, right) =>
      left.level - right.level
      || left.floorCode.localeCompare(right.floorCode))
  return { scenes, floors }
}

export function publishedFloorId(scene: ISpaceDesignSceneDto): string {
  const id = scene.floor?.revision?.logicalId
  if (!id) throw new Error('Published scene floor identity is missing.')
  return id
}

export function toPublishedFloorView(scene: ISpaceDesignSceneDto): FloorVO {
  const floor = scene.floor
  if (!floor) throw new Error('Published scene floor is missing.')
  return {
    id: publishedFloorId(scene),
    siteId: scene.siteId ?? '',
    level: floor.level ?? 0,
    floorCode: floor.floorCode ?? '',
    floorName: floor.name ?? '',
    height: floor.height ?? 0,
    underlayScale: floor.underlayScale,
    underlayOffsetX: floor.underlayOffsetX ?? 0,
    underlayOffsetY: floor.underlayOffsetY ?? 0,
    originX: 0,
    originY: 0,
  }
}
