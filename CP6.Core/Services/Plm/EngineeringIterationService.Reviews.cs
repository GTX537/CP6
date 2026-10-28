using CP6.Core.Services.Integration;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
public sealed partial class EngineeringIterationService
{
    internal string EvidenceMode=>sets.EvidenceMode;
    internal async Task<EngineeringIterationSnapshotV1> ReviewSource(OperationsAccessV1 a,Guid id,bool hold,CancellationToken ct)
    {
        var s=new EngineeringIterationStoreV1(db);await s.Fence(false,ct);a.RequireEngineeringIterationFields(false);
        var v=await Observe(a,s,await s.Read(id,null,ct),ct);var work=v.Body.Iteration??throw Error("ITERATION_REQUIRED",422);
        if(hold)
        {
            var project=await Project(a,s,work.ProjectId,ct);
            if(project.State!="ACTIVE" || work.Stage is not("IN_DESIGN" or "IN_REVIEW" or "REVIEW_APPROVED") || work.HoldState!="NORMAL")throw Error("ITERATION_REVIEW_STAGE_CHANGED",409);
            await ProjectSources(a,project.Definition,true,ct);await Inputs(a,work.Input,true,ct);
            if(await ProjectDrift(s,work,project,ct))throw Error("ITERATION_PROJECT_INPUT_CHANGED",412);
            await Staff([work.Plan.OwnerId],ct);
        }
        return v;
    }
    internal async Task<EngineeringIterationSnapshotV1> StartReview(OperationsAccessV1 a,EngineeringIterationSnapshotV1 expected,
        Guid operation,IterationReviewRoundLinkV1 link,string reason,CancellationToken ct)
    {
        a.RequireEngineeringIterationFields(true);var s=new EngineeringIterationStoreV1(db);await s.Fence(true,ct);
        var value=await ReviewSource(a,expected.Reference.Id,true,ct);
        if(value.Reference!=expected.Reference || value.Etag!=expected.Etag)throw Error("ITERATION_VERSION_CHANGED",412);
        var work=value.Body.Iteration!;var project=await Project(a,s,work.ProjectId,ct);
        if(work.Stage!="IN_DESIGN" || work.CurrentReview!=null)throw Error("ITERATION_REVIEW_ALREADY_SUBMITTED",409);
        if(a.ActorId!=project.Definition.OwnerId && a.ActorId!=work.Plan.OwnerId)throw Error("ITERATION_MANAGER_REQUIRED",403);
        var saved=await s.Append(new(operation,value.Reference.Id,"REVISE",value.Reference.Version,value.Etag,reason,
            value.Body with{Iteration=work with{Stage="IN_REVIEW",CurrentReview=link}}),a.ActorId,ct);
        var result=new EngineeringIterationResultV1(operation,saved.Body.Kind,saved.Reference.Id,saved.Reference.Version,saved.Reference.Digest);
        await new MainOwnerOperationJournalV1(db).Store(EngineeringIterationValuesV1.Owner,EngineeringIterationValuesV1.Contract,operation,a.ActorId,
            MainOwnerOperationJournalV1.Encode(new{action="review-submit",expected.Reference,expected.Etag,link,reason}),result,[value.Reference.Id],new{action="review-submit",link,result,a.ActorId},ct);
        return await Observe(a,s,saved,ct);
    }
    internal async Task<Guid[]> ReviewMembers(OperationsAccessV1 a,EngineeringIterationSnapshotV1 iteration,CancellationToken ct)
    {
        var s=new EngineeringIterationStoreV1(db);await s.Fence(false,ct);
        return (await Project(a,s,iteration.Body.Iteration!.ProjectId,ct)).Definition.Members;
    }
    internal Task ReviewStaff(Guid[] ids,CancellationToken ct)=>Staff(ids,ct);
    internal async Task ReviewEvidence(OperationsAccessV1 a,EngineeringIterationSnapshotV1 iteration,EngineeringSourceRefV1[] evidence,bool hold,CancellationToken ct)
    {
        var s=new EngineeringIterationStoreV1(db);await s.Fence(false,ct);
        var project=await Project(a,s,iteration.Body.Iteration!.ProjectId,ct);
        if(evidence.Length>0)await Deliverables(a,evidence,project.Definition.Product,hold,ct);
    }
    internal async Task<EngineeringIterationSnapshotV1> ReviewTransition(OperationsAccessV1 a,EngineeringIterationSnapshotV1 expected,
        Guid submission,Guid operation,string action,string stage,bool clearRound,string reason,CancellationToken ct)
    {
        a.RequireEngineeringIterationFields(true);var s=new EngineeringIterationStoreV1(db);await s.Fence(true,ct);
        var value=await ReviewSource(a,expected.Reference.Id,false,ct);var work=value.Body.Iteration!;
        if(value.Reference!=expected.Reference || value.Etag!=expected.Etag || work.Stage!="IN_REVIEW" || work.CurrentReview?.SubmissionId!=submission)
            throw Error("ITERATION_REVIEW_CONTROL_CHANGED",409);
        if(stage is not("IN_DESIGN" or "REVIEW_APPROVED" or "AWAITING_EVALUATION"))throw Error("ITERATION_REVIEW_STAGE_INVALID",503);
        var saved=await s.Append(new(operation,value.Reference.Id,"REVISE",value.Reference.Version,value.Etag,reason,
            value.Body with{Iteration=work with{Stage=stage,CurrentReview=clearRound?null:work.CurrentReview}}),a.ActorId,ct);
        var result=new EngineeringIterationResultV1(operation,saved.Body.Kind,saved.Reference.Id,saved.Reference.Version,saved.Reference.Digest);
        await new MainOwnerOperationJournalV1(db).Store(EngineeringIterationValuesV1.Owner,EngineeringIterationValuesV1.Contract,operation,a.ActorId,
            MainOwnerOperationJournalV1.Encode(new{action,expected.Reference,expected.Etag,submission,stage,reason}),result,[value.Reference.Id],
            new{action,submission,stage,result,a.ActorId},ct);
        return await Observe(a,s,saved,ct);
    }
}
