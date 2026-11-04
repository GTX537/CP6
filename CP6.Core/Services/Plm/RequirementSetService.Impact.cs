using System.Text.Json;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
public sealed partial class RequirementSetService
{
    public Task<RequirementImpactContextV1> ImpactAsync(Guid id,string version,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("REQUIREMENT_SET_IDENTITY_INVALID",400);var revision=Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.RequirementSet,"query",false,async(a,t)=>
        {
            var s=new RequirementSetStoreV1(db);await s.Fence(false,t);
            var candidate=await Observe(a,s,await s.Read(id,revision,t),t);
            return (await ImpactContext(a,candidate.Reference,true,t)) with{CandidateState=candidate.State,IsCurrent=candidate.IsCurrent};
        },ct);
    }
    private async Task<EngineeringIterationSnapshotV1[]> ImpactAdoptions(EngineeringSourceRefV1 candidate,CancellationToken ct)
    {
        var iterations=new EngineeringIterationStoreV1(db);await iterations.Fence(false,ct);
        var ids=await EngineeringIterationStoreV1.Heads(db).Where(h=>h.Kind=="ITERATION").OrderBy(h=>h.Id).Select(h=>h.Id).ToArrayAsync(ct);
        var result=new List<EngineeringIterationSnapshotV1>();
        foreach(var id in ids)
        {
            var v=await iterations.Read(id,null,ct);var adopted=v.Body.Iteration!.Input.Proof.RequirementSet;
            if(adopted.Id==candidate.Id && Revision(adopted.Version)<Revision(candidate.Version))result.Add(v);
        }
        return result.ToArray();
    }
    private async Task<RequirementImpactContextV1> ImpactContext(OperationsAccessV1 a,EngineeringSourceRefV1 candidate,bool disclose,CancellationToken ct)
    {
        var store=new RequirementImpactStoreV1(db);await store.Ready(ct);
        var request=(await store.Requests(ct)).SingleOrDefault(r=>r.Candidate==candidate);var cases=await store.Cases(ct);
        var consumers=new List<RequirementImpactConsumerV1>();OperationsAccessV1? access=null;
        foreach(var v in await ImpactAdoptions(candidate,ct))
        {
            if(disclose)
            {
                access??=await a.SourceAsync(OperationsOwnerV1.EngineeringIteration,ct);
                await access.RequireEngineeringIteration(v.Reference.Id,ct);access.AtCommit(t=>access.RequireEngineeringIteration(v.Reference.Id,t));
            }
            var c=cases.SingleOrDefault(c=>c.Candidate==candidate && c.CapturedIteration.Id==v.Reference.Id && c.InputVersion==v.Body.Iteration!.InputVersion);
            var row=RequirementImpactStoreV1.Consumer(v,c);
            if(request?.Action=="WITHDRAW")row=row with{State="WITHDRAWN"};
            consumers.Add(row);
        }
        var rows=consumers.ToArray();var etag=EngineeringProductStoreV1.Digest(EngineeringProductStoreV1.Encode(new{candidate,request,consumers=rows}));
        return new(candidate,"CURRENT_MAIN_ITERATION_ONLY",etag,request,rows);
    }
    // Current consumers are owned facts. Approval checks completeness without disclosing hidden consumer details.
    private async Task RequireImpactAssessed(OperationsAccessV1 a,EngineeringSourceRefV1 candidate,CancellationToken ct)
    {
        var context=await ImpactContext(a,candidate,false,ct);
        if(context.Request?.Action!="REQUEST" && await new RequirementSetReviewApplicationStoreV1(db,scope).HasApprovedPredecessor(candidate,ct))
            throw Error("REQUIREMENT_SET_IMPACT_REQUEST_REQUIRED",422);
        if(context.Consumers.Any(c=>c.State is not("KEEP_CURRENT" or "READY_TO_ADOPT")))
            throw Error("REQUIREMENT_SET_IMPACT_ASSESSMENT_REQUIRED",422);
    }
    public Task<RequirementSetResultV1> RequestImpactAsync(RequirementImpactRequestCommandV1 input,CancellationToken ct=default)
    {
        if(input==null || input.OperationId==Guid.Empty || input.Action is not("REQUEST" or "WITHDRAW") || !ValidDigest(input.ExpectedEtag))
            throw Error("REQUIREMENT_IMPACT_COMMAND_INVALID",400);
        RequirementSetValuesV1.Ref(input.Candidate);RequirementSetValuesV1.Text(input.Reason,true);
        var c=OperationsOwnerV1.Freeze(input);var bytes=MainOwnerOperationJournalV1.Encode(c);
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.RequirementSet,"impact-request",true,async(a,t)=>
        {
            var lease=await scope.AcquireAsync(t);a.AtCommit(token=>scope.ValidateAsync(lease,token));
            var s=new RequirementSetStoreV1(db);await s.Fence(true,t);
            if(original!=null)return await Disclose(a,s,original,c.OperationId,bytes,t);
            a.RequireRequirementSetFields(true);var candidate=await ReviewSource(a,c.Candidate,false,t);
            if(c.Action=="REQUEST" && !candidate.IsCurrent)throw Error("REQUIREMENT_SET_VERSION_CHANGED",412);
            var context=await ImpactContext(a,c.Candidate,true,t);
            if(context.Etag!=c.ExpectedEtag)throw Error("REQUIREMENT_IMPACT_COVERAGE_CHANGED",412);
            if(c.Action=="WITHDRAW" && (context.Request?.Action!="REQUEST" || candidate.State=="APPROVED"))
                throw Error("REQUIREMENT_IMPACT_WITHDRAWAL_FORBIDDEN",409);
            var store=new RequirementImpactStoreV1(db);await store.AddRequest(c,a.ActorId,context.Request?.Id,t);
            var request=(await store.Requests(t)).Single(r=>r.Candidate==c.Candidate);var created=new List<Guid>();
            if(c.Action=="REQUEST")
            {
                var iterations=new EngineeringIterationStoreV1(db);await iterations.Fence(false,t);
                foreach(var row in context.Consumers.Where(r=>r.Case==null))
                    created.Add(await store.AddCase(request,await iterations.Read(row.Iteration.Id,null,t),a.ActorId,c.OperationId,RequirementSetValuesV1.Owner,t));
            }
            var result=new RequirementSetResultV1(c.OperationId,candidate.Body.Kind,c.Candidate.Id,c.Candidate.Version,c.Candidate.Digest,new(c.OperationId,c.Action,created.ToArray()));
            await journal.Store(RequirementSetValuesV1.Owner,RequirementSetValuesV1.Contract,c.OperationId,a.ActorId,bytes,result,[c.Candidate.Id],
                new{action="REQUIREMENT_IMPACT_"+c.Action,c.Reason,actor=a.ActorId,result},t);return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(RequirementSetValuesV1.Owner,c.OperationId,t);return original==null?"impact-request":"query";});
    }
    public Task<RequirementSuccessorDifferenceV1> SuccessorDifferenceAsync(Guid id,string before,string after,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("REQUIREMENT_SET_IDENTITY_INVALID",400);var first=Revision(before);var last=Revision(after);
        if(first>=last)throw Error("REQUIREMENT_SUCCESSOR_ORDER_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.RequirementSet,"query",false,async(a,t)=>
        {
            var s=new RequirementSetStoreV1(db);await s.Fence(false,t);
            var old=await Observe(a,s,await s.Read(id,first,t),t);var next=await Observe(a,s,await s.Read(id,last,t),t);
            var changes=new List<RequirementSuccessorChangeV1>();
            void Add(Guid? member,string area,string key,object? left,object? right)
            {
                string? Encode(object? o)=>o==null?null:JsonSerializer.Serialize(o,EngineeringProductStoreV1.Json);
                var l=Encode(left);var r=Encode(right);if(l!=r)changes.Add(new(member,area,key,l==null?"ADDED":r==null?"REMOVED":"CHANGED",l,r));
            }
            Add(null,"SET","name",old.Body.Name,next.Body.Name);Add(null,"SET","scope",old.Body.Scope,next.Body.Scope);Add(null,"SET","completenessBasis",old.Body.CompletenessBasis,next.Body.CompletenessBasis);
            Add(null,"SET","conflicts",old.Body.Conflicts,next.Body.Conflicts);
            foreach(var member in old.Body.Members.Select(m=>m.Requirement.Id).Union(next.Body.Members.Select(m=>m.Requirement.Id)))
            {
                var l=old.MemberStatus!.SingleOrDefault(m=>m.Requirement.Reference.Id==member);var r=next.MemberStatus!.SingleOrDefault(m=>m.Requirement.Reference.Id==member);
                Add(member,"MEMBERSHIP","member",old.Body.Members.SingleOrDefault(m=>m.Requirement.Id==member),next.Body.Members.SingleOrDefault(m=>m.Requirement.Id==member));
                var ld=l?.Requirement.Body.Requirement;var rd=r?.Requirement.Body.Requirement;
                Add(member,"REQUIREMENT","name",l?.Requirement.Body.Name,r?.Requirement.Body.Name);
                Add(member,"REQUIREMENT","source",ld?.Source,rd?.Source);
                Add(member,"REQUIREMENT","interpretation",ld?.Interpretation,rd?.Interpretation);
                Add(member,"REQUIREMENT","necessity",ld?.Necessity,rd?.Necessity);
                Add(member,"REQUIREMENT","applicableObject",ld?.ApplicableObject,rd?.ApplicableObject);
                Add(member,"REQUIREMENT","questions",ld?.ClarificationQuestions,rd?.ClarificationQuestions);
                Add(member,"REQUIREMENT","relations",ld?.Relations,rd?.Relations);
                foreach(var key in (ld?.Criteria??[]).Select(c=>c.Id).Union((rd?.Criteria??[]).Select(c=>c.Id)))
                    Add(member,"CRITERION",key,ld?.Criteria.SingleOrDefault(c=>c.Id==key),rd?.Criteria.SingleOrDefault(c=>c.Id==key));
                var lc=l?.Source.Body.Source!.Clauses.Where(c=>ld!.ClauseIds.Contains(c.Id)).ToArray()??[];
                var rc=r?.Source.Body.Source!.Clauses.Where(c=>rd!.ClauseIds.Contains(c.Id)).ToArray()??[];
                foreach(var key in lc.Select(c=>c.Id).Union(rc.Select(c=>c.Id)))Add(member,"SOURCE_CLAUSE",key,lc.SingleOrDefault(c=>c.Id==key),rc.SingleOrDefault(c=>c.Id==key));
            }
            return new RequirementSuccessorDifferenceV1(old.Reference,next.Reference,changes.ToArray());
        },ct);
    }
    private async Task DiscloseImpact(OperationsAccessV1 a,RequirementImpactReceiptV1 receipt,CancellationToken ct)
    {
        if(receipt.CreatedCases.Length==0)return;
        var access=await a.SourceAsync(OperationsOwnerV1.EngineeringIteration,ct);
        foreach(var item in (await new RequirementImpactStoreV1(db).Cases(ct)).Where(c=>receipt.CreatedCases.Contains(c.Id)))
        {await access.RequireEngineeringIteration(item.CapturedIteration.Id,ct);access.AtCommit(t=>access.RequireEngineeringIteration(item.CapturedIteration.Id,t));}
    }
}
