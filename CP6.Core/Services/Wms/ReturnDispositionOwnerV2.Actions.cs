using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using static CP6.Core.Services.Mes.ReturnQualityPolicySourceV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

public abstract partial class ReturnDispositionOwnerV2
{
    public Task<ReturnDispositionCommandResultV2> PrepareAsync(Guid app,Guid command,ReturnDispositionPrepareV2 input,CancellationToken ct =default)
    {
        Reference(input.ProposalRef); Reference(input.SelectionRef); Reference(input.OptionRef); Reason(input.Reason);
        var frozen =OperationsOwnerV1.Freeze(input); var ranges =Ranges.Input(frozen.Ranges);
        return Command(app,command,"prepare",frozen,async(a,s,t) =>
        {
            await a.ActionAsync(OperationsOwnerV1.Rma,"disposition-prepare",t);
            var choice =(await Choices(a,s,t)).SingleOrDefault(x => x.Proposal.ProposalRef ==frozen.ProposalRef && x.Proposal.SelectionRef ==frozen.SelectionRef)
                ?? throw Error("DISPOSITION_PROPOSAL_CHANGED",412);
            var option =choice.Options.SingleOrDefault(x => x.Reference ==frozen.OptionRef) ?? throw Error("DISPOSITION_OPTION_CHANGED",412);
            if (!Ranges.Subset(ranges,Ranges.Read(choice.Proposal.EligibleRanges))) throw Error("DISPOSITION_RANGE_UNAVAILABLE",409);
            var source =Source(s,choice.LineId); var id =Guid.NewGuid(); var at =DateTimeOffset.UtcNow;
            var quantity =new FulfilmentQtyV1(Ranges.Text(Ranges.Measure(ranges)),choice.Proposal.EligibleQuantity.Uom);
            var plan =await posting.Prepare(a,id,Definition,source.Source,source.Inspection!.Decision!.Result,option,Ranges.Wire(ranges),quantity,frozen.Reason,t);
            await store.Prepare(new(id,app,a.ActorId,at,Definition,Contract,choice.Proposal,option,source.Source,source.Inspection.Decision.Result,
                Ranges.Wire(ranges),quantity,plan,frozen.Reason),t);
            return await View(a,s,id,t);
        },ct);
    }
    public Task<ReturnDispositionCommandResultV2> CommitAsync(Guid app,Guid id,Guid command,ReturnDispositionActionV2 input,CancellationToken ct =default)
    {
        Reference(input.ExpectedWorkRef); Reason(input.Reason); var frozen =OperationsOwnerV1.Freeze(input);
        return Command(app,command,"post",new { id,Input =frozen },async(a,s,t) =>
        {
            await a.ActionAsync(OperationsOwnerV1.Rma,"disposition-apply",t);
            var before =await View(a,s,id,t); RequirePrepared(before,frozen.ExpectedWorkRef);
            if (!before.QualityCurrent) throw Error("DISPOSITION_QUALITY_CHANGED",412);
            var p =before.Preparation; var source =Source(s,p.Source.LineId);
            var option =policies.Options(db.CurrentTenantId,source.Source,Kind,p.Proposal.Quality).SingleOrDefault(x => x.Reference ==p.Option.Reference)
                ?? throw Error("DISPOSITION_OPTION_CHANGED",412);
            var result =await posting.Commit(a,p,command,t);
            var at =DateTimeOffset.UtcNow; var reference =RmaInboundAdapterV1.Ref(Definition.Owner,id.ToString(),new { id,p.ContractRef,p.Option.BusinessRef,p.Option.PartyRef,p.Ranges,p.Quantity,p.Decision.DecisionRef,result,Actor =a.ActorId,at });
            await store.Apply(id,new(reference,p.ContractRef,option.BusinessRef,option.PartyRef,id,Kind,p.Ranges,p.Quantity,p.Decision.DecisionRef,
                result.QualityRelease,result.StockResult,a.ActorId,at),t);
            return await View(a,s,id,t);
        },ct);
    }
    public Task<ReturnDispositionCommandResultV2> SealAsync(Guid app,Guid id,Guid command,ReturnDispositionActionV2 input,CancellationToken ct =default)
    {
        Reference(input.ExpectedWorkRef); Reason(input.Reason); var frozen =OperationsOwnerV1.Freeze(input);
        return Command(app,command,"seal",new { id,Input =frozen },async(a,s,t) =>
        {
            await a.ActionAsync(OperationsOwnerV1.Rma,"disposition-seal",t);
            var before =await View(a,s,id,t); RequirePrepared(before,frozen.ExpectedWorkRef);
            // The effect Owner and Stock participant share this transaction; a durable PREPARED has no independently dispatched side effect.
            var proof =RmaInboundAdapterV1.Ref(Definition.Owner+".NoEffect",id.ToString(),new { id,before.Preparation.Stock.IntentRef,command,frozen.Reason,State ="SEALED_NO_EFFECT" });
            await store.Seal(id,proof,t); return await View(a,s,id,t);
        },ct);
    }
    public Task<ReturnDispositionOutcomeV2> OutcomeAsync(Guid app,Guid id,CancellationToken ct =default) => Run(app,"query",false,async(a,s,t) =>
    {
        var view =await View(a,s,id,t);
        return new ReturnDispositionOutcomeV2(view.State =="APPLIED" ? "FOUND" : view.State =="PREPARED" ? "PENDING" : "SEALED_NO_EFFECT",
            view.Result,view.WorkRef,view.NoEffectProof,view.State =="SEALED_NO_EFFECT");
    },ct);
    private static void RequirePrepared(ReturnDispositionViewV2 view,FulfilmentRefV1 expected)
    {
        if (view.WorkRef !=expected) throw Error("CAS_CONFLICT",412);
        if (view.State !="PREPARED") throw Error("DISPOSITION_ALREADY_TERMINAL",409);
    }
}
