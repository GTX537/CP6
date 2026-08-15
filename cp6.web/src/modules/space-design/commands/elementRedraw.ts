import type {
  ISpaceSceneElementAttributeDto,
  ISpaceSceneElementDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { sceneElementPropertiesPayload } from '@/modules/space-design/panels/elementProperties'
import type { ReversibleCommandBatch } from './editorBatchCommands'

export interface ElementRedrawPoint {
  x: number
  y: number
}

export interface ElementRedrawPlan {
  logicalId: string
  vertexCount: number
  areaSquareMillimeters: number
  batch: ReversibleCommandBatch
}

const guidPattern =
  /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const minimumInt32 = -2_147_483_648
const maximumInt32 = 2_147_483_647
export const maximumElementRedrawVertices = 100

export function validateElementRedrawTarget(
  element: ISpaceSceneElementDto,
): void {
  requireGuid(element.revision?.logicalId)
  if (element.revision?.lifecycleState !== 'Active') {
    throw new Error('Only an active Draft element can be redrawn')
  }
  if (element.modelAssetId || element.modelAssetScope) {
    throw new Error('Asset-backed elements cannot be redrawn')
  }
  const geometry = parseGeometry(element.geometryJson)
  if (geometry.kind === 'asset') {
    throw new Error('Asset geometry cannot be redrawn')
  }
  requirePositiveInt32(element.height, 'element height')
  requireInt32(element.z, 'element z')
}

export function buildElementRedrawPlan(
  element: ISpaceSceneElementDto,
  attributes: readonly ISpaceSceneElementAttributeDto[],
  worldPoints: readonly ElementRedrawPoint[],
): ElementRedrawPlan {
  validateElementRedrawTarget(element)
  const logicalId = requireGuid(element.revision?.logicalId)
  const points = normalizePolygon(worldPoints)
  const minX = Math.min(...points.map((point) => point.x))
  const maxX = Math.max(...points.map((point) => point.x))
  const minY = Math.min(...points.map((point) => point.y))
  const maxY = Math.max(...points.map((point) => point.y))
  const width = requirePositiveInt32(maxX - minX, 'redraw width')
  const depth = requirePositiveInt32(maxY - minY, 'redraw depth')
  const height = requirePositiveInt32(element.height, 'element height')
  const z = requireInt32(element.z, 'element z')
  const signedArea = polygonSignedArea(points)
  const canonicalPoints = signedArea < 0 ? [...points].reverse() : points
  const localOuter = canonicalPoints.map((point) => ({
    x: requireInt32(point.x - minX, 'local redraw x'),
    y: requireInt32(point.y - minY, 'local redraw y'),
  }))
  const before = sceneElementPropertiesPayload(element, attributes)
  const after = {
    ...before,
    geometryJson: JSON.stringify({
      schemaVersion: 1,
      kind: 'polygon',
      outer: localOuter,
      holes: [],
      height,
    }),
    x: minX,
    y: minY,
    z,
    rotationZ: 0,
    width,
    height,
    depth,
  }

  return {
    logicalId,
    vertexCount: points.length,
    areaSquareMillimeters: Math.abs(signedArea),
    batch: {
      forward: [update(logicalId, after)],
      reverse: [update(logicalId, before)],
    },
  }
}

function normalizePolygon(
  worldPoints: readonly ElementRedrawPoint[],
): ElementRedrawPoint[] {
  if (worldPoints.length < 3 || worldPoints.length > maximumElementRedrawVertices) {
    throw new Error(
      `Redraw polygons require between 3 and ${maximumElementRedrawVertices} vertices`,
    )
  }
  const seen = new Set<string>()
  const points = worldPoints.map((point, index) => {
    const normalized = {
      x: requireInt32(point.x, `redraw point ${index + 1} x`),
      y: requireInt32(point.y, `redraw point ${index + 1} y`),
    }
    const key = `${normalized.x}:${normalized.y}`
    if (seen.has(key)) throw new Error('Redraw polygon vertices must be distinct')
    seen.add(key)
    return normalized
  })
  const signedArea = polygonSignedArea(points)
  if (!Number.isFinite(signedArea) || signedArea === 0) {
    throw new Error('Redraw polygon area must be greater than zero')
  }
  if (hasSelfIntersection(points)) {
    throw new Error('Redraw polygon cannot intersect itself')
  }
  return points
}

function polygonSignedArea(points: readonly ElementRedrawPoint[]): number {
  let doubledArea = 0
  for (let index = 0; index < points.length; index++) {
    const current = points[index]!
    const next = points[(index + 1) % points.length]!
    doubledArea += current.x * next.y - next.x * current.y
  }
  return doubledArea / 2
}

function hasSelfIntersection(points: readonly ElementRedrawPoint[]): boolean {
  for (let first = 0; first < points.length; first++) {
    const firstNext = (first + 1) % points.length
    for (let second = first + 1; second < points.length; second++) {
      const secondNext = (second + 1) % points.length
      if (
        first === second
        || firstNext === second
        || secondNext === first
      ) {
        continue
      }
      if (segmentsIntersect(
        points[first]!,
        points[firstNext]!,
        points[second]!,
        points[secondNext]!,
      )) return true
    }
  }
  return false
}

function segmentsIntersect(
  firstStart: ElementRedrawPoint,
  firstEnd: ElementRedrawPoint,
  secondStart: ElementRedrawPoint,
  secondEnd: ElementRedrawPoint,
): boolean {
  const a = orientation(firstStart, firstEnd, secondStart)
  const b = orientation(firstStart, firstEnd, secondEnd)
  const c = orientation(secondStart, secondEnd, firstStart)
  const d = orientation(secondStart, secondEnd, firstEnd)
  if (a === 0 && onSegment(firstStart, secondStart, firstEnd)) return true
  if (b === 0 && onSegment(firstStart, secondEnd, firstEnd)) return true
  if (c === 0 && onSegment(secondStart, firstStart, secondEnd)) return true
  if (d === 0 && onSegment(secondStart, firstEnd, secondEnd)) return true
  return Math.sign(a) !== Math.sign(b) && Math.sign(c) !== Math.sign(d)
}

function orientation(
  start: ElementRedrawPoint,
  end: ElementRedrawPoint,
  point: ElementRedrawPoint,
): number {
  return (end.x - start.x) * (point.y - start.y)
    - (end.y - start.y) * (point.x - start.x)
}

function onSegment(
  start: ElementRedrawPoint,
  point: ElementRedrawPoint,
  end: ElementRedrawPoint,
): boolean {
  return point.x >= Math.min(start.x, end.x)
    && point.x <= Math.max(start.x, end.x)
    && point.y >= Math.min(start.y, end.y)
    && point.y <= Math.max(start.y, end.y)
}

function update(targetLogicalId: string, updateProperties: unknown) {
  return {
    type: 'UpdateProperties' as const,
    targetLogicalId,
    updateProperties,
  }
}

function parseGeometry(value: string | undefined): Record<string, unknown> {
  let parsed: unknown
  try {
    parsed = JSON.parse(value ?? '')
  } catch {
    throw new Error('The selected element requires valid geometry JSON')
  }
  if (
    !parsed
    || typeof parsed !== 'object'
    || Array.isArray(parsed)
    || (parsed as Record<string, unknown>).schemaVersion !== 1
    || typeof (parsed as Record<string, unknown>).kind !== 'string'
  ) {
    throw new Error('Only geometry schemaVersion 1 can be redrawn')
  }
  return parsed as Record<string, unknown>
}

function requireGuid(value: unknown): string {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase() : ''
  if (!guidPattern.test(normalized)) {
    throw new Error('The redrawn element requires a logical identity')
  }
  return normalized
}

function requirePositiveInt32(value: unknown, field: string): number {
  const integer = requireInt32(value, field)
  if (integer <= 0) throw new Error(`${field} must be positive`)
  return integer
}

function requireInt32(value: unknown, field: string): number {
  if (
    !Number.isInteger(value)
    || (value as number) < minimumInt32
    || (value as number) > maximumInt32
  ) {
    throw new Error(`${field} must be a 32-bit integer millimeter value`)
  }
  return value as number
}
