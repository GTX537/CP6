import type {
  EditorCommandInput,
  ReversibleCommandBatch,
} from '@/modules/space-design/commands/editorBatchCommands'

type ComponentGroup = 'building' | 'handling' | 'equipment'

interface ComponentDimensions {
  width: number
  height: number
  depth: number
}

export interface SpaceStudioComponentPreset {
  id: string
  label: string
  group: ComponentGroup
  elementType: string
  businessCodePrefix: string
  dimensions: ComponentDimensions
  equipmentKind?: string
}

export const spaceStudioComponentGroups: ReadonlyArray<{
  id: ComponentGroup
  label: string
}> = [
  { id: 'building', label: '建筑构件' },
  { id: 'handling', label: '存储构件' },
  { id: 'equipment', label: '静态设备' },
]

export const spaceStudioComponentPresets = [
  {
    id: 'wall',
    label: '墙体',
    group: 'building',
    elementType: 'Wall',
    businessCodePrefix: 'WALL',
    dimensions: { width: 4000, height: 3000, depth: 200 },
  },
  {
    id: 'column',
    label: '柱',
    group: 'building',
    elementType: 'Column',
    businessCodePrefix: 'COL',
    dimensions: { width: 500, height: 3000, depth: 500 },
  },
  {
    id: 'door',
    label: '门',
    group: 'building',
    elementType: 'Door',
    businessCodePrefix: 'DOOR',
    dimensions: { width: 1200, height: 2200, depth: 200 },
  },
  {
    id: 'dock',
    label: '月台',
    group: 'building',
    elementType: 'Dock',
    businessCodePrefix: 'DOCK',
    dimensions: { width: 3000, height: 1200, depth: 2500 },
  },
  {
    id: 'pallet',
    label: '托盘',
    group: 'handling',
    elementType: 'Pallet',
    businessCodePrefix: 'PAL',
    dimensions: { width: 1200, height: 150, depth: 1000 },
  },
  {
    id: 'conveyor',
    label: '输送线',
    group: 'equipment',
    elementType: 'Conveyor',
    businessCodePrefix: 'CONV',
    dimensions: { width: 2000, height: 900, depth: 800 },
    equipmentKind: 'Conveyor',
  },
  {
    id: 'agv',
    label: 'AGV',
    group: 'equipment',
    elementType: 'Device',
    businessCodePrefix: 'AGV',
    dimensions: { width: 900, height: 350, depth: 650 },
    equipmentKind: 'Agv',
  },
  {
    id: 'forklift',
    label: '叉车',
    group: 'equipment',
    elementType: 'Device',
    businessCodePrefix: 'FORK',
    dimensions: { width: 1200, height: 2200, depth: 2500 },
    equipmentKind: 'Forklift',
  },
  {
    id: 'workbench',
    label: '工作台',
    group: 'equipment',
    elementType: 'Workstation',
    businessCodePrefix: 'BENCH',
    dimensions: { width: 1800, height: 900, depth: 800 },
    equipmentKind: 'Workbench',
  },
  {
    id: 'electronic-scale',
    label: '电子秤',
    group: 'equipment',
    elementType: 'StaticEquipment',
    businessCodePrefix: 'SCALE',
    dimensions: { width: 800, height: 120, depth: 800 },
    equipmentKind: 'ElectronicScale',
  },
  {
    id: 'charging-station',
    label: '充电站',
    group: 'equipment',
    elementType: 'StaticEquipment',
    businessCodePrefix: 'CHG',
    dimensions: { width: 800, height: 1800, depth: 500 },
    equipmentKind: 'ChargingStation',
  },
] as const satisfies readonly SpaceStudioComponentPreset[]

export type SpaceStudioComponentPresetId =
  typeof spaceStudioComponentPresets[number]['id']

export interface SpaceStudioComponentCreationPlan {
  preset: typeof spaceStudioComponentPresets[number]
  batch: ReversibleCommandBatch
}

export function buildSpaceStudioComponentCreationPlan(
  presetId: SpaceStudioComponentPresetId,
  logicalId: string,
  x: number,
  y: number,
): SpaceStudioComponentCreationPlan {
  const preset = spaceStudioComponentPresets.find(item => item.id === presetId)
  if (!preset) throw new Error(`Unsupported Space Studio component preset: ${presetId}`)

  const attributes: unknown[] = [
    designAttribute('catalogPresetId', preset.id),
    designAttribute('runtimeBehavior', 'Static'),
  ]
  if ('equipmentKind' in preset) {
    attributes.push(designAttribute('equipmentKind', preset.equipmentKind))
  }

  const createCommand: EditorCommandInput = {
    type: 'CreateElement',
    targetLogicalId: logicalId,
    createElement: {
      elementType: preset.elementType,
      geometryJson: JSON.stringify({
        schemaVersion: 1,
        kind: 'box',
        ...preset.dimensions,
      }),
      x,
      y,
      z: 0,
      rotationZ: 0,
      ...preset.dimensions,
      businessCode: `${preset.businessCodePrefix}-${logicalId.replaceAll('-', '').slice(0, 12).toUpperCase()}`,
      attributes,
    },
  }

  return {
    preset,
    batch: {
      forward: [createCommand],
      reverse: [{ type: 'DeleteObject', targetLogicalId: logicalId }],
      redo: [{ type: 'RestoreLogicalObject', targetLogicalId: logicalId }],
    },
  }
}

function designAttribute(key: string, value: string) {
  return {
    namespace: 'design',
    key,
    valueType: 'String',
    value,
  }
}
