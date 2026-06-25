using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

/// <summary>
/// 租户 2FA 策略管理（S 类 #2 T7）。按当前请求租户（<see cref="ITenantContext.CurrentTenantId"/>）作用域，
/// 读写 <c>Sys_Tenant.TwoFactorMode</c>（0=关闭 1=可选 2=强制）。GET 任意登录可读；PUT 复用 user:edit。
/// </summary>
[ApiController]
[Route("api/sys/two-factor-policy")]
[Authorize]
public class TwoFactorPolicyController : ControllerBase
{
    private readonly CP6Context _context;
    private readonly ITenantContext _tenant;

    public TwoFactorPolicyController(CP6Context context, ITenantContext tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    /// <summary>查当前租户 2FA 策略 { mode }。</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var mode = await _context.Sys_Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == _tenant.CurrentTenantId)
            .Select(t => t.TwoFactorMode)
            .FirstOrDefaultAsync();
        return Ok(new { mode });
    }

    /// <summary>设置当前租户 2FA 策略（mode ∈ {0,1,2}）。复用 user:edit 权限点。</summary>
    [HttpPut]
    [RequirePermission("user", "edit")]
    public async Task<IActionResult> Put([FromBody] TwoFactorPolicyRequest req)
    {
        if (req.Mode < 0 || req.Mode > 2)
            throw new BizException("E-SEC-012");   // 非法 2FA 策略值（0/1/2 之外）

        var tenant = await _context.Sys_Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == _tenant.CurrentTenantId);
        if (tenant == null) return NotFound();

        tenant.TwoFactorMode = req.Mode;
        await _context.SaveChangesAsync();
        return Ok(new { code = 0 });
    }
}

/// <summary>租户 2FA 策略请求（mode 0=关闭 1=可选 2=强制）。</summary>
public record TwoFactorPolicyRequest(int Mode);
