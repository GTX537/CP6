using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/v2/admin/wms-features")]
[Authorize]
public sealed class WmsFeatureFlagsController : ControllerBase
{
    private readonly CP6Context _db;
    public WmsFeatureFlagsController(CP6Context db) => _db = db;

    [HttpGet]
    [RequirePermission("wms-mobile", "device-manage")]
    public async Task<IReadOnlyList<WmsFeatureFlagDto>> Get(CancellationToken ct)
        => await _db.WmsFeatureFlags.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.WarehouseCd)
            .Select(x => new WmsFeatureFlagDto
            {
                WarehouseCd = x.WarehouseCd,
                ProductionMoveEnabled = x.ProductionMoveEnabled,
                SerialLpnEnabled = x.SerialLpnEnabled,
                ScanRetentionDays = x.ScanRetentionDays,
                RowVersion = x.RowVersion == null
                    ? string.Empty
                    : Convert.ToBase64String(x.RowVersion)
            }).ToListAsync(ct);

    [HttpPut("{warehouseCd}")]
    [RequirePermission("wms-mobile", "device-manage")]
    public async Task<ActionResult<WmsFeatureFlagDto>> Put(
        string warehouseCd,
        UpdateWmsFeatureFlagRequest request,
        CancellationToken ct)
    {
        if (!await _db.Warehouses.AnyAsync(
            x => !x.IsDeleted && x.WarehouseCd == warehouseCd, ct))
            return NotFound(new { code = "WM-V2-WAREHOUSE-NOT-FOUND" });
        var row = await _db.WmsFeatureFlags.FirstOrDefaultAsync(
            x => !x.IsDeleted && x.WarehouseCd == warehouseCd, ct);
        if (row is null)
        {
            row = new WmsFeatureFlag
            {
                WarehouseCd = warehouseCd,
                Creator = User.Identity?.Name
            };
            _db.WmsFeatureFlags.Add(row);
        }
        row.ProductionMoveEnabled = request.ProductionMoveEnabled;
        row.SerialLpnEnabled = request.SerialLpnEnabled;
        row.ScanRetentionDays = Math.Clamp(request.ScanRetentionDays, 30, 3650);
        row.Modifier = User.Identity?.Name;
        row.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return Ok(new WmsFeatureFlagDto
        {
            WarehouseCd = row.WarehouseCd,
            ProductionMoveEnabled = row.ProductionMoveEnabled,
            SerialLpnEnabled = row.SerialLpnEnabled,
            ScanRetentionDays = row.ScanRetentionDays,
            RowVersion = row.RowVersion is { Length: > 0 }
                ? Convert.ToBase64String(row.RowVersion)
                : string.Empty
        });
    }
}
