import type { Node as VFNode, Edge as VFEdge } from '@vue-flow/core'

export interface ApproverSpecDto {
  strategy?: string
  approverLevels?: number
  approverRoleId?: number
  approverUserId?: string
  fieldName?: string
  mapKey?: string
  when?: string
  filter?: string
}

export interface ApprovalStageDto {
  name?: string
  code?: string
  kind: 'fixed' | 'managerChain'
  approverStrategy?: string
  approverLevels?: number
  approverRoleId?: number
  approverUserId?: string
  countersign?: 'all' | 'any' | 'veto'
  maxLevels?: number
  approverFieldName?: string
  approverMapKey?: string
  approverWhen?: string
  approverFilter?: string
  approverMembers?: ApproverSpecDto[]
}

export interface SchemaNode {
  id: string; type: string; name?: string
  code?: string                                        // 状态编号（对应后端 FlowNode.Code）
  approverStrategy?: string; approverLevels?: number; approverRoleId?: number; approverUserId?: string
  countersign?: string; timeoutHours?: number; timeoutAction?: string
  allowReject?: boolean                                // 允许退回
  ccUsers?: string[]; ccRoleId?: number
  stages?: ApprovalStageDto[]                          // 串簽多档审批配置
  approverFieldName?: string
  approverMapKey?: string
  approverWhen?: string
  approverFilter?: string
  approverMembers?: ApproverSpecDto[]
  x?: number; y?: number
}
export interface SchemaEdge { from: string; to: string; condition?: string; ccUsers?: string[] }
export interface FlowSchemaDto { start?: string; nodes: SchemaNode[]; edges: SchemaEdge[] }

// node-identity swatches (categorical, chart-color family §2.5). Rendering uses the
// tokenized `.dot-<type>` CSS classes in DesignerCanvas; this `color` field is legacy metadata.
export const NODE_PALETTE = [
  { type: 'start',         label: '填單(发起)', color: '#67c23a' }, /* cp-chart-color */
  { type: 'approval',      label: '審批',       color: '#409eff' }, /* cp-chart-color */
  { type: 'parallelSplit', label: '并行分叉',   color: '#e6a23c' }, /* cp-chart-color */
  { type: 'parallelJoin',  label: '并行汇聚',   color: '#e6a23c' }, /* cp-chart-color */
  { type: 'end',           label: '結束',       color: '#909399' }, /* cp-chart-color */
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
  if (nodes.some(n => n.type === 'approval' && !n.approverStrategy && !(n.stages?.length))) errs.push('oa.designer.errNoStrategy')
  for (const n of nodes) {
    if (n.type === 'approval' && n.stages && n.stages.length) {
      for (const s of n.stages) {
        const ruleOk = s.kind === 'managerChain'
          ? (s.maxLevels ?? 0) >= 1
          : !!s.approverStrategy
        const csOk = !s.countersign || ['all', 'any', 'veto'].includes(s.countersign)
        if (!ruleOk || !csOk) { errs.push('oa.designer.errStageInvalid'); break }
      }
    }
  }
  for (const n of nodes) {
    if (n.type !== 'approval') continue
    if (n.approverStrategy === 'FormField' && !n.approverFieldName) errs.push('oa.designer.errApproverConfig')
    if (n.approverStrategy === 'DataMap' && (!n.approverMapKey || !n.approverFieldName)) errs.push('oa.designer.errApproverConfig')
    if (n.approverStrategy === 'Group' && !(n.approverMembers?.length)) errs.push('oa.designer.errApproverConfig')
  }
  return errs
}
