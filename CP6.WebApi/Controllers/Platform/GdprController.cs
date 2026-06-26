using CP6.Core.Auth;
using CP6.Core.Services.Platform;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Platform;

/// <summary>
/// GDPR 双粒度导出 / 被遗忘权擦除（多租户合规 #5 块③）。仅平台超管放行
/// （<see cref="RequirePlatformAdminAttribute"/> 三道闸）。
/// <para>导出返回 <c>application/json</c> 附件流；擦除为破坏性操作，须 <c>confirm=true</c> 二次确认。</para>
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

    /// <summary>导出单数据主体（剔密钥）。用户不存在 → E-SEC-032。</summary>
    [HttpGet("export/subject/{userId:guid}")]
    public async Task<IActionResult> ExportSubject(Guid userId)
    {
        try
        {
            var stream = await _svc.ExportSubjectAsync(userId);
            return File(stream, "application/json", $"subject-{userId}-{DateTime.Now:yyyyMMdd}.json");
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);   // E-SEC-032
        }
    }

    /// <summary>擦除单数据主体（匿名化）。confirm!=true → E-SEC-038；平台超管/最后超管 → E-SEC-036/037。</summary>
    [HttpDelete("erase/subject/{userId:guid}")]
    public async Task<IActionResult> EraseSubject(Guid userId, [FromQuery] bool confirm = false)
    {
        if (!confirm) throw new BizException("E-SEC-038");
        try
        {
            await _svc.EraseSubjectAsync(userId);
            return Ok(new { code = 0 });
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);   // E-SEC-032 / 036 / 037
        }
    }

    /// <summary>擦除整租户（mode=anonymize|purge，默认 anonymize）。confirm!=true → E-SEC-038；
    /// 平台租户 → E-SEC-036；非法 mode → E-SEC-038。</summary>
    [HttpDelete("erase/tenant/{tenantId:guid}")]
    public async Task<IActionResult> EraseTenant(Guid tenantId, [FromQuery] string? mode = "anonymize", [FromQuery] bool confirm = false)
    {
        if (!confirm) throw new BizException("E-SEC-038");
        try
        {
            await _svc.EraseTenantAsync(tenantId, mode ?? "anonymize");
            return Ok(new { code = 0 });
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);   // E-SEC-032 / 036 / 038
        }
        catch (NotSupportedException ex)
        {
            throw new BizException(ex.Message);   // purge on non-relational
        }
    }
}
