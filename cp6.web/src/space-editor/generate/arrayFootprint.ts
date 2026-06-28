// arrayFootprint — 整阵列外包尺寸（SP2 ④幽灵预览用），步长对齐 genZoneArray
// 纯逻辑：单架 W=cols*cellW / D=depthCount*cellD；行内含 rackGap、行间含 rowGap。

interface FootprintTpl {
  cols: number
  cellW: number
  depthCount: number
  cellD: number
}

interface FootprintParams {
  rows: number
  racksPerRow: number
  rowGap: number
  rackGap: number
}

/** 返回未旋转阵列的外包尺寸 {w,d}（mm），原点在阵列锚点角。 */
export function arrayFootprint(tpl: FootprintTpl, params: FootprintParams): { w: number; d: number } {
  const rackWidth = tpl.cols * tpl.cellW
  const rackDepth = tpl.depthCount * tpl.cellD
  const w = params.racksPerRow * rackWidth + (params.racksPerRow - 1) * params.rackGap
  const d = params.rows * rackDepth + (params.rows - 1) * params.rowGap
  return { w, d }
}
