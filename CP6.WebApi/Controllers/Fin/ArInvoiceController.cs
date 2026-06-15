using CP6.Core.Auth;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>
/// 应收发票 REST —— 财务章04。/api/fin/ar/invoice。录入草稿→过账（收入确认+成本结转双凭证）；
/// 销售退货红字（接 CreditNote）；账龄/对账查询。
/// 功能权限（D-2）：变更端点贴 [RequirePermission("fin-ar-invoice", …)]；读端点仅 [Authorize]。
/// </summary>
[ApiController]
[Route("api/fin/ar/invoice")]
[Authorize]
public class ArInvoiceController : ControllerBase
{
    private readonly IArInvoiceService _svc;
    private readonly IArAgingService _aging;
    private readonly IArReconcileService _recon;

    public ArInvoiceController(IArInvoiceService svc, IArAgingService aging, IArReconcileService recon)
    {
        _svc = svc;
        _aging = aging;
        _recon = recon;
    }

    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? customerId, [FromQuery] ArInvoiceStatus? status)
        => Ok2(await _svc.ListAsync(customerId, status));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var e = await _svc.GetAsync(id);
        return e == null ? BadRequest(new { code = 400, message = "E-FIN-302" }) : Ok2(e);
    }

    /// <summary>录入草稿。返回新 Id。</summary>
    [HttpPost]
    [RequirePermission("fin-ar-invoice", "add")]
    public async Task<IActionResult> Create([FromBody] ArInvoice invoice)
    {
        var r = await _svc.CreateAsync(invoice, CurrentUser);
        return r.Ok ? Ok2(new { id = invoice.Id, no = invoice.No }) : Fin(r);
    }

    /// <summary>过账（生成收入+成本双凭证）。</summary>
    [HttpPost("{id}/post")]
    [RequirePermission("fin-ar-invoice", "post")]
    public async Task<IActionResult> Post(Guid id) => Fin(await _svc.PostAsync(id, CurrentUser));

    /// <summary>销售退货红字（接 CreditNote，幂等 CreditNoteId）。返回红字发票 Id/No。</summary>
    [HttpPost("credit-memo")]
    [RequirePermission("fin-ar-invoice", "credit-memo")]
    public async Task<IActionResult> CreditMemo([FromBody] FinCreditMemoRequest request)
    {
        var (r, id, no) = await _svc.CreateCreditMemoAsync(request, CurrentUser);
        return r.Ok ? Ok2(new { id, no }) : Fin(r);
    }

    /// <summary>红冲（作废发票，红冲双凭证）。</summary>
    [HttpPost("{id}/reverse")]
    [RequirePermission("fin-ar-invoice", "reverse")]
    public async Task<IActionResult> Reverse(Guid id, [FromBody] ReasonReq r)
        => Fin(await _svc.ReverseAsync(id, CurrentUser, r.Reason));

    /// <summary>账龄（按客户分桶，本位币）。</summary>
    [HttpGet("aging")]
    public async Task<IActionResult> Aging([FromQuery] DateTime? asOf, [FromQuery] string? customerId)
        => Ok2(await _aging.AgingAsync(asOf ?? DateTime.Today, customerId));

    /// <summary>子账↔GL 勾稽。</summary>
    [HttpGet("reconcile")]
    public async Task<IActionResult> Reconcile() => Ok2(await _recon.ReconcileArAsync());

    public record ReasonReq(string Reason);
}
