import { describe, expect, it } from 'vitest'
import { buildExcelCadApplyHistoryEntry } from './excelCadApplyHistory'

describe('buildExcelCadApplyHistoryEntry', () => {
  it('accepts only a succeeded v2 result with sealed server history', () => {
    const entry = buildExcelCadApplyHistoryEntry({
      matchJobId: 'match-1',
      applyJobId: 'apply-1',
      commandBatchId: 'batch-1',
      jobStatus: 'Succeeded',
      expectedContentRevision: 4,
      idempotentReplay: false,
      result: {
        schemaVersion: 2,
        historySha256: 'a'.repeat(64),
        historyCommandCount: 7,
      },
    } as never)

    expect(entry.label).toContain('7 项')
    expect(entry.excelCadCompensation).toEqual({
      matchJobId: 'match-1',
      applyJobId: 'apply-1',
      historySha256: 'a'.repeat(64),
      historyCommandCount: 7,
    })
  })

  it('rejects a legacy result without a sealed reversible history', () => {
    expect(() => buildExcelCadApplyHistoryEntry({
      matchJobId: 'match-1',
      applyJobId: 'apply-1',
      jobStatus: 'Succeeded',
      result: {
        schemaVersion: 1,
      },
    } as never)).toThrow('sealed reversible history')
  })
})
