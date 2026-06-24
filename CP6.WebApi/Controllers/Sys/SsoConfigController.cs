using CP6.Core.Auth;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

/// <summary>
/// 租户 SSO/OIDC 配置管理（S 类 #3 T7）。每租户一行，按当前请求租户（<see cref="ITenantContext.CurrentTenantId"/>）作用域。
/// GET 仅返投影 + <c>hasClientSecret</c> 布尔（<b>绝不</b>返 ClientSecret 密文/明文，spec §3.1 密钥边界）；
/// PUT upsert（ClientSecret 留空 = 保留原密文，前端"已设置"占位）。
/// </summary>
[ApiController]
[Route("api/sys/sso-config")]
[Authorize]
public class SsoConfigController : ControllerBase
{
    private readonly ITenantSsoConfigService _svc;
    private readonly ITenantContext _tenant;
    private readonly ISecurityAuditService _audit;

    public SsoConfigController(ITenantSsoConfigService svc, ITenantContext tenant, ISecurityAuditService audit)
    {
        _svc = svc;
        _tenant = tenant;
        _audit = audit;
    }

    /// <summary>查当前租户 SSO 配置投影（无密文）。资源键 sys-sso-config，操作点 query。</summary>
    [HttpGet]
    [RequirePermission("sys-sso-config", "query")]
    public async Task<IActionResult> Get()
    {
        var cfg = await _svc.GetByTenantIdAsync(_tenant.CurrentTenantId);
        return Ok(new
        {
            authority = cfg?.Authority ?? "",
            clientId = cfg?.ClientId ?? "",
            scopes = cfg?.Scopes ?? "openid email profile",
            emailClaim = cfg?.EmailClaim ?? "email",
            enabled = cfg?.Enabled ?? false,
            enforced = cfg?.Enforced ?? false,
            autoProvision = cfg?.AutoProvision ?? true,
            defaultRoleId = cfg?.DefaultRoleId,
            hasClientSecret = !string.IsNullOrEmpty(cfg?.ClientSecretProtected),   // 仅暴露"是否已配密钥"，绝不返密文
        });
    }

    /// <summary>upsert 当前租户 SSO 配置（ClientSecret 空=保留原密文）+ 审计 SsoConfigChanged。操作点 edit。</summary>
    [HttpPut]
    [RequirePermission("sys-sso-config", "edit")]
    public async Task<IActionResult> Put([FromBody] SsoConfigInput input)
    {
        await _svc.UpsertAsync(_tenant.CurrentTenantId, input);
        await _audit.LogAsync(SecurityEventType.SsoConfigChanged, null, null, null,
            HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
        return Ok(new { code = 0 });
    }
}
