using System.ComponentModel.DataAnnotations;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 租户 SSO/OIDC 配置（S 类 #3）。每租户一行（TenantId 唯一索引）。
/// CP6 作 SP（Service Provider），消费外部 OIDC IdP（Auth Code + PKCE）。
/// ClientSecret 经 DataProtection 加密存于 <see cref="ClientSecretProtected"/>；
/// 明文不出 ITenantSsoConfigService 边界（spec §3.1）。
/// </summary>
public class Sys_TenantSsoConfig : BaseTenantEntity
{
    /// <summary>OIDC 颁发者基址（discovery 文档前缀），e.g. https://login.example.com</summary>
    [MaxLength(500)]
    [Required]
    public string Authority { get; set; } = string.Empty;

    /// <summary>OIDC Client Id（IdP 注册的 SP 标识）</summary>
    [MaxLength(200)]
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client Secret 密文（DataProtection 保护）。null=公共客户端或未配密钥。</summary>
    public string? ClientSecretProtected { get; set; }

    /// <summary>OIDC scopes，空格分隔。默认 "openid email profile"。</summary>
    [MaxLength(500)]
    public string Scopes { get; set; } = "openid email profile";

    /// <summary>邮箱 claim 名（默认 "email"，部分 IdP 用 "upn"/"preferred_username"）。</summary>
    [MaxLength(100)]
    public string EmailClaim { get; set; } = "email";

    /// <summary>SSO 总开关（false=该租户禁用 SSO 不影响密码登录）</summary>
    public bool Enabled { get; set; }

    /// <summary>强制 SSO：true 时密码 Login 被拦截（除非用户 AllowPasswordFallback=true，spec break-glass）</summary>
    public bool Enforced { get; set; }

    /// <summary>JIT 自动供给：首次 SSO 命中新邮箱时自动建 Sys_User 行。</summary>
    public bool AutoProvision { get; set; } = true;

    /// <summary>JIT 自动供给的默认 RoleId（必填当 AutoProvision=true，spec R5：
    /// 现仓只有 RoleId=1 管理者，无最小权限角色 → 须配置者显式定一个新建低权角色 Id 防误授 admin）</summary>
    public int? DefaultRoleId { get; set; }
}
