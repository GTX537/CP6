using CP6.Core.Auth;
using CP6.Core.Services.Platform;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Platform;

/// <summary>
/// 完整 impersonation 端点（多租户合规 #5 块② heart）。路由 <c>api/platform/impersonation</c>。
/// <para><c>POST /start</c>：仅平台超管放行（<see cref="RequirePlatformAdminAttribute"/> 三道闸）。</para>
/// <para><c>POST /end</c>：仅 <c>[Authorize]</c>（imp 期间当前令牌无 <c>is_platform_admin</c> claim，
/// 故不能贴 <c>[RequirePlatformAdmin]</c>——服务层自查 <c>impersonator_id</c> claim + 真身 DB 回查 R9-c）。</para>
/// <para>服务层经 <see cref="System.InvalidOperationException"/> 携 E-SEC 码 → 此处转 <see cref="BizException"/> 本地化。</para>
/// </summary>
[ApiController]
[Route("api/platform/impersonation")]
public class ImpersonationController : ControllerBase
{
    private readonly IImpersonationService _svc;
    public ImpersonationController(IImpersonationService svc) => _svc = svc;

    /// <summary>踏入目标租户用户。目标租户不存在/停用 → E-SEC-032；无可用目标用户 → E-SEC-035。</summary>
    [HttpPost("start")]
    [Authorize]
    [RequirePlatformAdmin]
    public async Task<IActionResult> Start([FromBody] ImpersonationStartRequest req)
    {
        try
        {
            var r = await _svc.StartAsync(req.TenantId, req.UserId, req.Reason, User, Response);
            return Ok(r);
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);   // E-SEC-032 / E-SEC-035
        }
    }

    /// <summary>切出，恢复平台超管会话。非 imp 态 / 真身已撤销停用 → E-SEC-031。</summary>
    [HttpPost("end")]
    [Authorize]
    public async Task<IActionResult> End()
    {
        try
        {
            var r = await _svc.EndAsync(User, Response);
            return Ok(r);
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);   // E-SEC-031
        }
    }
}

/// <summary>踏入请求体（目标租户必填；目标用户可空=取该租户首个启用管理员；reason 可选审计原因）。</summary>
public record ImpersonationStartRequest(Guid TenantId, Guid? UserId, string? Reason);
