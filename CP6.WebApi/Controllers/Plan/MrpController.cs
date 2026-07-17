using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Plan;
using CP6.Entity.DomainModels.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Plan;

/// <summary>MRP 运算请求 DTO。</summary>
public class MrpRunRequestDto
{
    /// <summary>显式需求清单（手工/选单驱动）。</summary>
    public List<MrpDemandDto>? Demands { get; set; }
    /// <summary>从开口受注自动派生需求（OrderDetail 剩余量）。</summary>
    public bool FromOpenOrders { get; set; }
    /// <summary>运算范围（审计留痕）。</summary>
    public string? ScopeJson { get; set; }
}

/// <summary>MRP 需求项 DTO。</summary>
public class MrpDemandDto
{
    public string ItemCd { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public DateTime? RequiredDate { get; set; }
    public string? SourceRefNo { get; set; }
}

/// <summary>计划中台 MRP REST —— spec §9。/api/plan/mrp。运算 + 计划订单/净需求钻取。</summary>
[ApiController]
[Route("api/plan/mrp")]
[Authorize]
public class MrpController : ControllerBase
{
    private readonly IMrpEngine _engine;
    private readonly CP6Context _db;
    private readonly IPlanConvertService _convert;

    public MrpController(IMrpEngine engine, CP6Context db, IPlanConvertService convert)
    {
        _engine = engine;
        _db = db;
        _convert = convert;
    }

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    /// <summary>运行 MRP（显式需求 或 开口受注派生）→ 返回运算批次。</summary>
    [HttpPost("run")]
    [RequirePermission("plan-mrp", "run")]
    public async Task<IActionResult> Run([FromBody] MrpRunRequestDto dto)
    {
        var demands = dto.FromOpenOrders
            ? await BuildFromOpenOrdersAsync()
            : (dto.Demands ?? new List<MrpDemandDto>())
                .Select(d => new MrpDemand(d.ItemCd, d.Qty, d.RequiredDate ?? DateTime.Today, d.SourceRefNo))
                .ToList();

        if (demands.Count == 0)
            return BadRequest(new { code = 400, message = "E-PLAN-无需求: 无开口需求可运算" });

        var run = await _engine.RunAsync(new MrpRunRequest(demands, dto.ScopeJson));
        return Ok2(new { runId = run.Id, runNo = run.RunNo, status = run.Status });
    }

    /// <summary>运算批次清单（看板用）。</summary>
    [HttpGet("runs")]
    public async Task<IActionResult> Runs()
        => Ok2(await _db.MrpRuns.AsNoTracking().Where(r => !r.IsDeleted)
            .OrderByDescending(r => r.RunAt).ToListAsync());

    /// <summary>计划订单清单（按物料）。</summary>
    [HttpGet("run/{id}/planned-orders")]
    public async Task<IActionResult> PlannedOrders(Guid id)
        => Ok2(await _db.PlannedOrders.AsNoTracking()
            .Where(o => o.MrpRunId == id && !o.IsDeleted)
            .OrderBy(o => o.ItemCd).ToListAsync());

    /// <summary>净需求明细（毛-供给-净 钻取）。</summary>
    [HttpGet("run/{id}/net-requirements")]
    public async Task<IActionResult> NetRequirements(Guid id)
        => Ok2(await _db.NetRequirements.AsNoTracking()
            .Where(x => x.MrpRunId == id && !x.IsDeleted)
            .OrderBy(x => x.ItemCd).ToListAsync());

    /// <summary>确认计划订单（建议 → 已确认，进供给）。</summary>
    [HttpPost("planned-order/{id}/confirm")]
    [RequirePermission("plan-mrp", "confirm")]
    public async Task<IActionResult> Confirm(Guid id)
    {
        try { await _convert.ConfirmAsync(id, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>转单（采购类 → PR、生产类 → 工单；回填单号、置已转单）。</summary>
    [HttpPost("planned-order/{id}/convert")]
    [RequirePermission("plan-mrp", "convert")]
    public async Task<IActionResult> Convert(Guid id)
    {
        try { return Ok2(new { docNo = await _convert.ConvertAsync(id, CurrentUser) }); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>忽略计划订单（置已忽略，不计供给）。</summary>
    [HttpPost("planned-order/{id}/ignore")]
    [RequirePermission("plan-mrp", "ignore")]
    public async Task<IActionResult> Ignore(Guid id)
    {
        try { await _convert.IgnoreAsync(id, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>从开口受注派生需求：OrderDetail 剩余量(Quantity − ShippedQty)，剔除取消/出荷済。</summary>
    private async Task<List<MrpDemand>> BuildFromOpenOrdersAsync()
    {
        var rows = await (
            from d in _db.OrderDetails.AsNoTracking()
            join o in _db.Orders.AsNoTracking() on d.WebOrderNo equals o.WebOrderNo
            where !d.IsDeleted && !o.IsDeleted
                  && o.OrderStatus != OrderLifecycleStatus.Cancelled
                  && o.OrderStatus != OrderLifecycleStatus.Shipped
                  && (d.Quantity ?? 0) - (d.ShippedQty ?? 0) > 0
            select new
            {
                d.ProductCd,
                Remaining = (d.Quantity ?? 0) - (d.ShippedQty ?? 0),
                d.CustomerDeliveryDate,
                d.WebOrderNo,
            }).ToListAsync();

        return rows
            .Select(r => new MrpDemand(r.ProductCd, r.Remaining, r.CustomerDeliveryDate ?? DateTime.Today, r.WebOrderNo))
            .ToList();
    }
}
