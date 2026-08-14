export type EditorOwnerKind = 'Element' | 'Rack'

export interface EditorObjectSnapshot {
  logicalId: string
  ownerKind: EditorOwnerKind
  x: number
  y: number
  z: number
  rotationZ: number
  bounds: {
    minX: number
    maxX: number
    minY: number
    maxY: number
  }
}

export interface MoveObjectPayload {
  x: number
  y: number
  z: number
}

export interface RotateObjectPayload {
  rotationZ: number
}

export interface GenerateRackArrayPayload {
  rows: number
  columns: number
  rowGap: number
  columnGap: number
  staggerOffset: number
  codePrefix: string
  startNumber: number
  codeDigits: number
}

export interface EditorCommandInput {
  type:
    | 'MoveObject'
    | 'RotateObject'
    | 'DeleteObject'
    | 'RestoreLogicalObject'
    | 'GenerateRackArray'
    | 'CreateElement'
    | 'UpdateProperties'
  targetLogicalId: string
  moveObject?: MoveObjectPayload
  rotateObject?: RotateObjectPayload
  generateRackArray?: GenerateRackArrayPayload
  createElement?: {
    elementType: string
    geometryJson: string
    x: number
    y: number
    z: number
    rotationZ: number
    width: number
    height: number
    depth: number
    businessCode?: string
    parentLogicalId?: string
    sourceId?: string
    sourceRef?: string
    attributes: unknown[]
  }
  updateProperties?: unknown
}

export interface ReversibleCommandBatch {
  forward: EditorCommandInput[]
  reverse: EditorCommandInput[]
}

export type AlignmentMode =
  | 'left'
  | 'centerX'
  | 'right'
  | 'top'
  | 'centerY'
  | 'bottom'

export type DistributionMode = 'horizontal' | 'vertical'

export interface EditorHistoryEntry {
  label: string
  undo: EditorCommandInput[]
  redo: EditorCommandInput[]
}

export class SavedCommandHistory {
  private readonly undoEntries: EditorHistoryEntry[] = []
  private readonly redoEntries: EditorHistoryEntry[] = []

  constructor(private readonly capacity = 100) {}

  get canUndo(): boolean {
    return this.undoEntries.length > 0
  }

  get canRedo(): boolean {
    return this.redoEntries.length > 0
  }

  push(entry: EditorHistoryEntry): void {
    this.undoEntries.push(entry)
    if (this.undoEntries.length > this.capacity) this.undoEntries.shift()
    this.redoEntries.length = 0
  }

  takeUndo(): EditorHistoryEntry | undefined {
    return this.undoEntries.pop()
  }

  completeUndo(entry: EditorHistoryEntry): void {
    this.redoEntries.push(entry)
  }

  cancelUndo(entry: EditorHistoryEntry): void {
    this.undoEntries.push(entry)
  }

  takeRedo(): EditorHistoryEntry | undefined {
    return this.redoEntries.pop()
  }

  completeRedo(entry: EditorHistoryEntry): void {
    this.undoEntries.push(entry)
  }

  cancelRedo(entry: EditorHistoryEntry): void {
    this.redoEntries.push(entry)
  }

  clear(): void {
    this.undoEntries.length = 0
    this.redoEntries.length = 0
  }
}

export function buildAlignmentBatch(
  objects: readonly EditorObjectSnapshot[],
  mode: AlignmentMode,
): ReversibleCommandBatch {
  if (objects.length < 2) return { forward: [], reverse: [] }
  const vertical = mode === 'top' || mode === 'centerY' || mode === 'bottom'
  const values = objects.map((object) => alignmentValue(object, mode))
  const target =
    mode === 'centerX'
      ? (Math.min(...objects.map((object) => object.bounds.minX)) +
          Math.max(...objects.map((object) => object.bounds.maxX))) /
        2
      : mode === 'centerY'
        ? (Math.min(...objects.map((object) => object.bounds.minY)) +
            Math.max(...objects.map((object) => object.bounds.maxY))) /
          2
        : mode === 'left' || mode === 'bottom'
      ? Math.min(...values)
      : mode === 'right' || mode === 'top'
        ? Math.max(...values)
        : 0
  const destinations = objects.map((object, index) => {
    const delta = target - values[index]!
    return {
      object,
      x: vertical ? object.x : Math.round(object.x + delta),
      y: vertical ? Math.round(object.y + delta) : object.y,
    }
  })
  return moveBatch(destinations)
}

export function buildDistributionBatch(
  objects: readonly EditorObjectSnapshot[],
  mode: DistributionMode,
): ReversibleCommandBatch {
  if (objects.length < 3) return { forward: [], reverse: [] }
  const horizontal = mode === 'horizontal'
  const ordered = objects
    .slice()
    .sort(
      (left, right) =>
        minimum(left, horizontal) - minimum(right, horizontal) ||
        left.logicalId.localeCompare(right.logicalId),
    )
  const start = minimum(ordered[0]!, horizontal)
  const end = maximum(ordered[ordered.length - 1]!, horizontal)
  const occupied = ordered.reduce(
    (total, object) => total + size(object, horizontal),
    0,
  )
  const gap = (end - start - occupied) / (ordered.length - 1)
  let cursor = start
  const destinations = ordered.map((object) => {
    const delta = cursor - minimum(object, horizontal)
    cursor += size(object, horizontal) + gap
    return {
      object,
      x: horizontal ? Math.round(object.x + delta) : object.x,
      y: horizontal ? object.y : Math.round(object.y + delta),
    }
  })
  return moveBatch(destinations)
}

export function buildRotationBatch(
  objects: readonly EditorObjectSnapshot[],
  deltaDegrees: number,
): ReversibleCommandBatch {
  return {
    forward: objects.map((object) => ({
      type: 'RotateObject',
      targetLogicalId: object.logicalId,
      rotateObject: {
        rotationZ: normalizeRotation(object.rotationZ + deltaDegrees),
      },
    })),
    reverse: objects.map((object) => ({
      type: 'RotateObject',
      targetLogicalId: object.logicalId,
      rotateObject: { rotationZ: object.rotationZ },
    })),
  }
}

export function buildTranslationBatch(
  objects: readonly EditorObjectSnapshot[],
  deltaX: number,
  deltaY: number,
): ReversibleCommandBatch {
  if (!Number.isFinite(deltaX) || !Number.isFinite(deltaY)) {
    throw new Error('Object translation is invalid')
  }
  const x = Math.round(deltaX)
  const y = Math.round(deltaY)
  if (objects.length === 0 || (x === 0 && y === 0)) {
    return { forward: [], reverse: [] }
  }
  return moveBatch(objects.map((object) => ({
    object,
    x: object.x + x,
    y: object.y + y,
  })))
}

export function buildDeleteBatch(
  objects: readonly EditorObjectSnapshot[],
): ReversibleCommandBatch {
  return {
    forward: objects.map((object) => ({
      type: 'DeleteObject',
      targetLogicalId: object.logicalId,
    })),
    reverse: objects.map((object) => ({
      type: 'RestoreLogicalObject',
      targetLogicalId: object.logicalId,
    })),
  }
}

function moveBatch(
  destinations: readonly {
    object: EditorObjectSnapshot
    x: number
    y: number
  }[],
): ReversibleCommandBatch {
  return {
    forward: destinations.map(({ object, x, y }) => ({
      type: 'MoveObject',
      targetLogicalId: object.logicalId,
      moveObject: { x, y, z: object.z },
    })),
    reverse: destinations.map(({ object }) => ({
      type: 'MoveObject',
      targetLogicalId: object.logicalId,
      moveObject: { x: object.x, y: object.y, z: object.z },
    })),
  }
}

function alignmentValue(
  object: EditorObjectSnapshot,
  mode: AlignmentMode,
): number {
  switch (mode) {
    case 'left':
      return object.bounds.minX
    case 'centerX':
      return (object.bounds.minX + object.bounds.maxX) / 2
    case 'right':
      return object.bounds.maxX
    case 'top':
      return object.bounds.maxY
    case 'centerY':
      return (object.bounds.minY + object.bounds.maxY) / 2
    case 'bottom':
      return object.bounds.minY
  }
}

function minimum(object: EditorObjectSnapshot, horizontal: boolean): number {
  return horizontal ? object.bounds.minX : object.bounds.minY
}

function maximum(object: EditorObjectSnapshot, horizontal: boolean): number {
  return horizontal ? object.bounds.maxX : object.bounds.maxY
}

function size(object: EditorObjectSnapshot, horizontal: boolean): number {
  return maximum(object, horizontal) - minimum(object, horizontal)
}

function normalizeRotation(value: number): number {
  const normalized = value % 360
  return normalized < 0 ? normalized + 360 : normalized
}
