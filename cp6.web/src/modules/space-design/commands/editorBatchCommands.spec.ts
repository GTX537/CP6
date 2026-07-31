import { describe, expect, it } from 'vitest'
import {
  SavedCommandHistory,
  buildAlignmentBatch,
  buildDeleteBatch,
  buildDistributionBatch,
  buildRotationBatch,
  type EditorObjectSnapshot,
} from './editorBatchCommands'

const objects: EditorObjectSnapshot[] = [
  snapshot('a', 'Element', 0, 10),
  snapshot('b', 'Rack', 30, 20),
  snapshot('c', 'Element', 80, 10),
]

describe('editor batch commands', () => {
  it('aligns racks and common elements in one reversible move batch', () => {
    const batch = buildAlignmentBatch(objects.slice(0, 2), 'left')

    expect(batch.forward).toEqual([
      {
        type: 'MoveObject',
        targetLogicalId: 'a',
        moveObject: { x: 0, y: 0, z: 0 },
      },
      {
        type: 'MoveObject',
        targetLogicalId: 'b',
        moveObject: { x: 0, y: 0, z: 0 },
      },
    ])
    expect(batch.reverse[1]?.moveObject).toEqual({ x: 30, y: 0, z: 0 })
  })

  it('distributes centers while preserving the first and last anchors', () => {
    const batch = buildDistributionBatch(objects, 'horizontal')

    expect(batch.forward.map((command) => command.moveObject?.x)).toEqual([
      0, 35, 80,
    ])
  })

  it('treats larger world Y as visual top on the Y-flipped canvas', () => {
    const lower = snapshot('lower', 'Element', 0, 10)
    const upper = {
      ...snapshot('upper', 'Rack', 0, 10),
      y: 40,
      bounds: { minX: 0, maxX: 10, minY: 40, maxY: 50 },
    }

    const batch = buildAlignmentBatch([lower, upper], 'top')

    expect(batch.forward.map((command) => command.moveObject?.y)).toEqual([
      40, 40,
    ])
  })

  it('builds shared rotation and lifecycle compensation commands', () => {
    const rotation = buildRotationBatch(objects.slice(0, 2), 90)
    const deletion = buildDeleteBatch(objects.slice(0, 2))

    expect(rotation.forward.map((command) => command.type)).toEqual([
      'RotateObject',
      'RotateObject',
    ])
    expect(deletion.reverse.map((command) => command.type)).toEqual([
      'RestoreLogicalObject',
      'RestoreLogicalObject',
    ])
  })

  it('moves entries between saved undo and redo stacks only on completion', () => {
    const history = new SavedCommandHistory()
    const entry = {
      label: 'align',
      undo: buildAlignmentBatch(objects.slice(0, 2), 'left').reverse,
      redo: buildAlignmentBatch(objects.slice(0, 2), 'left').forward,
    }
    history.push(entry)

    const undo = history.takeUndo()!
    expect(history.canUndo).toBe(false)
    history.cancelUndo(undo)
    expect(history.canUndo).toBe(true)
    history.completeUndo(history.takeUndo()!)
    expect(history.canRedo).toBe(true)
    history.completeRedo(history.takeRedo()!)
    expect(history.canUndo).toBe(true)
  })
})

function snapshot(
  logicalId: string,
  ownerKind: 'Element' | 'Rack',
  x: number,
  width: number,
): EditorObjectSnapshot {
  return {
    logicalId,
    ownerKind,
    x,
    y: 0,
    z: 0,
    rotationZ: 0,
    bounds: { minX: x, maxX: x + width, minY: 0, maxY: width },
  }
}
