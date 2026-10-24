using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Erp;
using CP6.WebApi.Controllers.Erp;
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
[OrderOwnerResult]
[Route("api/wms/warehouse")]
[Authorize]
public partial class WarehouseController : ControllerBase
{
    private readonly CP6Context _db;
    private readonly WarehouseOwnerServiceV1? _owner;
    public WarehouseController(CP6Context db,WarehouseOwnerServiceV1? owner=null) { _db=db;_owner=owner; }
    private WarehouseOwnerServiceV1 Owner=>_owner??throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);

    private string? CurrentUser => User?.Identity?.Name;

    // ─────────── 倉庫マスタ ───────────

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? warehouseCd, [FromQuery] int? warehouseType, [FromQuery] string? baseCd)
    {
        if(_db.Database.IsNpgsql())await Owner.RejectLegacyAsync("warehouses","query");
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
        if(_db.Database.IsNpgsql())await Owner.RejectLegacyAsync("warehouses","query",cd);
        var w = await _db.Warehouses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WarehouseCd == cd && !x.IsDeleted);
        if (w == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = w });
    }

    [HttpPost]
    [RequirePermission("wms-warehouse", "add")]
    public async Task<IActionResult> Create([FromBody] Warehouse dto)
    {
        if(_db.Database.IsNpgsql())await Owner.RejectLegacyAsync("warehouses","add");
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
        if(_db.Database.IsNpgsql())await Owner.RejectLegacyAsync("warehouses","edit",cd);
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
        if(_db.Database.IsNpgsql())await Owner.RejectLegacyAsync("warehouses","del",cd);
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
        if(_db.Database.IsNpgsql())await Owner.RejectLegacyAsync("locations","query",cd,true);
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
        if(_db.Database.IsNpgsql())await Owner.RejectLegacyAsync("locations","add",dto.WarehouseCd,true);
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
        if(_db.Database.IsNpgsql())await Owner.RejectLegacyAsync("locations","edit",cd);
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
        if(_db.Database.IsNpgsql())await Owner.RejectLegacyAsync("locations","del",cd);
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
