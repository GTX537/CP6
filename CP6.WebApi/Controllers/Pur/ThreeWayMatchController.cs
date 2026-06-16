using CP6.Core.Auth;
using CP6.Core.Services.Pur;
using CP6.Entity.DomainModels.Pur;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Pur;

/// <summary>三单匹配 REST —— 采购章04（★MVP）。/api/pur/match。容差匹配→自动建应付 / 挂起→放行或拒绝。</summary>
[ApiController]
[Route("api/pur/match")]
[Authorize]
public class ThreeWayMatchController : ControllerBase
{
    private readonly IThreeWayMatchService _svc;
    public ThreeWayMatchController(IThreeWayMatchService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? poNo, [FromQuery] MatchStatus? status)
        => Ok2(await _svc.ListAsync(poNo, status));

    [HttpGet("{matchNo}")]
    public async Task<IActionResult> Get(string matchNo)
    {
        var m = await _svc.GetAsync(matchNo);
        return m == null ? Err(new InvalidOperationException("E-PUR-041")) : Ok2(m);
    }

    /// <summary>匹配一张供应商发票（容差内自动建应付，超容差挂起）。</summary>
    [HttpPost]
    [RequirePermission("pur-match", "add")]
    public async Task<IActionResult> Match([FromBody] MatchInvoiceDto dto)
    {
        try { return Ok2(await _svc.MatchInvoiceAsync(dto, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>人工放行挂起单（接受差异，建应付）。</summary>
    [HttpPost("{matchNo}/release")]
    [RequirePermission("pur-match", "release")]
    public async Task<IActionResult> Release(string matchNo, [FromBody] MatchHandleDto? body)
    {
        try { return Ok2(await _svc.ReleaseAsync(matchNo, CurrentUser, body?.Note)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>拒绝挂起单。</summary>
    [HttpPost("{matchNo}/reject")]
    [RequirePermission("pur-match", "reject")]
    public async Task<IActionResult> Reject(string matchNo, [FromBody] MatchHandleDto? body)
    {
        try { return Ok2(await _svc.RejectAsync(matchNo, CurrentUser, body?.Note)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>差异处理留痕入参。</summary>
    public class MatchHandleDto
    {
        /// <summary>处理说明</summary>
        public string? Note { get; set; }
    }
}
