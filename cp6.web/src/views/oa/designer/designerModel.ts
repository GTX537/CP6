import type { Node as VFNode, Edge as VFEdge } from '@vue-flow/core'

export interface SchemaNode {
  id: string; type: string; name?: string
  approverStrategy?: string; approverLevels?: number; approverRoleId?: number; approverUserId?: string
  countersign?: string; timeoutHours?: number; timeoutAction?: string; ccUsers?: string[]; ccRoleId?: number
  x?: number; y?: number
}
export interface SchemaEdge { from: string; to: string; condition?: string; ccUsers?: string[] }
export interface FlowSchemaDto { start?: string; nodes: SchemaNode[]; edges: SchemaEdge[] }

export const NODE_PALETTE = [
  { type: 'start',         label: '填單(发起)', color: '#67c23a' },
  { type: 'approval',      label: '審批',       color: '#409eff' },
  { type: 'parallelSplit', label: '并行分叉',   color: '#e6a23c' },
  { type: 'parallelJoin',  label: '并行汇聚',   color: '#e6a23c' },
  { type: 'end',           label: '結束',       color: '#909399' },
] as const

/** FlowSchema → Vue Flow 图（节点带 position + data 全字段；边 source/target + data 条件/CC）。 */
export function schemaToGraph(schema: FlowSchemaDto): { nodes: VFNode[]; edges: VFEdge[] } {
  const nodes: VFNode[] = (schema.nodes ?? []).map((n, i) => ({
    id: n.id,
    type: n.type || 'approval',
    position: { x: n.x ?? 80, y: n.y ?? i * 120 },        // 无坐标→竖排兜底
    data: { ...n },
    label: n.name || n.id,
  }))
  const edges: VFEdge[] = (schema.edges ?? []).map(e => ({
    id: `${e.from}__${e.to}`,
    source: e.from,
    target: e.to,
    data: { condition: e.condition, ccUsers: e.ccUsers },
    label: e.condition || undefined,
  }))
  return { nodes, edges }
}

/** Vue Flow 图 → FlowSchema（start = type==='start' 的节点；回写 x/y）。 */
export function graphToSchema(nodes: VFNode[], edges: VFEdge[]): FlowSchemaDto {
  const sn: SchemaNode[] = nodes.map(n => ({
    ...(n.data as SchemaNode),
    id: n.id,
    type: (n.data as SchemaNode)?.type || n.type || 'approval',
    x: n.position?.x,
    y: n.position?.y,
  }))
  const se: SchemaEdge[] = edges.map(e => ({
    from: e.source, to: e.target,
    condition: (e.data as any)?.condition || undefined,
    ccUsers: (e.data as any)?.ccUsers || undefined,
  }))
  const start = sn.find(n => n.type === 'start')?.id
  return { start, nodes: sn, edges: se }
}

/** 客户端基本校验（后端 FlowSchemaValidator 的轻量镜像；保存前预检）。返回错误文案 key 数组。 */
export function validateClient(schema: FlowSchemaDto): string[] {
  const errs: string[] = []
  const nodes = schema.nodes ?? [], edges = schema.edges ?? []
  const ids = new Set(nodes.map(n => n.id))
  if (nodes.filter(n => n.type === 'start').length !== 1) errs.push('oa.designer.errNoStart')
  if (!nodes.some(n => n.type === 'end')) errs.push('oa.designer.errNoEnd')
  if (edges.some(e => !ids.has(e.from) || !ids.has(e.to))) errs.push('oa.designer.errDanglingEdge')
  if (nodes.some(n => n.type === 'approval' && !n.approverStrategy)) errs.push('oa.designer.errNoStrategy')
  return errs
}
