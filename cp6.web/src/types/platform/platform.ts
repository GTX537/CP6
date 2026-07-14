// 多租户合规 #5（T9）平台区类型定义。字段对齐后端 CP6.Core/Services/Platform/* record + 控制器返回。
// 带外（out-of-band）：平台区不在 Sys_Menu RBAC 内，由 isPlatformAdmin + [RequirePlatformAdmin] 控制。

// ── 登录态菜单行（对齐 AuthController.BuildProfileAsync / impersonation MenuRow 投影）──
export interface MenuRow {
  id: number
  menuName: string
  routePath: string | null
  icon: string | null
  parentId: number | null
  orderNo: number
}

// ── 租户管理（块①）──
export interface TenantRow {
  id: string
  tenantCode: string
  tenantName: string
  enable: boolean
  expireDate: string | null
  userCount: number
  createDate: string
}

export interface TenantDetail {
  id: string
  tenantCode: string
  tenantName: string
  enable: boolean
  expireDate: string | null
  remark: string | null
  userCount: number
  timeZoneId: string | null
}

export interface CreateTenantResult {
  tenantId: string
  adminUserName: string
  tempPassword: string
}

// ── 平台超管授撤（块②）──
export interface PlatformAdminRow {
  id: string
  userName: string
  nickName: string | null
  tenantId: string
  enable: boolean
}

// ── impersonation（块② heart）──
export interface ImpersonationStartResult {
  tenantId: string
  tenantName: string
  userId: string
  userName: string
  menus: MenuRow[]
  expiresInMinutes: number
}

export interface ImpersonationEndResult {
  menus: MenuRow[]
}

// ── 跨租户审计（块④ R10）──
export interface AuditRow {
  id: string
  userId: string | null
  userName: string
  requestTenantCode: string | null
  eventType: number
  reason: string | null
  clientIp: string | null
  userAgent: string | null
  createdAt: string
}
