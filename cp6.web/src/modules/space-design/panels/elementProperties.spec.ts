import { describe, expect, it } from 'vitest'
import type { ISpaceSceneElementDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import {
  buildElementPropertiesPayload,
  createElementPropertiesDraft,
} from './elementProperties'

const element = {
  geometryJson:
    '{"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}',
  x: 1000,
  y: 2000,
  z: 0,
  rotationZ: 0,
  width: 400,
  height: 5000,
  depth: 400,
  businessCode: 'C-01',
} as ISpaceSceneElementDto

describe('elementProperties', () => {
  it('builds a typed property command and keeps box geometry in sync', () => {
    const draft = createElementPropertiesDraft(element, [
      {
        namespace: 'design',
        key: 'label',
        valueType: 'String',
        value: 'Column A',
      },
    ])
    draft.x = 1200
    draft.width = 600
    draft.height = 5200
    draft.depth = 500

    const payload = buildElementPropertiesPayload(element, draft)

    expect(payload.x).toBe(1200)
    expect(payload.attributes).toEqual([
      {
        namespace: 'design',
        key: 'label',
        valueType: 'String',
        value: 'Column A',
        unit: undefined,
      },
    ])
    expect(JSON.parse(payload.geometryJson)).toMatchObject({
      schemaVersion: 1,
      kind: 'box',
      width: 600,
      height: 5200,
      depth: 500,
    })
  })

  it('rejects invalid dimensions, half links and duplicate attributes', () => {
    const draft = createElementPropertiesDraft(element, [])
    draft.width = 0
    expect(() => buildElementPropertiesPayload(element, draft)).toThrow(
      'positive integer',
    )

    draft.width = 400
    draft.linkedEntityType = 'Location'
    expect(() => buildElementPropertiesPayload(element, draft)).toThrow('paired')

    draft.linkedEntityType = ''
    draft.attributes = [
      {
        namespace: 'Design',
        key: 'Label',
        valueType: 'String',
        value: 'A',
      },
      {
        namespace: 'design',
        key: 'label',
        valueType: 'String',
        value: 'B',
      },
    ]
    expect(() => buildElementPropertiesPayload(element, draft)).toThrow(
      'unique',
    )
  })
})
