// OA 章09 §3 流程设计器图拓扑（纯函数，便于 vitest）。图与业务语义分离（OA4-D5）：
// 本模块只管 id 生成 / 加边去重 / 删点级联 / 可达性；节点业务字段由属性面板写。
import type { FlowDesignNode, FlowDesignEdge } from '@/types/wf/wf'

/** 生成不重复 id：start→start；end→end,end2…；approval→n1,n2… */
function uniqueId(prefix: string, used: Set<string>, numbered: boolean): string {
  if (!numbered && !used.has(prefix)) return prefix
  let i = numbered ? 1 : 2
  while (used.has(`${prefix}${i}`)) i++
  return `${prefix}${i}`
}

/** 造默认节点（approval 预置会签 all + 指定人策略）。 */
export function defaultNode(type: string, x: number, y: number, existing: ReadonlyArray<FlowDesignNode>): FlowDesignNode {
  const used = new Set(existing.map((n) => n.id))
  let id: string
  if (type === 'start') id = uniqueId('start', used, false)
  else if (type === 'end') id = uniqueId('end', used, false)
  else id = uniqueId('n', used, true)

  const node: FlowDesignNode = { id, name: id, type, x, y }
  if (type === 'approval') {
    node.countersign = 'all'
    node.approverStrategy = 'Specified'
  }
  return node
}

/** 加边（去自环、去重复）。返回是否新增。 */
export function addEdge(edges: FlowDesignEdge[], from: string, to: string): boolean {
  if (from === to) return false
  if (edges.some((e) => e.from === from && e.to === to)) return false
  edges.push({ from, to })
  return true
}

/** 删节点 + 级联删其相关边（原地）。 */
export function removeNodeCascade(nodes: FlowDesignNode[], edges: FlowDesignEdge[], id: string): void {
  const ni = nodes.findIndex((n) => n.id === id)
  if (ni >= 0) nodes.splice(ni, 1)
  for (let i = edges.length - 1; i >= 0; i--) {
    if (edges[i]!.from === id || edges[i]!.to === id) edges.splice(i, 1)
  }
}

/** 能到达某 end 节点的节点 id 集合（反向 BFS：从所有 end 沿入边回溯）。 */
export function reachableEndIds(nodes: ReadonlyArray<FlowDesignNode>, edges: ReadonlyArray<FlowDesignEdge>): Set<string> {
  const ends = nodes.filter((n) => n.type === 'end').map((n) => n.id)
  const incoming = new Map<string, string[]>() // to → [from...]
  for (const e of edges) {
    if (!incoming.has(e.to)) incoming.set(e.to, [])
    incoming.get(e.to)!.push(e.from)
  }
  const can = new Set<string>(ends)
  const queue = [...ends]
  while (queue.length) {
    const cur = queue.shift()!
    for (const from of incoming.get(cur) || []) {
      if (!can.has(from)) {
        can.add(from)
        queue.push(from)
      }
    }
  }
  return can
}
