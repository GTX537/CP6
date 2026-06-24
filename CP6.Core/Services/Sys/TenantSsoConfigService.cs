using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 租户 SSO 配置服务（S 类 #3 T2）。DataProtection 加解密 ClientSecret，
/// 明文不出本服务边界。登录期无租户上下文 → 全部读用 IgnoreQueryFilters。
/// </summary>
public class TenantSsoConfigService : ITenantSsoConfigService
{
    private readonly CP6Context _db;
    private readonly IDataProtector _protector;

    public TenantSsoConfigService(CP6Context db, IDataProtectionProvider dp)
    {
        _db = db;
        // 固定 purpose 串：DataProtection 密钥环绑定该用途；与登出黑名单/2FA pending 等不复用
        _protector = dp.CreateProtector("CP6.Sso.ClientSecret");
    }

    public Task<Sys_TenantSsoConfig?> GetByTenantIdAsync(Guid tenantId) =>
        _db.Sys_TenantSsoConfigs.IgnoreQueryFilters()
           .FirstOrDefaultAsync(x => x.TenantId == tenantId);

    public async Task<Sys_TenantSsoConfig?> GetByTenantCodeAsync(string tenantCode)
    {
        var tenant = await _db.Sys_Tenants.IgnoreQueryFilters()
                                          .FirstOrDefaultAsync(t => t.TenantCode == tenantCode);
        if (tenant == null) return null;
        return await GetByTenantIdAsync(tenant.Id);
    }

    public async Task UpsertAsync(Guid tenantId, SsoConfigInput input)
    {
        var existing = await GetByTenantIdAsync(tenantId);
        if (existing == null)
        {
            existing = new Sys_TenantSsoConfig { TenantId = tenantId };
            _db.Sys_TenantSsoConfigs.Add(existing);
        }

        existing.Authority = input.Authority;
        existing.ClientId = input.ClientId;
        existing.Scopes = input.Scopes;
        existing.EmailClaim = input.EmailClaim;
        existing.Enabled = input.Enabled;
        existing.Enforced = input.Enforced;
        existing.AutoProvision = input.AutoProvision;
        existing.DefaultRoleId = input.DefaultRoleId;

        // ClientSecret 空/null=保留原密文（含初次新建时密文仍空，spec §3.1）；非空=Protect 后覆盖
        if (!string.IsNullOrEmpty(input.ClientSecret))
            existing.ClientSecretProtected = _protector.Protect(input.ClientSecret);

        await _db.SaveChangesAsync();
    }

    public string DecryptClientSecret(Sys_TenantSsoConfig cfg)
    {
        if (string.IsNullOrEmpty(cfg.ClientSecretProtected))
            throw new InvalidOperationException("E-SEC-028");
        return _protector.Unprotect(cfg.ClientSecretProtected);
    }
}
