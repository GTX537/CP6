import { describe, expect, it } from 'vitest'
import type {
  ISpaceSceneElementAttributeDto,
  ISpaceSceneElementDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { buildElementSplitPlan } from './elementSplit'
import { buildParametricRenderPlan } from '@/space-viewer/design/ParametricRenderPlan'
import { buildElementCanvasPlan } from '@/modules/space-design/canvas2d/elementCanvasPlan'

const groupId = '11111111-1111-1111-1111-111111111111'
const revisionId = '22222222-2222-2222-2222-222222222222'
const secondSourceId = '33333333-3333-3333-3333-333333333333'
const allocatedId = '44444444-4444-4444-4444-444444444444'

describe('buildElementSplitPlan', () => {
  it('allocates new identities, inherits metadata and builds distinct redo commands', () => {
    const group = groupElement()
    const attributes = [attribute(revisionId)]

    const plan = buildElementSplitPlan(group, attributes, () => allocatedId)
    const survivor = plan.batch.forward[0]!.updateProperties as Record<string, any>
    const created = plan.batch.forward[1]!.createElement!

    expect(plan).toMatchObject({
      groupLogicalId: groupId,
      splitLogicalIds: [allocatedId],
      partCount: 2,
    })
    expect(plan.batch.forward.map((command) => command.type)).toEqual([
      'UpdateProperties',
      'CreateElement',
    ])
    expect(plan.batch.reverse.map((command) => command.type)).toEqual([
      'UpdateProperties',
      'DeleteObject',
    ])
    expect(plan.batch.redo?.map((command) => command.type)).toEqual([
      'UpdateProperties',
      'RestoreLogicalObject',
    ])
    expect(survivor).toMatchObject({
      geometryJson: '{"schemaVersion":1,"kind":"box","width":400,"height":3000,"depth":400}',
      x: 1000,
      y: 2000,
      z: 10,
      rotationZ: 100,
      width: 400,
      height: 3000,
      depth: 400,
      linkedEntityType: 'WarehouseNode',
    })
    expect(created).toMatchObject({
      elementType: 'Column',
      x: 1000,
      y: 2800,
      z: 10,
      rotationZ: 110,
      width: 400,
      height: 3000,
      depth: 400,
      businessCode: 'COL-01',
      parentLogicalId: '77777777-7777-7777-7777-777777777777',
      sourceId: '88888888-8888-8888-8888-888888888888',
      sourceRef: 'CAD-COLUMN-02',
      linkedEntityType: 'WarehouseNode',
      linkedLogicalId: '99999999-9999-9999-9999-999999999999',
      attributes: [{ namespace: 'design', key: 'confidence', value: '0.85' }],
    })
    expect(JSON.parse(created.geometryJson)).toMatchObject({
      schemaVersion: 1,
      kind: 'box',
    })
    expect(
      (plan.batch.reverse[0]!.updateProperties as { geometryJson: string })
        .geometryJson,
    ).toBe(group.geometryJson)
  })

  it('keeps nested groups as independent top-level parts', () => {
    const group = groupElement()
    const geometry = JSON.parse(group.geometryJson!)
    geometry.parts[1].geometry = {
      schemaVersion: 1,
      kind: 'group',
      parts: [
        { ...geometry.parts[0], sourceLogicalId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' },
        { ...geometry.parts[0], sourceLogicalId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb' },
      ],
    }
    group.geometryJson = JSON.stringify(geometry)

    const plan = buildElementSplitPlan(group, [], () => allocatedId)

    expect(JSON.parse(plan.batch.forward[1]!.createElement!.geometryJson).kind)
      .toBe('group')
  })

  it('preserves the same 2D and 3D geometry after the split', () => {
    const group = groupElement()
    const plan = buildElementSplitPlan(group, [], () => allocatedId)
    const survivor = {
      ...group,
      ...(plan.batch.forward[0]!.updateProperties as Record<string, unknown>),
    }
    const create = plan.batch.forward[1]!.createElement!
    const created = {
      revision: {
        logicalId: allocatedId,
        revisionId: '55555555-5555-5555-5555-555555555555',
        lifecycleState: 'Active',
      },
      floorLogicalId: group.floorLogicalId,
      ...create,
    } as unknown as ISpaceSceneElementDto
    const scene = (elements: ISpaceSceneElementDto[]) => ({
      schemaVersion: 1,
      authority: 'DesignRevision',
      runtimeOverlayIncluded: false,
      zones: [],
      aisles: [],
      racks: [],
      rackLevels: [],
      locations: [],
      elements,
      elementAttributes: [],
      locationExternalBindings: [],
      designAttributes: [],
    }) as any
    const before = buildParametricRenderPlan(scene([group])).boxes
    const after = buildParametricRenderPlan(scene([survivor, created])).boxes
    const normalize3d = (boxes: typeof before) => boxes.map((box) => ({
      center: box.center,
      size: box.size,
      rotationZ: box.rotationZ,
      elementType: box.elementType,
    })).sort((left, right) => left.center.x - right.center.x || left.center.y - right.center.y)
    const normalize2d = (elements: ISpaceSceneElementDto[]) =>
      buildElementCanvasPlan(scene(elements)).map((item) => item.kind === 'rect'
        ? {
            centerX: item.centerX,
            centerY: item.centerY,
            width: item.width,
            depth: item.depth,
            rotationZ: item.rotationZ,
            elementType: item.elementType,
          }
        : item)
        .sort((left: any, right: any) =>
          (left.centerX ?? 0) - (right.centerX ?? 0)
          || (left.centerY ?? 0) - (right.centerY ?? 0))

    expect(normalize3d(after)).toEqual(normalize3d(before))
    expect(normalize2d([survivor, created])).toEqual(normalize2d([group]))
  })

  it('rejects non-groups, inactive or asset-backed groups and invalid provenance', () => {
    const group = groupElement()
    group.geometryJson = '{"schemaVersion":1,"kind":"box","width":1,"height":1,"depth":1}'
    expect(() => buildElementSplitPlan(group, [])).toThrow(/group element/)

    group.geometryJson = groupGeometry()
    group.revision!.lifecycleState = 'RemoveRequested'
    expect(() => buildElementSplitPlan(group, [])).toThrow(/active Draft/)

    group.revision!.lifecycleState = 'Active'
    group.modelAssetId = 'aaaaaaaa-1111-1111-1111-111111111111'
    expect(() => buildElementSplitPlan(group, [])).toThrow(/Asset-backed/)

    group.modelAssetId = undefined
    const geometry = JSON.parse(group.geometryJson!)
    delete geometry.parts[1].sourceRef
    group.geometryJson = JSON.stringify(geometry)
    expect(() => buildElementSplitPlan(group, [])).toThrow(/must be paired/)
  })

  it('rejects duplicate source and allocated identities before saving', () => {
    const group = groupElement()
    const geometry = JSON.parse(group.geometryJson!)
    geometry.parts[1].sourceLogicalId = groupId
    group.geometryJson = JSON.stringify(geometry)
    expect(() => buildElementSplitPlan(group, [])).toThrow(/must be unique/)

    group.geometryJson = groupGeometry()
    expect(() => buildElementSplitPlan(group, [], () => secondSourceId)).toThrow(
      /Allocated split logical identities must be unique/,
    )
  })
})

function groupElement(): ISpaceSceneElementDto {
  return {
    revision: {
      logicalId: groupId,
      revisionId,
      lifecycleState: 'Active',
    } as ISpaceSceneElementDto['revision'],
    floorLogicalId: '66666666-6666-6666-6666-666666666666',
    parentLogicalId: '77777777-7777-7777-7777-777777777777',
    elementType: 'Column',
    geometryJson: groupGeometry(),
    x: 1000,
    y: 2000,
    z: 10,
    rotationZ: 90,
    width: 1200,
    height: 3000,
    depth: 400,
    businessCode: 'COL-01',
    linkedEntityType: 'WarehouseNode',
    linkedLogicalId: '99999999-9999-9999-9999-999999999999',
  }
}

function groupGeometry(): string {
  return JSON.stringify({
    schemaVersion: 1,
    kind: 'group',
    parts: [
      {
        sourceLogicalId: groupId,
        x: 0,
        y: 0,
        z: 0,
        rotationZ: 10,
        width: 400,
        height: 3000,
        depth: 400,
        geometry: {
          schemaVersion: 1,
          kind: 'box',
          width: 400,
          height: 3000,
          depth: 400,
        },
      },
      {
        sourceLogicalId: secondSourceId,
        sourceId: '88888888-8888-8888-8888-888888888888',
        sourceRef: 'CAD-COLUMN-02',
        x: 800,
        y: 0,
        z: 0,
        rotationZ: 20,
        width: 400,
        height: 3000,
        depth: 400,
        geometry: {
          schemaVersion: 1,
          kind: 'box',
          width: 400,
          height: 3000,
          depth: 400,
        },
      },
    ],
  })
}

function attribute(elementRevisionId: string): ISpaceSceneElementAttributeDto {
  return {
    elementRevisionId,
    namespace: 'design',
    key: 'confidence',
    valueType: 'Decimal',
    value: '0.85',
  }
}
