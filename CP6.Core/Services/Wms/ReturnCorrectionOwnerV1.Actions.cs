using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using static CP6.Core.Services.Mes.ReturnQualityPolicySourceV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

public sealed partial class ReturnCorrectionOwnerV1
{
    private static ReturnCorrectionCandidateV1 Candidate(Guid id,Guid app,Guid actor,int version,ReturnCorrectionTargetV1 target,
        FulfilmentRangeV1[] ranges,FulfilmentRefV1 evidence,string reason,DateTimeOffset at)
    {
        if (!Ranges.Subset(Ranges.Read(ranges),Ranges.Read(target.Ranges))) throw Error("CORRECTION_RANGE_INVALID",422);
        var quantity =new FulfilmentQtyV1(Ranges.Text(Ranges.Measure(Ranges.Read(ranges))),target.Source.Receipt.Quantity.Uom);
        var value =new ReturnCorrectionCandidateV1(id,app,actor,version,target,ranges,quantity,reason,evidence,at,"");
        return value with { ContentDigest =Digest(value) };
    }
    public Task<ReturnCorrectionCommandResultV1> DraftAsync(Guid app,Guid command,ReturnCorrectionDraftV1 input,CancellationToken ct =default)
    {
        Reference(input.ExpectedTargetRef); Reference(input.ErrorEvidenceRef); Reason(input.Reason);
        var frozen =OperationsOwnerV1.Freeze(input); var ranges =Ranges.Wire(Ranges.Input(frozen.Ranges));
        return Command(app,command,"draft",frozen,async(a,s,t) =>
        {
            var target =(await targets.Read(s,t)).SingleOrDefault(x => x.Axis ==frozen.Axis && x.Id ==frozen.TargetId) ?? throw Error("CORRECTION_TARGET_NOT_FOUND",404);
            if (target.Reference !=frozen.ExpectedTargetRef) throw Error("CORRECTION_TARGET_CHANGED",412);
            await Scope(a,target,t);
            var c =Candidate(Guid.NewGuid(),app,a.ActorId,1,target,ranges,frozen.ErrorEvidenceRef,frozen.Reason,DateTimeOffset.UtcNow);
            await Available(c,t); await store.Insert(c,t); await store.Version(c,t); return await View(a,s,c.Id,t);
        },ct);
    }
    public Task<ReturnCorrectionCommandResultV1> EditAsync(Guid app,Guid id,Guid command,ReturnCorrectionEditV1 input,CancellationToken ct =default)
    {
        Reference(input.ExpectedHeadRef); Reference(input.ErrorEvidenceRef); Reason(input.Reason);
        var frozen =OperationsOwnerV1.Freeze(input); var ranges =Ranges.Wire(Ranges.Input(frozen.Ranges));
        return Command(app,command,"draft",new { id,Input =frozen },async(a,s,t) =>
        {
            var before =await View(a,s,id,t); Match(before,frozen.ExpectedHeadRef,"DRAFT"); var old =before.Candidate;
            if (Digest(old.Ranges) ==Digest(ranges) && old.ErrorEvidenceRef ==frozen.ErrorEvidenceRef && old.Reason ==frozen.Reason) return before;
            var c =Candidate(id,app,old.CreatorId,checked(old.Version+1),old.Target,ranges,frozen.ErrorEvidenceRef,frozen.Reason,old.RequestedAt);
            await Available(c,t); await store.Edit(c,t); await store.Version(c,t); return await View(a,s,id,t);
        },ct);
    }
    private async Task Dependencies(ReturnCorrectionCandidateV1 c,CancellationToken ct)
    {
        await Available(c,ct);
        var selected =Ranges.Read(c.Ranges);
        if (c.Target.Axis !="PHYSICAL_RECEIPT") return;
        foreach (var work in await new ReturnDispositionStoreV2(db).Rows().Where(x => x.Line ==c.Target.Source.LineId && x.State !="SEALED_NO_EFFECT").ToArrayAsync(ct))
        {
            var p =ReturnDispositionStoreV2.Decode<ReturnDispositionPreparationV2>(work.Body);
            var effective =Ranges.Except(Ranges.Read(p.Ranges),Ranges.Read(await store.Reversed("DISPOSITION",work.Id,ct)));
            if (Ranges.Intersects(selected,effective)) throw Error("CORRECTION_SUCCESSOR_DEPENDENCY",409);
        }
    }
    private async Task<ReturnCorrectionApprovalV1> Approval(ReturnCorrectionViewV1 view,CancellationToken ct)
    {
        var current =approvals.Read(db.CurrentTenantId,view.Candidate);
        if (current !=view.Approval) throw Error("CORRECTION_APPROVAL_CHANGED",412);
        await Dependencies(view.Candidate,ct); return current;
    }
    public Task<ReturnCorrectionCommandResultV1> ActAsync(Guid app,Guid id,Guid command,string action,ReturnCorrectionActionV1 input,CancellationToken ct =default)
    {
        if (action is not ("submit" or "bind-approval" or "prepare" or "apply" or "withdraw")) throw Error("VALIDATION_FAILED",400);
        Reference(input.ExpectedHeadRef); Reason(input.Reason); var frozen =OperationsOwnerV1.Freeze(input);
        return Command(app,command,action,new { id,Input =frozen },async(a,s,t) =>
        {
            var view =await View(a,s,id,t); var c =view.Candidate;
            if (view.HeadRef !=frozen.ExpectedHeadRef) throw Error("CAS_CONFLICT",412);
            switch (action)
            {
                case "submit":
                    Match(view,frozen.ExpectedHeadRef,"DRAFT"); await Dependencies(c,t);
                    await store.Submit(id,t); await store.Event(id,"APPROVAL_REQUESTED",c,t); break;
                case "bind-approval":
                    Match(view,frozen.ExpectedHeadRef,"SUBMITTED"); await Dependencies(c,t);
                    var approval =approvals.Read(db.CurrentTenantId,c); await store.Approve(id,approval,t); await store.Event(id,"APPROVAL_BOUND",approval,t); break;
                case "prepare":
                    Match(view,frozen.ExpectedHeadRef,"APPROVED"); await Approval(view,t);
                    if (view.Preparation !=null) throw Error("CORRECTION_ALREADY_PREPARED",409);
                    await store.Prepare(id,await posting.Prepare(a,c,view.Approval!,t),t); break;
                case "apply":
                    Match(view,frozen.ExpectedHeadRef,"APPROVED"); await Approval(view,t);
                    if (view.Preparation ==null) throw Error("CORRECTION_PREPARATION_REQUIRED",409);
                    await a.ActionAsync(c.Target.Resource,"correct",t);
                    await a.ActionAsync(OperationsOwnerV1.Rma,"correct-effect",t);
                    var effect =await posting.Apply(a,c,view.Preparation,command,t);
                    var successor =await rma.CorrectEffect(a,c,view.Preparation,effect.Stock,t);
                    var qualityHead =c.Target.Axis =="PHYSICAL_RECEIPT" ? await quality.VoidSource(a,c,t) : null;
                    var at =DateTimeOffset.UtcNow;
                    var reference =RmaInboundAdapterV1.Ref("Main.Wms.ReturnCorrection.Receipt.v1",id.ToString(),new { c.ContentDigest,view.Preparation,effect,successor,qualityHead,at });
                    await store.Apply(id,new(reference,id,c.Target.Reference,c.Ranges,c.Quantity,view.Preparation.SourceClosureRef,successor,qualityHead,effect.Quality,effect.Stock,at),t);
                    a.AtCommit(token => { if (approvals.Read(db.CurrentTenantId,c) !=view.Approval) throw Error("CORRECTION_APPROVAL_CHANGED",412); return Task.CompletedTask; });
                    break;
                default:
                    if (view.State is "POSTED" or "WITHDRAWN") throw Error("CORRECTION_STATE_INVALID",409);
                    var proof =RmaInboundAdapterV1.Ref("Main.Wms.ReturnCorrection.NoEffect.v1",id.ToString(),new { c.ContentDigest,view.Preparation,command,frozen.Reason,State ="WITHDRAWN" });
                    await store.Withdraw(id,proof,t); await store.Event(id,"WITHDRAWN",proof,t); break;
            }
            return await View(a,s,id,t);
        },ct);
    }
}
