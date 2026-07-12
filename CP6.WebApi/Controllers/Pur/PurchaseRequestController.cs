using CP6.Core.Auth;
using CP6.Core.Services.Pur;
using CP6.Entity.DomainModels.Pur;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Pur;

/// <summary>采购申请 REST —— 采购章05 §4/§5。/api/pur/pr。手工建单 + 送审 + PR→PO 按建议供应商分组转单。</summary>
[ApiController]
[Route("api/pur/pr")]
[Authorize]
public class PurchaseRequestController : ControllerBase
{
    private readonly IPurchaseRequestService _svc;
    public PurchaseRequestController(IPurchaseRequestService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PrStatus? status, [FromQuery] string? source)
        => Ok2(await _svc.ListAsync(status, source));

    [HttpGet("{prNo}")]
    public async Task<IActionResult> Get(string prNo)
    {
        var pr = await _svc.GetAsync(prNo);
        return pr == null ? Err(new InvalidOperationException("E-PUR-056")) : Ok2(pr);
    }

    [HttpPost]
    [RequirePermission("pur-pr", "add")]
    public async Task<IActionResult> Create([FromBody] PrCreateDto dto)
    {
        try { return Ok2(await _svc.CreateAsync(dto, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>送审（草稿 → 审批 → 已批）。</summary>
    [HttpPost("{prNo}/submit")]
    [RequirePermission("pur-pr", "submit")]
    public async Task<IActionResult> Submit(string prNo)
    {
        try { return Ok2(await _svc.SubmitForApprovalAsync(prNo, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>转 PO（仅已批；按建议供应商分组拆单，回填 ConvertedPoNo）。</summary>
    [HttpPost("{prNo}/convert")]
    [RequirePermission("pur-pr", "convert")]
    public async Task<IActionResult> Convert(string prNo)
    {
        try { return Ok2(await _svc.ConvertToPoAsync(prNo, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
