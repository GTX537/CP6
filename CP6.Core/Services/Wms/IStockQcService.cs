using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

public interface IStockQcService
{
    /// <summary>Set QcStatus for a single Stock row (manual operator action).</summary>
    Task<Stock> SetStockQcStatusAsync(Guid stockId, string newStatus, string? reason, string? userName);

    /// <summary>
    /// Mark all Stock rows produced via the given WorkOrder's production-inbound chain.
    /// </summary>
    Task<int> MarkLinkedStockByWorkOrderAsync(string workOrderNo, string newStatus, string? reason, string? userName);
}
