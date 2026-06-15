using CP6.Core.Auth;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>
/// 应付发票 REST —— 财务章03。/api/fin/ap/invoice。录入草稿→过账（自动凭证）；账龄/对账查询。
/// 功能权限（D-2）：变更端点贴 [RequirePermission("fin-ap-invoice", …)]；读端点仅 [Authorize]（菜单级已控）。
/// </summary>
[ApiController]
[Route("api/fin/ap/invoice")]
[Authorize]
public class ApInvoiceController : ControllerBase
{
    private readonly IApInvoiceService _svc;
    private readonly IApAgingService _aging;
    private readonly IApReconcileService _recon;

    public ApInvoiceController(IApInvoiceService svc, IApAgingService aging, IApReconcileService recon)
    {
        _svc = svc;
        _aging = aging;
        _recon = recon;
    }

    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? supplierId, [FromQuery] ApInvoiceStatus? status)
        => Ok2(await _svc.ListAsync(supplierId, status));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var e = await _svc.GetAsync(id);
        return e == null ? BadRequest(new { code = 400, message = "E-FIN-202" }) : Ok2(e);
    }

    /// <summary>录入草稿（含红字 IsCreditMemo）。返回新 Id。</summary>
    [HttpPost]
    [RequirePermission("fin-ap-invoice", "add")]
    public async Task<IActionResult> Create([FromBody] ApInvoice invoice)
    {
        var r = await _svc.CreateAsync(invoice, CurrentUser);
        return r.Ok ? Ok2(new { id = invoice.Id, no = invoice.No }) : Fin(r);
    }

    /// <summary>过账（生成凭证）。</summary>
    [HttpPost("{id}/post")]
    [RequirePermission("fin-ap-invoice", "post")]
    public async Task<IActionResult> Post(Guid id) => Fin(await _svc.PostAsync(id, CurrentUser));

    /// <summary>账龄（按供应商分桶，本位币）。</summary>
    [HttpGet("aging")]
    public async Task<IActionResult> Aging([FromQuery] DateTime? asOf, [FromQuery] string? supplierId)
        => Ok2(await _aging.AgingAsync(asOf ?? DateTime.Today, supplierId));

    /// <summary>子账↔GL 勾稽。</summary>
    [HttpGet("reconcile")]
    public async Task<IActionResult> Reconcile() => Ok2(await _recon.ReconcileApAsync());
}
