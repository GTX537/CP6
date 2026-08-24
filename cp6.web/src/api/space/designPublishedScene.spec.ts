import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '@/api/http'
import {
  designPublishedSceneApi,
  indexPublishedViewerScene,
  publishedFloorId,
  toPublishedFloorView,
} from './designPublishedScene'
import type {
  ISpaceDesignSceneDto,
  ISpacePublishedViewerSceneDto,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

vi.mock('@/api/http', () => ({
  default: { get: vi.fn() },
}))

describe('designPublishedSceneApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('uses only the Design V1 Published scene endpoint', () => {
    designPublishedSceneApi.get('site/a')
    expect(http.get).toHaveBeenCalledWith(
      '/space/design/v1/sites/site%2Fa/published-scene',
    )
  })

  it('derives runtime floor identity from the immutable logical id', () => {
    const scene = {
      siteId: 'site-1',
      floor: {
        revision: { logicalId: 'floor-1' },
        level: 2,
        floorCode: 'F2',
        name: 'Second floor',
        height: 6000,
      },
    } as unknown as ISpaceDesignSceneDto
    expect(publishedFloorId(scene)).toBe('floor-1')
    expect(toPublishedFloorView(scene)).toMatchObject({
      id: 'floor-1',
      siteId: 'site-1',
      level: 2,
      floorCode: 'F2',
      floorName: 'Second floor',
      height: 6000,
    })
  })

  it('rejects a Draft floor injected into the Published aggregate', () => {
    expect(() => indexPublishedViewerScene({
      schemaVersion: 1,
      authority: 'DesignRevision',
      runtimeOverlayIncluded: false,
      siteId: 'site-1',
      publishedVersionId: 'published-1',
      contentRevision: 7,
      contentHash: 'hash-1',
      floors: [{
        modelVersionId: 'draft-1',
        siteId: 'site-1',
        versionStatus: 'Draft',
        contentRevision: 7,
        contentHash: 'hash-1',
        runtimeOverlayIncluded: false,
        floor: { revision: { logicalId: 'floor-1' } },
      }],
    } as unknown as ISpacePublishedViewerSceneDto, 'site-1')).toThrow('floor authority')
  })

  it('rejects a floor from a different Published content revision', () => {
    expect(() => indexPublishedViewerScene({
      schemaVersion: 1,
      authority: 'DesignRevision',
      runtimeOverlayIncluded: false,
      siteId: 'site-1',
      publishedVersionId: 'published-1',
      contentRevision: 7,
      contentHash: 'hash-1',
      floors: [{
        modelVersionId: 'published-1',
        siteId: 'site-1',
        versionStatus: 'Published',
        contentRevision: 8,
        contentHash: 'hash-1',
        runtimeOverlayIncluded: false,
        floor: { revision: { logicalId: 'floor-1' } },
      }],
    } as unknown as ISpacePublishedViewerSceneDto, 'site-1')).toThrow('floor authority')
  })
})
