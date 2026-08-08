using System.ComponentModel.DataAnnotations;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 系统用户实体
/// </summary>
public class Sys_User : BaseTenantEntity, IAuditable
{
    /// <summary>
    /// 用户名（登录账号）
    /// </summary>
    [MaxLength(100)]
    [Required]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 密码（存储加密后的值）
    /// </summary>
    [MaxLength(200)]
    [Required]
    [AuditIgnore]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 昵称
    /// </summary>
    [MaxLength(100)]
    [PiiField(Mode = PiiErase.Null)]
    public string? NickName { get; set; }

    /// <summary>
    /// 所属角色ID
    /// </summary>
    public int? RoleId { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enable { get; set; } = true;

    // ───── PUB 章00 组织模型：补三字段（数据权限 + 审批路由用）─────

    /// <summary>所属部门 → Sys_Dept.Id（PUB 数据权限"本部门"用）</summary>
    public Guid? DeptId { get; set; }

    /// <summary>直属上级 → Sys_User.Id（OA"直属上级"审批路由用）</summary>
    public Guid? ManagerId { get; set; }

    /// <summary>邮箱（通知用）</summary>
    [MaxLength(100)]
    [PiiField(Mode = PiiErase.Null)]
    public string? Email { get; set; }

    // ───── S 类认证加固：密码安全 + 登录画像 ─────
    /// <summary>最后改密时间（密码有效期判定起点）</summary>
    public DateTime? PasswordChangedAt { get; set; }
    /// <summary>连续登录失败计数</summary>
    public int FailedLoginCount { get; set; }
    /// <summary>最后一次失败时刻（ResetCounterMinutes 滑动重置用）</summary>
    public DateTime? LastFailedLoginAt { get; set; }
    /// <summary>锁定截止（null=未锁）</summary>
    public DateTime? LockedUntil { get; set; }
    /// <summary>最后成功登录时刻</summary>
    public DateTime? LastLoginTime { get; set; }
    /// <summary>最后登录 IP</summary>
    [MaxLength(64)]
    [PiiField(Mode = PiiErase.Null)]
    public string? LastLoginIp { get; set; }
    /// <summary>强制改密标志</summary>
    public bool MustChangePassword { get; set; }

    // ───── S 类 #2 2FA：TOTP 密钥 + 状态 ─────
    /// <summary>是否已启用 2FA（TOTP 绑定完成）</summary>
    public bool TwoFactorEnabled { get; set; }
    /// <summary>TOTP 密钥（base32）。MVP 明文存列（列加密见 spec §9）</summary>
    [MaxLength(128)] public string? TwoFactorSecret { get; set; }
    /// <summary>2FA 绑定时刻</summary>
    public DateTime? TwoFactorEnrolledAt { get; set; }

    // ───── S 类 #3 SSO：联邦身份链 + break-glass ─────
    /// <summary>联邦身份 subject（IdP 的 sub）；与 ExternalProvider 共同唯一定位。null=本地账号。</summary>
    [MaxLength(200)] public string? ExternalSubject { get; set; }
    /// <summary>联邦身份提供方（ID Token 的 iss）；防跨 IdP sub 串号。</summary>
    [MaxLength(300)] public string? ExternalProvider { get; set; }
    /// <summary>强制 SSO 下的密码登录例外（break-glass）。默认 false。</summary>
    public bool AllowPasswordFallback { get; set; }

    // ───── S 类 #5 多租户合规：平台超管带外标志位 ─────
    /// <summary>
    /// 平台超级管理员（R1）：绕 RBAC 的带外标志位，防租户管理员自提权。
    /// true=可访问 /api/platform/* 端点族（[RequirePlatformAdmin] 守卫，claim 快判 + DB 回查纵深防御）。默认 false。
    /// </summary>
    public bool IsPlatformAdmin { get; set; }

    /// <summary>Warehouse badge identifier used for shared-device handover.</summary>
    [MaxLength(64)] public string? BadgeNo { get; set; }

    /// <summary>BCrypt hash of the six-digit quick-switch PIN.</summary>
    [MaxLength(200), AuditIgnore] public string? QuickPinHash { get; set; }
}
