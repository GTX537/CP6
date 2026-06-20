using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>资产折旧 REST（A3 §7.3）。Preview/Run/SetWorkload/Post/Reverse + 批次/明细列表。</summary>
[ApiController]
[Route("api/fin/asset-deprec")]
[Authorize]
public class AssetDepreciationController : ControllerBase
{
    private readonly IAssetDepreciationService _svc;
    private readonly CP6Context _db;

    public AssetDepreciationController(IAssetDepreciationService svc, CP6Context db) { _svc = svc; _db = db; }

    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    public sealed class WorkloadReq { public decimal Workload { get; set; } }
    public sealed class ReasonReq { public string Reason { get; set; } = string.Empty; }

    [HttpGet("preview")]
    [RequirePermission("fin-asset-deprec", "view")]
    public async Task<IActionResult> Preview([FromQuery] Guid periodId) => Ok2(await _svc.PreviewAsync(periodId));

    [HttpGet("list")]
    [RequirePermission("fin-asset-deprec", "view")]
    public async Task<IActionResult> List([FromQuery] Guid? periodId)
        => Ok2(await _db.DepreciationRuns
            .Where(r => periodId == null || r.FiscalPeriodId == periodId)
            .OrderByDescending(r => r.RunAt).ToListAsync());

    [HttpGet("{runId}/entries")]
    [RequirePermission("fin-asset-deprec", "view")]
    public async Task<IActionResult> Entries(Guid runId)
        => Ok2(await _db.DepreciationEntries.Where(e => e.RunId == runId).ToListAsync());

    [HttpPost("run")]
    [RequirePermission("fin-asset-deprec", "run")]
    public async Task<IActionResult> Run([FromQuery] Guid periodId)
        => Fin(await _svc.RunAsync(periodId, CurrentUser, DepreciationRunMode.Manual));

    [HttpPut("entry/{entryId}/workload")]
    [RequirePermission("fin-asset-deprec", "run")]
    public async Task<IActionResult> SetWorkload(Guid entryId, [FromBody] WorkloadReq r)
        => Fin(await _svc.SetWorkloadAsync(entryId, r.Workload));

    [HttpPost("{runId}/post")]
    [RequirePermission("fin-asset-deprec", "post")]
    public async Task<IActionResult> Post(Guid runId) => Fin(await _svc.PostAsync(runId, CurrentUser));

    [HttpPost("{runId}/reverse")]
    [RequirePermission("fin-asset-deprec", "reverse")]
    public async Task<IActionResult> Reverse(Guid runId, [FromBody] ReasonReq r)
        => Fin(await _svc.ReverseAsync(runId, CurrentUser, r.Reason));
}
