using CP6.Core.Auth;
using CP6.Core.Services.Platform;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Platform;

/// <summary>
/// 平台超管授撤（多租户合规 #5 块②）。仅平台超管放行（<see cref="RequirePlatformAdminAttribute"/> 三道闸）。
/// 列出平台超管名册 / 授予 / 撤销（含防自锁死 E-SEC-037）。
/// 服务层经 <see cref="System.InvalidOperationException"/> 携 E-SEC 码 → 此处转 <see cref="BizException"/> 本地化。
/// </summary>
[ApiController]
[Route("api/platform/admin")]
[Authorize]
[RequirePlatformAdmin]
public class PlatformAdminController : ControllerBase
{
    private readonly IPlatformAdminService _svc;
    public PlatformAdminController(IPlatformAdminService svc) => _svc = svc;

    /// <summary>列出全部平台超管（跨租户）。</summary>
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _svc.ListAsync());

    /// <summary>授予平台超管。目标用户不存在 → E-SEC-032。</summary>
    [HttpPost("{userId:guid}/grant")]
    public async Task<IActionResult> Grant(Guid userId)
    {
        try
        {
            await _svc.GrantAsync(userId);
            return Ok(new { code = 0 });
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);   // E-SEC-032（用户不存在）
        }
    }

    /// <summary>撤销平台超管。不存在 → E-SEC-032；撤最后一个启用超管 → E-SEC-037（防自锁死）。</summary>
    [HttpPost("{userId:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid userId)
    {
        try
        {
            await _svc.RevokeAsync(userId);
            return Ok(new { code = 0 });
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);   // E-SEC-032 / E-SEC-037
        }
    }
}
