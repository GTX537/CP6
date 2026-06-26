// #4 字段级审计 T7：字段审计相关类型

/** 操作类型（与后端 EntityState/Operation 对齐：1 新增 / 2 修改 / 3 删除）*/
export enum Operation {
  Added = 1,    // 新增
  Modified = 2, // 修改
  Deleted = 3,  // 删除
}

/** 字段审计列表行（GET /api/sys/field-audit 返回 rows 元素，仅含 changeCount，不含完整 changes）*/
export interface FieldAuditListItem {
  id: string
  entityName: string
  entityKey: string
  operation: Operation
  changeCount: number
  userId: string
  userName: string
  changedAt: string
}

/** 时间线行（GET /api/sys/field-audit/record 返回 rows 元素，含完整 changes 原始 JSON 串）*/
export interface FieldAuditTimelineItem {
  id: string
  operation: Operation
  changes: string // JSON 串 [{Field,Old,New}]（后端 System.Text.Json PascalCase），客户端 JSON.parse 后解析
  userId: string
  userName: string
  changedAt: string
}

/** 解析后的单条字段变更（已归一化为小写 field/old/new）*/
export interface FieldChange {
  field: string
  old: string | null
  new: string | null
}

/** 后端 changes JSON 串解析出的原始形状（PascalCase keys）*/
export interface RawFieldChange {
  Field: string
  Old: string | null
  New: string | null
}
