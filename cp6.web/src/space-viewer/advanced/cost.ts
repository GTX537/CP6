// cp6.web/src/space-viewer/advanced/cost.ts —— 通行成本常量与换算（时间=秒，SP5）
export const WALK_SPEED_MMPS = 1200 // 水平步行/叉车混合默认 1.2 m/s

/** 水平物理距离(mm) → 时间(秒)。 */
export const mmToSec = (mm: number): number => mm / WALK_SPEED_MMPS

/** 竖直边时间(秒)：等待(每停一次门周期) + 每层行程 × 跨层数。 */
export const verticalSec = (waitSec: number, perFloorSec: number, floorsSpanned: number): number =>
  waitSec + perFloorSec * Math.abs(floorsSpanned)

/** 编辑器预填用类型默认（后端 ConnectorService.DefaultCost 为持久化权威，此处镜像供 UX）。 */
export const TYPE_DEFAULT_COST: Record<number, { waitSec: number; travelSecPerFloor: number }> = {
  1: { waitSec: 20, travelSecPerFloor: 6 },
  2: { waitSec: 0, travelSecPerFloor: 15 },
  3: { waitSec: 0, travelSecPerFloor: 10 },
}
