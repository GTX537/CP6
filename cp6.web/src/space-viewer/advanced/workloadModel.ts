// cp6.web/src/space-viewer/advanced/workloadModel.ts
import type { WorkloadItem } from '@/types/space/advanced'

/** opCount → 归一化 t∈[0,1]（按最大值线性归一；max=0 → 全 0）。 */
export function normalizeOpCounts(items: WorkloadItem[]): Map<string, number> {
  const max = items.reduce((m, i) => Math.max(m, i.opCount), 0)
  const out = new Map<string, number>()
  for (const i of items) out.set(i.locationCode, max > 0 ? i.opCount / max : 0)
  return out
}

/** 作业热力色：复用 07 冷蓝→暖红渐变管线（DRY）。 */
export { utilizationToHex as workloadToHex } from '@/space-viewer/overlay/stockModel'
