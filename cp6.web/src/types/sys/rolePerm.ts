// PUB 章02 功能权限 前端类型

export interface MenuActionDto {
  actionCode: string
  actionName: string
  sort: number
}

export interface MenuActionFullDto {
  menuId: number
  actionCode: string
  actionName: string
  sort: number
}

export interface RoleActionItem {
  menuId: number
  actionCode: string
}

export interface RolePermDto {
  menuIds: number[]
  actions: RoleActionItem[]
}

// 章03 数据权限
export interface DataScopeResourceDto {
  key: string
  name: string
  supports: number[]
  default: number
}

export interface RoleDataScopeDto {
  resourceKey: string
  scopeType: number
  customDeptIds: string[]
}

// 章04 字段权限
export interface FieldDefDto {
  name: string
  label: string
}

export interface FieldResourceDto {
  key: string
  fields: FieldDefDto[]
}

export interface RoleFieldPermDto {
  resourceKey: string
  fieldName: string
  access: number
}
