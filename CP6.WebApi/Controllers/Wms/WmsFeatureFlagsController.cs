using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/v2/admin/wms-features")]
[Authorize]
public sealed class WmsFeatureFlagsController : ControllerBase
{
    private readonly CP6.Core.EFDbContext.CP6Context _db;
    public WmsFeatureFlagsController(CP6.Core.EFDbContext.CP6Context db) => _db = db;

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
        _ = warehouseCd;
        _ = request;
        _ = ct;
        return StatusCode(StatusCodes.Status410Gone, new
        {
            code = "WM-FEATURE-APPROVAL-REQUIRED",
            message = "Warehouse production flags must be changed through OA approval."
        });
    }
}
