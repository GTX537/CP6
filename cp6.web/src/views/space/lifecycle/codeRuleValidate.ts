// 编码规则段——值域常量 + 字段启用联动 + 本地镜像校验（纯函数，便于单测）。
// 权威校验在后端 preview（E-3xx 全量）；此处三条为编辑期即时提示（黄条不阻断保存）。
import type { CodeSegmentDef } from '@/types/space/scene'

/** 固定源（单独成组；仅 fixedValue 有效）。 */
export const FIXED_SOURCE = 'fixed'

/** 码源组（除 fixed 外）——upper 仅对这些启用；site-code/floor-level 参与 E-303 组合判定。 */
export const CODE_SOURCES = ['site-code', 'floor-level', 'zone-code', 'aisle-code', 'rack-code'] as const

/** 序号源组——width/pad/start/step 仅对这些启用。 */
export const SEQ_SOURCES = ['zone-seq', 'aisle-seq', 'rack-seq', 'col', 'level', 'depth'] as const

/** 下拉全 12 值域（渲染顺序：固定 → 码源 → 序号源）。 */
export const ALL_SOURCES = [FIXED_SOURCE, ...CODE_SOURCES, ...SEQ_SOURCES] as const

/** 巷道段（optional 应为 true——E-305）。 */
export const AISLE_SOURCES = ['aisle-code', 'aisle-seq'] as const

export const isFixedSource = (s: string): boolean => s === FIXED_SOURCE
export const isCodeSource = (s: string): boolean => (CODE_SOURCES as readonly string[]).includes(s)
export const isSeqSource = (s: string): boolean => (SEQ_SOURCES as readonly string[]).includes(s)
export const isAisleSource = (s: string): boolean => (AISLE_SOURCES as readonly string[]).includes(s)

/** width / pad / start / step —— 仅序号源启用。 */
export const seqFieldsEnabled = (s: string): boolean => isSeqSource(s)
/** upper —— 仅码源启用（fixed 无效）。 */
export const upperEnabled = (s: string): boolean => isCodeSource(s)
/** fixedValue —— 仅 fixed 启用。 */
export const fixedValueEnabled = (s: string): boolean => isFixedSource(s)

/**
 * 本地镜像校验——返回错误码数组（空=无提示）。三条：
 *  - E-303 缺 Zone 区分段：无 zone-code/zone-seq，且无 site-code + floor-level 组合。
 *  - E-305 巷道段未 Optional：存在 aisle-code/aisle-seq 段但 optional=false。
 *  - E-306 缺库位粒度段：col / level / depth 全无。
 * 纯函数：无副作用、幂等；权威口径以后端 preview.precheck 为准。
 */
export function validateSegmentsLocal(segments: CodeSegmentDef[]): string[] {
  const errs: string[] = []
  const sources = (segments || []).map((s) => s.source)
  const has = (s: string) => sources.includes(s)

  // E-303：缺 Zone 区分段
  const hasZoneSeg = has('zone-code') || has('zone-seq')
  const hasSiteFloorCombo = has('site-code') && has('floor-level')
  if (!hasZoneSeg && !hasSiteFloorCombo) errs.push('E-303')

  // E-305：巷道段未 Optional
  const aisleNotOptional = (segments || []).some((s) => isAisleSource(s.source) && !s.optional)
  if (aisleNotOptional) errs.push('E-305')

  // E-306：缺库位粒度段
  if (!has('col') && !has('level') && !has('depth')) errs.push('E-306')

  return errs
}

/** 新段初始值（对齐后端默认）。 */
export function newSegment(): CodeSegmentDef {
  return {
    key: '', name: '', source: 'zone-code', width: 0, pad: '0',
    start: 1, step: 1, sep: '-', upper: false, fixedValue: '', optional: false,
  }
}
