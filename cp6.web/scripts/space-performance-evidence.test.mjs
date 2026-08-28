import assert from 'node:assert/strict'
import test from 'node:test'
import {
  FORMAL_SPACE_PERFORMANCE_BUDGETS as budgets,
  aggregateEvidence,
  percentile,
  rendererMatchesOptionalPattern,
  summarizeSamples,
} from './space-performance-evidence.mjs'

function makeRun(overrides = {}) {
  return {
    result: {
      status: 'PASS',
      datasetVersion: 'E08-S05-STANDARD',
      racks: 500,
      locations: 10_000,
      drawCalls: 36,
      interactiveMilliseconds: 250,
      frameP95Milliseconds: 10,
      framesPerSecondP95: 100,
      labelUpdateP95Milliseconds: 4,
      pickP95Milliseconds: 1,
      pickHitCount: 100,
      stockApplyP95Milliseconds: 5,
      visibleLabels: 35,
      frameIntervalsMilliseconds: Array(150).fill(10),
      labelUpdateSamplesMilliseconds: Array(20).fill(4),
      pickSamplesMilliseconds: Array(100).fill(1),
      stockApplySamplesMilliseconds: Array(30).fill(5),
      ...overrides.result,
    },
    webgl: {
      renderer: 'ANGLE (Intel, Intel(R) Iris(R) Xe Graphics, D3D11)',
      vendor: 'Google Inc. (Intel)',
      version: 'WebGL 2.0',
      ...overrides.webgl,
    },
    softwareRenderer: overrides.softwareRenderer ?? false,
    consoleErrors: overrides.consoleErrors ?? [],
  }
}

test('uses nearest-rank percentiles and reports P50/P95/max', () => {
  assert.equal(percentile([5, 1, 4, 2, 3], 0.95), 5)
  assert.deepEqual(summarizeSamples([5, 1, 4, 2, 3]), {
    sampleCount: 5,
    p50: 3,
    p95: 5,
    max: 5,
  })
})

test('treats a GPU brand pattern as an optional environment diagnostic', () => {
  const renderer = 'ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 Laptop GPU, D3D11)'
  assert.equal(rendererMatchesOptionalPattern(renderer, null), true)
  assert.equal(rendererMatchesOptionalPattern(renderer, /RTX\s*3060/i), true)
  assert.equal(rendererMatchesOptionalPattern(renderer, /Iris.*Xe/i), false)
})

test('passes only complete 30-run hardware evidence with 100 successful picks per run', () => {
  const aggregate = aggregateEvidence(Array.from({ length: 30 }, () => makeRun()))
  assert.equal(aggregate.status, 'PASS')
  assert.equal(aggregate.failureCount, 0)
  assert.equal(aggregate.totalPickHits, 3_000)
  assert.equal(aggregate.metrics.frameMilliseconds.sampleCount, 4_500)
  assert.ok(Object.values(aggregate.checks).every(Boolean))
})

test('fails closed for fewer than 30 cold runs', () => {
  const aggregate = aggregateEvidence(Array.from({ length: 29 }, () => makeRun()))
  assert.equal(aggregate.status, 'FAIL')
  assert.equal(aggregate.checks.minimumColdRuns, false)
})

test('fails closed for software rendering, console errors, misses, or a frame P95 regression', () => {
  const runs = Array.from({ length: 30 }, () => makeRun())
  runs[0] = makeRun({ softwareRenderer: true })
  runs[1] = makeRun({ consoleErrors: ['WebGL context lost'] })
  runs[2] = makeRun({
    result: {
      pickHitCount: 99,
      frameIntervalsMilliseconds: Array(150).fill(21),
    },
  })
  runs[3] = makeRun({
    result: {
      frameIntervalsMilliseconds: Array(150).fill(21),
    },
  })
  const aggregate = aggregateEvidence(runs)
  assert.equal(aggregate.status, 'FAIL')
  assert.equal(aggregate.checks.hardwareRenderer, false)
  assert.equal(aggregate.checks.consoleClean, false)
  assert.equal(aggregate.checks.pickIntegrity, false)
  assert.equal(aggregate.checks.frameTime, false)
  assert.equal(aggregate.failureCount, 3)
})

test('locks the formal GA sample counts and thresholds', () => {
  assert.deepEqual(budgets, {
    minimumColdRuns: 30,
    locationCount: 10_000,
    rackCount: 500,
    maxDrawCalls: 100,
    maxInteractiveMilliseconds: 3_000,
    maxFrameP95Milliseconds: 20,
    maxLabelUpdateP95Milliseconds: 16,
    maxPickP95Milliseconds: 150,
    maxStockApplyP95Milliseconds: 3_000,
    maxVisibleLabels: 200,
    pickSamplesPerRun: 100,
    stockSamplesPerRun: 30,
  })
})
