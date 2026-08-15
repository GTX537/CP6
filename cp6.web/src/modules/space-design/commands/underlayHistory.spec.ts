import { describe, expect, it } from 'vitest'
import { buildUnderlayHistoryEntry } from './underlayHistory'

describe('buildUnderlayHistoryEntry', () => {
  it('builds a public history entry from sealed server history', () => {
    expect(buildUnderlayHistoryEntry({
      schemaVersion: 1,
      originalCommandBatchId: 'batch-1',
      operationType: 'UnderlayCalibrate',
      historySha256: 'a'.repeat(64),
    })).toEqual({
      label: '标定底图',
      underlayCompensation: {
        originalCommandBatchId: 'batch-1',
        operationType: 'UnderlayCalibrate',
        historySha256: 'a'.repeat(64),
      },
    })
  })

  it('rejects unsupported or unsealed history', () => {
    expect(() => buildUnderlayHistoryEntry({
      schemaVersion: 1,
      originalCommandBatchId: 'batch-1',
      operationType: 'Unknown',
      historySha256: 'bad',
    })).toThrow('sealed reversible history')
  })
})
