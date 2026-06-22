// S 类认证加固 T9：安全日志相关类型

/** 安全事件类型（与后端 SecurityEventType 枚举对齐，i18n key = sec.event.{n}）*/
export enum SecurityEventType {
  LoginSuccess = 1,       // 登录成功
  LoginFailed = 2,        // 登录失败
  AccountLocked = 3,      // 账号锁定
  Logout = 4,             // 登出
  PasswordChanged = 5,    // 改密
  TokenRefreshed = 6,     // 刷新令牌
  TokenReuseDetected = 7, // 刷新令牌重用检测
  PermissionDenied = 8,   // 权限拒绝
}

/** 安全日志行（GET /api/sys/security-log 返回 rows 元素）*/
export interface SecurityLogRow {
  id: string
  userId: string
  userName: string
  requestTenantCode: string
  eventType: SecurityEventType
  reason: string
  clientIp: string
  userAgent: string
  createdAt: string
}
