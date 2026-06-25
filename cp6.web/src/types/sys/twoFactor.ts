// S 类 #2 双因素认证（2FA）T9：前端类型定义。

/** 租户 2FA 策略模式（与后端 Sys_Tenant.TwoFactorMode 对齐）。 */
export enum TwoFactorMode {
  /** 关闭：租户禁用 2FA。 */
  Off = 0,
  /** 可选：用户可自助启用/关闭。 */
  Optional = 1,
  /** 强制：登录必须 2FA，用户不可自行关闭。 */
  Required = 2,
}

/** POST /api/auth/2fa/setup(-self) 返回：otpauth URI（渲二维码）+ secret（手输备选）。 */
export interface TwoFactorSetupResult {
  otpauthUri: string
  secret: string
}

/** GET /api/auth/2fa/status 返回：当前用户 2FA 状态 + 租户模式 + 是否可自助关闭。 */
export interface TwoFactorStatus {
  enabled: boolean
  tenantMode: TwoFactorMode
  /** 已启用 && 租户非强制(mode!=2) 才允许自助关闭。 */
  canDisable: boolean
}

/** GET /api/sys/two-factor-policy 返回：当前租户策略模式。 */
export interface TwoFactorPolicy {
  mode: TwoFactorMode
}

/** 2FA 验证方法：验证器 App（TOTP）或邮件验证码（email）。 */
export type TwoFactorMethod = 'totp' | 'email'
