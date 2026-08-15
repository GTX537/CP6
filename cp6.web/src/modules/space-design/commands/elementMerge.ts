import type {
  ISpaceSceneElementAttributeDto,
  ISpaceSceneElementDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { sceneElementPropertiesPayload } from '@/modules/space-design/panels/elementProperties'
import type { ReversibleCommandBatch } from './editorBatchCommands'

export interface ElementMergePlan {
  survivorLogicalId: string
  sourceLogicalIds: string[]
  batch: ReversibleCommandBatch
}

interface GeometryObject extends Record<string, unknown> {
  schemaVersion: 1
  kind: string
}

interface PartEnvelope {
  width: number
  height: number
  depth: number
}

const guidPattern =
  /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function buildElementMergePlan(
  elements: readonly ISpaceSceneElementDto[],
  allAttributes: readonly ISpaceSceneElementAttributeDto[],
): ElementMergePlan {
  if (elements.length < 2 || elements.length > 20) {
    throw new Error('Select between 2 and 20 common elements to merge')
  }

  const survivor = elements[0]!
  const survivorLogicalId = requireLogicalId(survivor)
  const identities = new Set<string>()
  const survivorAttributes = attributesFor(survivor, allAttributes)
  const survivorMetadata = comparableMetadata(survivor)
  const parsed = elements.map((element) => {
    const logicalId = requireLogicalId(element)
    if (identities.has(logicalId)) {
      throw new Error('The merge selection contains duplicate elements')
    }
    identities.add(logicalId)
    if (element.revision?.lifecycleState !== 'Active') {
      throw new Error('Only active Draft elements can be merged')
    }
    if (element.modelAssetId || element.modelAssetScope) {
      throw new Error('Asset-backed elements cannot be merged')
    }
    if (comparableMetadata(element) !== survivorMetadata) {
      throw new Error(
        'Merged elements must share type, parent, business code and business link',
      )
    }
    const attributes = attributesFor(element, allAttributes)
    if (canonicalAttributes(attributes) !== canonicalAttributes(survivorAttributes)) {
      throw new Error('Merged elements must have identical design attributes')
    }
    const geometry = parseGeometry(element.geometryJson)
    if (geometry.kind === 'asset') {
      throw new Error('Asset geometry cannot be merged')
    }
    const placement = requirePlacement(element, geometry)
    const sourceId = optionalSourceGuid(element.revision?.sourceId, 'source identity')
    const sourceRef = optionalSourceRef(element.revision?.sourceRef)
    return { element, logicalId, geometry, placement, sourceId, sourceRef }
  })

  const totalParts = parsed.reduce(
    (total, item) => total + countGeometryParts(item.geometry),
    0,
  )
  if (totalParts > 100) {
    throw new Error('Merged geometry cannot contain more than 100 parts')
  }

  const origin = {
    x: Math.min(...parsed.map((item) => item.element.x!)),
    y: Math.min(...parsed.map((item) => item.element.y!)),
    z: Math.min(...parsed.map((item) => item.element.z!)),
  }
  const maximum = {
    x: Math.max(
      ...parsed.map((item) => item.element.x! + item.placement.width),
    ),
    y: Math.max(
      ...parsed.map((item) => item.element.y! + item.placement.depth),
    ),
    z: Math.max(
      ...parsed.map((item) => item.element.z! + item.placement.height),
    ),
  }
  const envelope = {
    width: requirePositiveSafeInteger(maximum.x - origin.x, 'merge width'),
    height: requirePositiveSafeInteger(maximum.z - origin.z, 'merge height'),
    depth: requirePositiveSafeInteger(maximum.y - origin.y, 'merge depth'),
  }
  const geometryJson = JSON.stringify({
    schemaVersion: 1,
    kind: 'group',
    parts: parsed.map(({
      element,
      logicalId,
      geometry,
      placement,
      sourceId,
      sourceRef,
    }) => ({
      sourceLogicalId: logicalId,
      ...(sourceId ? { sourceId } : {}),
      ...(sourceRef ? { sourceRef } : {}),
      x: requireSafeInteger(element.x! - origin.x, 'group part x'),
      y: requireSafeInteger(element.y! - origin.y, 'group part y'),
      z: requireSafeInteger(element.z! - origin.z, 'group part z'),
      rotationZ: requireRotation(element.rotationZ),
      width: placement.width,
      height: placement.height,
      depth: placement.depth,
      geometry,
    })),
  })

  const before = sceneElementPropertiesPayload(survivor, survivorAttributes)
  const after = {
    ...before,
    geometryJson,
    x: origin.x,
    y: origin.y,
    z: origin.z,
    rotationZ: 0,
    ...envelope,
  }
  const sourceLogicalIds = parsed.slice(1).map((item) => item.logicalId)

  return {
    survivorLogicalId,
    sourceLogicalIds,
    batch: {
      forward: [
        {
          type: 'UpdateProperties',
          targetLogicalId: survivorLogicalId,
          updateProperties: after,
        },
        ...sourceLogicalIds.map((targetLogicalId) => ({
          type: 'DeleteObject' as const,
          targetLogicalId,
        })),
      ],
      reverse: [
        {
          type: 'UpdateProperties',
          targetLogicalId: survivorLogicalId,
          updateProperties: before,
        },
        ...sourceLogicalIds.map((targetLogicalId) => ({
          type: 'RestoreLogicalObject' as const,
          targetLogicalId,
        })),
      ],
    },
  }
}

function attributesFor(
  element: ISpaceSceneElementDto,
  attributes: readonly ISpaceSceneElementAttributeDto[],
): ISpaceSceneElementAttributeDto[] {
  const revisionId = element.revision?.revisionId
  if (!revisionId) {
    throw new Error('Every merged element requires a revision identity')
  }
  return attributes.filter((attribute) => attribute.elementRevisionId === revisionId)
}

function canonicalAttributes(
  attributes: readonly ISpaceSceneElementAttributeDto[],
): string {
  return JSON.stringify(
    attributes
      .map((attribute) => ({
        namespace: normalize(attribute.namespace).toLowerCase(),
        key: normalize(attribute.key).toLowerCase(),
        valueType: normalize(attribute.valueType),
        value: attribute.value ?? '',
        unit: normalize(attribute.unit),
      }))
      .sort((left, right) =>
        left.namespace.localeCompare(right.namespace)
        || left.key.localeCompare(right.key)),
  )
}

function comparableMetadata(element: ISpaceSceneElementDto): string {
  return JSON.stringify({
    floorLogicalId: normalize(element.floorLogicalId).toLowerCase(),
    parentLogicalId: normalize(element.parentLogicalId).toLowerCase(),
    elementType: normalize(element.elementType),
    businessCode: normalize(element.businessCode),
    linkedEntityType: normalize(element.linkedEntityType),
    linkedLogicalId: normalize(element.linkedLogicalId).toLowerCase(),
  })
}

function parseGeometry(value: string | undefined): GeometryObject {
  let parsed: unknown
  try {
    parsed = JSON.parse(value ?? '')
  } catch {
    throw new Error('Every merged element requires valid geometry JSON')
  }
  if (
    !isRecord(parsed)
    || parsed.schemaVersion !== 1
    || typeof parsed.kind !== 'string'
  ) {
    throw new Error('Only geometry schemaVersion 1 can be merged')
  }
  return parsed as GeometryObject
}

function countGeometryParts(geometry: GeometryObject): number {
  if (geometry.kind !== 'group') return 1
  if (!Array.isArray(geometry.parts) || geometry.parts.length < 2) {
    throw new Error('Existing group geometry is invalid')
  }
  return 1 + geometry.parts.reduce((total, candidate) => {
    if (!isRecord(candidate) || !isRecord(candidate.geometry)) {
      throw new Error('Existing group geometry is invalid')
    }
    const nested = candidate.geometry
    if (nested.schemaVersion !== 1 || typeof nested.kind !== 'string') {
      throw new Error('Existing group geometry is invalid')
    }
    return total + countGeometryParts(nested as GeometryObject)
  }, 0)
}

function requirePlacement(
  element: ISpaceSceneElementDto,
  geometry: GeometryObject,
): PartEnvelope {
  requireSafeInteger(element.x, 'element x')
  requireSafeInteger(element.y, 'element y')
  requireSafeInteger(element.z, 'element z')
  requireRotation(element.rotationZ)
  return {
    width: positiveEnvelope(element.width, geometry, 'width'),
    height: positiveEnvelope(element.height, geometry, 'height'),
    depth: positiveEnvelope(element.depth, geometry, 'depth'),
  }
}

function positiveEnvelope(
  value: number | undefined,
  geometry: GeometryObject,
  field: keyof PartEnvelope,
): number {
  if (Number.isSafeInteger(value) && value! > 0) return value!
  if (geometry.kind === 'box') {
    return requirePositiveSafeInteger(geometry[field], `geometry ${field}`)
  }
  if (geometry.kind === 'path' && field === 'depth') {
    return requirePositiveSafeInteger(geometry.width, 'path width')
  }
  if (geometry.kind === 'polygon' && field === 'height') {
    return requirePositiveSafeInteger(geometry.height, 'polygon height')
  }
  return 100
}

function requireLogicalId(element: ISpaceSceneElementDto): string {
  const logicalId = normalize(element.revision?.logicalId).toLowerCase()
  if (!logicalId || !guidPattern.test(logicalId)) {
    throw new Error('Every merged element requires a logical identity')
  }
  return logicalId
}

function optionalSourceGuid(
  value: string | undefined,
  field: string,
): string | undefined {
  const normalized = normalize(value).toLowerCase()
  if (!normalized) return undefined
  if (!guidPattern.test(normalized)) throw new Error(`${field} must be a GUID`)
  return normalized
}

function optionalSourceRef(value: string | undefined): string | undefined {
  const normalized = normalize(value)
  if (!normalized) return undefined
  if (normalized.length > 500) {
    throw new Error('source reference cannot exceed 500 characters')
  }
  return normalized
}

function requireRotation(value: number | undefined): number {
  if (!Number.isFinite(value) || value! < 0 || value! >= 360) {
    throw new Error('Every merged element requires a rotation in [0, 360)')
  }
  return value!
}

function requirePositiveSafeInteger(value: unknown, field: string): number {
  const integer = requireSafeInteger(value, field)
  if (integer <= 0) throw new Error(`${field} must be positive`)
  return integer
}

function requireSafeInteger(value: unknown, field: string): number {
  if (!Number.isSafeInteger(value)) throw new Error(`${field} must be an integer`)
  return value as number
}

function normalize(value: string | undefined): string {
  return value?.trim() ?? ''
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}
