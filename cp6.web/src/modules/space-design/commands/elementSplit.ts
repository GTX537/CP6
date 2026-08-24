import type {
  ISpaceSceneElementAttributeDto,
  ISpaceSceneElementDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { sceneElementPropertiesPayload } from '@/modules/space-design/panels/elementProperties'
import type {
  EditorCommandInput,
  ReversibleCommandBatch,
} from './editorBatchCommands'

export interface ElementSplitPlan {
  groupLogicalId: string
  splitLogicalIds: string[]
  partCount: number
  batch: ReversibleCommandBatch
}

interface GeometryObject extends Record<string, unknown> {
  schemaVersion: 1
  kind: string
}

interface GroupPart {
  sourceLogicalId: string
  sourceId?: string
  sourceRef?: string
  x: number
  y: number
  z: number
  rotationZ: number
  width: number
  height: number
  depth: number
  geometry: GeometryObject
}

const guidPattern =
  /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const minimumInt32 = -2_147_483_648
const maximumInt32 = 2_147_483_647

export function buildElementSplitPlan(
  element: ISpaceSceneElementDto,
  allAttributes: readonly ISpaceSceneElementAttributeDto[],
  allocateLogicalId: () => string = () => crypto.randomUUID(),
): ElementSplitPlan {
  const groupLogicalId = requireGuid(
    element.revision?.logicalId,
    'The group requires a logical identity',
  )
  if (element.revision?.lifecycleState !== 'Active') {
    throw new Error('Only an active Draft group can be split')
  }
  if (element.modelAssetId || element.modelAssetScope) {
    throw new Error('Asset-backed elements cannot be split')
  }

  const geometry = parseGeometry(element.geometryJson)
  if (geometry.kind !== 'group') {
    throw new Error('Select one group element to split')
  }
  const parts = parseGroupParts(geometry.parts)
  const origin = {
    x: requireInt32(element.x, 'group x'),
    y: requireInt32(element.y, 'group y'),
    z: requireInt32(element.z, 'group z'),
  }
  const groupRotation = requireRotation(element.rotationZ, 'group rotation')
  const elementType = requireText(element.elementType, 'group element type', 64)
  const parentLogicalId = optionalGuid(element.parentLogicalId, 'parent identity')
  const linkedEntityType = optionalText(
    element.linkedEntityType,
    'linked entity type',
    100,
  )
  const linkedLogicalId = optionalGuid(
    element.linkedLogicalId,
    'linked logical identity',
  )
  if (Boolean(linkedEntityType) !== Boolean(linkedLogicalId)) {
    throw new Error('Linked entity type and logical identity must be paired')
  }
  const businessCode = optionalText(element.businessCode, 'business code', 200)
  const revisionId = requireGuid(
    element.revision?.revisionId,
    'The group requires a revision identity',
  )
  const attributes = allAttributes.filter(
    (attribute) => attribute.elementRevisionId?.toLowerCase() === revisionId,
  )
  const groupBefore = sceneElementPropertiesPayload(element, attributes)
  const placements = parts.map((part) => worldPlacement(origin, groupRotation, part))

  const allocated = new Set<string>([
    groupLogicalId,
    ...parts.map((part) => part.sourceLogicalId),
  ])
  const splitLogicalIds = parts.slice(1).map(() => {
    const logicalId = requireGuid(
      allocateLogicalId(),
      'Allocated split logical identity is invalid',
    )
    if (allocated.has(logicalId)) {
      throw new Error('Allocated split logical identities must be unique')
    }
    allocated.add(logicalId)
    return logicalId
  })

  const survivorAfter = {
    ...groupBefore,
    geometryJson: JSON.stringify(parts[0]!.geometry),
    ...placements[0],
  }
  const createCommands: EditorCommandInput[] = splitLogicalIds.map(
    (logicalId, index) => {
      const part = parts[index + 1]!
      const placement = placements[index + 1]!
      return {
        type: 'CreateElement',
        targetLogicalId: logicalId,
        createElement: {
          elementType,
          geometryJson: JSON.stringify(part.geometry),
          ...placement,
          ...(businessCode ? { businessCode } : {}),
          ...(parentLogicalId ? { parentLogicalId } : {}),
          ...(part.sourceId ? { sourceId: part.sourceId } : {}),
          ...(part.sourceRef ? { sourceRef: part.sourceRef } : {}),
          attributes: groupBefore.attributes,
          ...(linkedEntityType ? { linkedEntityType } : {}),
          ...(linkedLogicalId ? { linkedLogicalId } : {}),
        },
      }
    },
  )

  return {
    groupLogicalId,
    splitLogicalIds,
    partCount: parts.length,
    batch: {
      forward: [
        update(groupLogicalId, survivorAfter),
        ...createCommands,
      ],
      reverse: [
        update(groupLogicalId, groupBefore),
        ...splitLogicalIds.map(deleteObject),
      ],
      redo: [
        update(groupLogicalId, survivorAfter),
        ...splitLogicalIds.map(restoreObject),
      ],
    },
  }
}

function update(
  targetLogicalId: string,
  updateProperties: unknown,
): EditorCommandInput {
  return { type: 'UpdateProperties', targetLogicalId, updateProperties }
}

function deleteObject(targetLogicalId: string): EditorCommandInput {
  return { type: 'DeleteObject', targetLogicalId }
}

function restoreObject(targetLogicalId: string): EditorCommandInput {
  return { type: 'RestoreLogicalObject', targetLogicalId }
}

function parseGeometry(value: string | undefined): GeometryObject {
  let parsed: unknown
  try {
    parsed = JSON.parse(value ?? '')
  } catch {
    throw new Error('The selected element requires valid geometry JSON')
  }
  if (
    !isRecord(parsed)
    || parsed.schemaVersion !== 1
    || typeof parsed.kind !== 'string'
  ) {
    throw new Error('Only geometry schemaVersion 1 can be split')
  }
  return parsed as GeometryObject
}

function parseGroupParts(value: unknown): GroupPart[] {
  if (!Array.isArray(value) || value.length < 2 || value.length > 100) {
    throw new Error('A split group must contain between 2 and 100 parts')
  }
  const sourceLogicalIds = new Set<string>()
  return value.map((candidate, index) => {
    const field = `group part ${index + 1}`
    if (!isRecord(candidate) || !isRecord(candidate.geometry)) {
      throw new Error(`${field} is invalid`)
    }
    const geometry = candidate.geometry
    if (
      geometry.schemaVersion !== 1
      || typeof geometry.kind !== 'string'
      || geometry.kind === 'asset'
    ) {
      throw new Error(`${field} contains unsupported geometry`)
    }
    const sourceId = optionalGuid(candidate.sourceId, `${field} source identity`)
    const sourceRef = optionalText(candidate.sourceRef, `${field} source reference`, 500)
    if (Boolean(sourceId) !== Boolean(sourceRef)) {
      throw new Error(`${field} source identity and reference must be paired`)
    }
    const sourceLogicalId = requireGuid(
      candidate.sourceLogicalId,
      `${field} source logical identity is invalid`,
    )
    if (sourceLogicalIds.has(sourceLogicalId)) {
      throw new Error('Group part source logical identities must be unique')
    }
    sourceLogicalIds.add(sourceLogicalId)
    return {
      sourceLogicalId,
      ...(sourceId ? { sourceId } : {}),
      ...(sourceRef ? { sourceRef } : {}),
      x: requireInt32(candidate.x, `${field} x`),
      y: requireInt32(candidate.y, `${field} y`),
      z: requireInt32(candidate.z, `${field} z`),
      rotationZ: requireRotation(candidate.rotationZ, `${field} rotation`),
      width: requirePositiveInt32(candidate.width, `${field} width`),
      height: requirePositiveInt32(candidate.height, `${field} height`),
      depth: requirePositiveInt32(candidate.depth, `${field} depth`),
      geometry: geometry as GeometryObject,
    }
  })
}

function worldPlacement(
  origin: { x: number; y: number; z: number },
  rotationZ: number,
  part: GroupPart,
) {
  const radians = (rotationZ * Math.PI) / 180
  const cos = Math.cos(radians)
  const sin = Math.sin(radians)
  return {
    x: requireInt32(
      Math.round(origin.x + part.x * cos - part.y * sin),
      'split x',
    ),
    y: requireInt32(
      Math.round(origin.y + part.x * sin + part.y * cos),
      'split y',
    ),
    z: requireInt32(origin.z + part.z, 'split z'),
    rotationZ: normalizeRotation(rotationZ + part.rotationZ),
    width: part.width,
    height: part.height,
    depth: part.depth,
  }
}

function normalizeRotation(value: number): number {
  const normalized = value % 360
  return normalized < 0 ? normalized + 360 : normalized
}

function requireRotation(value: unknown, field: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < 0 || value >= 360) {
    throw new Error(`${field} must be in [0, 360)`)
  }
  return value
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

function requireGuid(value: unknown, message: string): string {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase() : ''
  if (!guidPattern.test(normalized)) throw new Error(message)
  return normalized
}

function optionalGuid(value: unknown, field: string): string | undefined {
  if (value === undefined || value === null || value === '') return undefined
  return requireGuid(value, `${field} must be a GUID`)
}

function requireText(value: unknown, field: string, maximumLength: number): string {
  const normalized = optionalText(value, field, maximumLength)
  if (!normalized) throw new Error(`${field} is required`)
  return normalized
}

function optionalText(
  value: unknown,
  field: string,
  maximumLength: number,
): string | undefined {
  if (value === undefined || value === null || value === '') return undefined
  if (typeof value !== 'string' || !value.trim() || value.trim().length > maximumLength) {
    throw new Error(`${field} must be non-empty text up to ${maximumLength} characters`)
  }
  return value.trim()
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value)
}
