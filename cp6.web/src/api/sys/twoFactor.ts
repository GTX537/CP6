import http from '../http'
import type {
  TwoFactorSetupResult,
  TwoFactorStatus,
  TwoFactorPolicy,
  TwoFactorMethod,
  TwoFactorMode,
} from '@/types/sys/twoFactor'

// S 类 #2 双因素认证（2FA）T9：2FA 全端点封装。
// 登录阶段（匿名，pending cookie cp6_2fa + cp6_csrf 已由 /auth/login 写入）：setup/enroll/verify/emailOtp。
// 自助阶段（[Authorize]，登录后）：status/setupSelf/enrollSelf/disableSelf/emailOtpSelf。
// 租户策略：getPolicy/setPolicy。与 auth.ts 同 http 实例（withCredentials + CSRF 双提交）。
export const twoFactorApi = {
  // ── 登录阶段 ──────────────────────────────────────────────
  // 入会准备：开新密钥 → 返 otpauthUri + secret（前端渲二维码 + 手输备选）。已启用拒 E-SEC-017。
  setup() {
    return http.post('/auth/2fa/setup') as Promise<TwoFactorSetupResult>
  },
  // 入会确认：验码通过 → 置 Enabled + 完成登录（后端写 auth cookies）。
  enroll(data: { code: string }) {
    return http.post('/auth/2fa/enroll', data)
  },
  // 挑战验证：method=totp / email → 通过则完成登录。失败 E-SEC-011。
  verify(data: { code: string; method: TwoFactorMethod }) {
    return http.post('/auth/2fa/verify', data)
  },
  // 发送邮件 OTP（仅 verify 态；enroll 态调用直接拒 E-SEC-014）。
  emailOtp() {
    return http.post('/auth/2fa/email-otp')
  },

  // ── 自助阶段（登录后）─────────────────────────────────────
  // 状态：返 { enabled, tenantMode, canDisable }。
  status() {
    return http.get('/auth/2fa/status') as Promise<TwoFactorStatus>
  },
  // 自助入会准备：开新密钥 → 返 otpauthUri + secret。已启用拒 E-SEC-017。
  setupSelf() {
    return http.post('/auth/2fa/setup-self') as Promise<TwoFactorSetupResult>
  },
  // 自助入会确认：验码通过 → 置 Enabled。失败 E-SEC-011。
  enrollSelf(data: { code: string }) {
    return http.post('/auth/2fa/enroll-self', data)
  },
  // 自助关闭：验密码 → 租户强制拒 E-SEC-019 → 验码(email/totp) → 重置。
  disableSelf(data: { currentPassword: string; code: string; method: TwoFactorMethod }) {
    return http.post('/auth/2fa/disable-self', data)
  },
  // 自助发送邮件 OTP（用于 disable-self 的 email 分支）。
  emailOtpSelf() {
    return http.post('/auth/2fa/email-otp-self')
  },

  // ── 租户策略 ──────────────────────────────────────────────
  // 查当前租户 2FA 策略模式。
  getPolicy() {
    return http.get('/sys/two-factor-policy') as Promise<TwoFactorPolicy>
  },
  // 设当前租户 2FA 策略模式（Off/Optional/Required）。
  setPolicy(data: { mode: TwoFactorMode }) {
    return http.put('/sys/two-factor-policy', data)
  },
}
