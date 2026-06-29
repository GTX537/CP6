// cp6.web/src/space-viewer/overlay/stockModel.ts
import type { WmsStockDto } from '@/types/space/overlay'

const STATUS_HEX: Record<number, number> = {
  0: 0x4caf50, // 空 绿
  1: 0x2196f3, // 有货 蓝
  2: 0xf44336, // 满 红
  3: 0x9e9e9e, // 锁定 灰
  4: 0xffc107, // 在拣 黄
}
export const NO_DATA_HEX = 0x455a64 // 无数据 中性灰（区别于锁定灰）

export function binStatusToHex(status: number): number {
  return STATUS_HEX[status] ?? NO_DATA_HEX
}

/** 库位利用率 [0,1]：有容量用 qty/capacity；无容量按 BinStatus 粗估（空0/有货0.5/满1，锁定/在拣按量近似）。 */
export function locationUtilization(d: WmsStockDto): number {
  if (d.capacity && d.capacity > 0) return Math.min(1, d.qty / d.capacity)
  if (d.binStatus === 2) return 1
  if (d.binStatus === 0) return 0
  return d.qty > 0 ? 0.5 : 0
}

/** 冷→暖渐变：0=蓝 0.5=黄 1=红（线性插值 RGB）。 */
export function utilizationToHex(u: number): number {
  const t = Math.max(0, Math.min(1, u))
  const lerp = (a: number, b: number, k: number) => Math.round(a + (b - a) * k)
  let r: number, g: number, b: number
  if (t < 0.5) { const k = t / 0.5; r = lerp(0x21, 0xff, k); g = lerp(0x96, 0xc1, k); b = lerp(0xf3, 0x07, k) }
  else { const k = (t - 0.5) / 0.5; r = lerp(0xff, 0xf4, k); g = lerp(0xc1, 0x43, k); b = lerp(0x07, 0x36, k) }
  return (r << 16) | (g << 8) | b
}

/** 货架/库区聚合利用率：Σqty / Σcapacity（仅含有容量库位；无则返 0）。 */
export function aggregateUtilization(items: WmsStockDto[]): number {
  let q = 0, c = 0
  for (const it of items) if (it.capacity && it.capacity > 0) { q += it.qty; c += it.capacity }
  return c > 0 ? q / c : 0
}
