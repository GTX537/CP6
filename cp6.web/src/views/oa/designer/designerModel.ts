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
  serviceHttpMethod?: 'GET' | 'POST' | 'PUT' | 'DELETE' // webApi：节点级 HTTP method 覆盖（镜像后端 FlowNode.ServiceHttpMethod）
  serviceTimeoutSec?: number                           // webApi：节点级单次调用超时秒覆盖（镜像后端 FlowNode.ServiceTimeoutSec）
  serviceDelayMode?: string                            // timer：固定/相对
  serviceDelayValue?: string                           // timer：延时值（镜像后端 string? ServiceDelayValue，承载 "3d"/"PT2H"/日期串）
  serviceMaxRetries?: number
  serviceRetryBackoffSec?: number
  // 内核 hardening：分支驳回策略（仅 parallelSplit/inclusiveSplit；镜像后端 FlowNode.OnBranchReject，camelCase 契约）
  onBranchReject?: 'cascade' | 'prune'
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
  // 包容网关两入口（色彩由组件层 `.dot-<type>` token 决定，不带 color 字段——对齐 serviceTask 先例）。
  { type: 'inclusiveSplit', label: '包容分叉' },
  { type: 'inclusiveJoin',  label: '包容汇聚' },
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
    // 票9：失败边（IsError）用 danger 虚线视觉区分。颜色走 Design System token（禁硬编码色）。
    ...(e.isError === true
      ? { style: { stroke: 'var(--cp-danger)', strokeWidth: 2, strokeDasharray: '6 4' }, class: 'edge-error', animated: false }
      : {}),
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

// ── 超时到点动作枚举（NodePropertyPanel 下拉数据源；导出以便单测/防漂移）──
// 值镜像后端 FlowNode.TimeoutAction；`errorEdge`＝「超时走失败边」（B-T1/E-WF-027）。
// 标签为 i18n key，t() 解析——`errorEdge` 用 `oa.designer.timeout.errorEdge`
// （键文本入库归 F-T1，落库前 t() 回退键名，属既定中间态）；余四项沿旧 `timeoutAction.*` 前缀。
export const TIMEOUT_ACTIONS: { value: string; label: string }[] = [
  { value: 'remind',    label: 'oa.designer.timeoutAction.remind' },
  { value: 'approve',   label: 'oa.designer.timeoutAction.approve' },
  { value: 'reject',    label: 'oa.designer.timeoutAction.reject' },
  { value: 'escalate',  label: 'oa.designer.timeoutAction.escalate' },
  { value: 'errorEdge', label: 'oa.designer.timeout.errorEdge' },
]

// ── 失败边（IsError）来源节点类型集合——前端等价常量，镜像后端
// `FlowSchemaValidator.ErrorEdgeSourceTypes = {serviceTask, approval, subFlow}`（OrdinalIgnoreCase）。
// B-T1 由「仅 serviceTask」放宽含 approval（本波超时走失败边）+ subFlow（三期 A 波子流程零改动）。
// 存小写形态，比较前 toLowerCase 归一——对齐后端 OrdinalIgnoreCase 姿态。
export const ERROR_EDGE_SOURCE_TYPES = new Set<string>(['servicetask', 'approval', 'subflow'])

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
          ? n.serviceDelayValue != null && n.serviceDelayMode != null  // 镜像后端：值与模式(radio 无默认)双字段必填
            // workdays 模式：值须正整数（镜像后端 ComputeTimerDueUtcAsync 降级立即，并入 E-WF-016 家族）
            && (n.serviceDelayMode !== 'workdays' || /^[1-9]\d*$/.test(String(n.serviceDelayValue).trim()))
          : false                                        // 缺/未知 serviceKind 即配置不完整
    if (!ok) errs.push('oa.designer.errServiceConfig')
  }
  // E-WF-028 镜像（节点级 HTTP 覆盖值域，E-T1）：serviceHttpMethod ∈ {GET,POST,PUT,DELETE}；
  //   serviceTimeoutSec 有值须正整数且 <=3600。租约下界（>=lease）属后端保存侧服务层，前端无租约常量故不镜像。
  const HTTP_METHODS = new Set(['GET', 'POST', 'PUT', 'DELETE'])
  for (const n of nodes) {
    if (n.type !== 'serviceTask') continue
    const m = n.serviceHttpMethod
    const t = n.serviceTimeoutSec
    const methodBad = m != null && !HTTP_METHODS.has(String(m).trim().toUpperCase())
    const timeoutBad = t != null && (!Number.isInteger(t) || t <= 0 || t > 3600)
    if (methodBad || timeoutBad) { errs.push('oa.designer.errHttpOverride'); break }
  }
  // ── inclusive 网关镜像（后端 E-WF-020/021，kernel hardening）──
  const nodeType = (id: string) => nodes.find(n => n.id === id)?.type
  const isJoinType = (ty?: string) => ty === 'parallelJoin' || ty === 'inclusiveJoin'
  const bfsDepths = (from: string): Map<string, number> => {
    const depth = new Map<string, number>([[from, 0]])
    const q = [from]
    while (q.length) {
      const cur = q.shift()!
      for (const e of edges.filter(e => e.from === cur))
        if (!depth.has(e.to)) { depth.set(e.to, depth.get(cur)! + 1); q.push(e.to) }
    }
    return depth
  }
  const nearestCommonJoin = (splitId: string): string | undefined => {
    const outs = edges.filter(e => e.from === splitId && !e.isError)
    if (!outs.length) return undefined
    const sets = outs.map(e => new Set(bfsDepths(e.to).keys()))
    const common = [...sets[0]!].filter(id => sets.every(s => s.has(id)))
    const depths = bfsDepths(splitId)
    let best: string | undefined
    let bestD = Infinity
    for (const id of common) {
      const d = depths.get(id) ?? Infinity
      if (isJoinType(nodeType(id)) && d < bestD) { best = id; bestD = d }
    }
    return best
  }
  // E-WF-020 镜像：inclusiveSplit 出边 ≥2 且恰好一条无条件 default 边
  for (const n of nodes) {
    if (n.type !== 'inclusiveSplit') continue
    const outs = edges.filter(e => e.from === n.id && !e.isError)
    const dflt = outs.filter(e => !e.condition?.trim())
    if (outs.length < 2 || dflt.length !== 1) { errs.push('oa.designer.errInclusiveDefault'); break }
  }
  // E-WF-021a/b 镜像：split 最近公共汇聚须为 inclusiveJoin；inclusiveJoin 入边≥2 且被配对
  const pairedJoins = new Set<string>()
  let pairBad = false
  for (const n of nodes) {
    if (n.type !== 'inclusiveSplit') continue
    const j = nearestCommonJoin(n.id)
    if (!j || nodeType(j) !== 'inclusiveJoin') { pairBad = true; continue }
    pairedJoins.add(j)
  }
  for (const n of nodes) {
    if (n.type !== 'inclusiveJoin') continue
    if (edges.filter(e => e.to === n.id).length < 2 || !pairedJoins.has(n.id)) pairBad = true
  }
  if (pairBad) errs.push('oa.designer.errInclusivePair')
  // ── 失败边镜像（后端 FlowSchemaValidator，B-T1）──
  // E-WF-017 镜像：IsError 边来源类型须 ∈ ErrorEdgeSourceTypes；任一节点 IsError 出边不得 >1。
  let errEdgeSrcBad = false
  for (const n of nodes) {
    const errOuts = edges.filter(e => e.from === n.id && e.isError === true)
    if (!errOuts.length) continue
    if (errOuts.length > 1) errEdgeSrcBad = true
    if (!ERROR_EDGE_SOURCE_TYPES.has((n.type ?? '').toLowerCase())) errEdgeSrcBad = true
  }
  if (errEdgeSrcBad) errs.push('oa.designer.errErrorEdgeSource')
  // E-WF-027 镜像：TimeoutAction=errorEdge 的节点必须有 IsError 出边（否则超时无处可去）。
  for (const n of nodes) {
    if (n.timeoutAction !== 'errorEdge') continue
    const hasErrOut = edges.some(e => e.from === n.id && e.isError === true)
    if (!hasErrOut) { errs.push('oa.designer.errTimeoutErrorEdge'); break }
  }
  // E-WF-021c 镜像：onBranchReject 值域 + 只许写在 split 型节点
  for (const n of nodes) {
    if (n.onBranchReject == null) continue
    const ok = (n.type === 'parallelSplit' || n.type === 'inclusiveSplit')
      && (n.onBranchReject === 'cascade' || n.onBranchReject === 'prune')
    if (!ok) { errs.push('oa.designer.errBranchReject'); break }
  }
  return errs
}
