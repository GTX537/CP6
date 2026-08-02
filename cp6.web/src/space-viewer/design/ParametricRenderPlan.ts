import type {
  ISpaceDesignSceneDto,
  SpaceSceneElementDto,
  SpaceSceneRackDto,
  SpaceSceneRackLevelDto,
  SpaceSceneRevisionDto,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

export const PARAMETRIC_RENDERER_VERSION = 'space-parametric-v1'

type RevisionInput = Pick<SpaceSceneRevisionDto, 'logicalId' | 'lifecycleState'>

type RackInput = Omit<
  Pick<
    SpaceSceneRackDto,
    | 'revision'
    | 'floorLogicalId'
    | 'zoneLogicalId'
    | 'aisleLogicalId'
    | 'rackCode'
    | 'x'
    | 'y'
    | 'z'
    | 'rotationZ'
    | 'width'
    | 'depth'
    | 'height'
  >,
  'revision'
> & {
  revision?: RevisionInput
}

type RackLevelInput = Omit<
  Pick<
    SpaceSceneRackLevelDto,
    | 'revision'
    | 'rackLogicalId'
    | 'levelNo'
    | 'bottomZ'
    | 'clearHeight'
    | 'binCount'
    | 'depthCount'
    | 'cellWidth'
    | 'cellDepth'
    | 'beamHeight'
    | 'maxLoad'
  >,
  'revision'
> & {
  revision?: RevisionInput
}

type ElementInput = Omit<
  Pick<
    SpaceSceneElementDto,
    | 'revision'
    | 'floorLogicalId'
    | 'parentLogicalId'
    | 'elementType'
    | 'geometryJson'
    | 'modelAssetId'
    | 'modelAssetScope'
    | 'x'
    | 'y'
    | 'z'
    | 'rotationZ'
    | 'width'
    | 'height'
    | 'depth'
    | 'businessCode'
  >,
  'revision'
> & {
  revision?: RevisionInput
}

export type ParametricDesignSceneInput = Pick<
  ISpaceDesignSceneDto,
  'schemaVersion' | 'authority' | 'runtimeOverlayIncluded'
> & {
  racks?: readonly RackInput[]
  rackLevels?: readonly RackLevelInput[]
  elements?: readonly ElementInput[]
}

export type ParametricOwnerKind = 'Rack' | 'RackLevel' | 'Element'

export type ParametricMaterialRole =
  | 'rack-envelope'
  | 'rack-beam'
  | 'rack-cell'
  | 'element'
  | 'asset-placeholder'

export interface DataPoint3 {
  x: number
  y: number
  z: number
}

export interface DataSize3 {
  width: number
  depth: number
  height: number
}

export interface ParametricPrimitiveIdentity {
  key: string
  logicalId: string
  ownerKind: ParametricOwnerKind
  materialRole: ParametricMaterialRole
  elementType?: string
  parentLogicalId?: string
  businessCode?: string
  rackLevelSpec?: ParametricRackLevelSpecification
  lifecycleState: string
  assetVersionId?: string
  assetScope?: string
}

export interface ParametricRackLevelSpecification {
  logicalId: string
  levelNo: number
  bottomZ: number
  clearHeight: number
  binCount: number
  depthCount: number
  cellWidth: number
  cellDepth: number
  beamHeight: number
  maxLoad: number | null
}

export interface ParametricBoxPrimitive extends ParametricPrimitiveIdentity {
  kind: 'box'
  center: DataPoint3
  size: DataSize3
  rotationZ: number
}

export interface ParametricPolygonPrimitive
  extends ParametricPrimitiveIdentity {
  kind: 'extruded-polygon'
  origin: DataPoint3
  outer: readonly DataPoint3[]
  holes: readonly (readonly DataPoint3[])[]
  height: number
  rotationZ: number
}

export interface ParametricRenderPlan {
  schemaVersion: 1
  rendererVersion: typeof PARAMETRIC_RENDERER_VERSION
  boxes: readonly ParametricBoxPrimitive[]
  polygons: readonly ParametricPolygonPrimitive[]
  primitiveCount: number
}

export class ParametricRenderPlanError extends Error {
  constructor(
    public readonly field: string,
    message: string,
  ) {
    super(`${field}: ${message}`)
    this.name = 'ParametricRenderPlanError'
  }
}

export function buildParametricRenderPlan(
  scene: ParametricDesignSceneInput,
): ParametricRenderPlan {
  if (scene.schemaVersion !== 1) {
    throw invalid(
      'schemaVersion',
      'only Design scene schemaVersion 1 is supported',
    )
  }
  if (scene.authority !== 'DesignRevision') {
    throw invalid('authority', 'DesignRevision is required')
  }
  if (scene.runtimeOverlayIncluded !== false) {
    throw invalid(
      'runtimeOverlayIncluded',
      'runtime state cannot be embedded in a parametric design render plan',
    )
  }

  const allRacks = requireArray(scene.racks, 'racks')
  const allRackIds = new Set(
    allRacks.map((rack) => logicalId(rack.revision, 'rack')),
  )
  const racks = allRacks
    .filter(isActiveRevision)
    .slice()
    .sort((left, right) =>
      logicalId(left.revision, 'rack').localeCompare(
        logicalId(right.revision, 'rack'),
      ),
    )
  const rackLevels = requireArray(scene.rackLevels, 'rackLevels').filter(
    isActiveRevision,
  )
  const elements = requireArray(scene.elements, 'elements')
    .filter(isActiveRevision)
    .slice()
    .sort((left, right) =>
      logicalId(left.revision, 'element').localeCompare(
        logicalId(right.revision, 'element'),
      ),
    )

  const rackIds = new Set(racks.map((rack) => logicalId(rack.revision, 'rack')))
  const levelsByRack = new Map<string, RackLevelInput[]>()
  for (const level of rackLevels) {
    const rackLogicalId = requireGuid(
      level.rackLogicalId,
      'rackLevel.rackLogicalId',
    )
    if (!rackIds.has(rackLogicalId)) {
      if (allRackIds.has(rackLogicalId)) continue
      throw invalid(
        'rackLevels',
        `rack level references rack ${rackLogicalId} outside the scene`,
      )
    }
    const values = levelsByRack.get(rackLogicalId) ?? []
    values.push(level)
    levelsByRack.set(rackLogicalId, values)
  }

  const boxes: ParametricBoxPrimitive[] = []
  const polygons: ParametricPolygonPrimitive[] = []
  const keys = new Set<string>()

  for (const rack of racks) {
    addRackPrimitives(rack, levelsByRack, boxes, keys)
  }
  for (const element of elements) {
    addElementPrimitives(element, boxes, polygons, keys)
  }

  return {
    schemaVersion: 1,
    rendererVersion: PARAMETRIC_RENDERER_VERSION,
    boxes,
    polygons,
    primitiveCount: boxes.length + polygons.length,
  }
}

function addRackPrimitives(
  rack: RackInput,
  levelsByRack: ReadonlyMap<string, RackLevelInput[]>,
  boxes: ParametricBoxPrimitive[],
  keys: Set<string>,
): void {
  const rackLogicalId = logicalId(rack.revision, 'rack')
  const lifecycleState = requireText(
    rack.revision?.lifecycleState,
    `rack.${rackLogicalId}.lifecycleState`,
  )
  const origin = {
    x: requireInteger(rack.x, `rack.${rackLogicalId}.x`),
    y: requireInteger(rack.y, `rack.${rackLogicalId}.y`),
    z: requireInteger(rack.z, `rack.${rackLogicalId}.z`),
  }
  const rotationZ = requireRotation(
    rack.rotationZ,
    `rack.${rackLogicalId}.rotationZ`,
  )
  const envelope = {
    width: requirePositiveInteger(rack.width, `rack.${rackLogicalId}.width`),
    depth: requirePositiveInteger(rack.depth, `rack.${rackLogicalId}.depth`),
    height: requirePositiveInteger(rack.height, `rack.${rackLogicalId}.height`),
  }
  const levels = (levelsByRack.get(rackLogicalId) ?? [])
    .slice()
    .sort(
      (left, right) =>
        requireInteger(left.levelNo, `rack.${rackLogicalId}.levelNo`) -
        requireInteger(right.levelNo, `rack.${rackLogicalId}.levelNo`),
    )
  if (levels.length === 0) {
    throw invalid(
      `rack.${rackLogicalId}.levels`,
      'at least one RackLevelRevision is required; uniform fallback is forbidden',
    )
  }

  pushBox(boxes, keys, {
    key: `rack:${rackLogicalId}:envelope`,
    logicalId: rackLogicalId,
    ownerKind: 'Rack',
    parentLogicalId: optionalGuid(
      rack.zoneLogicalId,
      `rack.${rackLogicalId}.zoneLogicalId`,
    ),
    businessCode: optionalText(rack.rackCode),
    materialRole: 'rack-envelope',
    lifecycleState,
    kind: 'box',
    center: originBoxCenter(origin, envelope, rotationZ),
    size: envelope,
    rotationZ,
  })

  const levelNumbers = new Set<number>()
  let previousTop = 0
  for (const level of levels) {
    const levelNo = requirePositiveInteger(
      level.levelNo,
      `rack.${rackLogicalId}.levelNo`,
    )
    if (levelNumbers.has(levelNo)) {
      throw invalid(
        `rack.${rackLogicalId}.levels`,
        `duplicate levelNo ${levelNo}`,
      )
    }
    levelNumbers.add(levelNo)

    const levelLogicalId = logicalId(level.revision, `rackLevel.${levelNo}`)
    const levelLifecycle = requireText(
      level.revision?.lifecycleState,
      `rackLevel.${levelLogicalId}.lifecycleState`,
    )
    const bottomZ = requireNonNegativeInteger(
      level.bottomZ,
      `rackLevel.${levelLogicalId}.bottomZ`,
    )
    const clearHeight = requirePositiveInteger(
      level.clearHeight,
      `rackLevel.${levelLogicalId}.clearHeight`,
    )
    const binCount = requirePositiveInteger(
      level.binCount,
      `rackLevel.${levelLogicalId}.binCount`,
    )
    const depthCount = requirePositiveInteger(
      level.depthCount,
      `rackLevel.${levelLogicalId}.depthCount`,
    )
    const cellWidth = requirePositiveInteger(
      level.cellWidth,
      `rackLevel.${levelLogicalId}.cellWidth`,
    )
    const cellDepth = requirePositiveInteger(
      level.cellDepth,
      `rackLevel.${levelLogicalId}.cellDepth`,
    )
    const beamHeight = requireNonNegativeInteger(
      level.beamHeight,
      `rackLevel.${levelLogicalId}.beamHeight`,
    )
    const levelWidth = binCount * cellWidth
    const levelDepth = depthCount * cellDepth
    const levelTop = bottomZ + beamHeight + clearHeight
    const rackLevelSpec: ParametricRackLevelSpecification = {
      logicalId: levelLogicalId,
      levelNo,
      bottomZ,
      clearHeight,
      binCount,
      depthCount,
      cellWidth,
      cellDepth,
      beamHeight,
      maxLoad: level.maxLoad ?? null,
    }

    if (levelWidth > envelope.width || levelDepth > envelope.depth) {
      throw invalid(
        `rackLevel.${levelLogicalId}`,
        'cell array exceeds the RackRevision envelope',
      )
    }
    if (levelTop > envelope.height) {
      throw invalid(
        `rackLevel.${levelLogicalId}`,
        'level height exceeds the RackRevision envelope',
      )
    }
    if (bottomZ < previousTop) {
      throw invalid(
        `rackLevel.${levelLogicalId}.bottomZ`,
        'rack levels overlap',
      )
    }
    previousTop = levelTop

    if (beamHeight > 0) {
      const beamSize = {
        width: levelWidth,
        depth: levelDepth,
        height: beamHeight,
      }
      pushBox(boxes, keys, {
        key: `rack:${rackLogicalId}:level:${levelNo}:beam`,
        logicalId: levelLogicalId,
        ownerKind: 'RackLevel',
        parentLogicalId: rackLogicalId,
        rackLevelSpec,
        materialRole: 'rack-beam',
        lifecycleState: levelLifecycle,
        kind: 'box',
        center: localCenterToWorld(
          origin,
          rotationZ,
          levelWidth / 2,
          levelDepth / 2,
          bottomZ + beamHeight / 2,
        ),
        size: beamSize,
        rotationZ,
      })
    }

    for (let bin = 1; bin <= binCount; bin += 1) {
      for (let depth = 1; depth <= depthCount; depth += 1) {
        pushBox(boxes, keys, {
          key: `rack:${rackLogicalId}:level:${levelNo}:bin:${bin}:depth:${depth}`,
          logicalId: levelLogicalId,
          ownerKind: 'RackLevel',
          parentLogicalId: rackLogicalId,
          rackLevelSpec,
          materialRole: 'rack-cell',
          lifecycleState: levelLifecycle,
          kind: 'box',
          center: localCenterToWorld(
            origin,
            rotationZ,
            (bin - 0.5) * cellWidth,
            (depth - 0.5) * cellDepth,
            bottomZ + beamHeight + clearHeight / 2,
          ),
          size: {
            width: cellWidth,
            depth: cellDepth,
            height: clearHeight,
          },
          rotationZ,
        })
      }
    }
  }
}

function addElementPrimitives(
  element: ElementInput,
  boxes: ParametricBoxPrimitive[],
  polygons: ParametricPolygonPrimitive[],
  keys: Set<string>,
): void {
  const logicalIdValue = logicalId(element.revision, 'element')
  const lifecycleState = requireText(
    element.revision?.lifecycleState,
    `element.${logicalIdValue}.lifecycleState`,
  )
  const elementType = requireText(
    element.elementType,
    `element.${logicalIdValue}.elementType`,
  )
  const origin = {
    x: requireInteger(element.x, `element.${logicalIdValue}.x`),
    y: requireInteger(element.y, `element.${logicalIdValue}.y`),
    z: requireInteger(element.z, `element.${logicalIdValue}.z`),
  }
  const rotationZ = requireRotation(
    element.rotationZ,
    `element.${logicalIdValue}.rotationZ`,
  )
  const modelAssetId = optionalGuid(
    element.modelAssetId,
    `element.${logicalIdValue}.modelAssetId`,
  )
  const modelAssetScope = optionalText(element.modelAssetScope)
  if (
    modelAssetId &&
    modelAssetScope !== 'System' &&
    modelAssetScope !== 'Tenant'
  ) {
    throw invalid(
      `element.${logicalIdValue}.modelAssetScope`,
      'an attached asset requires System or Tenant scope',
    )
  }
  if (!modelAssetId && modelAssetScope) {
    throw invalid(
      `element.${logicalIdValue}.modelAssetScope`,
      'asset scope cannot exist without an asset version',
    )
  }

  const geometry = parseGeometry(
    element.geometryJson,
    `element.${logicalIdValue}.geometryJson`,
  )
  const identity = {
    logicalId: logicalIdValue,
    ownerKind: 'Element' as const,
    parentLogicalId: optionalGuid(
      element.parentLogicalId,
      `element.${logicalIdValue}.parentLogicalId`,
    ),
    businessCode: optionalText(element.businessCode),
    elementType,
    lifecycleState,
    assetVersionId: modelAssetId,
    assetScope: modelAssetScope,
  }

  switch (geometry.kind) {
    case 'box': {
      const size = {
        width: positiveGeometryInteger(
          geometry.width,
          `element.${logicalIdValue}.geometry.width`,
        ),
        depth: positiveGeometryInteger(
          geometry.depth,
          `element.${logicalIdValue}.geometry.depth`,
        ),
        height: positiveGeometryInteger(
          geometry.height,
          `element.${logicalIdValue}.geometry.height`,
        ),
      }
      requireMatchingEnvelope(element, size, logicalIdValue)
      pushBox(boxes, keys, {
        ...identity,
        key: `element:${logicalIdValue}:box`,
        materialRole: modelAssetId ? 'asset-placeholder' : 'element',
        kind: 'box',
        center: originBoxCenter(origin, size, rotationZ),
        size,
        rotationZ,
      })
      return
    }
    case 'path': {
      const points = geometryPoints(
        geometry.points,
        `element.${logicalIdValue}.geometry.points`,
        2,
      )
      const pathWidth = positiveGeometryInteger(
        geometry.width,
        `element.${logicalIdValue}.geometry.width`,
      )
      const height = requirePositiveInteger(
        element.height,
        `element.${logicalIdValue}.height`,
      )
      for (let index = 0; index < points.length - 1; index += 1) {
        const start = points[index]!
        const end = points[index + 1]!
        if (start.z !== end.z) {
          throw invalid(
            `element.${logicalIdValue}.geometry.points`,
            'parametric path segments must be horizontal in renderer v1',
          )
        }
        const dx = end.x - start.x
        const dy = end.y - start.y
        const length = Math.hypot(dx, dy)
        if (!Number.isFinite(length) || length <= 0) {
          throw invalid(
            `element.${logicalIdValue}.geometry.points`,
            'path segment length must be positive',
          )
        }
        const segmentRotation = normalizeRotation(
          rotationZ + (Math.atan2(dy, dx) * 180) / Math.PI,
        )
        pushBox(boxes, keys, {
          ...identity,
          key: `element:${logicalIdValue}:path:${index}`,
          materialRole: modelAssetId ? 'asset-placeholder' : 'element',
          kind: 'box',
          center: localCenterToWorld(
            origin,
            rotationZ,
            (start.x + end.x) / 2,
            (start.y + end.y) / 2,
            start.z + height / 2,
          ),
          size: {
            width: length,
            depth: pathWidth,
            height,
          },
          rotationZ: segmentRotation,
        })
      }
      return
    }
    case 'polygon': {
      const outer = geometryPoints(
        geometry.outer,
        `element.${logicalIdValue}.geometry.outer`,
        3,
      )
      const holes = geometryHoles(
        geometry.holes,
        `element.${logicalIdValue}.geometry.holes`,
      )
      const baseZ = requirePlanarPoints(
        [outer, ...holes],
        `element.${logicalIdValue}.geometry`,
      )
      const height = positiveGeometryInteger(
        geometry.height,
        `element.${logicalIdValue}.geometry.height`,
      )
      const key = `element:${logicalIdValue}:polygon`
      requireUniqueKey(keys, key)
      polygons.push({
        ...identity,
        key,
        materialRole: modelAssetId ? 'asset-placeholder' : 'element',
        kind: 'extruded-polygon',
        origin: {
          x: origin.x,
          y: origin.y,
          z: origin.z + baseZ,
        },
        outer,
        holes,
        height,
        rotationZ,
      })
      return
    }
    case 'point': {
      if (geometry.z === undefined) {
        throw invalid(
          `element.${logicalIdValue}.geometry.z`,
          'an integer millimeter value is required',
        )
      }
      const point = geometryPoint(
        geometry,
        `element.${logicalIdValue}.geometry`,
      )
      const size = {
        width: positiveOrDefault(element.width, 100),
        depth: positiveOrDefault(element.depth, 100),
        height: positiveOrDefault(element.height, 100),
      }
      pushBox(boxes, keys, {
        ...identity,
        key: `element:${logicalIdValue}:point`,
        materialRole: modelAssetId ? 'asset-placeholder' : 'element',
        kind: 'box',
        center: localCenterToWorld(
          origin,
          rotationZ,
          point.x,
          point.y,
          point.z,
        ),
        size,
        rotationZ,
      })
      return
    }
    case 'asset': {
      const geometryAssetId = requireGuid(
        geometry.assetVersionId,
        `element.${logicalIdValue}.geometry.assetVersionId`,
      )
      if (!modelAssetId || geometryAssetId !== modelAssetId) {
        throw invalid(
          `element.${logicalIdValue}.geometry.assetVersionId`,
          'asset geometry must match the attached concrete asset version',
        )
      }
      const transform = assetTransform(
        geometry.transform,
        `element.${logicalIdValue}.geometry.transform`,
      )
      const size = {
        width:
          requirePositiveInteger(
            element.width,
            `element.${logicalIdValue}.width`,
          ) * transform.scaleX,
        depth:
          requirePositiveInteger(
            element.depth,
            `element.${logicalIdValue}.depth`,
          ) * transform.scaleY,
        height:
          requirePositiveInteger(
            element.height,
            `element.${logicalIdValue}.height`,
          ) * transform.scaleZ,
      }
      const assetRadians = (transform.rotationZ * Math.PI) / 180
      const assetCenterX =
        transform.x +
        (size.width / 2) * Math.cos(assetRadians) -
        (size.depth / 2) * Math.sin(assetRadians)
      const assetCenterY =
        transform.y +
        (size.width / 2) * Math.sin(assetRadians) +
        (size.depth / 2) * Math.cos(assetRadians)
      pushBox(boxes, keys, {
        ...identity,
        key: `element:${logicalIdValue}:asset:${geometryAssetId}`,
        materialRole: 'asset-placeholder',
        kind: 'box',
        center: localCenterToWorld(
          origin,
          rotationZ,
          assetCenterX,
          assetCenterY,
          transform.z + size.height / 2,
        ),
        size,
        rotationZ: normalizeRotation(rotationZ + transform.rotationZ),
      })
      return
    }
    default:
      throw invalid(
        `element.${logicalIdValue}.geometry.kind`,
        `unsupported geometry kind '${String(geometry.kind)}'`,
      )
  }
}

function isActiveRevision(
  value: { revision?: RevisionInput },
): boolean {
  return requireText(
    value.revision?.lifecycleState,
    'revision.lifecycleState',
  ) === 'Active'
}

function requireMatchingEnvelope(
  element: ElementInput,
  size: DataSize3,
  logicalIdValue: string,
): void {
  const cached = [
    ['width', element.width, size.width],
    ['depth', element.depth, size.depth],
    ['height', element.height, size.height],
  ] as const
  for (const [field, raw, expected] of cached) {
    const value = requireNonNegativeInteger(
      raw,
      `element.${logicalIdValue}.${field}`,
    )
    if (value !== 0 && value !== expected) {
      throw invalid(
        `element.${logicalIdValue}.${field}`,
        'cached envelope does not match GeometryJson',
      )
    }
  }
}

function pushBox(
  boxes: ParametricBoxPrimitive[],
  keys: Set<string>,
  primitive: ParametricBoxPrimitive,
): void {
  requireUniqueKey(keys, primitive.key)
  boxes.push(primitive)
}

function requireUniqueKey(keys: Set<string>, key: string): void {
  if (keys.has(key)) {
    throw invalid(
      'primitives',
      `duplicate deterministic primitive key '${key}'`,
    )
  }
  keys.add(key)
}

function originBoxCenter(
  origin: DataPoint3,
  size: DataSize3,
  rotationZ: number,
): DataPoint3 {
  return localCenterToWorld(
    origin,
    rotationZ,
    size.width / 2,
    size.depth / 2,
    size.height / 2,
  )
}

function localCenterToWorld(
  origin: DataPoint3,
  rotationZ: number,
  localX: number,
  localY: number,
  localZ: number,
): DataPoint3 {
  const radians = (rotationZ * Math.PI) / 180
  const cos = Math.cos(radians)
  const sin = Math.sin(radians)
  return {
    x: Math.round(origin.x + localX * cos - localY * sin),
    y: Math.round(origin.y + localX * sin + localY * cos),
    z: Math.round(origin.z + localZ),
  }
}

function parseGeometry(
  value: string | undefined,
  field: string,
): Record<string, unknown> & { kind: string } {
  if (!value) {
    throw invalid(field, 'geometry JSON is required')
  }
  let parsed: unknown
  try {
    parsed = JSON.parse(value)
  } catch {
    throw invalid(field, 'geometry JSON is invalid')
  }
  if (!isRecord(parsed)) {
    throw invalid(field, 'geometry must be a JSON object')
  }
  if (parsed.schemaVersion !== 1) {
    throw invalid(
      `${field}.schemaVersion`,
      'only geometry schemaVersion 1 is supported',
    )
  }
  if (typeof parsed.kind !== 'string') {
    throw invalid(`${field}.kind`, 'geometry kind is required')
  }
  return parsed as Record<string, unknown> & { kind: string }
}

function geometryPoints(
  value: unknown,
  field: string,
  minimum: number,
): DataPoint3[] {
  if (!Array.isArray(value) || value.length < minimum) {
    throw invalid(field, `at least ${minimum} points are required`)
  }
  return value.map((point, index) => geometryPoint(point, `${field}[${index}]`))
}

function geometryHoles(value: unknown, field: string): DataPoint3[][] {
  if (value === undefined) return []
  if (!Array.isArray(value)) {
    throw invalid(field, 'polygon holes must be an array')
  }
  return value.map((hole, index) =>
    geometryPoints(hole, `${field}[${index}]`, 3),
  )
}

function geometryPoint(value: unknown, field: string): DataPoint3 {
  if (!isRecord(value)) {
    throw invalid(field, 'point must be an object')
  }
  return {
    x: positiveOrNegativeGeometryInteger(value.x, `${field}.x`),
    y: positiveOrNegativeGeometryInteger(value.y, `${field}.y`),
    z:
      value.z === undefined
        ? 0
        : positiveOrNegativeGeometryInteger(value.z, `${field}.z`),
  }
}

function requirePlanarPoints(
  rings: readonly (readonly DataPoint3[])[],
  field: string,
): number {
  const first = rings[0]?.[0]
  if (!first) throw invalid(field, 'polygon has no points')
  for (const ring of rings) {
    for (const point of ring) {
      if (point.z !== first.z) {
        throw invalid(field, 'polygon rings must be planar in renderer v1')
      }
    }
  }
  return first.z
}

function assetTransform(
  value: unknown,
  field: string,
): {
  x: number
  y: number
  z: number
  rotationZ: number
  scaleX: number
  scaleY: number
  scaleZ: number
} {
  if (!isRecord(value)) {
    throw invalid(field, 'asset transform must be an object')
  }
  const allowed = new Set([
    'x',
    'y',
    'z',
    'rotationZ',
    'scaleX',
    'scaleY',
    'scaleZ',
  ])
  for (const key of Object.keys(value)) {
    if (!allowed.has(key)) {
      throw invalid(field, `unsupported asset transform field '${key}'`)
    }
  }
  return {
    x: optionalFinite(value.x, 0, `${field}.x`),
    y: optionalFinite(value.y, 0, `${field}.y`),
    z: optionalFinite(value.z, 0, `${field}.z`),
    rotationZ: normalizeRotation(
      optionalFinite(value.rotationZ, 0, `${field}.rotationZ`),
    ),
    scaleX: optionalPositive(value.scaleX, 1, `${field}.scaleX`),
    scaleY: optionalPositive(value.scaleY, 1, `${field}.scaleY`),
    scaleZ: optionalPositive(value.scaleZ, 1, `${field}.scaleZ`),
  }
}

function requireArray<T>(value: readonly T[] | undefined, field: string): T[] {
  if (!Array.isArray(value)) {
    throw invalid(field, 'array is required')
  }
  return [...value]
}

function logicalId(revision: RevisionInput | undefined, field: string): string {
  return requireGuid(revision?.logicalId, `${field}.revision.logicalId`)
}

function requireGuid(value: unknown, field: string): string {
  const normalized =
    typeof value === 'string' ? value.trim().toLowerCase() : undefined
  if (
    !normalized ||
    normalized === EMPTY_GUID ||
    !GUID_PATTERN.test(normalized)
  ) {
    throw invalid(field, 'a non-empty GUID is required')
  }
  return normalized
}

function optionalGuid(
  value: string | undefined,
  field: string,
): string | undefined {
  return value === undefined || value === null || value.trim() === ''
    ? undefined
    : requireGuid(value, field)
}

function requireText(value: string | undefined, field: string): string {
  const normalized = value?.trim()
  if (!normalized) throw invalid(field, 'text is required')
  return normalized
}

function optionalText(value: string | undefined): string | undefined {
  const normalized = value?.trim()
  return normalized || undefined
}

function requireInteger(value: number | undefined, field: string): number {
  if (!Number.isInteger(value)) {
    throw invalid(field, 'an integer millimeter value is required')
  }
  return value!
}

function requirePositiveInteger(
  value: number | undefined,
  field: string,
): number {
  const integer = requireInteger(value, field)
  if (integer <= 0) throw invalid(field, 'a positive value is required')
  return integer
}

function requireNonNegativeInteger(
  value: number | undefined,
  field: string,
): number {
  const integer = requireInteger(value, field)
  if (integer < 0) throw invalid(field, 'a non-negative value is required')
  return integer
}

function requireRotation(value: number | undefined, field: string): number {
  if (!Number.isFinite(value) || value! < 0 || value! >= 360) {
    throw invalid(field, 'rotation must be in [0, 360) degrees')
  }
  return value!
}

function positiveGeometryInteger(value: unknown, field: string): number {
  const integer = positiveOrNegativeGeometryInteger(value, field)
  if (integer <= 0) throw invalid(field, 'a positive value is required')
  return integer
}

function positiveOrNegativeGeometryInteger(
  value: unknown,
  field: string,
): number {
  if (!Number.isInteger(value)) {
    throw invalid(field, 'an integer millimeter value is required')
  }
  return value as number
}

function positiveOrDefault(
  value: number | undefined,
  fallback: number,
): number {
  return Number.isInteger(value) && value! > 0 ? value! : fallback
}

function optionalFinite(
  value: unknown,
  fallback: number,
  field: string,
): number {
  if (value === undefined) return fallback
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw invalid(field, 'a finite number is required')
  }
  return value
}

function optionalPositive(
  value: unknown,
  fallback: number,
  field: string,
): number {
  const parsed = optionalFinite(value, fallback, field)
  if (parsed <= 0) throw invalid(field, 'a positive scale is required')
  return parsed
}

function normalizeRotation(value: number): number {
  return ((value % 360) + 360) % 360
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function invalid(field: string, message: string): ParametricRenderPlanError {
  return new ParametricRenderPlanError(field, message)
}

const GUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'
