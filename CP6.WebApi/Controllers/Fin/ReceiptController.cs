using CP6.Core.Auth;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>
/// 收款 REST —— 财务章04。/api/fin/ar/receipt。收款/预收过账→撤销红冲；核销（多对多+尾差+汇差）。
/// 功能权限（D-2）：变更端点贴 [RequirePermission("fin-ar-receipt", …)]；信用查询同键 check（CreditControlController）。
/// </summary>
[ApiController]
[Route("api/fin/ar/receipt")]
[Authorize]
public class ReceiptController : ControllerBase
{
    private readonly IReceiptService _svc;
    private readonly IArSettlementService _settle;

    public ReceiptController(IReceiptService svc, IArSettlementService settle)
    {
        _svc = svc;
        _settle = settle;
    }

    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? customerId, [FromQuery] ReceiptStatus? status)
        => Ok2(await _svc.ListAsync(customerId, status));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var p = await _svc.GetAsync(id);
        return p == null ? BadRequest(new { code = 400, message = "E-FIN-311" }) : Ok2(p);
    }

    /// <summary>收款过账（IsAdvance=true 走预收账款）。返回新 Id。</summary>
    [HttpPost]
    [RequirePermission("fin-ar-receipt", "add")]
    public async Task<IActionResult> Receive([FromBody] Receipt receipt)
    {
        var r = await _svc.ReceiveAsync(receipt, CurrentUser);
        return r.Ok ? Ok2(new { id = receipt.Id, no = receipt.No }) : Fin(r);
    }

    /// <summary>撤销收款（解核销→红冲）。</summary>
    [HttpPost("{id}/reverse")]
    [RequirePermission("fin-ar-receipt", "reverse")]
    public async Task<IActionResult> Reverse(Guid id, [FromBody] ReasonReq r)
        => Fin(await _svc.ReverseReceiptAsync(id, CurrentUser, r.Reason));

    /// <summary>核销：把本收款应用到若干发票（可带销售折扣）。</summary>
    [HttpPost("{id}/settle")]
    [RequirePermission("fin-ar-receipt", "settle")]
    public async Task<IActionResult> Settle(Guid id, [FromBody] SettleReq req)
        => Fin(await _settle.SettleAsync(id, req.Applies, CurrentUser));

    public record ReasonReq(string Reason);
    public record SettleReq(List<SettlementApply> Applies);
}
