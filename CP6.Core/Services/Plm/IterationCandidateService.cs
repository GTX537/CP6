using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.IterationCandidateValuesV1;
namespace CP6.Core.Services.Plm;
public sealed class IterationCandidateService(CP6Context db,OperationsOwnerV1 owner,EngineeringIterationService iterations,
    EngineeringSourceCheckService checks,ScopeAuthorizationFenceV1 scope)
{
    private OperationsOwnerV1 Authority=>owner.Owns(db) && iterations.Owns(db) && checks.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private async Task<IterationCandidateSnapshotV1> Observe(OperationsAccessV1 a,IterationCandidateSnapshotV1 value,CancellationToken ct)
    {
        var iteration=await iterations.CandidateSource(a,value.Original.IterationId,false,ct);var d=value.Original.Definition;
        if(value.Original.EnvironmentId!=scope.EnvironmentId || value.Original.EvidenceMode!=iteration.Body.Iteration!.Input.Proof.EvidenceMode)throw Error("ITERATION_CANDIDATE_SCOPE_CHANGED",503);
        await iterations.CandidateHistory(a,d.Iteration,ct);
        var project=await iterations.CandidateProject(iteration.Body.Iteration!,ct);
        var exact=await checks.CandidateSource(a,d.SourceCheck,ct);
        if(exact.OriginalJson!=d.TechnicalSourceOriginal || exact.Original.Command.Revision!=d.EngineeringRevision
            || exact.Original.Command.Candidate!=d.EngineeringCandidate)throw Error("ITERATION_CANDIDATE_ORIGINAL_UNAVAILABLE",503);
        // Persisted diffs include old source values; their current permissions are independent of the new source.
        var dependencies=new List<IterationCandidateDefinitionV1>();var originals=new IterationCandidateStoreV1(db);await originals.Fence(false,ct);
        if(value.Original.PreviousVersion is {} previous)dependencies.Add((await originals.Read(value.Original.IterationId,value.Reference.Id,Revision(previous),ct)).Original.Definition);
        if(d.ParentCandidate is {} parent)dependencies.Add((await originals.Read(value.Original.IterationId,parent.Id,Revision(parent.Version),ct)).Original.Definition);
        foreach(var dependency in dependencies)
        {
            await iterations.CandidateHistory(a,dependency.Iteration,ct);
            if(dependency.SourceCheck!=d.SourceCheck)await checks.CandidateSource(a,dependency.SourceCheck,ct);
        }
        var findings=new List<string>(exact.Changes);
        if(iteration.Body.Iteration!.InputVersion!=d.InputVersion || iteration.Body.Iteration.Input.Proof.RequirementSet!=d.RequirementSet) findings.Add("ITERATION_INPUT_CHANGED");
        if(project.Product!=d.Product || project.Site!=d.Site)findings.Add("ITERATION_PROJECT_INPUT_CHANGED");
        if(iteration.SourceDrift!="UNCHANGED")findings.Add("ITERATION_INPUT_"+iteration.SourceDrift);
        if(exact.Original.State!="PASS")findings.Add("TECHNICAL_CHECK_"+exact.Original.State);
        if(string.IsNullOrWhiteSpace(d.ChangeSummary))findings.Add("CHANGE_SUMMARY_REQUIRED");
        var drift=iteration.SourceDrift=="UNKNOWN" || exact.Original.Items.Any(x=>x.State=="UNKNOWN")?"UNKNOWN"
            :exact.Freshness!="CURRENT" || iteration.SourceDrift!="UNCHANGED" || findings.Any(x=>x.EndsWith("CHANGED",StringComparison.Ordinal))?"CHANGED":"UNCHANGED";
        return value with{SourceDrift=drift,Findings=findings.Distinct(StringComparer.Ordinal).ToArray()};
    }
    public Task<IterationCandidateSnapshotV1[]> ListAsync(Guid iterationId,string? search=null,int page=1,CancellationToken ct=default)
    {
        if(iterationId==Guid.Empty || search?.Length>256 || page is <1 or >100000)throw Error("ITERATION_CANDIDATE_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",false,async(a,t)=>{
            await scope.Root(t);await iterations.CandidateSource(a,iterationId,false,t);
            var store=new IterationCandidateStoreV1(db);await store.Fence(false,t);var rows=store.Heads().Where(x=>x.IterationId==iterationId);
            if(!string.IsNullOrEmpty(search))rows=rows.Where(x=>x.Title.Contains(search));
            var ids=await rows.OrderBy(x=>x.Title).ThenBy(x=>x.Id).Skip((page-1)*20).Take(20).Select(x=>x.Id).ToArrayAsync(t);
            var result=new List<IterationCandidateSnapshotV1>();foreach(var id in ids)result.Add(await Observe(a,await store.Read(iterationId,id,null,t),t));return result.ToArray();
        },ct);
    }
    public Task<IterationCandidateSnapshotV1> ReadAsync(Guid iterationId,Guid id,string? version=null,CancellationToken ct=default)
    {
        if(iterationId==Guid.Empty || id==Guid.Empty)throw Error("ITERATION_CANDIDATE_QUERY_INVALID",400);var selected=version==null?(long?)null:Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",false,async(a,t)=>{
            await scope.Root(t);await iterations.CandidateSource(a,iterationId,false,t);var s=new IterationCandidateStoreV1(db);await s.Fence(false,t);
            return await Observe(a,await s.Read(iterationId,id,selected,t),t);
        },ct);
    }
    public Task<IterationCandidateDiffV1> CompareAsync(Guid iterationId,EngineeringSourceRefV1 before,EngineeringSourceRefV1 after,CancellationToken ct=default)
    {
        Reference(before,IterationCandidateValuesV1.Owner);Reference(after,IterationCandidateValuesV1.Owner);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",false,async(a,t)=>{
            await scope.Root(t);await iterations.CandidateSource(a,iterationId,false,t);var s=new IterationCandidateStoreV1(db);await s.Fence(false,t);
            var left=await Observe(a,await s.Read(iterationId,before.Id,Revision(before.Version),t),t);
            var right=await Observe(a,await s.Read(iterationId,after.Id,Revision(after.Version),t),t);
            if(left.Reference!=before || right.Reference!=after)throw Error("ITERATION_CANDIDATE_REFERENCE_CHANGED",412);
            return new IterationCandidateDiffV1(before,after,Differences(left.Original.Definition,right.Original.Definition));
        },ct);
    }
    public Task<IterationCandidateResultV1> CommandAsync(IterationCandidateCommandV1 input,CancellationToken ct=default)
    {
        Command(input);var c=OperationsOwnerV1.Freeze(input);var bytes=MainOwnerOperationJournalV1.Encode(c);
        if(bytes.Length>2097152)throw Error("ITERATION_CANDIDATE_SIZE_LIMIT",400);
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"candidate-write",true,async(a,t)=>{
            var lease=await scope.AcquireAsync(t);
            if(!ReferenceEquals(lease.Database,db) || lease.Transaction!=db.Database.CurrentTransaction?.TransactionId)throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
            a.AtCommit(token=>scope.ValidateAsync(lease,token));
            // All candidate writes take the parent fence first, so stage/input changes cannot race adoption.
            var iteration=await iterations.CandidateSource(a,c.IterationId,original==null,t);
            var s=new IterationCandidateStoreV1(db);await s.Fence(true,t);
            if(original!=null)return await Disclose(a,s,original,c.OperationId,bytes,t);
            if(iteration.Reference!=c.ExpectedIteration || iteration.Etag!=c.ExpectedIterationEtag)throw Error("ITERATION_VERSION_CHANGED",412);
            var work=iteration.Body.Iteration!;var p=await iterations.CandidateProject(work,t);
            if(p.Product==null || p.Site==null)throw Error("ITERATION_CANDIDATE_PRODUCT_SITE_REQUIRED",422);
            IterationCandidateSnapshotV1? previous=null,parent=null;
            if(c.Action=="REVISE")
            {
                previous=await Observe(a,await s.Read(c.IterationId,c.CandidateId,null,t),t);
                if(previous.Reference.Version!=c.ExpectedVersion || previous.Original.Etag!=c.ExpectedEtag)throw Error("ITERATION_CANDIDATE_VERSION_CHANGED",412);
                if(previous.Original.Definition.ParentCandidate!=c.ParentCandidate)throw Error("ITERATION_CANDIDATE_PARENT_IMMUTABLE",409);
            }
            if(c.ParentCandidate!=null)
            {
                parent=await Observe(a,await s.Read(c.IterationId,c.ParentCandidate.Id,Revision(c.ParentCandidate.Version),t),t);
                if(parent.Reference!=c.ParentCandidate)throw Error("ITERATION_CANDIDATE_REFERENCE_CHANGED",412);
            }
            var source=await checks.CandidateSource(a,c.SourceCheck,t);
            if(c.SourceDecision=="ADOPT_CURRENT" && source.Freshness!="CURRENT")throw Error("ITERATION_CANDIDATE_SOURCE_CHANGED",412);
            if(source.Original.Input.ProductRef!=p.Product)throw Error("ITERATION_CANDIDATE_PRODUCT_MISMATCH",422);
            var definition=new IterationCandidateDefinitionV1(c.Title,c.ChangeSummary,c.Assumptions,c.ParentCandidate,work.Origin.ParentBaselineRef,
                iteration.Reference,work.InputVersion,work.Input.Proof.RequirementSet,p.Product,p.Site,c.SourceCheck,
                source.Original.Command.Revision,source.Original.Command.Candidate,source.Original,source.OriginalJson,c.SourceDecision,c.DriftReason);
            var evidenceMode=work.Input.Proof.EvidenceMode;
            var saved=await s.Save(new(scope.EnvironmentId,evidenceMode,db.CurrentTenantId,c.CandidateId,c.IterationId,
                previous==null?"1":EngineeringIterationValuesV1.Next(previous.Reference.Version),previous?.Original.CreatorId??a.ActorId,a.ActorId,Guid.NewGuid(),
                c.OperationId,c.Action,previous?.Reference.Version,definition,Differences(previous?.Original.Definition,definition),
                Differences(parent?.Original.Definition,definition),c.Reason,DateTimeOffset.UtcNow),t);
            await Observe(a,saved,t);
            var result=new IterationCandidateResultV1(c.OperationId,c.IterationId,c.CandidateId,saved.Reference.Version,saved.Reference.Digest);
            await journal.Store(IterationCandidateValuesV1.Owner,Contract,c.OperationId,a.ActorId,bytes,result,[c.IterationId,c.CandidateId],new{c.Action,c.Reason,a.ActorId,result},t);return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(IterationCandidateValuesV1.Owner,c.OperationId,t);return original==null?"candidate-write":"query";});
    }
    private async Task<IterationCandidateResultV1> Disclose(OperationsAccessV1 a,IterationCandidateStoreV1 s,MainOwnerOperationJournalV1.Original o,Guid id,byte[]? bytes,CancellationToken ct)
    {
        if(o.Actor!=a.ActorId || o.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(o.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null && !MainOwnerOperationJournalV1.Matches(o,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var r=JsonSerializer.Deserialize<IterationCandidateResultV1>(o.Result);
        if(o.State!="COMMITTED" || r==null || r.OperationId!=id || !o.Objects.SequenceEqual(new[]{r.IterationId,r.CandidateId}))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        var v=await Observe(a,await s.Read(r.IterationId,r.CandidateId,Revision(r.Version),ct),ct);
        if(v.Reference.Digest!=r.Digest)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);return r;
    }
    public Task<IterationCandidateOperationV1> OperationAsync(Guid id,bool close,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",close,async(a,t)=>{
            await scope.Root(t);a.RequireEngineeringIterationFields(false);var journal=new MainOwnerOperationJournalV1(db);var o=await journal.Read(IterationCandidateValuesV1.Owner,id,t);
            if(o!=null)
            {
                if(o.Actor!=a.ActorId || o.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(o.State=="CLOSED")return new IterationCandidateOperationV1(id,"CLOSED");
                if(o.Objects.Length!=2)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
                await iterations.CandidateSource(a,o.Objects[0],false,t);var s=new IterationCandidateStoreV1(db);await s.Fence(false,t);
                return new IterationCandidateOperationV1(id,"COMMITTED",await Disclose(a,s,o,id,null,t));
            }
            if(!close)return new IterationCandidateOperationV1(id,"UNKNOWN");
            var result=new IterationCandidateOperationV1(id,"CLOSED");await journal.Store(IterationCandidateValuesV1.Owner,Contract,id,a.ActorId,null,result,[],new{action="close-uncommitted-iteration-candidate",a.ActorId},t);
            var closed=await journal.Read(IterationCandidateValuesV1.Owner,id,t);
            if(closed==null || closed.State!="CLOSED" || closed.Actor!=a.ActorId)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
