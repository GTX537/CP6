using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>
/// 付款 REST —— 财务章03。/api/fin/ap/payment。付款/预付过账→撤销红冲；核销（多对多+尾差+汇差）。
/// 功能权限点（fin.ap.payment:*）与 [RequirePermission] 在 D-2 统一贴附+seed。
/// </summary>
[ApiController]
[Route("api/fin/ap/payment")]
[Authorize]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _svc;
    private readonly IApSettlementService _settle;

    public PaymentController(IPaymentService svc, IApSettlementService settle)
    {
        _svc = svc;
        _settle = settle;
    }

    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? supplierId, [FromQuery] PaymentStatus? status)
        => Ok2(await _svc.ListAsync(supplierId, status));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var p = await _svc.GetAsync(id);
        return p == null ? BadRequest(new { code = 400, message = "E-FIN-211" }) : Ok2(p);
    }

    /// <summary>付款过账（IsPrepayment=true 走预付账款）。返回新 Id。</summary>
    [HttpPost]
    public async Task<IActionResult> Pay([FromBody] Payment payment)
    {
        var r = await _svc.PayAsync(payment, CurrentUser);
        return r.Ok ? Ok2(new { id = payment.Id, no = payment.No }) : Fin(r);
    }

    /// <summary>撤销付款（解核销→红冲）。</summary>
    [HttpPost("{id}/reverse")]
    public async Task<IActionResult> Reverse(Guid id, [FromBody] ReasonReq r)
        => Fin(await _svc.ReversePaymentAsync(id, CurrentUser, r.Reason));

    /// <summary>核销：把本付款应用到若干发票（可带现金折扣）。</summary>
    [HttpPost("{id}/settle")]
    public async Task<IActionResult> Settle(Guid id, [FromBody] SettleReq req)
        => Fin(await _settle.SettleAsync(id, req.Applies, CurrentUser));

    public record ReasonReq(string Reason);
    public record SettleReq(List<SettlementApply> Applies);
}
