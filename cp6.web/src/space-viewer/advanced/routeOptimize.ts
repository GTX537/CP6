// cp6.web/src/space-viewer/advanced/routeOptimize.ts
// 纯矩阵优化：给距离矩阵 → 出开放路径访问序。不依赖 PickPathPlanner 运行时（避免循环依赖）。

/** 按访问序计算开放路径总长（相邻项矩阵距离之和；无回程）。order 为下标排列。 */
export function routeLengthByOrder(matrix: number[][], order: number[]): number {
  let len = 0
  for (let i = 0; i + 1 < order.length; i++) len += matrix[order[i]!]![order[i + 1]!]!
  return len
}

/** 开放路径优化：起点固定 index 0，最近邻 seed + 2-opt 改进，返回访问序（order[0]===0）。 */
export function optimizeOrder(matrix: number[][]): number[] {
  const n = matrix.length
  if (n === 0) return []
  if (n === 1) return [0]
  // 最近邻 seed（从 0 出发）
  const visited = new Array<boolean>(n).fill(false)
  const order: number[] = [0]
  visited[0] = true
  for (let step = 1; step < n; step++) {
    const cur = order[order.length - 1]!
    let nextIdx = -1, best = Infinity
    for (let j = 0; j < n; j++) {
      if (visited[j]) continue
      if (matrix[cur]![j]! < best) { best = matrix[cur]![j]!; nextIdx = j }
    }
    order.push(nextIdx)
    visited[nextIdx] = true
  }
  // 2-opt：反转区间 [i,j]（i≥1 保持 order[0] 固定），若降低总长则采纳，直到无改进
  let improved = true
  let guard = 0
  while (improved && guard++ < 1000) {
    improved = false
    for (let i = 1; i < n - 1; i++) {
      for (let j = i + 1; j < n; j++) {
        const cand = order.slice(0, i).concat(order.slice(i, j + 1).reverse(), order.slice(j + 1))
        if (routeLengthByOrder(matrix, cand) + 1e-9 < routeLengthByOrder(matrix, order)) {
          for (let k = 0; k < n; k++) order[k] = cand[k]!
          improved = true
        }
      }
    }
  }
  return order
}
