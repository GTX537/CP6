using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

public sealed partial class RmaFormalOwnerV1
{
    internal async Task<FulfilmentRefV1> CorrectEffect(OperationsAccessV1 access,ReturnCorrectionCandidateV1 c,ReturnCorrectionPreparationV1 preparation,ReturnStockResponseV1 stock,CancellationToken ct)
    {
        if (c.Target.Axis =="PHYSICAL_RECEIPT")
        {
            var line =c.Target.Source.LineId.ToString();
            var body =await db.Database.SqlQuery<byte[]>($"SELECT p.body AS \"Value\" FROM cp6_stock_return_v1.plans p WHERE p.tenant_id={db.CurrentTenantId} AND EXISTS(SELECT FROM cp6_stock_return_v1.root_receipts r WHERE r.tenant_id=p.tenant_id AND r.plan_id=p.plan_id AND r.line_key={line})").SingleAsync(ct);
            var claim =Guid.Parse(PhysicalDecode<StockRmaPlanV1>(body).Lines.Single(x => x.Line.Input.LineId ==c.Target.Source.LineId).Line.Share.RangeClaimRef.Id);
            var rows =await ClaimSegments().Where(x => x.ClaimId ==claim).ToArrayAsync(ct); var ranges =Ranges.Read(c.Ranges);
            var consumed =rows.Where(x => x.State =="CONSUMED_BY_RECEIPT").Select(x => new Ranges.Segment(x.From,x.To));
            if (!Ranges.Subset(ranges,consumed)) throw Error("CORRECTION_SOURCE_SCOPE_CHANGED",409);
            var next =rows.Where(x => x.State !="CONSUMED_BY_RECEIPT").Select(x => (Range:new Ranges.Segment(x.From,x.To),x.State)).ToList();
            next.AddRange(Ranges.Except(consumed,ranges).Select(x => (x,"CONSUMED_BY_RECEIPT")));
            next.AddRange(ranges.Select(x => (x,"VOID_BY_SOURCE_CORRECTION"))); var key =rows[0].Key;
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_rma_formal_v1.return_claim_segments WHERE tenant_id={db.CurrentTenantId} AND claim_id={claim}",ct);
            for (var i =0;i <next.Count;i++)
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_rma_formal_v1.return_claim_segments(tenant_id,claim_id,ordinal,coordinate_key,range_from,range_to,state) VALUES({db.CurrentTenantId},{claim},{i},{key},{next[i].Range.From},{next[i].Range.To},{next[i].State})",ct);
            var old =await db.Database.SqlQuery<EntitlementHeadRow>($"SELECT revision AS \"Revision\",source_signature AS \"Signature\",reference AS \"Reference\" FROM cp6_rma_formal_v1.entitlement_heads WHERE tenant_id={db.CurrentTenantId} AND coordinate_key={key}").SingleAsync(ct);
            await EntitlementHead(key,old.Signature,true,ct);
        }
        var head =await PhysicalHead(c.ApplicationId,true,c.Id,ct);
        var value =new { c.Id,c.Target.Axis,OriginalEffect =c.Target.Reference,c.Ranges,preparation.SourceClosureRef,StockReceipt =stock.Result!.Receipt,CurrentRmaHead =head,Actor =access.ActorId };
        var reference =RmaInboundAdapterV1.Ref("Main.Wms.RmaCorrectionSuccessor.v1",c.Id.ToString(),value);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_return_correction_v1.successors(tenant_id,proposal_id,body) VALUES({db.CurrentTenantId},{c.Id},{Encode(new { Reference =reference,Value =value })})",ct);
        return reference;
    }
}
