import { describe, expect, it } from 'vitest'
import {
  buildSpaceStudioComponentCreationPlan,
  spaceStudioComponentPresets,
  type SpaceStudioComponentPresetId,
} from './staticComponentCatalog'

describe('staticComponentCatalog', () => {
  it('freezes the V1 building, pallet and six static-equipment presets', () => {
    expect(spaceStudioComponentPresets.map(({ id, elementType }) => ({ id, elementType })))
      .toEqual([
        { id: 'wall', elementType: 'Wall' },
        { id: 'column', elementType: 'Column' },
        { id: 'door', elementType: 'Door' },
        { id: 'dock', elementType: 'Dock' },
        { id: 'pallet', elementType: 'Pallet' },
        { id: 'conveyor', elementType: 'Conveyor' },
        { id: 'agv', elementType: 'Device' },
        { id: 'forklift', elementType: 'Device' },
        { id: 'workbench', elementType: 'Workstation' },
        { id: 'electronic-scale', elementType: 'StaticEquipment' },
        { id: 'charging-station', elementType: 'StaticEquipment' },
      ])

    const equipment = spaceStudioComponentPresets.filter(
      preset => preset.group === 'equipment',
    )
    expect(equipment).toHaveLength(6)
    expect(equipment.map(preset => 'equipmentKind' in preset
      ? preset.equipmentKind
      : undefined)).toEqual([
      'Conveyor',
      'Agv',
      'Forklift',
      'Workbench',
      'ElectronicScale',
      'ChargingStation',
    ])
  })

  it.each(spaceStudioComponentPresets)(
    'builds a fenced static create/undo/redo plan for $id',
    (preset) => {
      const logicalId = '12345678-1234-1234-1234-123456789abc'
      const creation = buildSpaceStudioComponentCreationPlan(
        preset.id as SpaceStudioComponentPresetId,
        logicalId,
        1200,
        3400,
      )
      const command = creation.batch.forward[0]!

      expect(command).toMatchObject({
        type: 'CreateElement',
        targetLogicalId: logicalId,
        createElement: {
          elementType: preset.elementType,
          x: 1200,
          y: 3400,
          z: 0,
          rotationZ: 0,
          ...preset.dimensions,
          businessCode: `${preset.businessCodePrefix}-123456781234`,
        },
      })
      expect(command.createElement!.attributes).toEqual(expect.arrayContaining([
        {
          namespace: 'design',
          key: 'catalogPresetId',
          valueType: 'String',
          value: preset.id,
        },
        {
          namespace: 'design',
          key: 'runtimeBehavior',
          valueType: 'String',
          value: 'Static',
        },
      ]))
      expect(JSON.parse(command.createElement!.geometryJson)).toEqual({
        schemaVersion: 1,
        kind: 'box',
        ...preset.dimensions,
      })
      if ('equipmentKind' in preset) {
        expect(command.createElement!.attributes).toContainEqual({
          namespace: 'design',
          key: 'equipmentKind',
          valueType: 'String',
          value: preset.equipmentKind,
        })
      }
      expect(creation.batch.reverse).toEqual([
        { type: 'DeleteObject', targetLogicalId: logicalId },
      ])
      expect(creation.batch.redo).toEqual([
        { type: 'RestoreLogicalObject', targetLogicalId: logicalId },
      ])
    },
  )
})
