using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>资产卡片 REST（A3 §7.2）。建档采番 FA、起折期=购置次月；启用定格；GetSchedule 前瞻计划。</summary>
[ApiController]
[Route("api/fin/asset-card")]
[Authorize]
public class AssetCardController : ControllerBase
{
    private readonly CP6Context _db;
    private readonly IFinSequenceService _seq;
    private readonly IAssetDepreciationService _dep;

    public AssetCardController(CP6Context db, IFinSequenceService seq, IAssetDepreciationService dep)
    {
        _db = db; _seq = seq; _dep = dep;
    }

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    [HttpGet]
    [RequirePermission("fin-asset-card", "view")]
    public async Task<IActionResult> List([FromQuery] Guid? categoryId, [FromQuery] AssetStatus? status)
        => Ok2(await _db.AssetCards
            .Where(c => (categoryId == null || c.CategoryId == categoryId) && (status == null || c.Status == status))
            .OrderByDescending(c => c.AcquisitionDate).ToListAsync());

    [HttpGet("{id}")]
    [RequirePermission("fin-asset-card", "view")]
    public async Task<IActionResult> Get(Guid id) => Ok2(await _db.AssetCards.FindAsync(id));

    [HttpPost]
    [RequirePermission("fin-asset-card", "add")]
    public async Task<IActionResult> Create([FromBody] AssetCard card)
    {
        var cat = await _db.AssetCategories.FindAsync(card.CategoryId);
        if (cat == null) return Fin(FinResult.Fail("FA001"));
        card.Id = Guid.NewGuid();
        card.AssetNo = await _seq.NextAsync("FA", card.AcquisitionDate);
        if (card.UsefulLifeMonths <= 0) card.UsefulLifeMonths = cat.DefaultUsefulLifeMonths;
        if (card.Method == 0) card.Method = cat.DefaultMethod;
        if (card.SalvageRate == 0m) card.SalvageRate = cat.DefaultSalvageRate;
        card.SalvageValue = card.SalvageValue > 0m
            ? card.SalvageValue
            : Math.Round(card.OriginalValue * card.SalvageRate, 2);
        var next = new DateTime(card.AcquisitionDate.Year, card.AcquisitionDate.Month, 1).AddMonths(1);
        card.DepreciationStartPeriod = next.ToString("yyyy-MM");
        if (!card.IsOpeningImport) { card.AccumulatedDepreciation = 0m; card.DepreciatedPeriods = 0; }
        card.Status = AssetStatus.Draft;
        _db.AssetCards.Add(card);
        await _db.SaveChangesAsync();
        return Ok2(new { id = card.Id, assetNo = card.AssetNo });
    }

    [HttpPut("{id}")]
    [RequirePermission("fin-asset-card", "edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AssetCard card)
    {
        var e = await _db.AssetCards.FindAsync(id);
        if (e == null) return Fin(FinResult.Fail("FA006"));
        e.CostCenterId = card.CostCenterId; e.MachineId = card.MachineId; e.DeptId = card.DeptId;
        e.Custodian = card.Custodian; e.Location = card.Location; e.Remarks = card.Remarks;
        e.DeprecExpenseAccountId = card.DeprecExpenseAccountId;
        await _db.SaveChangesAsync();
        return Ok2();
    }

    [HttpPost("{id}/activate")]
    [RequirePermission("fin-asset-card", "activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var e = await _db.AssetCards.FindAsync(id);
        if (e == null) return Fin(FinResult.Fail("FA006"));
        if (e.Status != AssetStatus.Draft) return Fin(FinResult.Fail("FA009"));
        e.Status = AssetStatus.InUse;
        await _db.SaveChangesAsync();
        return Ok2();
    }

    [HttpGet("{id}/schedule")]
    [RequirePermission("fin-asset-card", "view")]
    public async Task<IActionResult> Schedule(Guid id) => Ok2(await _dep.GetScheduleAsync(id));
}
