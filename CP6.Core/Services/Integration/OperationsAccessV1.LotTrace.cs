using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal bool LotFieldReadable(string field) => authority.Fields.Fields[field].CanRead;
    internal IQueryable<Stock> LotStocks(OperationsAccessV1 source)
    {
        var allowed = source.InventoryStocks();
        return InventoryStocks().Where(s => allowed.Any(x => x.Id == s.Id));
    }
    internal IQueryable<StockTransaction> LotTransactions(OperationsAccessV1 source)
    {
        var allowed = source.InventoryTransactions();
        return InventoryTransactions().Where(s => allowed.Any(x => x.Id == s.Id));
    }
    internal async Task<OperationsAccessV1?> OptionalLotSource(string resource, CancellationToken ct)
    {
        try { return await SourceAsync(resource, ct); }
        catch (OrderPriceCorrectionException error) when (error.Message == "ACTION_FORBIDDEN") { return null; }
    }
}
