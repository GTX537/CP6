using System.Globalization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using static CP6.Core.Services.Mes.ReturnQualityPolicySourceV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Mes;

public sealed partial class ReturnQualityOwnerV1
{
    public Task<ReturnQualityCommandResultV1> DecideAsync(Guid app,Guid inspection,Guid command,ReturnQualityDecideV1 input,CancellationToken ct = default)
    {
        Reference(input.ExpectedHeadRef); Reference(input.ExpectedPolicyRef); Reason(input.Reason);
        if (input.Partitions == null || input.Partitions.Length !=4 || input.Partitions.Any(x => x == null)
            || !input.Partitions.Select(x => x.Quality).Order().SequenceEqual(States.Order())) throw Error("QUALITY_PARTITION_INVALID",400);
        var frozen = OperationsOwnerV1.Freeze(input);
        var ranges = frozen.Partitions.ToDictionary(x => x.Quality,x => Ranges.Input(x.Ranges,empty:true,limit:200));
        if (ranges.Values.Sum(x => x.Length) >400) throw Error("QUALITY_PARTITION_LIMIT",400);
        foreach (var partition in frozen.Partitions)
        {
            if (partition.BasisRefs == null || partition.BasisRefs.Length >100 || partition.BasisRefs.Distinct().Count() !=partition.BasisRefs.Length
                || partition.ReasonCodes == null || partition.ReasonCodes.Length >50 || partition.ReasonCodes.Any(x => string.IsNullOrWhiteSpace(x) || x.Length >200)
                || ranges[partition.Quality].Length >0 && partition.ReasonCodes.Length ==0) throw Error("QUALITY_BASIS_REQUIRED",400);
            foreach (var basis in partition.BasisRefs) Reference(basis);
        }
        return Command(app,command,OperationsOwnerV1.ReturnQuality,"decide",new { inspection,Input = frozen },async (a,qa,s,t) =>
        {
            var view = await View(qa,s,inspection,t); var source = Source(s,view.Inspection.Source.LineId);
            if (view.HeadRef !=frozen.ExpectedHeadRef) throw Error("CAS_CONFLICT",412);
            var policy = Policy(source,frozen.ExpectedPolicyRef);
            if (policy.RequireIndependentRegistrar && source.RegistrarId ==a.ActorId) throw Error("QUALITY_INDEPENDENT_DECIDER_REQUIRED",403);
            var all = ranges.Values.SelectMany(x => x).ToArray(); var effective = Ranges.Except(Ranges.Read(source.Receipt.ReceivedRanges),Ranges.Read(await new ReturnCorrectionStoreV1(db).Reversed("PHYSICAL_RECEIPT",source.LineId,t)));
            if (all.Sum(x => x.To-x.From) !=Ranges.Measure(all) || !Ranges.Subset(all,effective) || !Ranges.Subset(effective,all))
                throw Error("QUALITY_PARTITION_INVALID",422);
            foreach (var partition in frozen.Partitions)
                if (partition.BasisRefs.Except(policy.EvidenceRefs).Any() || ranges[partition.Quality].Length >0 && partition.Quality !="PENDING" && partition.BasisRefs.Length ==0)
                    throw Error("QUALITY_EVIDENCE_NOT_AVAILABLE",422);
            var parts = States.Select(state =>
            {
                var p = frozen.Partitions.Single(x => x.Quality ==state);
                return new ReturnQualityPartitionV1(state,Ranges.Wire(ranges[state]),new(Ranges.Text(Ranges.Measure(ranges[state])),source.Receipt.Quantity.Uom),p.BasisRefs,p.ReasonCodes);
            }).ToArray();
            await SaveDecision(view,a.ActorId,policy.Reference,parts,"ACTIVE",frozen.Reason,t);
            return await View(qa,s,inspection,t);
        },ct);
    }
    public Task<ReturnQualityCommandResultV1> WithdrawAsync(Guid app,Guid inspection,Guid command,ReturnQualityWithdrawV1 input,CancellationToken ct = default)
    {
        Reference(input.ExpectedHeadRef); Reason(input.Reason); var frozen = OperationsOwnerV1.Freeze(input);
        return Command(app,command,OperationsOwnerV1.ReturnQuality,"withdraw",new { inspection,Input = frozen },async (a,qa,s,t) =>
        {
            var view = await View(qa,s,inspection,t);
            if (view.HeadRef !=frozen.ExpectedHeadRef) throw Error("CAS_CONFLICT",412);
            if (view.Decision?.Result.DecisionState !="ACTIVE") throw Error("QUALITY_ACTIVE_DECISION_REQUIRED",409);
            await SaveDecision(view,a.ActorId,view.Decision.PolicyRef,view.Decision.Result.Partitions,"WITHDRAWN",frozen.Reason,t);
            return await View(qa,s,inspection,t);
        },ct);
    }
    private async Task SaveDecision(ReturnQualityViewV1 before,Guid actor,FulfilmentRefV1 policy,ReturnQualityPartitionV1[] partitions,
        string state,string reason,CancellationToken ct,FulfilmentRangeV1[]? sourceVoids =null)
    {
        var voids =sourceVoids ?? await new ReturnCorrectionStoreV1(db).Reversed("PHYSICAL_RECEIPT",before.Inspection.Source.LineId,ct);
        var previous = before.Decision?.Result; var revision = previous ==null ? 1 : checked(int.Parse(previous.DecisionVersion,CultureInfo.InvariantCulture)+1);
        var version = revision.ToString(CultureInfo.InvariantCulture); var at = DateTimeOffset.UtcNow; var id = before.Inspection.Id;
        var body = new { Inspection = before.Inspection.Reference,actor,policy,partitions,voids,state,reason,Previous = previous?.DecisionRef,revision,at };
        var reference = new FulfilmentRefV1("QA-NCR-01",id.ToString(),version,Digest(body));
        var head = new FulfilmentRefV1("QA-NCR-01.ReturnHead",id.ToString(),version,Digest(new { reference,state }));
        var result = new ReturnQualityDecisionV1("QA-NCR-01",reference,version,before.Inspection.Request.ScopeRef,
            before.Inspection.Request.PhysicalReceiptRefs,partitions,voids,head,Digest(body),state,previous?.DecisionRef,at,at);
        await store.Save(id,revision,new(result,actor,policy,reason),ct);
    }
    public Task<ReturnQualityCommandResultV1> ObserveAsync(Guid app,Guid inspection,Guid command,RmaObserveQualityV1 input,CancellationToken ct = default)
    {
        Reference(input.ExpectedDecisionRef); Reference(input.ExpectedRmaHeadRef); var frozen = OperationsOwnerV1.Freeze(input);
        return Command(app,command,OperationsOwnerV1.Rma,"quality-observe",new { inspection,Input = frozen },async (a,qa,s,t) =>
        {
            var view = await View(qa,s,inspection,t); var source = Source(s,view.Inspection.Source.LineId);
            if (view.Decision ==null) throw Error("QUALITY_NOT_DECIDED",409);
            if (view.Decision.Result.DecisionRef !=frozen.ExpectedDecisionRef || source.PhysicalHeadRef !=frozen.ExpectedRmaHeadRef) throw Error("CAS_CONFLICT",412);
            // Internal Owner original is read directly in the shared transaction; RMA never accepts a caller-signed decision.
            var result = view.Decision.Result; var at = DateTimeOffset.UtcNow;
            var reference = RmaInboundAdapterV1.Ref("Main.Wms.RmaQualityObservation.v1",Guid.NewGuid().ToString(),new { inspection,result.DecisionRef,Actor = a.ActorId,at });
            await store.Observe(new(reference,inspection,result,a.ActorId,at),t);
            return await View(qa,s,inspection,t);
        },ct);
    }
}
