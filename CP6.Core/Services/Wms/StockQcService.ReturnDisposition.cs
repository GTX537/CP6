using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Wms;

public partial class StockQcService
{
    internal async Task<FulfilmentRefV1> ReleaseReturnRangeAsync(OperationsAccessV1 parent,ReturnDispositionPreparationV2 p,CancellationToken ct)
    {
        var access =await parent.ActionAsync(OperationsOwnerV1.StockQc,"return-release",ct);
        var source =p.Stock.Source;
        await access.RequireStockKey(source.Warehouse,source.Location,source.Product,source.Lot,false,ct);
        // The caller already holds the actual current Quality decision and exact range claim.
        // This release covers those roots only; the mixed source pool is never marked wholly passed.
        var at =DateTimeOffset.UtcNow;
        var body =new { p.WorkId,p.Source.Reference,p.Decision.DecisionRef,p.Ranges,p.Stock.Spans,p.Quantity,Destination =p.Stock.Destination,Actor =access.ActorId,at };
        var proof =RmaInboundAdapterV1.Ref("Main.Wms.StockQc.ReturnRelease.v2",p.WorkId.ToString(),body);
        await _db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_return_disposition_v2.quality_releases(tenant_id,work_id,body) VALUES({_db.CurrentTenantId},{p.WorkId},{Encode(new { Reference =proof,Value =body })})",ct);
        return proof;
    }
}
