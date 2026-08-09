export type FlowPreviewNodeKind =
  | 'start'
  | 'approval'
  | 'gateway'
  | 'finance'
  | 'compliance'
  | 'join'
  | 'service'
  | 'timer'
  | 'subflow'
  | 'end'
  | 'reject'

export interface FlowPreviewNode {
  id: string
  kind: FlowPreviewNodeKind
  code: string
  title: string
  subtitle: string
  assignee: string
  sla: string
  status: 'ready' | 'warning' | 'system'
  x: number
  y: number
}

export interface FlowPreviewEdge {
  id: string
  source: string
  target: string
  sourceHandle?: 'top' | 'right' | 'bottom' | 'left'
  targetHandle?: 'top' | 'right' | 'bottom' | 'left'
  label?: string
  tone?: 'default' | 'warning' | 'danger'
  dashed?: boolean
  condition?: string
  routeType?: 'normal' | 'condition' | 'exception'
  isDefault?: boolean
  priority?: number
}

export interface FlowPreviewEditableEdge {
  id: string
  source: string
  target: string
  sourceHandle: string | null
  targetHandle: string | null
  sourceName: string
  targetName: string
  label: string
  condition: string
  routeType: 'normal' | 'condition' | 'exception'
  isDefault: boolean
  priority: number
  addSignMode: string
  apiMethod: string
  apiPath: string
  apiTimeout: number
  apiRetry: number
  jobCode: string
  jobQueue: string
  jobParameters: string
}

export type FlowPreviewSelection =
  | { kind: 'node'; node: FlowPreviewNode }
  | { kind: 'edge'; edge: FlowPreviewEditableEdge }
  | { kind: 'none' }

export interface FlowPreviewDefinition {
  id: string
  code: string
  name: string
  category: string
  version: string
  status: 'published' | 'draft' | 'warning'
  updatedAt: string
}

export const FLOW_PREVIEW_NODES: FlowPreviewNode[] = [
  {
    id: 'start',
    kind: 'start',
    code: '01',
    title: '申请人填单',
    subtitle: '设备采购申请表',
    assignee: '申请人',
    sla: '即时',
    status: 'ready',
    x: 420,
    y: 20,
  },
  {
    id: 'manager',
    kind: 'approval',
    code: '02',
    title: '直属主管审核',
    subtitle: '确认采购必要性',
    assignee: '申请人直属主管',
    sla: '8 小时',
    status: 'ready',
    x: 420,
    y: 150,
  },
  {
    id: 'split',
    kind: 'gateway',
    code: '10',
    title: '金额条件分流',
    subtitle: '按采购金额并行会签',
    assignee: '系统判断',
    sla: '即时',
    status: 'warning',
    x: 420,
    y: 286,
  },
  {
    id: 'finance',
    kind: 'finance',
    code: '21',
    title: '财务预算审核',
    subtitle: '预算科目与余额确认',
    assignee: '财务 BP',
    sla: '1 个工作日',
    status: 'ready',
    x: 100,
    y: 430,
  },
  {
    id: 'department',
    kind: 'approval',
    code: '22',
    title: '部门负责人审批',
    subtitle: '部门采购额度确认',
    assignee: '部门负责人',
    sla: '8 小时',
    status: 'ready',
    x: 420,
    y: 430,
  },
  {
    id: 'compliance',
    kind: 'compliance',
    code: '23',
    title: '采购合规会签',
    subtitle: '供应商与询价规则检查',
    assignee: '采购合规组',
    sla: '1 个工作日',
    status: 'warning',
    x: 740,
    y: 430,
  },
  {
    id: 'join',
    kind: 'join',
    code: '30',
    title: '会签结果汇聚',
    subtitle: '全部通过后继续',
    assignee: '系统判断',
    sla: '即时',
    status: 'ready',
    x: 420,
    y: 584,
  },
  {
    id: 'service',
    kind: 'service',
    code: '31',
    title: '写入 ERP 请购单',
    subtitle: '调用 PUR-PR-Create',
    assignee: 'WebAPI 服务',
    sla: '30 秒',
    status: 'system',
    x: 420,
    y: 718,
  },
  {
    id: 'end',
    kind: 'end',
    code: '1000',
    title: '流程完成',
    subtitle: '通知申请人与采购员',
    assignee: '系统',
    sla: '即时',
    status: 'ready',
    x: 420,
    y: 852,
  },
  {
    id: 'reject',
    kind: 'reject',
    code: '1500',
    title: '申请取消',
    subtitle: '终止并退回申请人',
    assignee: '系统',
    sla: '即时',
    status: 'warning',
    x: 740,
    y: 150,
  },
]

export const FLOW_PREVIEW_EDGES: FlowPreviewEdge[] = [
  { id: 'start-manager', source: 'start', target: 'manager', sourceHandle: 'bottom', targetHandle: 'top', label: '提交申请', isDefault: true, priority: 1 },
  { id: 'manager-split', source: 'manager', target: 'split', sourceHandle: 'bottom', targetHandle: 'top', label: '审核通过', isDefault: true, priority: 2 },
  { id: 'manager-reject', source: 'manager', target: 'reject', sourceHandle: 'right', targetHandle: 'left', label: '取消申请', tone: 'danger', routeType: 'exception', priority: 1 },
  { id: 'split-finance', source: 'split', target: 'finance', sourceHandle: 'left', targetHandle: 'top', label: '金额 >= 5,000', condition: '${amount >= 5000}', routeType: 'condition', priority: 1 },
  { id: 'split-department', source: 'split', target: 'department', sourceHandle: 'bottom', targetHandle: 'top', label: '部门审批', isDefault: true, priority: 3 },
  { id: 'split-compliance', source: 'split', target: 'compliance', sourceHandle: 'right', targetHandle: 'top', label: '金额 >= 50,000', condition: '${amount >= 50000}', routeType: 'condition', tone: 'warning', priority: 2 },
  { id: 'finance-join', source: 'finance', target: 'join', sourceHandle: 'bottom', targetHandle: 'left', isDefault: true, priority: 1 },
  { id: 'department-join', source: 'department', target: 'join', sourceHandle: 'bottom', targetHandle: 'top', isDefault: true, priority: 1 },
  { id: 'compliance-join', source: 'compliance', target: 'join', sourceHandle: 'bottom', targetHandle: 'right', isDefault: true, priority: 1 },
  { id: 'join-service', source: 'join', target: 'service', sourceHandle: 'bottom', targetHandle: 'top', label: '全部通过', isDefault: true, priority: 1 },
  { id: 'service-end', source: 'service', target: 'end', sourceHandle: 'bottom', targetHandle: 'top', label: '写入成功', isDefault: true, priority: 2 },
  { id: 'service-manager', source: 'service', target: 'manager', sourceHandle: 'right', targetHandle: 'right', label: '接口失败重试', tone: 'warning', dashed: true, routeType: 'exception', priority: 1 },
]

export const FLOW_PREVIEW_DEFINITIONS: FlowPreviewDefinition[] = [
  { id: 'purchase', code: 'OA-PUR-014', name: '设备采购审批', category: '采购管理', version: 'v12', status: 'published', updatedAt: '10 分钟前' },
  { id: 'expense', code: 'OA-FIN-008', name: '费用报销审批', category: '财务管理', version: 'v8', status: 'draft', updatedAt: '32 分钟前' },
  { id: 'supplier', code: 'OA-PUR-021', name: '供应商准入审批', category: '采购管理', version: 'v6', status: 'published', updatedAt: '昨天' },
  { id: 'leave', code: 'OA-HR-003', name: '员工请假审批', category: '人事管理', version: 'v15', status: 'published', updatedAt: '2 天前' },
  { id: 'change', code: 'OA-ENG-011', name: '工程变更审批', category: '制造执行', version: 'v4', status: 'warning', updatedAt: '3 天前' },
  { id: 'contract', code: 'OA-LEG-005', name: '合同用印审批', category: '法务管理', version: 'v9', status: 'published', updatedAt: '5 天前' },
]

export function findPreviewNode(id: string): FlowPreviewNode {
  return FLOW_PREVIEW_NODES.find(node => node.id === id) ?? FLOW_PREVIEW_NODES[1]!
}
