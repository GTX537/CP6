import { describe, expect, it } from 'vitest'
import { dataSourceLabel, isUsableDataSource, type SpaceDataSource } from './dataSource'

const source = (kind: SpaceDataSource['kind']): SpaceDataSource => ({
  kind,
  dataSourceId: kind === 'Unavailable' ? 'UNCONFIGURED' : `TEST_${kind}`,
  observedAtUtc: '2026-07-25T00:00:00Z',
  isSimulated: kind === 'Simulated',
  isAvailable: kind !== 'Unavailable',
})

describe('Space data source contract', () => {
  it.each([
    ['Real', 'REAL', true],
    ['Simulated', 'SIMULATED', true],
    ['Unavailable', 'UNAVAILABLE', false],
  ] as const)('keeps %s label and availability consistent', (kind, label, usable) => {
    const value = source(kind)
    expect(dataSourceLabel(value)).toBe(label)
    expect(isUsableDataSource(value)).toBe(usable)
    expect(value.isSimulated).toBe(kind === 'Simulated')
  })
})
