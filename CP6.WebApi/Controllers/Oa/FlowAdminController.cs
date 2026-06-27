using CP6.Core.Services.Oa;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>
/// 轻量流程管理 REST（Phase B）。/api/oa/flow-admin — 流程列表/启停（每表单1:1）。
/// 流程管理面向平台管理员，[Authorize] 登录态即可（P1 不加细粒度权限属性）。
/// </summary>
[ApiController]
[Route("api/oa/flow-admin")]
[Authorize]
public class FlowAdminController : LocalizedControllerBase
{
    private readonly IFlowAdminService _admin;

    public FlowAdminController(IFlowAdminService admin)
    {
        _admin = admin;
    }

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    // ── 列表 ──

    [HttpGet("list")]
    public async Task<IActionResult> List()
    {
        return Ok2(await _admin.ListFlowsAsync());
    }

    // ── 单条 ──

    [HttpGet("{flowKey}")]
    public async Task<IActionResult> Get(string flowKey)
    {
        var item = await _admin.GetFlowAsync(flowKey);
        return item is null
            ? NotFound(new { code = 404, message = "E-WF-006" })
            : Ok2(item);
    }

    // ── 启停 ──

    [HttpPost("enable")]
    public async Task<IActionResult> Enable([FromBody] EnableReq r)
    {
        try
        {
            await _admin.SetEnabledAsync(r.FlowKey, r.Enabled);
            return Ok2();
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    public record EnableReq(string FlowKey, bool Enabled);
}
