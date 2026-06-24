using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 租户 SSO 配置服务（S 类 #3 T2）。每租户一行 Sys_TenantSsoConfig，
/// ClientSecret 经 DataProtection 加密存于 ClientSecretProtected 列；
/// 明文不出本服务边界（spec §3.1 密钥边界）。
/// </summary>
public interface ITenantSsoConfigService
{
    /// <summary>按 TenantId 取配置（IgnoreQueryFilters，登录期无租户上下文）。</summary>
    Task<Sys_TenantSsoConfig?> GetByTenantIdAsync(Guid tenantId);

    /// <summary>按 TenantCode 取配置（先按 code 取 Sys_Tenant 的 Id，再转 GetByTenantIdAsync）。</summary>
    Task<Sys_TenantSsoConfig?> GetByTenantCodeAsync(string tenantCode);

    /// <summary>
    /// Upsert：现有行→覆盖标量；ClientSecret 空/null=保留原密文；非空=Protect 后覆盖。
    /// </summary>
    Task UpsertAsync(Guid tenantId, SsoConfigInput input);

    /// <summary>
    /// 解密 ClientSecret（仅内部供 SsoService 调用换码用）。
    /// 实体 ClientSecretProtected 为空时抛 InvalidOperationException("E-SEC-028")。
    /// </summary>
    string DecryptClientSecret(Sys_TenantSsoConfig cfg);
}

/// <summary>SSO 配置写入入参（控制器→服务层）。ClientSecret 空=不改原值。</summary>
public record SsoConfigInput(
    string Authority,
    string ClientId,
    string? ClientSecret,
    string Scopes,
    string EmailClaim,
    bool Enabled,
    bool Enforced,
    bool AutoProvision,
    int? DefaultRoleId);
