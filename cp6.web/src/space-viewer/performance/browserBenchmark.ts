import { Group, PerspectiveCamera, Scene, Vector3 } from 'three'
import { CSS2DRenderer } from 'three/examples/jsm/renderers/CSS2DRenderer'
import { Renderer } from '../core/Renderer'
import { SceneRoot } from '../core/SceneRoot'
import { SceneBuilder } from '../build/SceneBuilder'
import { LabelVirtualizer } from '../labels/LabelVirtualizer'
import { Picker } from '../navigate/Picker'
import { SPACE_PERFORMANCE_BUDGETS as budgets, percentile95 } from './budgets'
import { createStandardWarehouseScene } from './standardWarehouse'

declare global {
  interface Window {
    __SPACE_PERFORMANCE_RESULT__?: BrowserPerformanceResult
  }
}

export interface BrowserPerformanceResult {
  status: 'PASS' | 'FAIL'
  datasetVersion: string
  racks: number
  locations: number
  drawCalls: number
  interactiveMilliseconds: number
  frameP95Milliseconds: number
  framesPerSecondP95: number
  labelUpdateP95Milliseconds: number
  pickP95Milliseconds: number
  pickHitCount: number
  stockApplyP95Milliseconds: number
  visibleLabels: number
  frameIntervalsMilliseconds: number[]
  labelUpdateSamplesMilliseconds: number[]
  pickSamplesMilliseconds: number[]
  stockApplySamplesMilliseconds: number[]
}

function sample(count: number, action: () => void): number[] {
  const values: number[] = []
  for (let index = 0; index < count; index++) {
    const started = performance.now()
    action()
    values.push(performance.now() - started)
  }
  return values
}

function nextFrame(): Promise<number> {
  return new Promise((resolve) => requestAnimationFrame(resolve))
}

async function measureFrames(
  count: number,
  render: (index: number) => void,
): Promise<number[]> {
  const intervals: number[] = []
  let previous = await nextFrame()
  for (let index = 0; index < count; index++) {
    const now = await nextFrame()
    render(index)
    if (index >= 30) intervals.push(now - previous)
    previous = now
  }
  return intervals
}

function renderMetrics(result: BrowserPerformanceResult): void {
  const status = document.querySelector<HTMLElement>('#status')!
  status.dataset.status = result.status.toLowerCase()
  status.className = result.status === 'PASS' ? 'pass' : 'fail'
  status.textContent = result.status === 'PASS'
    ? 'All locked browser budgets passed.'
    : 'One or more locked browser budgets failed.'
  const rows: Array<[string, string]> = [
    ['Locations', result.locations.toLocaleString()],
    ['Racks', result.racks.toLocaleString()],
    ['WebGL draw calls', `${result.drawCalls} / ≤${budgets.maxDrawCalls}`],
    ['Interactive', `${result.interactiveMilliseconds.toFixed(1)}ms / ≤${budgets.maxInteractiveMilliseconds}ms`],
    ['Frame time P95', `${result.frameP95Milliseconds.toFixed(2)}ms / ≤${budgets.maxFrameP95Milliseconds}ms`],
    ['FPS (P95 frame)', `${result.framesPerSecondP95.toFixed(1)} / ≥${budgets.minFramesPerSecond}`],
    ['Label update P95', `${result.labelUpdateP95Milliseconds.toFixed(2)}ms / ≤${budgets.maxLabelUpdateP95Milliseconds}ms`],
    ['Pick P95', `${result.pickP95Milliseconds.toFixed(2)}ms / ≤${budgets.maxPickP95Milliseconds}ms`],
    ['Pick hits', `${result.pickHitCount} / ${budgets.pickSampleCount}`],
    ['Stock apply P95', `${result.stockApplyP95Milliseconds.toFixed(2)}ms / ≤${budgets.maxStockApplyP95Milliseconds}ms`],
    ['Visible labels', `${result.visibleLabels} / ≤${budgets.maxVisibleLabels}`],
  ]
  document.querySelector('#metrics')!.innerHTML = rows
    .map(([label, value]) => `<tr><th>${label}</th><td>${value}</td></tr>`)
    .join('')
}

async function run(): Promise<void> {
  const started = performance.now()
  const canvas = document.querySelector<HTMLCanvasElement>('#space-canvas')!
  const stage = document.querySelector<HTMLElement>('#stage')!
  const renderer = new Renderer(canvas)
  const scene = new Scene()
  const root = new SceneRoot()
  scene.add(root)
  const standardScene = createStandardWarehouseScene()
  const build = new SceneBuilder().build(standardScene)
  for (const object of build.objects) root.add(object)

  const labelGroup = new Group()
  scene.add(labelGroup)
  const labels = new LabelVirtualizer(budgets.maxVisibleLabels)
  for (const label of labels.pool.objects) labelGroup.add(label)
  const labelRenderer = new CSS2DRenderer()
  labelRenderer.setSize(canvas.clientWidth, canvas.clientHeight)
  labelRenderer.domElement.style.cssText = 'position:absolute;inset:0;pointer-events:none'
  stage.appendChild(labelRenderer.domElement)

  const camera = new PerspectiveCamera(
    45,
    canvas.clientWidth / Math.max(1, canvas.clientHeight),
    0.1,
    2_000,
  )
  const target = root.dataToWorld({ x: 110_000, y: 42_000, z: 1_200 })
  camera.position.copy(target).add(new Vector3(120, 110, 160))
  camera.lookAt(target)
  camera.updateProjectionMatrix()
  camera.updateMatrixWorld(true)
  scene.updateMatrixWorld(true)

  labels.update(camera, build.buckets, 'near', build.locationCodes, renderer.gl)
  renderer.gl.render(scene, camera)
  labelRenderer.render(scene, camera)
  const interactiveMilliseconds = performance.now() - started
  const drawCalls = renderer.gl.info.render.calls

  const labelSamples = sample(20, () => {
    labels.update(camera, build.buckets, 'near', build.locationCodes, renderer.gl)
  })
  const picker = new Picker()
  const pickLocation = standardScene.locations.reduce((closest, candidate) => {
    const closestDistance = Math.hypot(closest.absX - 110_000, closest.absY - 42_000)
    const candidateDistance = Math.hypot(candidate.absX - 110_000, candidate.absY - 42_000)
    return candidateDistance < closestDistance ? candidate : closest
  })
  const pickNdc = root.dataToWorld({
    x: pickLocation.absX,
    y: pickLocation.absY,
    z: pickLocation.absZ,
  }).project(camera)
  let pickHitCount = 0
  const pickSamples = sample(budgets.pickSampleCount, () => {
    if (picker.pick(pickNdc.x, pickNdc.y, camera, build.buckets, root)) pickHitCount++
  })
  const colors = standardScene.locations.map((location, index) => ({
    locationId: location.id,
    hex: index % 2 === 0 ? 0x2da44e : 0xd29922,
  }))
  const stockSamples = sample(budgets.stockSampleCount, () => {
    build.buckets.setColors(colors)
    renderer.gl.render(scene, camera)
  })

  const frameIntervals = await measureFrames(180, (index) => {
    const angle = index * 0.006
    camera.position.set(
      target.x + Math.cos(angle) * 160,
      target.y + 110,
      target.z + Math.sin(angle) * 160,
    )
    camera.lookAt(target)
    camera.updateMatrixWorld(true)
    if (index % 6 === 0) {
      labels.update(camera, build.buckets, 'near', build.locationCodes, renderer.gl)
    }
    renderer.gl.render(scene, camera)
    labelRenderer.render(scene, camera)
  })
  const frameP95Milliseconds = percentile95(frameIntervals)
  const framesPerSecondP95 = frameP95Milliseconds > 0 ? 1_000 / frameP95Milliseconds : 0

  const result: BrowserPerformanceResult = {
    status: 'PASS',
    datasetVersion: standardScene.source.dataSourceId,
    racks: standardScene.racks.length,
    locations: build.locationCodes.size,
    drawCalls,
    interactiveMilliseconds,
    frameP95Milliseconds,
    framesPerSecondP95,
    labelUpdateP95Milliseconds: percentile95(labelSamples),
    pickP95Milliseconds: percentile95(pickSamples),
    pickHitCount,
    stockApplyP95Milliseconds: percentile95(stockSamples),
    visibleLabels: labels.pool.activeCount,
    frameIntervalsMilliseconds: frameIntervals,
    labelUpdateSamplesMilliseconds: labelSamples,
    pickSamplesMilliseconds: pickSamples,
    stockApplySamplesMilliseconds: stockSamples,
  }
  result.status = (
    result.locations === budgets.locationCount &&
    result.racks === budgets.rackCount &&
    result.drawCalls <= budgets.maxDrawCalls &&
    result.interactiveMilliseconds <= budgets.maxInteractiveMilliseconds &&
    result.frameP95Milliseconds <= budgets.maxFrameP95Milliseconds &&
    result.labelUpdateP95Milliseconds <= budgets.maxLabelUpdateP95Milliseconds &&
    result.pickP95Milliseconds <= budgets.maxPickP95Milliseconds &&
    result.pickHitCount === budgets.pickSampleCount &&
    result.stockApplyP95Milliseconds <= budgets.maxStockApplyP95Milliseconds &&
    result.visibleLabels <= budgets.maxVisibleLabels
  ) ? 'PASS' : 'FAIL'
  window.__SPACE_PERFORMANCE_RESULT__ = result
  renderMetrics(result)
  console.info(`SPACE_E08_S05_BROWSER_METRICS=${JSON.stringify(result)}`)
}

void run().catch((error: unknown) => {
  const status = document.querySelector<HTMLElement>('#status')!
  status.dataset.status = 'error'
  status.className = 'fail'
  status.textContent = error instanceof Error ? error.message : String(error)
  console.error(error)
})
