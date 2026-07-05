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
  // 服务任务（serviceTask）配置——镜像后端 FlowNode 的 Service* / IsError（后端 PascalCase，交换 JSON 用 camelCase）
  serviceKind?: 'dataWriteback' | 'webApi' | 'timer'
  serviceMode?: string                                 // sync | async
  serviceActionName?: string                           // dataWriteback：服务目录 action
  serviceConnectorName?: string                        // webApi：连接器
  servicePath?: string                                 // webApi：路径
  serviceParamsJson?: string
  serviceDelayMode?: string                            // timer：固定/相对
  serviceDelayValue?: string                           // timer：延时值（镜像后端 string? ServiceDelayValue，承载 "3d"/"PT2H"/日期串）
  serviceMaxRetries?: number
  serviceRetryBackoffSec?: number
  x?: number; y?: number
}
export interface SchemaEdge { from: string; to: string; condition?: string; ccUsers?: string[]; isError?: boolean }
export interface FlowSchemaDto { start?: string; nodes: SchemaNode[]; edges: SchemaEdge[] }

// node-identity swatches (categorical, chart-color family §2.5). Rendering uses the
// tokenized `.dot-<type>` CSS classes in DesignerCanvas; this `color` field is legacy metadata.
export const NODE_PALETTE = [
  { type: 'start',         label: '填單(发起)', color: '#67c23a' }, /* cp-chart-color */
  { type: 'approval',      label: '審批',       color: '#409eff' }, /* cp-chart-color */
  { type: 'parallelSplit', label: '并行分叉',   color: '#e6a23c' }, /* cp-chart-color */
  { type: 'parallelJoin',  label: '并行汇聚',   color: '#e6a23c' }, /* cp-chart-color */
  { type: 'end',           label: '結束',       color: '#909399' }, /* cp-chart-color */
  // 服务任务三入口（同 type='serviceTask'，以 kind 区分）。色彩由组件层 `.dot-<type>` token 决定，
  // 故不带 color 字段（OA 批次4 已裁定 color 为死字段；DesignerCanvas 只读 type/label）。
  { type: 'serviceTask',   kind: 'dataWriteback', label: '数据回写' },
  { type: 'serviceTask',   kind: 'webApi',        label: '接口调用' },
  { type: 'serviceTask',   kind: 'timer',         label: '定时器' },
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
    data: { condition: e.condition, ccUsers: e.ccUsers, isError: e.isError },
    label: e.condition || undefined,
  }))
  return { nodes, edges }
}

/**
 * Vue Flow 图 → FlowSchema（start = type==='start' 的节点；回写 x/y）。
 * 支持两种调用：`graphToSchema(nodes, edges)` 或 `graphToSchema(schemaToGraph(...))`。
 */
export function graphToSchema(graph: { nodes: VFNode[]; edges: VFEdge[] }): FlowSchemaDto
export function graphToSchema(nodes: VFNode[], edges: VFEdge[]): FlowSchemaDto
export function graphToSchema(
  a: VFNode[] | { nodes: VFNode[]; edges: VFEdge[] },
  b?: VFEdge[],
): FlowSchemaDto {
  const nodes: VFNode[] = Array.isArray(a) ? a : a.nodes
  const edges: VFEdge[] = Array.isArray(a) ? (b ?? []) : a.edges
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
    isError: (e.data as any)?.isError || undefined,
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
  // serviceTask 必填校验（镜像后端 E-WF-016）：webApi 缺 connector/path、dataWriteback 缺 action、timer 缺 delay。
  for (const n of nodes) {
    if (n.type !== 'serviceTask') continue
    const ok = n.serviceKind === 'webApi'
      ? !!n.serviceConnectorName && !!n.servicePath
      : n.serviceKind === 'dataWriteback'
        ? !!n.serviceActionName
        : n.serviceKind === 'timer'
          ? n.serviceDelayValue != null
          : false                                        // 缺/未知 serviceKind 即配置不完整
    if (!ok) errs.push('oa.designer.errServiceConfig')
  }
  return errs
}
