using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed partial class WmsFeatureFlagChangeService
{
    private async Task<IReadOnlyList<WmsFeatureFlagDto>> GetFeaturesCoreAsync(CancellationToken ct)
    {
        return await _db.WmsFeatureFlags.AsNoTracking()
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
    }
}
