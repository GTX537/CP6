using CP6.Core.Auth;
using CP6.Core.Services.Pur;
using CP6.Entity.DomainModels.Pur;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Pur;

/// <summary>采购订单 REST —— 采购章02。/api/pur/po。建单带出 + 派生状态机 + 送审/取消。</summary>
[ApiController]
[Route("api/pur/po")]
[Authorize]
public class PurchaseOrderController : ControllerBase
{
    private readonly IPurchaseOrderService _svc;
    public PurchaseOrderController(IPurchaseOrderService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? supplierId, [FromQuery] PoStatus? status)
        => Ok2(await _svc.ListAsync(supplierId, status));

    [HttpGet("{poNo}")]
    public async Task<IActionResult> Get(string poNo)
    {
        var po = await _svc.GetAsync(poNo);
        return po == null ? Err(new InvalidOperationException("E-PUR-027")) : Ok2(po);
    }

    [HttpPost]
    [RequirePermission("pur-po", "add")]
    public async Task<IActionResult> Create([FromBody] PoCreateDto dto)
    {
        try { return Ok2(await _svc.CreateAsync(dto, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>送审（草稿 → 审批 → 确认）。</summary>
    [HttpPost("{poNo}/submit")]
    [RequirePermission("pur-po", "submit")]
    public async Task<IActionResult> Submit(string poNo)
    {
        try { return Ok2(await _svc.SubmitForApprovalAsync(poNo, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>取消（仅草稿/确认且未收货可取消）。</summary>
    [HttpPost("{poNo}/cancel")]
    [RequirePermission("pur-po", "cancel")]
    public async Task<IActionResult> Cancel(string poNo)
    {
        try { await _svc.CancelAsync(poNo, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
