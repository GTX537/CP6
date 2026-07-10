using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>
/// 出庫ルーティングルール管理（多倉庫引当 Gap 4.2 / T14）。
/// </summary>
[ApiController]
[Route("api/wms/outbound-routing")]
[Authorize]
public class OutboundRoutingController : ControllerBase
{
    private readonly IOutboundRoutingService _svc;

    public OutboundRoutingController(IOutboundRoutingService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;

    /// <summary>ルール一覧。</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeDisabled = true)
        => Ok(new { code = 0, message = "OK", data = await _svc.ListRulesAsync(includeDisabled) });

    /// <summary>引当候補倉庫のプレビュー（設定検証用）。</summary>
    [HttpGet("preview")]
    public async Task<IActionResult> Preview(
        [FromQuery] string productCd,
        [FromQuery] string? customerCd,
        [FromQuery] int outboundType = 1,
        [FromQuery] string fallbackWarehouseCd = "")
        => Ok(new
        {
            code = 0,
            message = "OK",
            data = await _svc.ResolveCandidateWarehousesAsync(customerCd, productCd ?? "", outboundType, fallbackWarehouseCd ?? ""),
        });

    /// <summary>ルール新規作成。</summary>
    [HttpPost]
    [RequirePermission("wms-outbound-routing", "add")]
    public async Task<IActionResult> Create([FromBody] OutboundRoutingRule rule)
    {
        try
        {
            var data = await _svc.CreateRuleAsync(rule, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-072", data });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>ルール更新。</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("wms-outbound-routing", "edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] OutboundRoutingRule rule)
    {
        try
        {
            await _svc.UpdateRuleAsync(id, rule, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-073" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>ルール削除（論理削除）。</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("wms-outbound-routing", "del")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _svc.DeleteRuleAsync(id, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-074" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }
}
