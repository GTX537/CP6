import type {
  ISpaceElementAttributeWriteDto,
  ISpaceSceneElementDto,
  ISpaceSceneRackDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import type { EditorCommandInput } from './editorBatchCommands'

export const objectCopyGapMillimeters = 500
export const maximumObjectCopyCount = 100

export interface ElementCopySource {
  ownerKind: 'Element'
  element: ISpaceSceneElementDto
  attributes: readonly ISpaceElementAttributeWriteDto[]
}

export interface RackCopySource {
  ownerKind: 'Rack'
  rack: ISpaceSceneRackDto
  hasActiveLevel: boolean
}

export type ObjectCopySource = ElementCopySource | RackCopySource

export interface ObjectCopyPlan {
  commands: EditorCommandInput[]
  elementLogicalIds: string[]
  expectedRackCopies: number
}

const guidPattern =
  /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const minimumInt32 = -2_147_483_648
const maximumInt32 = 2_147_483_647

export function inspectObjectCopySelection(
  sources: readonly ObjectCopySource[],
): { eligible: boolean; reason: string } {
  if (sources.length === 0) {
    return { eligible: false, reason: '请选择至少一个通用元素或货架' }
  }
  if (sources.length > maximumObjectCopyCount) {
    return {
      eligible: false,
      reason: `一次最多复制 ${maximumObjectCopyCount} 个对象`,
    }
  }
  for (const source of sources) {
    const revision = source.ownerKind === 'Element'
      ? source.element.revision
      : source.rack.revision
    if (!revision?.logicalId || !guidPattern.test(revision.logicalId)) {
      return { eligible: false, reason: '选择中存在缺少 LogicalId 的对象' }
    }
    if (revision.lifecycleState !== 'Active') {
      return { eligible: false, reason: '只能复制 Active 草稿对象' }
    }
    if (source.ownerKind === 'Element' &&
      (source.element.modelAssetId || source.element.modelAssetScope)) {
      return { eligible: false, reason: '资产实例不能通过通用元素复制' }
    }
    if (source.ownerKind === 'Rack' && !source.hasActiveLevel) {
      return { eligible: false, reason: '货架至少需要一个 Active 设计层' }
    }
  }
  return { eligible: true, reason: '' }
}

export function buildObjectCopyPlan(
  sources: readonly ObjectCopySource[],
  allActiveRacks: readonly ISpaceSceneRackDto[],
  allocateLogicalId: () => string = () => crypto.randomUUID(),
): ObjectCopyPlan {
  const eligibility = inspectObjectCopySelection(sources)
  if (!eligibility.eligible) throw new Error(eligibility.reason)

  const allocated = new Set(
    sources.map((source) => requireGuid(
      source.ownerKind === 'Element'
        ? source.element.revision?.logicalId
        : source.rack.revision?.logicalId,
      'Source logical identity is invalid',
    )),
  )
  const commands: EditorCommandInput[] = []
  const elementLogicalIds: string[] = []
  let expectedRackCopies = 0

  for (const source of sources) {
    if (source.ownerKind === 'Element') {
      const element = source.element
      const logicalId = requireGuid(
        allocateLogicalId(),
        'Allocated copy logical identity is invalid',
      )
      if (allocated.has(logicalId)) {
        throw new Error('Allocated copy logical identities must be unique')
      }
      allocated.add(logicalId)
      const placement = copiedElementPlacement(element)
      const elementType = requireText(element.elementType, 'element type', 64)
      const geometryJson = requireGeometry(element.geometryJson)
      const parentLogicalId = optionalGuid(element.parentLogicalId, 'parent identity')
      commands.push({
        type: 'CreateElement',
        targetLogicalId: logicalId,
        createElement: {
          elementType,
          geometryJson,
          ...placement,
          ...(parentLogicalId ? { parentLogicalId } : {}),
          attributes: source.attributes.map((attribute) => ({ ...attribute })),
        },
      })
      elementLogicalIds.push(logicalId)
      continue
    }

    const rack = source.rack
    const logicalId = requireGuid(
      rack.revision?.logicalId,
      'Rack logical identity is invalid',
    )
    const codePrefix = rackCopyCodePrefix(
      requireText(rack.rackCode, 'rack code', 100),
      logicalId,
    )
    const startNumber = nextRackCopyNumber(
      codePrefix,
      rack.zoneLogicalId,
      allActiveRacks,
    )
    commands.push({
      type: 'GenerateRackArray',
      targetLogicalId: logicalId,
      generateRackArray: {
        rows: 1,
        columns: 2,
        rowGap: 0,
        columnGap: objectCopyGapMillimeters,
        staggerOffset: 0,
        codePrefix,
        startNumber,
        codeDigits: 3,
      },
    })
    expectedRackCopies++
  }

  return { commands, elementLogicalIds, expectedRackCopies }
}

function copiedElementPlacement(element: ISpaceSceneElementDto) {
  const x = requireInt32(element.x, 'element x')
  const y = requireInt32(element.y, 'element y')
  const z = requireInt32(element.z, 'element z')
  const width = requirePositiveInt32(element.width, 'element width')
  const height = requirePositiveInt32(element.height, 'element height')
  const depth = requirePositiveInt32(element.depth, 'element depth')
  const rotationZ = requireRotation(element.rotationZ)
  const radians = rotationZ * Math.PI / 180
  const distance = width + objectCopyGapMillimeters
  return {
    x: requireInt32(
      x + Math.round(distance * Math.cos(radians)),
      'copied element x',
    ),
    y: requireInt32(
      y + Math.round(distance * Math.sin(radians)),
      'copied element y',
    ),
    z,
    rotationZ,
    width,
    height,
    depth,
  }
}

function rackCopyCodePrefix(rackCode: string, logicalId: string): string {
  // Keep room for the six-digit bounded sequence while staying within the
  // server's 100-character rack-code contract.
  return `${rackCode.slice(0, 79)}-COPY-${logicalId.slice(0, 8)}-`
}

function nextRackCopyNumber(
  prefix: string,
  zoneLogicalId: string | undefined,
  racks: readonly ISpaceSceneRackDto[],
): number {
  const reserved = new Set(racks
    .filter((rack) => rack.zoneLogicalId === zoneLogicalId)
    .map((rack) => rack.rackCode?.toUpperCase())
    .filter((code): code is string => Boolean(code)))
  for (let value = 1; value <= 999_999; value++) {
    const code = `${prefix}${String(value).padStart(3, '0')}`.toUpperCase()
    if (!reserved.has(code)) return value
  }
  throw new Error('No rack copy code remains available')
}

function requireGuid(value: string | undefined, field: string): string {
  if (!value || !guidPattern.test(value)) throw new Error(field)
  return value.toLowerCase()
}

function optionalGuid(value: string | undefined, field: string): string | undefined {
  if (!value) return undefined
  return requireGuid(value, field)
}

function requireText(
  value: string | undefined,
  field: string,
  maximumLength: number,
): string {
  const normalized = value?.trim()
  if (!normalized || normalized.length > maximumLength) throw new Error(`${field} is invalid`)
  return normalized
}

function requireGeometry(value: string | undefined): string {
  try {
    const parsed = JSON.parse(value ?? '') as { schemaVersion?: unknown; kind?: unknown }
    if (parsed.schemaVersion !== 1 || typeof parsed.kind !== 'string') throw new Error()
    return JSON.stringify(parsed)
  } catch {
    throw new Error('Element geometry is invalid')
  }
}

function requireInt32(value: number | undefined, field: string): number {
  if (!Number.isInteger(value) || value! < minimumInt32 || value! > maximumInt32) {
    throw new Error(`${field} is outside Int32 millimeters`)
  }
  return value!
}

function requirePositiveInt32(value: number | undefined, field: string): number {
  const normalized = requireInt32(value, field)
  if (normalized <= 0) throw new Error(`${field} must be positive`)
  return normalized
}

function requireRotation(value: number | undefined): number {
  if (!Number.isFinite(value) || value! < 0 || value! >= 360) {
    throw new Error('Element rotation is invalid')
  }
  return value!
}
