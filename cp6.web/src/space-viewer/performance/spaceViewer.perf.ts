import { describe, expect, it } from 'vitest'
import { PerspectiveCamera } from 'three'
import type { WebGLRenderer } from 'three'
import { InstancedBuckets } from '../build/InstancedBuckets'
import { SceneBuilder } from '../build/SceneBuilder'
import { LabelVirtualizer } from '../labels/LabelVirtualizer'
import { Picker } from '../navigate/Picker'
import { SceneRoot } from '../core/SceneRoot'
import { SPACE_PERFORMANCE_BUDGETS as budgets, percentile95 } from './budgets'
import { createStandardWarehouseScene, createStressLocations } from './standardWarehouse'

function elapsedSamples(count: number, action: () => void): number[] {
  const samples: number[] = []
  for (let index = 0; index < count; index++) {
    const started = performance.now()
    action()
    samples.push(performance.now() - started)
  }
  return samples
}

function renderableCount(objects: readonly object[]): number {
  let count = 0
  for (const object of objects) {
    ;(object as { traverse: (visit: (child: { visible: boolean; type: string }) => void) => void })
      .traverse((child) => {
        if (child.visible && (child.type === 'Mesh' || child.type === 'LineSegments')) count++
      })
  }
  return count
}

describe.sequential('E08-S05 10,000-location performance gate', () => {
  it('meets the locked CPU-side interaction, label, draw-call and stock budgets', () => {
    const standardScene = createStandardWarehouseScene()
    let buildResult = new SceneBuilder().build(standardScene)
    buildResult.buckets.dispose()
    const buildSamples = elapsedSamples(5, () => {
      buildResult = new SceneBuilder().build(standardScene)
      buildResult.buckets.dispose()
    })
    buildResult = new SceneBuilder().build(standardScene)

    const stressBuckets = new InstancedBuckets()
    stressBuckets.build(createStressLocations())
    for (const mesh of stressBuckets.meshes) mesh.updateMatrixWorld(true)

    const camera = new PerspectiveCamera(45, 16 / 9, 1, 1_000_000)
    camera.position.set(100_000, -90_000, 90_000)
    camera.lookAt(100_000, 8_000, 600)
    camera.updateProjectionMatrix()
    camera.updateMatrixWorld(true)

    const locationCodes = new Map<string, string>()
    for (let index = 0; index < budgets.locationCount; index++) {
      locationCodes.set(`stress-location-${index + 1}`, `S-${index + 1}`)
    }
    const canvas = document.createElement('canvas')
    canvas.width = 1_440
    canvas.height = 900
    const renderer = { domElement: canvas } as WebGLRenderer
    const labels = new LabelVirtualizer(budgets.maxVisibleLabels)
    labels.update(camera, stressBuckets, 'near', locationCodes, renderer)
    const labelSamples = elapsedSamples(20, () => {
      labels.update(camera, stressBuckets, 'near', locationCodes, renderer)
    })

    const picker = new Picker()
    const root = new SceneRoot()
    root.updateMatrixWorld(true)
    picker.pick(0, 0, camera, stressBuckets, root)
    const pickSamples = elapsedSamples(30, () => {
      picker.pick(0, 0, camera, stressBuckets, root)
    })

    const colors = Array.from({ length: budgets.locationCount }, (_, index) => ({
      id: standardScene.locations[index]!.id,
      hex: index % 2 === 0 ? 0x2da44e : 0xd29922,
    }))
    const stockSamples = elapsedSamples(10, () => {
      buildResult.buckets.setColors(colors.map((color) => ({
        locationId: color.id,
        hex: color.hex,
      })))
    })

    const metrics = {
      locations: standardScene.locations.length,
      standardBuckets: buildResult.buckets.bucketCount(),
      stressBuckets: stressBuckets.bucketCount(),
      drawCalls: renderableCount(buildResult.objects),
      buildP95Milliseconds: percentile95(buildSamples),
      labelP95Milliseconds: percentile95(labelSamples),
      pickP95Milliseconds: percentile95(pickSamples),
      stockApplyP95Milliseconds: percentile95(stockSamples),
      visibleLabels: labels.pool.activeCount,
    }
    console.info(`SPACE_E08_S05_METRICS=${JSON.stringify(metrics)}`)

    expect(metrics.locations).toBe(budgets.locationCount)
    expect(metrics.standardBuckets).toBe(budgets.standardZoneCount)
    expect(metrics.stressBuckets).toBe(budgets.stressBucketCount)
    expect(metrics.drawCalls).toBeLessThanOrEqual(budgets.maxDrawCalls)
    expect(metrics.buildP95Milliseconds).toBeLessThanOrEqual(budgets.maxInteractiveMilliseconds)
    expect(metrics.labelP95Milliseconds).toBeLessThanOrEqual(budgets.maxLabelUpdateP95Milliseconds)
    expect(metrics.pickP95Milliseconds).toBeLessThanOrEqual(budgets.maxPickP95Milliseconds)
    expect(metrics.stockApplyP95Milliseconds).toBeLessThanOrEqual(budgets.maxStockApplyP95Milliseconds)
    expect(metrics.visibleLabels).toBeLessThanOrEqual(budgets.maxVisibleLabels)

    stressBuckets.dispose()
    buildResult.buckets.dispose()
  })
})
