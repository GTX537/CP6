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
  changeCount: number | null
  evidenceStatus: 'READABLE' | 'UNREADABLE'
  userId: string
  userName: string
  changedAt: string
}

/** 服务端当前 Owner 生成的安全投影；不包含原 Changes JSON 或操作者 PII。 */
export interface FieldAuditTimelineItem {
  id: string
  operation: Operation
  changes: FieldChange[]
  evidenceStatus: 'READABLE' | 'RESTRICTED' | 'UNREADABLE'
  changedAt: string
}

/** 解析后的单条字段变更（已归一化为小写 field/old/new）*/
export interface FieldChange {
  field: string
  old: string | null
  new: string | null
}

export interface FieldAuditTimelinePage {
  rows: FieldAuditTimelineItem[]
  nextCursor: string | null
  projectionVersion: string
}
