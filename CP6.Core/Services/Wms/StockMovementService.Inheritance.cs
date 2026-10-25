using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;

namespace CP6.Core.Services.Wms;

public partial class StockMovementService
{
    private StockTransactionInheritanceV1? inheritanceAccess;
    internal IDisposable InheritTransactions(OperationsAccessV1 access)=>InheritTransactions(access.StockInheritance());
    internal IDisposable InheritTransactions(StockTransactionInheritanceV1 access)
    {
        RequireParentTransaction();access.RequireStockInheritanceContext(_db);
        if(inheritanceAccess!=null)throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
        inheritanceAccess=access;return new InheritanceScope(this,access);
    }
    private sealed class InheritanceScope(StockMovementService service,StockTransactionInheritanceV1 access):IDisposable
    {
        public void Dispose()
        {if(ReferenceEquals(service.inheritanceAccess,access))service.inheritanceAccess=null;}
    }
    private StockTransactionInheritanceV1 InheritanceAccess=>inheritanceAccess??
        throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
}
