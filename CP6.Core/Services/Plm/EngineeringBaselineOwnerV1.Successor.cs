using System.Text.Json;
using CP6.Core.Services.Integration;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringBaselineValuesV1;
namespace CP6.Core.Services.Plm;
public sealed partial class EngineeringBaselineOwnerV1
{
    public Task<EngineeringBaselineSuccessorResultV1> SuccessorAsync(EngineeringBaselineSuccessorV1 input,CancellationToken ct=default)
    {
        if(input==null || input.OperationId==Guid.Empty || input.ExpectedRevision==null || input.ExpectedEtag==Guid.Empty)throw Error("ENGINEERING_SUCCESSOR_COMMAND_INVALID",400);
        Ref(input.Baseline,EngineeringBaselineValuesV1.Owner);EngineeringRevisionValuesV1.Text(input.Reason);var c=OperationsOwnerV1.Freeze(input);var bytes=MainOwnerOperationJournalV1.Encode(c);
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringBaseline,"successor",true,async(a,t)=>{
            var s=await Store(a,true,t);
            if(original!=null)return await DiscloseSuccessor(a,s,original,c.OperationId,bytes,t);
            // Consume current read checks before the deliberate version advance; only the independent authority fences survive it.
            var baseline=await a.EngineeringFinalObservation((read,token)=>View(read,s,c.Baseline.Id,token),t);
            if(baseline.Reference!=c.Baseline || baseline.Original.Command.Revision!=c.ExpectedRevision || baseline.Original.Command.ExpectedEtag!=c.ExpectedEtag)
                throw Error("ENGINEERING_SUCCESSOR_BASELINE_CHANGED",412);
            var edit=await a.ActionAsync(OperationsOwnerV1.EngineeringRevision,"edit",t);await edit.RequireEngineeringRevision(c.ExpectedRevision.Id,t);
            a.AtCommit(token=>edit.RequireEngineeringRevision(c.ExpectedRevision.Id,token));
            var revisions=new EngineeringRevisionStoreV1(db);await revisions.Fence(true,t);var before=await revisions.Read(c.ExpectedRevision.Id,null,t);
            if(before.Reference!=c.ExpectedRevision || before.Etag!=c.ExpectedEtag || before.State!="FROZEN")throw Error("ENGINEERING_SUCCESSOR_ALREADY_ADVANCED",412);
            var next=await revisions.Append(before.Reference.Id,a.ActorId,before.Draft,c.Reason,Revision(before.Reference.Version),before.Etag,null,null,null,t,allowFrozenSuccessor:true);
            var result=new EngineeringBaselineSuccessorResultV1(c.OperationId,c.Baseline,next.Reference,next.CandidateRef,next.Etag);
            await s.SaveSuccessor(c,result,a.ActorId,t);
            await journal.Store(SuccessorJournal,SuccessorJournal,c.OperationId,a.ActorId,bytes,result,[c.Baseline.Id,next.Reference.Id],new{c.Reason,c.Baseline,next.Reference},t);return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(SuccessorJournal,c.OperationId,t);return original==null?"successor":"query";});
    }
    private async Task<EngineeringBaselineSuccessorResultV1> DiscloseSuccessor(OperationsAccessV1 a,EngineeringBaselineStoreV1 s,MainOwnerOperationJournalV1.Original o,Guid id,byte[]? input,CancellationToken ct)
    {
        if(o.Actor!=a.ActorId || o.Contract!=SuccessorJournal)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(o.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null && !MainOwnerOperationJournalV1.Matches(o,input))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var r=JsonSerializer.Deserialize<EngineeringBaselineSuccessorResultV1>(o.Result)??throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        if(r.OperationId!=id || !o.Objects.SequenceEqual(new[]{r.ParentBaseline.Id,r.Revision.Id}) || (await View(a,s,r.ParentBaseline.Id,ct)).Reference!=r.ParentBaseline)
            throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        var revision=new EngineeringRevisionStoreV1(db);await revision.Fence(false,ct);
        if((await revision.Read(r.Revision.Id,Revision(r.Revision.Version),ct)).Reference!=r.Revision)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);return r;
    }
    public Task<EngineeringBaselineSuccessorOperationV1> SuccessorOperationAsync(Guid id,bool close=false,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringBaseline,"query",close,async(a,t)=>{
        if(id==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);var s=await Store(a,close,t);var journal=new MainOwnerOperationJournalV1(db);var o=await journal.Read(SuccessorJournal,id,t);
        if(o!=null)
        {if(o.Actor!=a.ActorId || o.Contract!=SuccessorJournal)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
            return o.State=="CLOSED"?new(id,"CLOSED"):new EngineeringBaselineSuccessorOperationV1(id,"COMMITTED",await DiscloseSuccessor(a,s,o,id,null,t));}
        var result=new EngineeringBaselineSuccessorOperationV1(id,close?"CLOSED":"UNKNOWN");
        if(close)await journal.Store(SuccessorJournal,SuccessorJournal,id,a.ActorId,null,result,[],new{action="close-uncommitted-successor"},t);return result;
    },ct);
}
