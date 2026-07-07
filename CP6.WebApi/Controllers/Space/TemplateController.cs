using CP6.Core.Services.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

/// <summary>
/// 模板 Web API（ch01 §F-1）。
/// 路由 /api/space/template*；租户隔离由 TenantMiddleware + CP6Context 全局过滤自动施加。
/// </summary>
[ApiController]
[Route("api/space")]
[Authorize]
public class TemplateController : ControllerBase
{
    private readonly ITemplateService _svc;
    public TemplateController(ITemplateService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null, string msg = "OK") =>
        Ok(new { code = 0, message = msg, data });

    /// <summary>列出全部模板</summary>
    [HttpGet("template")]
    public async Task<IActionResult> List() => Ok2(await _svc.ListAsync());

    /// <summary>创建模板</summary>
    [HttpPost("template")]
    public async Task<IActionResult> Create([FromBody] TemplateDto d)
    {
        return Ok2(new { id = await _svc.CreateAsync(d, CurrentUser) });
    }

    /// <summary>更新模板</summary>
    [HttpPut("template/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] TemplateDto d)
    {
        await _svc.UpdateAsync(id, d, CurrentUser); return Ok2();
    }

    /// <summary>删除模板</summary>
    [HttpDelete("template/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _svc.DeleteAsync(id); return Ok2();
    }

    /// <summary>克隆模板（新 Id + 新编码，同 Params/Type）</summary>
    [HttpPost("template/{id:guid}/clone")]
    public async Task<IActionResult> Clone(Guid id)
    {
        return Ok2(new { id = await _svc.CloneAsync(id, CurrentUser) });
    }
}
