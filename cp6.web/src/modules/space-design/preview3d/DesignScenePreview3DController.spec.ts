import { describe, expect, it } from 'vitest'
import type { ParametricPickTarget } from '@/space-viewer/design/ParametricDesignSceneBuilder'
import {
  isDesignPreviewViewState,
  selectionForDesignPreviewTarget,
} from './DesignScenePreview3D'

describe('DesignScenePreview3D view and selection contracts', () => {
  it('accepts only finite, bounded camera state with the current schema', () => {
    expect(isDesignPreviewViewState({
      schemaVersion: 1,
      cameraPosition: [10, 20, 30],
      target: [1, 2, 3],
    })).toBe(true)
    expect(isDesignPreviewViewState({
      schemaVersion: 2,
      cameraPosition: [10, 20, 30],
      target: [1, 2, 3],
    })).toBe(false)
    expect(isDesignPreviewViewState({
      schemaVersion: 1,
      cameraPosition: [Number.POSITIVE_INFINITY, 20, 30],
      target: [1, 2, 3],
    })).toBe(false)
  })

  it('maps a picked rack level back to the editable rack authority', () => {
    expect(selectionForDesignPreviewTarget(target({
      ownerKind: 'RackLevel',
      logicalId: 'level-1',
      parentLogicalId: 'rack-1',
    }))).toEqual({ logicalId: 'rack-1', ownerKind: 'Rack' })
    expect(selectionForDesignPreviewTarget(target({
      ownerKind: 'Element',
      logicalId: 'column-1',
    }))).toEqual({ logicalId: 'column-1', ownerKind: 'Element' })
    expect(selectionForDesignPreviewTarget(target({
      ownerKind: 'Location',
      logicalId: 'location-1',
    }))).toBeNull()
  })
})

function target(
  values: Pick<ParametricPickTarget, 'ownerKind' | 'logicalId'>
    & Partial<ParametricPickTarget>,
): ParametricPickTarget {
  return {
    primitiveKey: `test:${values.logicalId}`,
    materialRole: 'element',
    ...values,
  }
}
