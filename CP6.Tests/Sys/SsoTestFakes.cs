using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;

namespace CP6.Tests.Sys;

/// <summary>
/// 可注入的 <see cref="ITenantSsoConfigService"/> 假实现（AuthController 测试用）。
/// 默认零配置（GetByTenantId 返 null）→ 不触发强制 SSO 拦截。
/// </summary>
internal sealed class FakeTenantSsoConfigService : ITenantSsoConfigService
{
    public Sys_TenantSsoConfig? ByTenantId { get; set; }

    public Task<Sys_TenantSsoConfig?> GetByTenantIdAsync(Guid tenantId) => Task.FromResult(ByTenantId);
    public Task<Sys_TenantSsoConfig?> GetByTenantCodeAsync(string tenantCode) => Task.FromResult(ByTenantId);
    public Task UpsertAsync(Guid tenantId, SsoConfigInput input) => Task.CompletedTask;
    public string DecryptClientSecret(Sys_TenantSsoConfig cfg) => "";
}

/// <summary>
/// 可注入的 <see cref="ISsoService"/> 假实现。通过委托桩 authorize/callback 行为；
/// 默认 authorize 返固定 URL、callback 抛 NotImplemented。
/// </summary>
internal sealed class FakeSsoService : ISsoService
{
    public Func<string, string, string?, Task<string>>? OnAuthorize { get; set; }
    public Func<string, string, string, Task<Sys_User>>? OnCallback { get; set; }

    public Task<string> BuildAuthorizeUrlAsync(string tenantCode, string redirectUri, string? returnUrl)
        => OnAuthorize?.Invoke(tenantCode, redirectUri, returnUrl) ?? Task.FromResult("https://idp.example/authorize?x=1");

    public Task<Sys_User> HandleCallbackAsync(string code, string state, string redirectUri)
        => OnCallback?.Invoke(code, state, redirectUri) ?? throw new NotImplementedException();
}
