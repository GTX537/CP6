import { describe, expect, it } from 'vitest'
import type {
  ISpaceElementAttributeWriteDto,
  ISpaceSceneElementDto,
  ISpaceSceneRackDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import {
  buildObjectCopyPlan,
  inspectObjectCopySelection,
  type ObjectCopySource,
} from './objectCopy'

const elementId = '10000000-0000-0000-0000-000000000001'
const copiedElementId = '10000000-0000-0000-0000-000000000002'
const rackId = '20000000-0000-0000-0000-000000000001'

describe('object copy', () => {
  it('copies elements and racks in one command plan with safe identity semantics', () => {
    const attributes: ISpaceElementAttributeWriteDto[] = [{
      namespace: 'design',
      key: 'fire-rating',
      valueType: 'String',
      value: '2h',
      unit: undefined,
    }]
    const sources: ObjectCopySource[] = [
      { ownerKind: 'Element', element: element(), attributes },
      { ownerKind: 'Rack', rack: rack(), hasActiveLevel: true },
    ]

    const plan = buildObjectCopyPlan(sources, [rack()], () => copiedElementId)

    expect(plan.elementLogicalIds).toEqual([copiedElementId])
    expect(plan.expectedRackCopies).toBe(1)
    expect(plan.commands).toHaveLength(2)
    expect(plan.commands[0]).toMatchObject({
      type: 'CreateElement',
      targetLogicalId: copiedElementId,
      createElement: {
        elementType: 'Wall',
        x: 2_500,
        y: 2_000,
        width: 1_000,
        parentLogicalId: '30000000-0000-0000-0000-000000000001',
        attributes,
      },
    })
    expect(plan.commands[0]?.createElement).not.toHaveProperty('businessCode')
    expect(plan.commands[0]?.createElement).not.toHaveProperty('linkedEntityType')
    expect(plan.commands[0]?.createElement).not.toHaveProperty('sourceId')
    expect(plan.commands[1]).toMatchObject({
      type: 'GenerateRackArray',
      targetLogicalId: rackId,
      generateRackArray: {
        rows: 1,
        columns: 2,
        columnGap: 500,
        codePrefix: `R-001-COPY-${rackId.slice(0, 8)}-`,
        startNumber: 1,
        codeDigits: 3,
      },
    })
  })

  it('offsets an element along its local X axis and allocates the next rack code', () => {
    const rotated = element({ rotationZ: 90 })
    const longRackCode = 'R'.repeat(100)
    const sourceRack = rack({ rackCode: longRackCode })
    const boundedPrefix = `${longRackCode.slice(0, 79)}-COPY-${rackId.slice(0, 8)}-`
    const existingCopy = rack({
      logicalId: '20000000-0000-0000-0000-000000000002',
      rackCode: `${boundedPrefix}001`,
    })

    const plan = buildObjectCopyPlan([
      { ownerKind: 'Element', element: rotated, attributes: [] },
      { ownerKind: 'Rack', rack: sourceRack, hasActiveLevel: true },
    ], [sourceRack, existingCopy], () => copiedElementId)

    expect(plan.commands[0]?.createElement).toMatchObject({ x: 1_000, y: 3_500 })
    expect(plan.commands[1]?.generateRackArray?.startNumber).toBe(2)
    expect(plan.commands[1]?.generateRackArray?.codePrefix).toBe(boundedPrefix)
    expect(`${boundedPrefix}999999`).toHaveLength(100)
  })

  it('fails closed for empty, inactive, asset-backed, level-less and oversized selections', () => {
    expect(inspectObjectCopySelection([]).reason).toMatch(/至少一个/)
    expect(inspectObjectCopySelection([{
      ownerKind: 'Element',
      element: element({ lifecycleState: 'RemoveRequested' }),
      attributes: [],
    }]).reason).toMatch(/Active/)
    expect(inspectObjectCopySelection([{
      ownerKind: 'Element',
      element: { ...element(), modelAssetId: copiedElementId },
      attributes: [],
    }]).reason).toMatch(/资产/)
    expect(inspectObjectCopySelection([{
      ownerKind: 'Rack', rack: rack(), hasActiveLevel: false,
    }]).reason).toMatch(/设计层/)
    expect(inspectObjectCopySelection(Array.from({ length: 101 }, () => ({
      ownerKind: 'Rack' as const, rack: rack(), hasActiveLevel: true,
    }))).reason).toMatch(/100/)
  })

  it('rejects duplicate allocated identities and Int32 placement overflow', () => {
    expect(() => buildObjectCopyPlan([{
      ownerKind: 'Element', element: element(), attributes: [],
    }], [], () => elementId)).toThrow(/unique/)
    expect(() => buildObjectCopyPlan([{
      ownerKind: 'Element',
      element: element({ x: 2_147_483_000 }),
      attributes: [],
    }], [], () => copiedElementId)).toThrow(/Int32/)
  })
})

function element(overrides: {
  x?: number
  rotationZ?: number
  lifecycleState?: string
} = {}): ISpaceSceneElementDto {
  return {
    revision: {
      logicalId: elementId,
      revisionId: '10000000-0000-0000-0000-000000000010',
      lifecycleState: overrides.lifecycleState ?? 'Active',
    } as unknown as NonNullable<ISpaceSceneElementDto['revision']>,
    parentLogicalId: '30000000-0000-0000-0000-000000000001',
    elementType: 'Wall',
    geometryJson: JSON.stringify({
      schemaVersion: 1,
      kind: 'box',
      width: 1_000,
      height: 3_000,
      depth: 200,
    }),
    x: overrides.x ?? 1_000,
    y: 2_000,
    z: 0,
    rotationZ: overrides.rotationZ ?? 0,
    width: 1_000,
    height: 3_000,
    depth: 200,
    businessCode: 'UNIQUE-WALL-1',
    linkedEntityType: 'Door',
    linkedLogicalId: '40000000-0000-0000-0000-000000000001',
  }
}

function rack(overrides: {
  logicalId?: string
  rackCode?: string
} = {}): ISpaceSceneRackDto {
  return {
    revision: {
      logicalId: overrides.logicalId ?? rackId,
      revisionId: '20000000-0000-0000-0000-000000000010',
      lifecycleState: 'Active',
    } as unknown as NonNullable<ISpaceSceneRackDto['revision']>,
    floorLogicalId: '50000000-0000-0000-0000-000000000001',
    zoneLogicalId: '60000000-0000-0000-0000-000000000001',
    rackCode: overrides.rackCode ?? 'R-001',
    x: 5_000,
    y: 6_000,
    z: 0,
    rotationZ: 0,
    width: 2_700,
    height: 5_000,
    depth: 1_100,
  }
}
