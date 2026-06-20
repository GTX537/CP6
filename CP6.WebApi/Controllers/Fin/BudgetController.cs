using CP6.Core.Auth;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>
/// 预算方案/版本 REST —— 财务 A5。/api/fin/budget。
/// 方案 CRUD + 版本状态机（草稿→送审→激活）。
/// </summary>
[ApiController]
[Route("api/fin/budget")]
[Authorize]
public class BudgetController : ControllerBase
{
    private readonly IBudgetService _svc;
    private readonly ICurrentPermissionContext _ctx;

    public BudgetController(IBudgetService svc, ICurrentPermissionContext ctx)
    {
        _svc = svc;
        _ctx = ctx;
    }

    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private async Task<Guid> CurrentUserIdAsync() => (await _ctx.GetAsync()).UserId;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    // ── 预算方案 ──

    [HttpGet]
    [RequirePermission("fin-budget", "view")]
    public async Task<IActionResult> List()
        => Ok2(await _svc.ListBudgetsAsync());

    [HttpPost]
    [RequirePermission("fin-budget", "add")]
    public async Task<IActionResult> Create([FromBody] Budget dto)
    {
        var r = await _svc.CreateBudgetAsync(dto, CurrentUser);
        return r.Ok ? Ok2(r.Data) : BadRequest(new { code = 400, message = r.Code, args = r.Args });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("fin-budget", "edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBudgetReq req)
        => Fin(await _svc.UpdateBudgetAsync(id, req.Name, req.Description));

    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission("fin-budget", "edit")]
    public async Task<IActionResult> Deactivate(Guid id)
        => Fin(await _svc.DeactivateBudgetAsync(id));

    // ── 预算版本 ──

    [HttpGet("{id:guid}/versions")]
    [RequirePermission("fin-budget", "view")]
    public async Task<IActionResult> ListVersions(Guid id)
        => Ok2(await _svc.ListVersionsAsync(id));

    [HttpPost("{id:guid}/versions")]
    [RequirePermission("fin-budget", "add")]
    public async Task<IActionResult> CreateVersion(Guid id, [FromBody] CreateVersionReq req)
    {
        var r = await _svc.CreateVersionAsync(id, req.Name, CurrentUser, req.CopyFromVersionId, req.CopyFromActualFiscalYear);
        return r.Ok ? Ok2(r.Data) : BadRequest(new { code = 400, message = r.Code, args = r.Args });
    }

    [HttpPut("versions/{vid:guid}")]
    [RequirePermission("fin-budget", "edit")]
    public async Task<IActionResult> UpdateVersion(Guid vid, [FromBody] UpdateVersionReq req)
        => Fin(await _svc.UpdateVersionAsync(vid, req.Name, req.ControlMode, req.ControlBasis));

    [HttpPost("versions/{vid:guid}/submit")]
    [RequirePermission("fin-budget", "submit")]
    public async Task<IActionResult> Submit(Guid vid)
    {
        var userId = await CurrentUserIdAsync();
        return Fin(await _svc.SubmitForApprovalAsync(vid, userId, CurrentUser));
    }

    [HttpPost("versions/{vid:guid}/activate")]
    [RequirePermission("fin-budget", "activate")]
    public async Task<IActionResult> Activate(Guid vid)
        => Fin(await _svc.ActivateAsync(vid));

    [HttpDelete("versions/{vid:guid}")]
    [RequirePermission("fin-budget", "delete")]
    public async Task<IActionResult> DeleteVersion(Guid vid)
        => Fin(await _svc.DeleteVersionAsync(vid));

    // ── 请求 DTO ──
    public record UpdateBudgetReq(string Name, string? Description);
    public record CreateVersionReq(string Name, Guid? CopyFromVersionId, int? CopyFromActualFiscalYear);
    public record UpdateVersionReq(string Name, BudgetControlMode ControlMode, BudgetControlBasis ControlBasis);
}
