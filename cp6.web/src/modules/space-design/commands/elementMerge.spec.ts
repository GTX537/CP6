import { describe, expect, it } from 'vitest'
import type {
  ISpaceSceneElementAttributeDto,
  ISpaceSceneElementDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import { buildElementMergePlan } from './elementMerge'

const firstId = '11111111-1111-1111-1111-111111111111'
const secondId = '22222222-2222-2222-2222-222222222222'

describe('buildElementMergePlan', () => {
  it('preserves the first identity and builds atomic merge and compensation batches', () => {
    const first = element(firstId, 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 100, 200)
    const second = element(secondId, 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 900, 200)
    second.revision!.sourceId = 'cccccccc-cccc-cccc-cccc-cccccccccccc'
    second.revision!.sourceRef = 'CAD-COLUMN-02'
    const attributes = [
      attribute(first.revision!.revisionId!),
      attribute(second.revision!.revisionId!),
    ]

    const plan = buildElementMergePlan([first, second], attributes)
    const update = plan.batch.forward[0]!.updateProperties as {
      geometryJson: string
      x: number
      y: number
      width: number
    }
    const geometry = JSON.parse(update.geometryJson)

    expect(plan.survivorLogicalId).toBe(firstId)
    expect(plan.sourceLogicalIds).toEqual([secondId])
    expect(plan.batch.forward.map((command) => command.type)).toEqual([
      'UpdateProperties',
      'DeleteObject',
    ])
    expect(plan.batch.reverse.map((command) => command.type)).toEqual([
      'UpdateProperties',
      'RestoreLogicalObject',
    ])
    expect(update).toMatchObject({ x: 100, y: 200, width: 1200 })
    expect(geometry).toMatchObject({
      schemaVersion: 1,
      kind: 'group',
      parts: [
        { sourceLogicalId: firstId, x: 0, y: 0 },
        {
          sourceLogicalId: secondId,
          sourceId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
          sourceRef: 'CAD-COLUMN-02',
          x: 800,
          y: 0,
        },
      ],
    })
    expect(
      (plan.batch.reverse[0]!.updateProperties as { geometryJson: string })
        .geometryJson,
    ).toBe(first.geometryJson)
  })

  it('is deterministic for the same ordered selection', () => {
    const values = [
      element(firstId, 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 100, 200),
      element(secondId, 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 900, 200),
    ]
    const attributes = values.map((value) => attribute(value.revision!.revisionId!))

    expect(buildElementMergePlan(values, attributes)).toEqual(
      buildElementMergePlan(values, attributes),
    )
  })

  it('rejects asset-backed, metadata-mismatched and attribute-mismatched elements', () => {
    const first = element(firstId, 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 0, 0)
    const second = element(secondId, 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 500, 0)
    const attributes = [
      attribute(first.revision!.revisionId!),
      attribute(second.revision!.revisionId!),
    ]

    second.modelAssetId = 'dddddddd-dddd-dddd-dddd-dddddddddddd'
    expect(() => buildElementMergePlan([first, second], attributes)).toThrow(
      /Asset-backed/,
    )

    second.modelAssetId = undefined
    second.elementType = 'Door'
    expect(() => buildElementMergePlan([first, second], attributes)).toThrow(
      /share type/,
    )

    second.elementType = 'Column'
    attributes[1]!.value = 'B'
    expect(() => buildElementMergePlan([first, second], attributes)).toThrow(
      /identical design attributes/,
    )
  })

  it('rejects duplicate, inactive, invalid geometry and oversized selections', () => {
    const first = element(firstId, 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 0, 0)
    const second = element(secondId, 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 500, 0)

    expect(() => buildElementMergePlan([first, first], [])).toThrow(/duplicate/)

    second.revision!.lifecycleState = 'RemoveRequested'
    expect(() => buildElementMergePlan([first, second], [])).toThrow(/active Draft/)

    second.revision!.lifecycleState = 'Active'
    second.geometryJson = '{}'
    expect(() => buildElementMergePlan([first, second], [])).toThrow(
      /schemaVersion 1/,
    )

    const oversized = Array.from({ length: 21 }, (_, index) =>
      element(
        `${String(index + 1).padStart(8, '0')}-1111-1111-1111-111111111111`,
        `${String(index + 1).padStart(8, '0')}-2222-2222-2222-222222222222`,
        index * 500,
        0,
      ),
    )
    expect(() => buildElementMergePlan(oversized, [])).toThrow(/between 2 and 20/)
  })
})

function element(
  logicalId: string,
  revisionId: string,
  x: number,
  y: number,
): ISpaceSceneElementDto {
  return {
    revision: {
      logicalId,
      revisionId,
      lifecycleState: 'Active',
    } as ISpaceSceneElementDto['revision'],
    floorLogicalId: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
    parentLogicalId: 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
    elementType: 'Column',
    geometryJson:
      '{"schemaVersion":1,"kind":"box","width":400,"height":3000,"depth":400}',
    x,
    y,
    z: 0,
    rotationZ: 0,
    width: 400,
    height: 3000,
    depth: 400,
  }
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
