import type { TimelineRow, ForecastStep } from '@/types/oa/inbox'

/** 关卡状态码 → i18n 键（FlowFormToStatus）。status 7 = SentBack。 */
export function formToStatusText(s: number): string {
  return ['oa.formto.pending', 'oa.formto.approved', 'oa.formto.rejected', 'oa.formto.transferred',
    'oa.formto.addsigned', 'oa.formto.skipped', 'oa.formto.voided', 'oa.timeline.sentBack'][s] ?? 'oa.formto.pending'
}

/** 实例状态码 → el-tag type（FlowInstanceStatus 0..4）。 */
export function instanceStatusType(s: number): string {
  return (['warning', 'success', 'danger', 'info', 'info'][s]) ?? 'info'
}

export function instanceStatusText(s: number): string {
  return ['oa.inst.running', 'oa.inst.approved', 'oa.inst.rejected', 'oa.inst.withdrawn', 'oa.inst.suspended', 'oa.inst.draft'][s] ?? 'oa.inst.running'
}

export interface MergedRow {
  forecast: boolean; nodeId: string; nodeName?: string; status?: number;
  expectedHandlerName?: string; actualHandlerName?: string; onBehalfOfName?: string;
  approvers?: string[]; resolved?: boolean; comment?: string; sentAt?: string; handledAt?: string; tokenId?: string;
  stageIndex?: number | null; stageRound?: number | null
}

/** 时间线 = 持久行（完成/当前）+ 预计段（灰）。预计行 forecast=true。 */
export function mergeTimeline(rows: TimelineRow[], forecast: ForecastStep[]): MergedRow[] {
  const persisted: MergedRow[] = rows.map(r => ({
    forecast: false, nodeId: r.nodeId, nodeName: r.nodeName, status: r.status,
    expectedHandlerName: r.expectedHandlerName, actualHandlerName: r.actualHandlerName ?? undefined,
    onBehalfOfName: r.onBehalfOfName ?? undefined, comment: r.comment ?? undefined,
    sentAt: r.sentAt, handledAt: r.handledAt ?? undefined, tokenId: r.tokenId,
    stageIndex: r.stageIndex ?? null, stageRound: r.stageRound ?? null,
  }))
  const future: MergedRow[] = forecast.map(f => ({
    forecast: true, nodeId: f.nodeId, nodeName: f.nodeName, approvers: f.approvers, resolved: f.resolved,
  }))
  return [...persisted, ...future]
}

/** 按 TokenId 分支分组（并行履历各成一串；无 token 归 '_'）。 */
export function groupByBranch<T extends { tokenId?: string }>(rows: T[]): Map<string, T[]> {
  const m = new Map<string, T[]>()
  for (const r of rows) {
    const k = r.tokenId ?? '_'
    ;(m.get(k) ?? m.set(k, []).get(k)!).push(r)
  }
  return m
}

/** rowMode 显示偏好解析（wfs-inbox-ux §5）：PrefsJson 顶层 rowMode 键；缺省/非法/畸形 → merged。 */
export function parseRowMode(prefsJson: string | undefined): 'merged' | 'expanded' {
  if (!prefsJson) return 'merged'
  try {
    const parsed = JSON.parse(prefsJson)
    return parsed?.rowMode === 'expanded' ? 'expanded' : 'merged'
  } catch {
    return 'merged'
  }
}
