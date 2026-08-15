import type {
  ISpaceElementAttributeWriteDto,
  ISpaceSceneElementAttributeDto,
  ISpaceSceneElementDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import type { ElementPropertiesPayload } from '@/api/space/designElements'

export const SPACE_ELEMENT_TYPES = [
  'Wall',
  'Column',
  'Door',
  'Dock',
  'Stair',
  'Elevator',
  'Pallet',
  'Device',
  'Workstation',
  'Conveyor',
  'StaticEquipment',
  'Annotation',
  'Dimension',
  'Guide',
  'RestrictedArea',
  'Decoration',
  'ImportedReference',
] as const

export interface ElementPropertiesDraft {
  elementType: string
  x: number
  y: number
  z: number
  rotationZ: number
  width: number
  height: number
  depth: number
  businessCode: string
  linkedEntityType: string
  linkedLogicalId: string
  attributes: ISpaceElementAttributeWriteDto[]
}

export function createElementPropertiesDraft(
  element: ISpaceSceneElementDto,
  attributes: readonly ISpaceSceneElementAttributeDto[],
): ElementPropertiesDraft {
  return {
    elementType: element.elementType ?? '',
    x: element.x ?? 0,
    y: element.y ?? 0,
    z: element.z ?? 0,
    rotationZ: element.rotationZ ?? 0,
    width: element.width ?? 0,
    height: element.height ?? 0,
    depth: element.depth ?? 0,
    businessCode: element.businessCode ?? '',
    linkedEntityType: element.linkedEntityType ?? '',
    linkedLogicalId: element.linkedLogicalId ?? '',
    attributes: attributes.map((attribute) => ({
      namespace: attribute.namespace,
      key: attribute.key,
      valueType: attribute.valueType,
      value: attribute.value,
      unit: attribute.unit,
    })),
  }
}

export function buildElementPropertiesPayload(
  element: ISpaceSceneElementDto,
  draft: ElementPropertiesDraft,
): ElementPropertiesPayload {
  const elementType = draft.elementType.trim()
  if (!(SPACE_ELEMENT_TYPES as readonly string[]).includes(elementType)) {
    throw new Error('A supported Space element type is required')
  }
  if (
    !Number.isInteger(draft.x) ||
    !Number.isInteger(draft.y) ||
    !Number.isInteger(draft.z) ||
    !Number.isInteger(draft.width) ||
    !Number.isInteger(draft.height) ||
    !Number.isInteger(draft.depth) ||
    draft.width <= 0 ||
    draft.height <= 0 ||
    draft.depth <= 0
  ) {
    throw new Error('Element placement requires positive integer millimeters')
  }
  if (
    !Number.isFinite(draft.rotationZ) ||
    draft.rotationZ < 0 ||
    draft.rotationZ >= 360
  ) {
    throw new Error('Element rotation must be in [0, 360)')
  }

  const linkedEntityType = draft.linkedEntityType.trim()
  const linkedLogicalId = draft.linkedLogicalId.trim()
  if (Boolean(linkedEntityType) !== Boolean(linkedLogicalId)) {
    throw new Error('Linked entity type and logical identity are paired')
  }
  const attributes = draft.attributes.map((attribute) => ({
    namespace: attribute.namespace?.trim(),
    key: attribute.key?.trim(),
    valueType: attribute.valueType?.trim(),
    value: attribute.value?.trim(),
    unit: attribute.unit?.trim() || undefined,
  }))
  const keys = new Set<string>()
  for (const attribute of attributes) {
    const key = `${attribute.namespace ?? ''}\u001f${attribute.key ?? ''}`.toLowerCase()
    if (!attribute.namespace || !attribute.key || !attribute.valueType || !attribute.value) {
      throw new Error('Element attributes require namespace, key, type and value')
    }
    if (keys.has(key)) throw new Error('Element attribute keys must be unique')
    keys.add(key)
  }

  return {
    elementType,
    geometryJson: updateGeometryEnvelope(element.geometryJson ?? '{}', draft),
    x: draft.x,
    y: draft.y,
    z: draft.z,
    rotationZ: draft.rotationZ,
    width: draft.width,
    height: draft.height,
    depth: draft.depth,
    businessCode: draft.businessCode.trim() || undefined,
    linkedEntityType: linkedEntityType || undefined,
    linkedLogicalId: linkedLogicalId || undefined,
    attributes,
  }
}

function updateGeometryEnvelope(
  geometryJson: string,
  draft: Pick<ElementPropertiesDraft, 'width' | 'height' | 'depth'>,
): string {
  const geometry = JSON.parse(geometryJson) as Record<string, unknown>
  if (geometry.schemaVersion !== 1) {
    throw new Error('Only geometry schemaVersion 1 is editable')
  }
  if (geometry.kind === 'box') {
    geometry.width = draft.width
    geometry.height = draft.height
    geometry.depth = draft.depth
  } else if (geometry.kind === 'path') {
    geometry.width = draft.depth
  } else if (geometry.kind === 'polygon') {
    geometry.height = draft.height
  }
  return JSON.stringify(geometry)
}
