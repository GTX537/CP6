import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const productionConsumers = [
  './SpaceViewer.ts',
  './stacked/StackedViewer.ts',
  '../views/space/viewer/FloorViewer.vue',
  '../views/space/viewer/FloorList.vue',
  '../views/space/stacked/StackedViewer.vue',
  '../views/space/control-tower/SpaceControlTowerView.vue',
]

describe('production viewer Published-only boundary', () => {
  it.each(productionConsumers)('%s does not consume mutable legacy geometry', (relativePath) => {
    const source = readFileSync(
      fileURLToPath(new URL(relativePath, import.meta.url)),
      'utf8',
    )
    expect(source).not.toContain("@/api/space/scene")
    expect(source).not.toContain("@/api/space/floor")
    expect(source).not.toContain('/space/floor/')
  })
})
