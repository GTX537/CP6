using CP6.Core.Services.Integration;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
public sealed partial class EngineeringIterationService
{
    private async Task<RequirementImpactConsumerV1[]> ImpactCases(EngineeringIterationSnapshotV1 v,CancellationToken ct,bool activeOnly=false)
    {
        var store=new RequirementImpactStoreV1(db);await store.Ready(ct);var requests=await store.Requests(ct);
        return (await store.Cases(ct)).Where(c=>c.CapturedIteration.Id==v.Reference.Id)
            .Where(c=>!activeOnly || c.InputVersion==v.Body.Iteration!.InputVersion && requests.Single(r=>r.Candidate==c.Candidate).Action=="REQUEST")
            .Select(c=>requests.Single(r=>r.Candidate==c.Candidate).Action=="WITHDRAW"
                ?RequirementImpactStoreV1.Consumer(v,c) with{State="WITHDRAWN"}:RequirementImpactStoreV1.Consumer(v,c)).ToArray();
    }
    private async Task RequireImpactClear(EngineeringIterationSnapshotV1 v,CancellationToken ct)
    {
        if((await ImpactCases(v,ct,true)).Any(c=>c.State is not("KEEP_CURRENT" or "ADOPTED" or "INPUT_REPLACED")))
            throw Error("ITERATION_REQUIREMENT_IMPACT_PENDING",422);
    }
    private async Task<Guid[]> CaptureLateImpacts(EngineeringIterationSnapshotV1 v,Guid operation,Guid actor,CancellationToken ct)
    {
        var store=new RequirementImpactStoreV1(db);await store.Ready(ct);var adopted=v.Body.Iteration!.Input.Proof.RequirementSet;
        var created=new List<Guid>();
        foreach(var request in (await store.Requests(ct)).Where(r=>r.Action=="REQUEST" && r.Candidate.Id==adopted.Id && Revision(r.Candidate.Version)>Revision(adopted.Version)))
            created.Add(await store.AddCase(request,v,actor,operation,EngineeringIterationValuesV1.Owner,ct));
        return created.ToArray();
    }
    public Task<IterationImpactContextV1> ImpactAsync(Guid id,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("ITERATION_IDENTITY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",false,async(a,t)=>
        {
            var s=new EngineeringIterationStoreV1(db);await s.Fence(false,t);var v=await Observe(a,s,await s.Read(id,null,t),t);
            if(v.Body.Iteration==null)throw Error("ITERATION_REQUIRED",422);
            var cases=await ImpactCases(v,t);var source=await a.SourceAsync(OperationsOwnerV1.RequirementSet,t);
            foreach(var candidate in cases.Select(c=>c.Case!.Candidate).Distinct())await sets.ReviewSource(source,candidate,false,t);
            return new IterationImpactContextV1(v.Reference,cases);
        },ct);
    }
    public Task<EngineeringIterationResultV1> AssessImpactAsync(IterationImpactAssessmentCommandV1 input,CancellationToken ct=default)
    {
        if(input==null || input.OperationId==Guid.Empty || input.CaseId==Guid.Empty || input.ExpectedAssessmentId==Guid.Empty
            || input.Decision is not("KEEP_CURRENT" or "READY_TO_ADOPT" or "BLOCKED"))throw Error("ITERATION_IMPACT_COMMAND_INVALID",400);
        EngineeringIterationValuesV1.Text(input.Reason);
        if(input.Iteration==null || input.Iteration.Owner!=EngineeringIterationValuesV1.Owner || input.Iteration.Id==Guid.Empty || !ValidDigest(input.Iteration.Digest))
            throw Error("ITERATION_IMPACT_COMMAND_INVALID",400);
        Revision(input.Iteration.Version);
        var c=OperationsOwnerV1.Freeze(input);var bytes=MainOwnerOperationJournalV1.Encode(c);
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"impact-assess",true,async(a,t)=>
        {
            var lease=await scope.AcquireAsync(t);a.AtCommit(token=>scope.ValidateAsync(lease,token));
            var s=new EngineeringIterationStoreV1(db);await s.Fence(true,t);
            if(original!=null)return await Disclose(a,s,original,c.OperationId,bytes,t);
            a.RequireEngineeringIterationFields(true);var v=await Observe(a,s,await s.Read(c.Iteration.Id,null,t),t);
            if(v.Reference!=c.Iteration)throw Error("ITERATION_VERSION_CHANGED",412);
            var work=v.Body.Iteration??throw Error("ITERATION_REQUIRED",422);var project=await Project(a,s,work.ProjectId,t);
            if(a.ActorId!=project.Definition.OwnerId && a.ActorId!=work.Plan.OwnerId)throw Error("ITERATION_MANAGER_REQUIRED",403);
            var row=(await ImpactCases(v,t,true)).SingleOrDefault(r=>r.Case!.Id==c.CaseId)??throw Error("ITERATION_IMPACT_CASE_CHANGED",412);
            if(row.Case!.Assessment?.Id!=c.ExpectedAssessmentId)throw Error("ITERATION_IMPACT_ASSESSMENT_CHANGED",412);
            var source=await a.SourceAsync(OperationsOwnerV1.RequirementSet,t);await sets.ReviewSource(source,row.Case.Candidate,false,t);
            if(c.Decision=="READY_TO_ADOPT" && (project.State!="ACTIVE" || work.Stage is not("PLANNED" or "IN_DESIGN") || work.HoldState!="NORMAL"
                || work.CurrentReview!=null || work.Tasks.Any(x=>x.State is "SUBMITTED" or "DONE")))throw Error("ITERATION_INPUT_REPLACEMENT_REVIEW_REQUIRED",422);
            await new RequirementImpactStoreV1(db).AddAssessment(c,work,a.ActorId,t);
            var result=new EngineeringIterationResultV1(c.OperationId,v.Body.Kind,v.Reference.Id,v.Reference.Version,v.Reference.Digest,c.OperationId);
            await journal.Store(EngineeringIterationValuesV1.Owner,EngineeringIterationValuesV1.Contract,c.OperationId,a.ActorId,bytes,result,[v.Reference.Id],
                new{action="REQUIREMENT_IMPACT_ASSESS",c.CaseId,c.Decision,c.Reason,actor=a.ActorId,result},t);return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(EngineeringIterationValuesV1.Owner,c.OperationId,t);return original==null?"impact-assess":"query";});
    }
}
