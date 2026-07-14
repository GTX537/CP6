import { describe, it, expect } from 'vitest'
import { formToStatusText, instanceStatusType, mergeTimeline, groupByBranch, parseRowMode } from './inboxModel'
import type { TimelineRow, ForecastStep } from '@/types/oa/inbox'

describe('inboxModel', () => {
  it('formToStatusText maps known codes', () => {
    expect(formToStatusText(0)).toBe('oa.formto.pending')
    expect(formToStatusText(1)).toBe('oa.formto.approved')
    expect(formToStatusText(6)).toBe('oa.formto.voided')
  })

  it('instanceStatusType maps to el-tag types', () => {
    expect(instanceStatusType(0)).toBe('warning')   // Running
    expect(instanceStatusType(1)).toBe('success')   // Approved
    expect(instanceStatusType(2)).toBe('danger')    // Rejected
  })

  it('mergeTimeline appends forecast steps after persisted rows, flagged', () => {
    const rows: TimelineRow[] = [{ stepSeq: 1, nodeId: 'n1', expectedHandlerId: 'u1', expectedHandlerName: 'A',
      status: 1, sentAt: '2026-06-27T10:00:00', actualHandlerName: 'A' } as any]
    const forecast: ForecastStep[] = [{ nodeId: 'n2', type: 'approval', approvers: ['B'], resolved: true }]
    const merged = mergeTimeline(rows, forecast)
    expect(merged).toHaveLength(2)
    expect(merged[0]!.forecast).toBe(false)
    expect(merged[1]!.forecast).toBe(true)
    expect(merged[1]!.nodeId).toBe('n2')
  })

  it('groupByBranch groups rows by tokenId', () => {
    const rows: any[] = [{ tokenId: 't1', nodeId: 'a' }, { tokenId: 't1', nodeId: 'b' }, { tokenId: 't2', nodeId: 'c' }]
    const groups = groupByBranch(rows)
    expect(groups.size).toBe(2)
    expect(groups.get('t1')).toHaveLength(2)
  })
})

describe('parseRowMode', () => {
  it('缺省/缺键/非法/畸形 → merged', () => {
    expect(parseRowMode(undefined)).toBe('merged')
    expect(parseRowMode('')).toBe('merged')
    expect(parseRowMode('{}')).toBe('merged')
    expect(parseRowMode('{"rowMode":"weird"}')).toBe('merged')
    expect(parseRowMode('NOT_JSON{{{')).toBe('merged')
  })
  it('expanded 显式识别', () => {
    expect(parseRowMode('{"rowMode":"expanded"}')).toBe('expanded')
    expect(parseRowMode('{"rowMode":"merged"}')).toBe('merged')
  })
})
