using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 2FA 编排服务（S 类 #2 T4）：租户策略 + 启用矩阵 + 入会 + 验证 + 邮件 OTP + 重置。
/// 设计要点：①密钥边界(评审#7) BeginEnrollment 拒已启用 E-SEC-017；
/// 审计/日志不带 secret/uri ②邮件 OTP 绑 otpKey(=pendingJti 或 self:{uid})防跨会话 ③邮件不绕过入会(T6 端点守卫 E-SEC-014)。
/// </summary>
public interface ITwoFactorService
{
    /// <summary>读租户 2FA 策略：0=关闭 1=可选 2=强制。</summary>
    int ResolveTenantMode(Guid tenantId);

    /// <summary>需要 2FA 挑战 = 用户已启用 || 租户强制。</summary>
    bool IsChallengeRequired(Sys_User user, int tenantMode);

    /// <summary>必须入会 = 租户强制 && 用户未启用（首登拦截）。</summary>
    bool MustEnroll(Sys_User user, int tenantMode);

    /// <summary>开启入会：生成 base32 密钥写入 user.TwoFactorSecret（不置 Enabled），返 otpauth URI。已 Enabled 抛 E-SEC-017。</summary>
    string BeginEnrollment(Sys_User user);

    /// <summary>确认入会：验码通过则 user.TwoFactorEnabled=true、EnrolledAt=Now，写审计 Enrolled。</summary>
    Task<bool> ConfirmEnrollmentAsync(Sys_User user, string code);

    /// <summary>TOTP 验码（不消费状态，纯函数）。</summary>
    bool VerifyTotp(Sys_User user, string code);

    /// <summary>发送邮件 OTP（otpKey=pendingJti 或 self:{uid}）。无邮箱→E-SEC-015；命中冷却→E-SEC-016；发送失败→E-SEC-018。</summary>
    Task SendEmailOtpAsync(Sys_User user, string otpKey);

    /// <summary>校验邮件 OTP；真则消费（一次性）。</summary>
    Task<bool> VerifyEmailOtpAsync(string otpKey, string code);

    /// <summary>重置（admin reset / self-disable / break-glass）：清 Enabled/Secret/EnrolledAt，写审计 Reset。</summary>
    Task ResetAsync(Sys_User user, string? reason = null);
}
