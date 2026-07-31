import type { ISpaceDesignSceneDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import {
  buildParametricRenderPlan,
  type DataPoint3,
} from '@/space-viewer/design/ParametricRenderPlan'

export interface ElementCanvasRect {
  kind: 'rect'
  logicalId: string
  ownerKind: 'Element' | 'Rack'
  elementType: string
  centerX: number
  centerY: number
  width: number
  depth: number
  rotationZ: number
}

export interface ElementCanvasPolygon {
  kind: 'polygon'
  logicalId: string
  ownerKind: 'Element'
  elementType: string
  points: readonly { x: number; y: number }[]
}

export type ElementCanvasDrawable =
  | ElementCanvasRect
  | ElementCanvasPolygon

export function buildElementCanvasPlan(
  scene: ISpaceDesignSceneDto,
): readonly ElementCanvasDrawable[] {
  const plan = buildParametricRenderPlan(scene)
  const drawables: ElementCanvasDrawable[] = []

  for (const box of plan.boxes) {
    if (box.lifecycleState !== 'Active') {
      continue
    }
    const elementType =
      box.ownerKind === 'Rack' && box.materialRole === 'rack-envelope'
        ? 'Rack'
        : box.ownerKind === 'Element'
          ? box.elementType
          : undefined
    if (!elementType) continue
    drawables.push({
      kind: 'rect',
      logicalId: box.logicalId,
      ownerKind: box.ownerKind as 'Element' | 'Rack',
      elementType,
      centerX: box.center.x,
      centerY: box.center.y,
      width: box.size.width,
      depth: box.size.depth,
      rotationZ: box.rotationZ,
    })
  }

  for (const polygon of plan.polygons) {
    if (
      polygon.ownerKind !== 'Element' ||
      polygon.lifecycleState !== 'Active' ||
      !polygon.elementType
    ) {
      continue
    }
    drawables.push({
      kind: 'polygon',
      logicalId: polygon.logicalId,
      ownerKind: 'Element',
      elementType: polygon.elementType,
      points: polygon.outer.map((point) =>
        localToWorld(point, polygon.origin, polygon.rotationZ),
      ),
    })
  }

  return drawables
}

function localToWorld(
  point: DataPoint3,
  origin: DataPoint3,
  rotationZ: number,
): { x: number; y: number } {
  const radians = (rotationZ * Math.PI) / 180
  const cosine = Math.cos(radians)
  const sine = Math.sin(radians)
  return {
    x: origin.x + point.x * cosine - point.y * sine,
    y: origin.y + point.x * sine + point.y * cosine,
  }
}
