using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Space;

[ApiController]
[Route("api/space")]
[Authorize]
public class SpaceStockController : ControllerBase
{
    private readonly IWmsStockQuery _stock;
    private readonly CP6Context _db;
    public SpaceStockController(IWmsStockQuery stock, CP6Context db) { _stock = stock; _db = db; }

    private IActionResult Ok2(object? data = null, string msg = "OK") => Ok(new { code = 0, message = msg, data });

    /// <summary>取某层库存快照（服务端枚举该层 Placed 库位编码 → 批量查 WMS）。</summary>
    [HttpGet("floor/{floorId:guid}/stock")]
    public async Task<IActionResult> FloorStock(Guid floorId, CancellationToken ct)
    {
        var codes = await _db.Space_Locations
            .Where(l => l.FloorId == floorId && l.Placed && l.LocationCode != null)
            .Select(l => l.LocationCode!)
            .ToListAsync(ct);
        var items = await _stock.GetStockByLocationsAsync(codes, ct);
        return Ok2(new { items, ts = DateTime.Now });
    }

    /// <summary>按物料/批次/容器反查库位（命中列表，前端逐个复用 06 定位）。</summary>
    [HttpGet("stock/locate")]
    public async Task<IActionResult> Locate(
        [FromQuery] string? material, [FromQuery] string? lot, [FromQuery] string? container, CancellationToken ct)
    {
        var hits = await _stock.FindLocationsAsync(
            new StockLocateQuery { MaterialNo = material, Lot = lot, Container = container }, ct);
        return Ok2(hits);
    }
}
