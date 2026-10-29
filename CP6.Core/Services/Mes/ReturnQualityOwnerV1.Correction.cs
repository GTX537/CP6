using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using Microsoft.EntityFrameworkCore;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Mes;

public sealed partial class ReturnQualityOwnerV1
{
    internal async Task<FulfilmentRefV1?> VoidSource(OperationsAccessV1 parent,ReturnCorrectionCandidateV1 c,CancellationToken ct)
    {
        var qa =await parent.ActionAsync(OperationsOwnerV1.ReturnQuality,"source-void",ct);
        await ReturnCorrectionTargetsV1.Scope(qa,c.Target,ct);
        var head =await store.Heads().SingleOrDefaultAsync(x => x.Line ==c.Target.Source.LineId,ct);
        if (head ==null) return null; // Later decisions read the durable source reversal budget.
        var before =await store.View(head,null,ct);
        var voids =Ranges.Union(Ranges.Read(await new ReturnCorrectionStoreV1(db).Reversed("PHYSICAL_RECEIPT",c.Target.Source.LineId,ct)).Concat(Ranges.Read(c.Ranges)));
        var effective =Ranges.Except(Ranges.Read(c.Target.Source.Receipt.ReceivedRanges),voids);
        var previous =before.Decision?.Result;
        var parts =States.Select(state =>
        {
            var old =previous?.Partitions.Single(x => x.Quality ==state);
            var ranges =old ==null ? state =="PENDING" ? effective : [] : Ranges.Except(Ranges.Read(old.Ranges),voids);
            return new ReturnQualityPartitionV1(state,Ranges.Wire(ranges),new(Ranges.Text(Ranges.Measure(ranges)),c.Quantity.Uom),old?.BasisRefs ?? [],old?.ReasonCodes ?? []);
        }).ToArray();
        await SaveDecision(before,qa.ActorId,before.Decision?.PolicyRef ?? c.Target.Source.QualityPolicyRef,parts,previous?.DecisionState ?? "ACTIVE",c.Reason,ct,Ranges.Wire(voids));
        return (await store.View(await store.Heads().SingleAsync(x => x.Id ==head.Id,ct),null,ct)).HeadRef;
    }
}
