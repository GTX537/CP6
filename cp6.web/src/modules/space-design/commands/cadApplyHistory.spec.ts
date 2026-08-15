import { describe, expect, it } from 'vitest'
import { buildCadApplyHistoryEntry } from './cadApplyHistory'

describe('buildCadApplyHistoryEntry', () => {
  it('maps a mixed server-sealed CAD history into the shared command stack', () => {
    const entry = buildCadApplyHistoryEntry({
      appliedChangeCount: 3,
      undoCommands: [
        { type: 'RestoreLogicalObject', targetLogicalId: 'deleted' },
        {
          type: 'UpdateProperties',
          targetLogicalId: 'modified',
          updateProperties: { x: 1250, attributes: [] },
        },
        { type: 'DeleteObject', targetLogicalId: 'added' },
      ],
      redoCommands: [
        { type: 'RestoreLogicalObject', targetLogicalId: 'added' },
        {
          type: 'UpdateProperties',
          targetLogicalId: 'modified',
          updateProperties: { x: 1000, attributes: [] },
        },
        { type: 'DeleteObject', targetLogicalId: 'deleted' },
      ],
    })

    expect(entry.label).toBe('合入 3 项 CAD 变更')
    expect(entry.undo.map(command => command.type)).toEqual([
      'RestoreLogicalObject',
      'UpdateProperties',
      'DeleteObject',
    ])
    expect(entry.redo[1]?.updateProperties).toEqual({ x: 1000, attributes: [] })
  })

  it.each([
    {
      name: 'count mismatch',
      response: {
        appliedChangeCount: 2,
        undoCommands: [{ type: 'DeleteObject', targetLogicalId: 'added' }],
        redoCommands: [{ type: 'RestoreLogicalObject', targetLogicalId: 'added' }],
      },
    },
    {
      name: 'unsupported command',
      response: {
        appliedChangeCount: 1,
        undoCommands: [{ type: 'CreateElement', targetLogicalId: 'added' }],
        redoCommands: [{ type: 'RestoreLogicalObject', targetLogicalId: 'added' }],
      },
    },
    {
      name: 'missing update snapshot',
      response: {
        appliedChangeCount: 1,
        undoCommands: [{ type: 'UpdateProperties', targetLogicalId: 'changed' }],
        redoCommands: [{ type: 'UpdateProperties', targetLogicalId: 'changed' }],
      },
    },
  ])('fails closed for $name', ({ response }) => {
    expect(() => buildCadApplyHistoryEntry(response)).toThrow()
  })
})
