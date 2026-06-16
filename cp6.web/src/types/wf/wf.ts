// OA(Wf) 阶段1 前端类型（对应后端 CP6.Core/Services/Wf 与 Wf_ 实体，camelCase）

/** 表单字段定义（schema.fields 元素） */
export interface FormFieldDef {
  name: string
  label?: string
  /** input/textarea/number/select/radio/checkbox/date/datetime/user/dept/upload */
  type: string
  required?: boolean
  maxLength?: number
  pattern?: string
  placeholder?: string
  /** select/radio/checkbox 选项 */
  options?: { label: string; value: string | number }[]
}

/** 规则动作类型（章06 §4）：显隐/必填/禁用(前端)/联动选项(前端)/计算回写 */
export type RuleAction =
  | 'show' | 'hide'
  | 'require' | 'optional'
  | 'disable' | 'enable'
  | 'setOptions'
  | 'compute'

/** 规则动作（命中 when 后依序应用） */
export interface RuleEffect {
  action: RuleAction
  /** 目标字段名 */
  target: string
  /** compute 的计算表达式（仅 compute 用，ExpressionEvaluator 语义） */
  expr?: string
  /** setOptions 的选项（仅 setOptions 用） */
  options?: { label: string; value: string | number }[]
}

/** 表单规则（章06 §4）：when 表达式为真时应用 then 动作。与后端共用同一 schema + 求值器语义 */
export interface FormRule {
  /** 触发条件表达式（空=恒成立） */
  when: string
  then: RuleEffect[]
}

/** 表单 schema */
export interface FormSchema {
  fields: FormFieldDef[]
  /** 显隐/计算/联动/必填规则（章06，可选） */
  rules?: FormRule[]
}

/** 字段权限级别 */
export type FieldPerm = 'edit' | 'readonly' | 'hidden'
/** 字段权限掩码：字段名 → 权限级 */
export type FieldMask = Record<string, FieldPerm>

/** 流程节点（schema.nodes 元素，前端仅取所需） */
export interface FlowNodeDef {
  id: string
  name?: string
  type: string
  countersign?: string
  fieldPerms?: Record<string, FieldPerm>
}

/** 待办项（/wf/my-todos） */
export interface TodoItem {
  taskId: string
  instanceId: string
  flowKey: string
  nodeId: string
  starterId: string
  createDate: string
}

/** 我的申请项（/wf/my-applications） */
export interface MyApplicationItem {
  instanceId: string
  flowKey: string
  currentNode: string
  status: number
  createDate: string
}

export interface FlowHistory {
  id: string
  instanceId: string
  nodeId: string
  actorId: string
  action: string
  comment?: string
  createDate: string
}

export interface FlowTask {
  id: string
  instanceId: string
  nodeId: string
  assigneeId: string
  status: number
  comment?: string
}

export interface FlowInstance {
  id: string
  flowKey: string
  currentNode: string
  status: number
  varsJson: string
  starterId: string
  createDate: string
}

export interface FlowInstanceDetail {
  instance: FlowInstance
  history: FlowHistory[]
  tasks: FlowTask[]
}

/** 实例状态文案（对应后端 FlowInstanceStatus） */
export const FLOW_INSTANCE_STATUS: Record<number, string> = {
  0: '进行中',
  1: '通过',
  2: '驳回',
  3: '撤回',
  4: '挂起',
}

/** 实例状态 → el-tag type */
export type ElTagType = 'primary' | 'success' | 'info' | 'warning' | 'danger'
export const FLOW_INSTANCE_STATUS_TAG: Record<number, ElTagType> = {
  0: 'primary',
  1: 'success',
  2: 'danger',
  3: 'info',
  4: 'warning',
}

/** 痕迹动作文案 */
export const FLOW_ACTION_TEXT: Record<string, string> = {
  submit: '提交',
  approve: '同意',
  reject: '驳回',
  withdraw: '撤回',
  suspend: '挂起待指派',
  end: '结束',
}
