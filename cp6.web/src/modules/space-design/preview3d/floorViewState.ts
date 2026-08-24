import {
  isDesignPreviewViewState,
  type DesignPreviewViewState,
} from './DesignScenePreview3D'

export type SpaceStudioProjectionMode = '2d' | '3d'

export interface SpaceStudioCanvasViewport {
  panX: number
  panY: number
  zoom: number
}

export interface SpaceStudioUnderlayViewState {
  visible: boolean
  opacityPercent: number
  locked: boolean
}

export interface SpaceStudioFloorViewState {
  schemaVersion: 1
  projectionMode: SpaceStudioProjectionMode
  canvasViewport: SpaceStudioCanvasViewport
  preview3d?: DesignPreviewViewState
  underlay?: SpaceStudioUnderlayViewState
}

export function spaceStudioFloorViewStorageKey(
  versionId: string,
  floorLogicalId: string,
): string {
  return `cp6-space-studio-floor-view-v1:${versionId}:${floorLogicalId}`
}

export function parseSpaceStudioFloorViewState(
  serialized: string | null,
): SpaceStudioFloorViewState | null {
  if (!serialized) return null
  try {
    const candidate = JSON.parse(serialized) as Partial<SpaceStudioFloorViewState>
    if (candidate.schemaVersion !== 1) return null
    if (candidate.projectionMode !== '2d' && candidate.projectionMode !== '3d') {
      return null
    }
    if (!validCanvasViewport(candidate.canvasViewport)) return null
    if (candidate.preview3d !== undefined
      && !isDesignPreviewViewState(candidate.preview3d)) return null
    if (candidate.underlay !== undefined
      && !validUnderlayViewState(candidate.underlay)) return null
    return {
      schemaVersion: 1,
      projectionMode: candidate.projectionMode,
      canvasViewport: { ...candidate.canvasViewport },
      ...(candidate.preview3d ? { preview3d: candidate.preview3d } : {}),
      ...(candidate.underlay ? { underlay: { ...candidate.underlay } } : {}),
    }
  } catch {
    return null
  }
}

function validUnderlayViewState(
  value: unknown,
): value is SpaceStudioUnderlayViewState {
  if (!value || typeof value !== 'object') return false
  const state = value as Partial<SpaceStudioUnderlayViewState>
  return typeof state.visible === 'boolean'
    && typeof state.locked === 'boolean'
    && typeof state.opacityPercent === 'number'
    && Number.isInteger(state.opacityPercent)
    && state.opacityPercent >= 0
    && state.opacityPercent <= 100
}

function validCanvasViewport(value: unknown): value is SpaceStudioCanvasViewport {
  if (!value || typeof value !== 'object') return false
  const viewport = value as Partial<SpaceStudioCanvasViewport>
  return finiteViewportCoordinate(viewport.panX)
    && finiteViewportCoordinate(viewport.panY)
    && typeof viewport.zoom === 'number'
    && Number.isFinite(viewport.zoom)
    && viewport.zoom >= 0.001
    && viewport.zoom <= 1
}

function finiteViewportCoordinate(value: unknown): value is number {
  return typeof value === 'number'
    && Number.isFinite(value)
    && Math.abs(value) <= 1_000_000_000
}
