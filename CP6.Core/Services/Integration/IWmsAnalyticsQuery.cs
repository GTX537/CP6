namespace CP6.Core.Services.Integration;

/// <summary>Read-only WMS analytics boundary consumed by Space.</summary>
public interface IWmsAnalyticsQuery
{
    Task<IReadOnlyList<WmsOutboundAggregate>> GetOutboundAggregatesAsync(
        string warehouseCd, DateTime fromInclusive, DateTime toExclusive,
        CancellationToken ct = default);

    Task<WmsActivitySummary> GetActivitySummaryAsync(
        string warehouseCd, DateTime fromInclusive, DateTime toExclusive,
        CancellationToken ct = default);
}

public sealed class WmsOutboundAggregate
{
    public string ProductCd { get; set; } = string.Empty;
    public int OutCount { get; set; }
    public decimal OutQty { get; set; }
}

public sealed class WmsActivitySummary
{
    public int InboundCount { get; set; }
    public int OutboundCount { get; set; }
}

public sealed class StubWmsAnalyticsQuery : IWmsAnalyticsQuery
{
    public Task<IReadOnlyList<WmsOutboundAggregate>> GetOutboundAggregatesAsync(
        string warehouseCd, DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WmsOutboundAggregate>>(Array.Empty<WmsOutboundAggregate>());

    public Task<WmsActivitySummary> GetActivitySummaryAsync(
        string warehouseCd, DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default)
        => Task.FromResult(new WmsActivitySummary());
}
