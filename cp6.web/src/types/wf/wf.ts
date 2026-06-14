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

/** 表单 schema */
export interface FormSchema {
  fields: FormFieldDef[]
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
