export const FORMAL_SPACE_PERFORMANCE_BUDGETS = Object.freeze({
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

export function percentile(samples, percentileValue) {
  if (!Array.isArray(samples) || samples.length === 0) return null
  const sorted = [...samples].sort((left, right) => left - right)
  const index = Math.max(0, Math.ceil(sorted.length * percentileValue) - 1)
  return sorted[index] ?? null
}

export function summarizeSamples(samples) {
  return {
    sampleCount: samples.length,
    p50: percentile(samples, 0.5),
    p95: percentile(samples, 0.95),
    max: samples.length > 0 ? Math.max(...samples) : null,
  }
}

function rendererKey(run) {
  return `${run.webgl?.vendor ?? ''}|${run.webgl?.renderer ?? ''}|${run.webgl?.version ?? ''}`
}

function runFailed(run, budgets) {
  const result = run.result
  return !result
    || result.status !== 'PASS'
    || result.pickHitCount !== budgets.pickSamplesPerRun
    || result.pickSamplesMilliseconds?.length !== budgets.pickSamplesPerRun
    || result.stockApplySamplesMilliseconds?.length !== budgets.stockSamplesPerRun
    || !Array.isArray(result.frameIntervalsMilliseconds)
    || result.frameIntervalsMilliseconds.length === 0
    || run.softwareRenderer
    || !/WebGL\s*2/i.test(run.webgl?.version ?? '')
    || (run.consoleErrors?.length ?? 0) > 0
}

export function aggregateEvidence(
  runs,
  budgets = FORMAL_SPACE_PERFORMANCE_BUDGETS,
) {
  const results = runs.map((run) => run.result).filter(Boolean)
  const interactive = results.map((result) => result.interactiveMilliseconds)
  const frames = results.flatMap((result) => result.frameIntervalsMilliseconds ?? [])
  const labels = results.flatMap((result) => result.labelUpdateSamplesMilliseconds ?? [])
  const picks = results.flatMap((result) => result.pickSamplesMilliseconds ?? [])
  const stock = results.flatMap((result) => result.stockApplySamplesMilliseconds ?? [])
  const failureCount = runs.filter((run) => runFailed(run, budgets)).length
  const rendererKeys = new Set(runs.map(rendererKey))
  const metrics = {
    interactiveMilliseconds: summarizeSamples(interactive),
    frameMilliseconds: summarizeSamples(frames),
    labelUpdateMilliseconds: summarizeSamples(labels),
    pickMilliseconds: summarizeSamples(picks),
    stockApplyMilliseconds: summarizeSamples(stock),
    drawCalls: summarizeSamples(results.map((result) => result.drawCalls)),
    visibleLabels: summarizeSamples(results.map((result) => result.visibleLabels)),
  }
  const totalPickHits = results.reduce((total, result) => total + result.pickHitCount, 0)
  const expectedPickHits = runs.length * budgets.pickSamplesPerRun
  const checks = {
    minimumColdRuns: runs.length >= budgets.minimumColdRuns,
    allRunsProducedResults: results.length === runs.length,
    datasetShape: results.every((result) => (
      result.locations === budgets.locationCount
      && result.racks === budgets.rackCount
    )),
    drawCalls: (metrics.drawCalls.max ?? Number.POSITIVE_INFINITY) <= budgets.maxDrawCalls,
    interactive: (metrics.interactiveMilliseconds.p95 ?? Number.POSITIVE_INFINITY) <= budgets.maxInteractiveMilliseconds,
    frameTime: (metrics.frameMilliseconds.p95 ?? Number.POSITIVE_INFINITY) <= budgets.maxFrameP95Milliseconds,
    labels: (metrics.labelUpdateMilliseconds.p95 ?? Number.POSITIVE_INFINITY) <= budgets.maxLabelUpdateP95Milliseconds,
    pickLatency: (metrics.pickMilliseconds.p95 ?? Number.POSITIVE_INFINITY) <= budgets.maxPickP95Milliseconds,
    pickIntegrity: totalPickHits === expectedPickHits && picks.length === expectedPickHits,
    stockApply: (metrics.stockApplyMilliseconds.p95 ?? Number.POSITIVE_INFINITY) <= budgets.maxStockApplyP95Milliseconds,
    visibleLabels: (metrics.visibleLabels.max ?? Number.POSITIVE_INFINITY) <= budgets.maxVisibleLabels,
    hardwareRenderer: runs.every((run) => !run.softwareRenderer),
    webgl2: runs.every((run) => /WebGL\s*2/i.test(run.webgl?.version ?? '')),
    consistentRenderer: rendererKeys.size === 1,
    consoleClean: runs.every((run) => (run.consoleErrors?.length ?? 0) === 0),
    zeroFailedRuns: failureCount === 0,
  }
  const status = Object.values(checks).every(Boolean) ? 'PASS' : 'FAIL'
  return {
    status,
    runCount: runs.length,
    failureCount,
    failureRate: runs.length > 0 ? failureCount / runs.length : 1,
    totalPickHits,
    expectedPickHits,
    rendererKeys: [...rendererKeys],
    metrics,
    checks,
  }
}
