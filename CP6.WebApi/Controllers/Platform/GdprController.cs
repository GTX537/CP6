using CP6.Core.Auth;
using CP6.Core.Services.Platform;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Platform;

/// <summary>
/// Legacy GDPR compatibility routes. Only platform administrators may enter
/// （<see cref="RequirePlatformAdminAttribute"/> 三道闸）。
/// <para>Direct subject export and both direct erasure routes are retired. Tenant export remains a legacy stream.</para>
/// <para>服务层经 <see cref="System.InvalidOperationException"/> 携 E-SEC 码 → 此处转 <see cref="BizException"/> 本地化。</para>
/// </summary>
[ApiController]
[Route("api/platform/gdpr")]
[Authorize]
[RequirePlatformAdmin]
public class GdprController : ControllerBase
{
    private readonly IGdprService _svc;
    public GdprController(IGdprService svc) => _svc = svc;

    /// <summary>导出整租户数据包（剔密钥）。租户不存在 → E-SEC-032。</summary>
    [HttpGet("export/tenant/{tenantId:guid}")]
    public async Task<IActionResult> ExportTenant(Guid tenantId)
    {
        try
        {
            var stream = await _svc.ExportTenantAsync(tenantId);
            return File(stream, "application/json", $"tenant-{tenantId}-{DateTime.Now:yyyyMMdd}.json");
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);   // E-SEC-032
        }
    }

    /// <summary>Retired direct subject export; purpose, recipient and current artifact download are required.</summary>
    [HttpGet("export/subject/{userId:guid}")]
    public IActionResult ExportSubject(Guid userId)
    {
        Response.Headers.CacheControl = "no-store";
        return StatusCode(410,new {code="PRIVACY_USE_DURABLE_EXPORT"});
    }

    /// <summary>Legacy confirmation flags cannot authorize a new destructive effect.</summary>
    [HttpDelete("erase/subject/{userId:guid}")]
    public IActionResult EraseSubject(Guid userId,[FromQuery] bool confirm=false)=>ApprovedPlanRequired();
    [HttpDelete("erase/tenant/{tenantId:guid}")]
    public IActionResult EraseTenant(Guid tenantId,[FromQuery] string? mode="anonymize",[FromQuery] bool confirm=false)=>ApprovedPlanRequired();
    private IActionResult ApprovedPlanRequired()
    {
        Response.Headers.CacheControl="no-store";
        return StatusCode(410,new{code="PRIVACY_APPROVED_PLAN_REQUIRED"});
    }
}
