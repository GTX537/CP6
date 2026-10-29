using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

public sealed partial class RmaFormalOwnerV1
{
    private RmaInboundAdapterV1 Inbound => inbound ?? throw Error("RMA_INBOUND_NOT_CONFIGURED",503);
    public Task<RmaPhysicalWorkViewV1> PreparePhysicalReturnAsync(Guid application, Guid work, Guid command, RmaPhysicalWorkCommandV1 input, CancellationToken ct = default)
        => PhysicalAction(application,work,command,input,"prepare",ct);
    public Task<RmaPhysicalWorkViewV1> CommitPhysicalReturnAsync(Guid application, Guid work, Guid command, RmaPhysicalWorkCommandV1 input, CancellationToken ct = default)
        => PhysicalAction(application,work,command,input,"post",ct);
    public Task<RmaPhysicalWorkViewV1> SealPhysicalReturnAsync(Guid application, Guid work, Guid command, RmaPhysicalWorkCommandV1 input, CancellationToken ct = default)
        => PhysicalAction(application,work,command,input,"seal",ct);
    private Task<RmaPhysicalWorkViewV1> PhysicalAction(Guid application, Guid work, Guid command, RmaPhysicalWorkCommandV1 input, string action, CancellationToken ct)
    {
        Reference(input.ExpectedWorkRef); Reference(input.ExpectedRmaHeadRef);
        if (work == Guid.Empty || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 500) throw Error("VALIDATION_FAILED",400);
        return PhysicalCommand(application,command,"physical-"+action,action,new { WorkId = work,Command = input },async (store,a,t) =>
        {
            var app = await Current(store,a,application,t); var row = await PhysicalWork(application,work,t);
            if (PhysicalWorkRef(row) != input.ExpectedWorkRef || await PhysicalHead(application,false,null,t) != input.ExpectedRmaHeadRef) throw Error("CAS_CONFLICT",412);
            if (row.State is "SUCCEEDED" or "SEALED_NO_EFFECT" || action == "prepare" && row.State != "REGISTERED" || action == "post" && row.State != "PREPARED")
                throw Error("RMA_PHYSICAL_STATE_CONFLICT",409);
            var original = PhysicalDecode<RmaPhysicalWorkOriginalV1>(row.Original);
            var next = row with { Revision = checked(row.Revision+1),UpdatedAt = DateTimeOffset.UtcNow };
            var preparations = row.Preparation == null ? [] : PhysicalDecode<RmaInboundPreparationV1[]>(row.Preparation);
            FulfilmentRefV1? head = null;
            if (action == "seal")
            {
                // The actual same-UoW fence plus durable tombstone rules out future effects; absence alone is never proof.
                var proofs = preparations.Length == 0 ? [] : await Inbound.Seal(preparations,command,input.Reason,t);
                next = next with { State = "SEALED_NO_EFFECT",Proof = Encode(RmaInboundAdapterV1.Ref("Main.Wms.RmaPhysicalNoEffect.v1",work.ToString(),new { Work = input.ExpectedWorkRef,command,input.Reason,proofs })) };
            }
            else
            {
                var auth = await ReceivingAuthorization(app,t);
                if (auth.DecisionRef != original.AuthorizationRef) throw Error("RETURN_AUTHORIZATION_CHANGED",409);
                foreach (var line in original.Lines)
                {
                    await PhysicalLocation(a,app.Scope,line.Location.LocationId,t);
                    var ranges = line.Input.Ranges.Select(x => new Ranges.Segment(StockRmaPostingV1.Number(x.From),StockRmaPostingV1.Number(x.To)));
                    if (!Ranges.Subset(ranges,await AvailableReceiptRanges(line.Share,work,t))) throw Error("RETURN_RANGE_NOT_AVAILABLE",409);
                }
                if (action == "prepare")
                {
                    next = next with { State = "PREPARED" };
                    next = next with { Preparation = Encode(await Inbound.Prepare(a,app.Scope,RmaIdentity(application),PhysicalWorkRef(next),original,next.Revision,input.Reason,t)) };
                }
                else
                {
                    head = await PhysicalHead(application,true,work,t);
                    var results = await Inbound.Commit(a,preparations,command,head,t);
                    foreach (var receipt in results.SelectMany(x => x.PhysicalReceipts))
                        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_rma_formal_v1.physical_receipts(tenant_id,work_id,line_id,body) VALUES({db.CurrentTenantId},{work},{Guid.Parse(receipt.ReceiptLineId)},{Encode(receipt)})",t);
                    await ConsumePhysicalClaims(original,t);
                    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_rma_formal_v1.inbound_outbox(tenant_id,event_id,work_id,body) VALUES({db.CurrentTenantId},{Guid.NewGuid()},{work},{Encode(results)})",t);
                    next = next with { State = "SUCCEEDED",Result = Encode(results) };
                    a.AtCommit(async token => { await ReceivingAuthorization(app,token); });
                }
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_rma_formal_v1.physical_work SET revision={next.Revision},state={next.State},updated_at={next.UpdatedAt},
                  preparation={next.Preparation},result={next.Result},no_effect_proof={next.Proof}
                WHERE tenant_id={db.CurrentTenantId} AND work_id={work}
                """,t);
            return await AppendPhysicalView(next,t,head);
        },ct);
    }
    private async Task ConsumePhysicalClaims(RmaPhysicalWorkOriginalV1 work, CancellationToken ct)
    {
        var keys = new HashSet<string>();
        foreach (var group in work.Lines.GroupBy(x => Guid.Parse(x.Share.RangeClaimRef.Id)))
        {
            var received = group.SelectMany(x => x.Input.Ranges).Select(x => new Ranges.Segment(StockRmaPostingV1.Number(x.From),StockRmaPostingV1.Number(x.To))).ToArray();
            var segments = await ClaimSegments().Where(x => x.ClaimId == group.Key).ToArrayAsync(ct);
            var key = segments[0].Key; keys.Add(key);
            var next = segments.Where(x => x.State != "ACTIVE_RESERVED").Select(x => (Range: new Ranges.Segment(x.From,x.To),x.State)).ToList();
            next.AddRange(Ranges.Except(segments.Where(x => x.State == "ACTIVE_RESERVED").Select(x => new Ranges.Segment(x.From,x.To)),received).Select(x => (x,"ACTIVE_RESERVED")));
            next.AddRange(Ranges.Union(received).Select(x => (x,"CONSUMED_BY_RECEIPT")));
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_rma_formal_v1.return_claim_segments WHERE tenant_id={db.CurrentTenantId} AND claim_id={group.Key}",ct);
            for (var i = 0; i < next.Count; i++)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_rma_formal_v1.return_claim_segments(tenant_id,claim_id,ordinal,coordinate_key,range_from,range_to,state)
                    VALUES({db.CurrentTenantId},{group.Key},{i},{key},{next[i].Range.From},{next[i].Range.To},{next[i].State})
                    """,ct);
        }
        foreach (var key in keys)
        {
            var row = await db.Database.SqlQuery<EntitlementHeadRow>($"SELECT revision AS \"Revision\",source_signature AS \"Signature\",reference AS \"Reference\" FROM cp6_rma_formal_v1.entitlement_heads WHERE tenant_id={db.CurrentTenantId} AND coordinate_key={key}").SingleAsync(ct);
            await EntitlementHead(key,row.Signature,true,ct);
        }
    }
}
