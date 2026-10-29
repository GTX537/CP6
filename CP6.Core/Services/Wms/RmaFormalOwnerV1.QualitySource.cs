using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Wms;

public sealed partial class RmaFormalOwnerV1
{
    internal async Task QualityFence(bool write, CancellationToken ct)
        => await new RmaFormalStoreV1(db,protector).Fence(environment.EnvironmentId,runtime.EvidenceMode,write,ct);
    internal async Task<RmaQualitySourceV1[]> QualitySources(OperationsAccessV1 access, Guid application, CancellationToken ct)
    {
        var store = new RmaFormalStoreV1(db,protector); var app = await Current(store,access,application,ct);
        var rows = await PhysicalWorks().Where(x => x.Application == application && x.State == "SUCCEEDED").ToArrayAsync(ct);
        if (rows.Length == 0) return [];
        var head = await PhysicalHead(application,false,null,ct); var sources = new List<RmaQualitySourceV1>();
        foreach (var row in rows)
        {
            var view = PhysicalView(row,head); await ReadPhysicalResults(access,view,ct);
            var plans = (await db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_stock_return_v1.plans WHERE tenant_id={db.CurrentTenantId} AND work_id={row.Id}").ToArrayAsync(ct))
                .Select(PhysicalDecode<StockRmaPlanV1>).SelectMany(x => x.Lines).ToDictionary(x => x.Line.Input.LineId);
            foreach (var receipt in view.Results.SelectMany(x => x.PhysicalReceipts))
            {
                var line = plans[Guid.Parse(receipt.ReceiptLineId)];
                var reference = RmaInboundAdapterV1.Ref("Main.Wms.RmaPhysicalReceipt.v1",receipt.ReceiptLineId,receipt);
                sources.Add(new(application,line.Line.Input.LineId,row.Actor,app.Scope.SiteId,
                    Guid.Parse(line.Line.Share.Original.ShipmentRef.Id),reference,RmaIdentity(application),head,receipt,line.Source.QualityBasisRef));
            }
        }
        return sources.ToArray();
    }
}
