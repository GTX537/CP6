using System.ComponentModel.DataAnnotations;

namespace CP6.Core.Services.Space;

public interface ISpaceAnalyticsService
{
    Task<SpaceAnalyticsConfigDto> GetConfigAsync(CancellationToken ct = default);
    Task<SpaceAnalyticsConfigDto> UpdateConfigAsync(
        SpaceAnalyticsConfigUpdate request, string? user, CancellationToken ct = default);
    Task<SpaceAbcSnapshotMetaDto> RebuildAbcAsync(
        Guid siteId, string trigger, string? user, CancellationToken ct = default);
    Task<int> RebuildDueSnapshotsAsync(CancellationToken ct = default);
    Task<SpaceUtilizationResponse> GetUtilizationAsync(Guid floorId, CancellationToken ct = default);
    Task<SpaceStorageTypeResponse> GetStorageTypesAsync(Guid floorId, CancellationToken ct = default);
    Task<SpaceAbcResponse> GetAbcAsync(
        Guid floorId, CancellationToken ct = default, bool includeProducts = true);
    Task<SpaceControlTowerDto> GetControlTowerAsync(Guid siteId, CancellationToken ct = default);
}

public class SpaceAnalyticsConfigDto
{
    [Range(1, 365)]
    public int WindowDays { get; set; } = 90;
    [RegularExpression("^(quantity|frequency)$")]
    public string Metric { get; set; } = "quantity";
    [Range(typeof(decimal), "0.00001", "0.99999")]
    public decimal ThresholdA { get; set; } = 0.80m;
    [Range(typeof(decimal), "0.00002", "1")]
    public decimal ThresholdB { get; set; } = 0.95m;
    [Range(1, 720)]
    public int StaleAfterHours { get; set; } = 48;
    [Range(0, 23)]
    public int ScheduledHourLocal { get; set; } = 2;
    public bool EnableScheduledSnapshot { get; set; } = true;
}

public sealed class SpaceAnalyticsConfigUpdate : SpaceAnalyticsConfigDto { }

public sealed class SpaceAbcSnapshotMetaDto
{
    public Guid SnapshotId { get; set; }
    public Guid SiteId { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public DateTime CalculatedAt { get; set; }
    public DateTime WindowFrom { get; set; }
    public DateTime WindowTo { get; set; }
    public int WindowDays { get; set; }
    public string Metric { get; set; } = "quantity";
    public decimal ThresholdA { get; set; }
    public decimal ThresholdB { get; set; }
    public int ItemCount { get; set; }
    public string Trigger { get; set; } = string.Empty;
}

public sealed class SpaceAnalyticsWarningDto
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? LocationCode { get; set; }
    public string Severity { get; set; } = "warning";
}

public sealed class SpaceUtilizationItemDto
{
    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public Guid? RackId { get; set; }
    public string? RackCode { get; set; }
    public Guid? ZoneId { get; set; }
    public string? ZoneCode { get; set; }
    public string? ZoneName { get; set; }
    public int? ZoneType { get; set; }
    public decimal? Qty { get; set; }
    public decimal? Capacity { get; set; }
    public int? CapacityUom { get; set; }
    public string? CapacitySource { get; set; }
    public decimal? Utilization { get; set; }
    public int? BinStatus { get; set; }
    public bool StockAvailable { get; set; }
    public bool IncludedInAggregate { get; set; }
    public string? WarningCode { get; set; }
}

public sealed class SpaceUtilizationAggregateDto
{
    public Guid? EntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CapacityUom { get; set; }
    public int LocationCount { get; set; }
    public decimal Qty { get; set; }
    public decimal Capacity { get; set; }
    public decimal Utilization { get; set; }
    public int OverCapacityCount { get; set; }
}

public sealed class SpaceUtilizationResponse
{
    public Guid FloorId { get; set; }
    public Guid SiteId { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool StockAvailable { get; set; }
    public List<SpaceUtilizationItemDto> Items { get; set; } = new();
    public List<SpaceUtilizationAggregateDto> Racks { get; set; } = new();
    public List<SpaceUtilizationAggregateDto> Zones { get; set; } = new();
    public List<SpaceAnalyticsWarningDto> Warnings { get; set; } = new();
}

public sealed class SpaceStorageTypeItemDto
{
    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public Guid? ZoneId { get; set; }
    public string? ZoneCode { get; set; }
    public string? ZoneName { get; set; }
    public int ZoneType { get; set; }
    public string TypeKey { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}

public sealed class SpaceStorageTypeSummaryDto
{
    public int ZoneType { get; set; }
    public string TypeKey { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int LocationCount { get; set; }
    public decimal Percentage { get; set; }
}

public sealed class SpaceStorageTypeResponse
{
    public Guid FloorId { get; set; }
    public int TotalLocations { get; set; }
    public List<SpaceStorageTypeItemDto> Items { get; set; } = new();
    public List<SpaceStorageTypeSummaryDto> Summary { get; set; } = new();
}

public sealed class SpaceAbcProductDto
{
    public string ProductCd { get; set; } = string.Empty;
    public int OutCount { get; set; }
    public decimal OutQty { get; set; }
    public decimal Score { get; set; }
    public decimal CumulativeRatio { get; set; }
    public string AbcRank { get; set; } = "C";
}

public sealed class SpaceAbcLocationDto
{
    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public decimal? Qty { get; set; }
    public List<string> ProductCodes { get; set; } = new();
    public string? AbcRank { get; set; }
    public int? AbsX { get; set; }
    public int? AbsY { get; set; }
}

public sealed class SpacePointDto
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class SpaceAbcResponse
{
    public Guid FloorId { get; set; }
    public Guid SiteId { get; set; }
    public bool HasSnapshot { get; set; }
    public bool IsStale { get; set; }
    public bool StockAvailable { get; set; }
    public SpaceAbcSnapshotMetaDto? Snapshot { get; set; }
    public List<SpaceAbcProductDto> Products { get; set; } = new();
    public List<SpaceAbcLocationDto> Items { get; set; } = new();
    public List<SpacePointDto> ShippingTargets { get; set; } = new();
    public decimal? AverageAShippingDistanceMm { get; set; }
    public string? DistanceMethod { get; set; }
    public List<SpaceAnalyticsWarningDto> Warnings { get; set; } = new();
}

public sealed class SpaceTowerFloorDto
{
    public Guid FloorId { get; set; }
    public string FloorCode { get; set; } = string.Empty;
    public string FloorName { get; set; } = string.Empty;
    public int Level { get; set; }
    public int TotalLocations { get; set; }
    public int OccupiedLocations { get; set; }
    public int AlertCount { get; set; }
    public List<SpaceTowerLocationUtilizationDto> Locations { get; set; } = new();
}

public sealed class SpaceTowerLocationUtilizationDto
{
    public string LocationCode { get; set; } = string.Empty;
    public decimal? Utilization { get; set; }
}

public sealed class SpaceTowerUtilizationDto
{
    public int CapacityUom { get; set; }
    public decimal Qty { get; set; }
    public decimal Capacity { get; set; }
    public decimal Utilization { get; set; }
    public int LocationCount { get; set; }
}

public sealed class SpaceControlTowerDto
{
    public Guid SiteId { get; set; }
    public string SiteCode { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string WarehouseCd { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public bool StockAvailable { get; set; }
    public int TotalLocations { get; set; }
    public int OccupiedLocations { get; set; }
    public int EmptyLocations { get; set; }
    public int FullOrOverCapacityLocations { get; set; }
    public int AnomalyCount { get; set; }
    public int TodayInboundCount { get; set; }
    public int TodayOutboundCount { get; set; }
    public Dictionary<string, int> AbcProductCounts { get; set; } = new();
    public SpaceAbcSnapshotMetaDto? AbcSnapshot { get; set; }
    public List<SpaceTowerUtilizationDto> UtilizationByUom { get; set; } = new();
    public List<SpaceTowerFloorDto> Floors { get; set; } = new();
    public List<SpaceAnalyticsWarningDto> Alerts { get; set; } = new();
}
