using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>
/// MSBBWM010 — 倉庫・ロケーション マスタ Web API
/// </summary>
[ApiController]
[Route("api/wms/warehouse")]
[Authorize]
public class WarehouseController : ControllerBase
{
    private readonly CP6Context _db;
    public WarehouseController(CP6Context db) => _db = db;

    private string? CurrentUser => User?.Identity?.Name;

    // ─────────── 倉庫マスタ ───────────

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? warehouseCd, [FromQuery] int? warehouseType, [FromQuery] string? baseCd)
    {
        var q = _db.Warehouses.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(warehouseCd)) q = q.Where(x => x.WarehouseCd.Contains(warehouseCd));
        if (warehouseType.HasValue) q = q.Where(x => x.WarehouseType == warehouseType.Value);
        if (!string.IsNullOrWhiteSpace(baseCd)) q = q.Where(x => x.BaseCd == baseCd);
        var list = await q.OrderBy(x => x.WarehouseCd).ToListAsync();
        return Ok(new { code = 0, message = "OK", data = list });
    }

    [HttpGet("{cd}")]
    public async Task<IActionResult> Get(string cd)
    {
        var w = await _db.Warehouses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WarehouseCd == cd && !x.IsDeleted);
        if (w == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = w });
    }

    [HttpPost]
    [RequirePermission("wms-warehouse", "add")]
    public async Task<IActionResult> Create([FromBody] Warehouse dto)
    {
        if (string.IsNullOrWhiteSpace(dto.WarehouseCd))
            return BadRequest(new { code = 400, message = "WarehouseCd is required" });

        if (await _db.Warehouses.AnyAsync(x => x.WarehouseCd == dto.WarehouseCd && !x.IsDeleted))
            return BadRequest(new { code = 400, message = "WM-MSG-001" });

        dto.Id = Guid.Empty; // 強制再採番
        dto.Creator = CurrentUser;
        dto.CreateDate = DateTime.Now;
        dto.IsDeleted = false;
        _db.Warehouses.Add(dto);
        await _db.SaveChangesAsync();
        return Ok(new { code = 0, message = "WM-MSG-071", data = new { warehouseCd = dto.WarehouseCd } });
    }

    [HttpPut("{cd}")]
    [RequirePermission("wms-warehouse", "edit")]
    public async Task<IActionResult> Update(string cd, [FromBody] Warehouse dto)
    {
        var w = await _db.Warehouses.FirstOrDefaultAsync(x => x.WarehouseCd == cd && !x.IsDeleted);
        if (w == null) return NotFound(new { code = 404, message = "WM-MSG-070" });

        w.WarehouseName = dto.WarehouseName;
        w.WarehouseType = dto.WarehouseType;
        w.BaseCd = dto.BaseCd;
        w.AddressText = dto.AddressText;
        w.ManagerCd = dto.ManagerCd;
        w.AllowNegative = dto.AllowNegative;
        w.Remarks = dto.Remarks;
        w.Modifier = CurrentUser;
        w.ModifyDate = DateTime.Now;
        try
        {
            await _db.SaveChangesAsync();
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { code = 409, message = "WM-MSG-072" });
        }
    }

    [HttpDelete("{cd}")]
    [RequirePermission("wms-warehouse", "del")]
    public async Task<IActionResult> Delete(string cd)
    {
        var w = await _db.Warehouses.FirstOrDefaultAsync(x => x.WarehouseCd == cd && !x.IsDeleted);
        if (w == null) return NotFound(new { code = 404, message = "WM-MSG-070" });

        // WM-MSG-004：在庫残あり削除禁止
        var hasStock = await _db.Stocks.AnyAsync(s => s.WarehouseCd == cd && !s.IsDeleted && s.PhysicalQty != 0);
        if (hasStock) return BadRequest(new { code = 400, message = "WM-MSG-004" });

        w.IsDeleted = true;
        w.Modifier = CurrentUser;
        w.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { code = 0, message = "WM-MSG-071" });
    }

    // ─────────── ロケーション ───────────

    [HttpGet("{cd}/tree")]
    public async Task<IActionResult> GetTree(string cd)
    {
        var list = await _db.Locations.AsNoTracking()
            .Where(x => x.WarehouseCd == cd && !x.IsDeleted)
            .OrderBy(x => x.LocationLevel).ThenBy(x => x.LocationCd)
            .ToListAsync();
        return Ok(new { code = 0, message = "OK", data = list });
    }

    [HttpPost("location")]
    [RequirePermission("wms-location", "add")]
    public async Task<IActionResult> CreateLocation([FromBody] Location dto)
    {
        if (string.IsNullOrWhiteSpace(dto.LocationCd) || string.IsNullOrWhiteSpace(dto.WarehouseCd))
            return BadRequest(new { code = 400, message = "LocationCd / WarehouseCd is required" });
        if (await _db.Locations.AnyAsync(x => x.LocationCd == dto.LocationCd && !x.IsDeleted))
            return BadRequest(new { code = 400, message = "WM-MSG-001" });
        if (dto.LocationLevel > 5)
            return BadRequest(new { code = 400, message = "WM-MSG-002" });
        if (!string.IsNullOrWhiteSpace(dto.ParentLocationCd))
        {
            var parent = await _db.Locations.FirstOrDefaultAsync(x => x.LocationCd == dto.ParentLocationCd && !x.IsDeleted);
            if (parent == null) return BadRequest(new { code = 400, message = "Parent location not found" });
            if (parent.IsBlocked) return BadRequest(new { code = 400, message = "WM-MSG-003" });
        }

        dto.Id = Guid.Empty;
        dto.Creator = CurrentUser;
        dto.CreateDate = DateTime.Now;
        dto.IsDeleted = false;
        _db.Locations.Add(dto);
        await _db.SaveChangesAsync();
        return Ok(new { code = 0, message = "WM-MSG-071", data = new { locationCd = dto.LocationCd } });
    }

    [HttpPut("location/{cd}")]
    [RequirePermission("wms-location", "edit")]
    public async Task<IActionResult> UpdateLocation(string cd, [FromBody] Location dto)
    {
        var l = await _db.Locations.FirstOrDefaultAsync(x => x.LocationCd == cd && !x.IsDeleted);
        if (l == null) return NotFound(new { code = 404, message = "WM-MSG-070" });

        l.LocationName = dto.LocationName;
        l.LocationLevel = dto.LocationLevel;
        l.XCoord = dto.XCoord;
        l.YCoord = dto.YCoord;
        l.ZCoord = dto.ZCoord;
        l.CapacityQty = dto.CapacityQty;
        l.AllowedProductType = dto.AllowedProductType;
        l.IsPickable = dto.IsPickable;
        l.IsBlocked = dto.IsBlocked;
        l.Barcode = dto.Barcode;
        l.Modifier = CurrentUser;
        l.ModifyDate = DateTime.Now;
        try
        {
            await _db.SaveChangesAsync();
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { code = 409, message = "WM-MSG-072" });
        }
    }

    [HttpDelete("location/{cd}")]
    [RequirePermission("wms-location", "del")]
    public async Task<IActionResult> DeleteLocation(string cd)
    {
        var l = await _db.Locations.FirstOrDefaultAsync(x => x.LocationCd == cd && !x.IsDeleted);
        if (l == null) return NotFound(new { code = 404, message = "WM-MSG-070" });

        var hasStock = await _db.Stocks.AnyAsync(s => s.LocationCd == cd && !s.IsDeleted && s.PhysicalQty != 0);
        if (hasStock) return BadRequest(new { code = 400, message = "WM-MSG-004" });

        l.IsDeleted = true;
        l.Modifier = CurrentUser;
        l.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { code = 0, message = "WM-MSG-071" });
    }
}
