export const SPACE_PERFORMANCE_BUDGETS = Object.freeze({
  locationCount: 10_000,
  standardZoneCount: 7,
  stressBucketCount: 50,
  rackCount: 500,
  aisleCount: 20,
  maxDrawCalls: 100,
  minFramesPerSecond: 50,
  maxInteractiveMilliseconds: 3_000,
  maxLabelUpdateP95Milliseconds: 16,
  maxPickP95Milliseconds: 150,
  maxStockApplyP95Milliseconds: 3_000,
  maxVisibleLabels: 200,
  runtimeQueryChunkSize: 500,
  runtimeQueryChunkCount: 20,
})

export function percentile95(samples: readonly number[]): number {
  if (samples.length === 0) return 0
  const sorted = [...samples].sort((left, right) => left - right)
  return sorted[Math.ceil(sorted.length * 0.95) - 1] ?? 0
}
