using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Space;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringIterationService(CP6Context db,OperationsOwnerV1 owner,RequirementSetService sets,
    ControlledDocumentOwnerV1 documents,ScopeAuthorizationFenceV1 scope)
{
    private OperationsOwnerV1 Authority=>owner.Owns(db) && sets.Owns(db) && documents.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<EngineeringSiteChoiceV1[]> SitesAsync(string search="",CancellationToken ct=default)
    {
        if(search==null || search.Length>100)throw Error("ITERATION_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",false,async(a,t)=>
        {
            a.RequireEngineeringIterationFields(false);var source=await a.SourceAsync(OperationsOwnerV1.PhysicalSite,t);source.RequirePhysicalSiteSourceAccess(db);
            var ids=await db.Space_Sites.AsNoTracking().Where(x=>!x.IsDeleted && x.Enable && x.SiteCode.Contains(search)).OrderBy(x=>x.SiteCode).ThenBy(x=>x.Id).Take(50).Select(x=>x.Id).ToArrayAsync(t);
            var result=new List<EngineeringSiteChoiceV1>();foreach(var id in ids)result.Add(await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,source,id,t));return result.ToArray();
        },ct);
    }
    public Task<RequirementSetScopeV1> ScopeAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",false,
        async(a,t)=>{await scope.Root(t);if(sets.EvidenceMode is not("REAL" or "DEMO"))throw Error("ITERATION_ENVIRONMENT_UNAVAILABLE",503);return new RequirementSetScopeV1(scope.EnvironmentId,db.CurrentTenantId,a.ActorId,sets.EvidenceMode);},ct,readOnlyAuthorityFence:true);
    public Task<EngineeringRevisionCreationContextV1> CreationContextAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"add",false,
        async(a,t)=>{await new EngineeringIterationStoreV1(db).Fence(false,t);a.RequireEngineeringIterationFields(true);return await a.EngineeringIterationCreationContext(t);},ct,readOnlyAuthorityFence:true);
    public Task<IterationMemberChoiceV1[]> MembersAsync(string? search=null,CancellationToken ct=default)
    {
        if(search?.Length>256)throw Error("ITERATION_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",false,async(a,t)=>
        {a.RequireEngineeringIterationFields(false);return await db.Sys_Users.AsNoTracking().Where(u=>u.Enable && (search==null || u.UserName.Contains(search)))
            .OrderBy(u=>u.UserName).ThenBy(u=>u.Id).Take(50).Select(u=>new IterationMemberChoiceV1(u.Id,u.UserName)).ToArrayAsync(t);},ct);
    }
    public Task<EngineeringIterationSnapshotV1[]> ListAsync(string? kind=null,Guid? projectId=null,string? search=null,int page=1,CancellationToken ct=default)
    {
        if(kind!=null && kind is not("PROJECT" or "ITERATION") || search?.Length>256 || page is <1 or >100000)throw Error("ITERATION_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",false,async(a,t)=>
        {
            var s=new EngineeringIterationStoreV1(db);await s.Fence(false,t);var rows=a.EngineeringIterations();
            if(kind!=null)rows=rows.Where(x=>x.Kind==kind);if(projectId!=null)rows=rows.Where(x=>x.ProjectId==projectId);
            if(!string.IsNullOrEmpty(search))rows=rows.Where(x=>x.Name.Contains(search));
            var ids=await rows.OrderBy(x=>x.Name).ThenBy(x=>x.Id).Skip((page-1)*20).Take(20).Select(x=>x.Id).ToArrayAsync(t);
            var result=new List<EngineeringIterationSnapshotV1>();foreach(var id in ids)result.Add(await Observe(a,s,await s.Read(id,null,t),t));return result.ToArray();
        },ct);
    }
    public Task<EngineeringIterationSnapshotV1> ReadAsync(Guid id,string? version=null,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("ITERATION_IDENTITY_INVALID",400);var v=version==null?(long?)null:Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",false,async(a,t)=>
        {var s=new EngineeringIterationStoreV1(db);await s.Fence(false,t);await a.RequireEngineeringIteration(id,t);return await Observe(a,s,await s.Read(id,v,t),t);},ct);
    }
    private async Task<IterationProjectV1> Project(OperationsAccessV1 a,EngineeringIterationStoreV1 s,Guid id,CancellationToken ct)
    {
        await a.RequireEngineeringIteration(id,ct);a.AtCommit(t=>a.RequireEngineeringIteration(id,t));
        var value=await s.Read(id,null,ct);return value.Body.Project??throw Error("ITERATION_PROJECT_REQUIRED",422);
    }
    private async Task Staff(Guid[] ids,CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"Sys_Users\" IN SHARE MODE",ct);
        var selected=ids.Distinct().ToArray();
        if(await db.Sys_Users.AsNoTracking().CountAsync(u=>u.Enable && selected.Contains(u.Id),ct)!=selected.Length)throw Error("ITERATION_MEMBER_UNAVAILABLE",422);
    }
    private async Task<string[]> ProjectSources(OperationsAccessV1 a,IterationProjectInputV1 p,bool hold,CancellationToken ct)
    {
        var missing=new List<string>();
        if(p.Site==null)missing.Add("SITE_REQUIRED");
        else
        {
            var source=await a.SourceAsync(OperationsOwnerV1.PhysicalSite,ct);
            var site=await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,source,p.Site.Id,ct);
            if(site.Reference!=p.Site || !site.Enabled)missing.Add("SITE_SOURCE_CHANGED");
        }
        if(p.Product!=null)
        {
            var source=await a.SourceAsync(OperationsOwnerV1.EngineeringProduct,ct);await source.RequireEngineeringProduct(p.Product.Id,ct);
            source.AtCommit(t=>source.RequireEngineeringProduct(p.Product.Id,t));var products=new EngineeringProductStoreV1(db);await products.Fence(false,ct);
            var product=await products.Read(p.Product.Id,Revision(p.Product.Version),ct);
            if(product.Reference!=p.Product)throw Error("ITERATION_PRODUCT_REFERENCE_CHANGED",412);
            if(!product.IsCurrent || product.Product.Enabled!=true)missing.Add("PRODUCT_SOURCE_CHANGED");
        }
        if(string.IsNullOrWhiteSpace(p.Objective))missing.Add("OBJECTIVE_REQUIRED");
        if(p.StartDate==null || p.EndDate==null)missing.Add("PROJECT_DATES_REQUIRED");
        if(p.Milestones.Length==0)missing.Add("MILESTONE_CRITERION_REQUIRED");
        if(p.Milestones.Any(m=>p.StartDate!=null && m.DueDate<p.StartDate || p.EndDate!=null && m.DueDate>p.EndDate))missing.Add("MILESTONE_OUTSIDE_PROJECT_DATES");
        if(hold && missing.Count>0)throw Error("ITERATION_PROJECT_NOT_READY",422);return missing.ToArray();
    }
    private async Task<string> Inputs(OperationsAccessV1 a,IterationInputCaptureV1 capture,bool hold,CancellationToken ct)
    {
        if(capture.Proof.EnvironmentId!=scope.EnvironmentId || capture.Proof.TenantId!=db.CurrentTenantId || capture.Proof.EvidenceMode!=sets.EvidenceMode)
            throw Error("ITERATION_INPUT_SCOPE_CHANGED",503);
        try
        {
            var proof=await sets.Source(a,capture.Proof.RequirementSet,hold,ct);
            var changed=proof.Availability!="AVAILABLE" || proof.SubmissionId!=capture.Proof.SubmissionId || proof.DecisionId!=capture.Proof.DecisionId || proof.ApprovalId!=capture.Proof.ApprovalId;
            if(changed && hold)throw Error("ITERATION_INPUT_CHANGED",412);return changed?"CHANGED":"UNCHANGED";
        }
        catch(OrderPriceCorrectionException e) when(!hold && e.Status is 412 or 422 or 503)
        {return e.Status==503?"UNKNOWN":"CHANGED";}
    }
    private async Task<IterationInputCaptureV1> Capture(OperationsAccessV1 a,EngineeringSourceRefV1 reference,CancellationToken ct)
    {
        var proof=await sets.HoldApproved(a,reference,ct);
        if(proof.EnvironmentId!=scope.EnvironmentId || proof.TenantId!=db.CurrentTenantId || proof.EvidenceMode!=sets.EvidenceMode)throw Error("ITERATION_INPUT_SCOPE_CHANGED",503);
        var access=await a.SourceAsync(OperationsOwnerV1.RequirementSet,ct);var exact=await sets.ReviewSource(access,reference,true,ct);
        return new(proof,exact.OriginalJson,DateTimeOffset.UtcNow,a.ActorId);
    }
    private async Task Deliverables(OperationsAccessV1 a,EngineeringSourceRefV1[] refs,EngineeringProductRefV1? product,bool hold,CancellationToken ct)
    {
        if(refs.Length==0)throw Error("ITERATION_DELIVERABLE_OR_APPROVED_EXEMPTION_REQUIRED",422);
        if(refs.Length>128 || refs.Distinct().Count()!=refs.Length)throw Error("ITERATION_DELIVERABLE_INVALID",400);
        foreach(var reference in refs)
        {
            ControlledDocumentValuesV1.Ref(reference);var access=await a.SourceAsync(OperationsOwnerV1.ControlledDocument,ct);
            var document=await documents.ReviewSource(access,reference,hold,ct);
            if(product!=null && document.Body.ProductRef!=product)throw Error("ITERATION_DELIVERABLE_PRODUCT_CHANGED",412);
            // The exact document is a deliverable, not a PASS claim or an implicit professional approval.
            if(hold && (document.Body.Contents.Length==0 || document.ContentStatus?.Any(x=>x.Availability!="AVAILABLE")!=false))throw Error("ITERATION_DELIVERABLE_UNAVAILABLE",422);
        }
    }
    private async Task<EngineeringIterationSnapshotV1> Observe(OperationsAccessV1 a,EngineeringIterationStoreV1 s,EngineeringIterationSnapshotV1 v,CancellationToken ct)
    {
        await a.RequireEngineeringIteration(v.Reference.Id,ct);a.AtCommit(t=>a.RequireEngineeringIteration(v.Reference.Id,t));
        var p=v.Body.Project??await Project(a,s,v.Body.Iteration!.ProjectId,ct);
        var missing=(await ProjectSources(a,p.Definition,false,ct)).ToList();var drift="NOT_APPLICABLE";
        if(v.Body.Iteration is {} i)
        {
            drift=await Inputs(a,i.Input,false,ct);if(drift!="UNCHANGED")missing.Add("INPUT_SOURCE_"+drift);
            var captured=await CapturedProject(s,i,ct);
            missing.AddRange(await ProjectSources(a,captured,false,ct));
            if(captured.Product!=p.Definition.Product || captured.Site!=p.Definition.Site){if(drift!="UNKNOWN")drift="CHANGED";missing.Add("PROJECT_INPUT_CHANGED");}
            if(drift!="UNKNOWN" && missing.Any(x=>x is "SITE_SOURCE_CHANGED" or "PRODUCT_SOURCE_CHANGED"))drift="CHANGED";
            foreach(var task in i.Tasks.Where(x=>x.Deliverables.Length>0))await Deliverables(a,task.Deliverables,captured.Product,false,ct);
        }
        return v with{Missing=missing.Distinct().ToArray(),SourceDrift=drift,Progress=v.Body.Iteration is {} work?EngineeringIterationValuesV1.Progress(work.Tasks):null};
    }
    public Task<EngineeringIterationResultV1> CommandAsync(EngineeringIterationCommandV1 input,CancellationToken ct=default)
    {
        if(input==null || input.OperationId==Guid.Empty || input.ObjectId==Guid.Empty)throw Error("ITERATION_COMMAND_INVALID",400);
        var c=OperationsOwnerV1.Freeze(input);ValidateCommand(c);var bytes=MainOwnerOperationJournalV1.Encode(c);
        if(bytes.Length>2097152)throw Error("ITERATION_SIZE_LIMIT",400);
        var create=c.Action is "CREATE_PROJECT" or "CREATE_ITERATION";
        var action=create?"add":c.Action switch{"START"=>"start","HOLD"=>"hold","RESUME"=>"resume","SUBMIT_TASK"=>"task-submit","CONFIRM_TASK" or "RETURN_TASK"=>"task-confirm",_=>"edit"};
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,action,true,async(a,t)=>
        {
            var lease=await scope.AcquireAsync(t);
            if(!ReferenceEquals(lease.Database,db) || lease.Transaction!=db.Database.CurrentTransaction?.TransactionId)throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
            a.AtCommit(token=>scope.ValidateAsync(lease,token));
            var s=new EngineeringIterationStoreV1(db);await s.Fence(true,t);
            if(original!=null)return await Disclose(a,s,original,c.OperationId,bytes,t);
            a.RequireEngineeringIterationFields(true);OperationsAccessV1.NewEngineeringIterationBinding? ticket=null;
            EngineeringIterationSnapshotV1? old=null;
            if(c.Action=="CREATE_PROJECT")ticket=await a.BeginEngineeringIterationCreation(c.ObjectId,c.ExpectedAuthorityVersion,c.DepartmentId,t);
            else if(!create)
            {
                await a.RequireEngineeringIteration(c.ObjectId,t);old=await s.Read(c.ObjectId,null,t);
                if(old.Reference.Version!=c.ExpectedVersion || old.Etag!=c.ExpectedEtag)throw Error("ITERATION_VERSION_CHANGED",412);
                await Observe(a,s,old,t);
            }
            var body=await Change(a,s,c,old,t);
            var saved=await s.Append(new(c.OperationId,c.ObjectId,create?"CREATE":"REVISE",c.ExpectedVersion,c.ExpectedEtag,c.Reason,body),a.ActorId,t);
            if(ticket!=null)await a.CompleteEngineeringIterationCreation(ticket,t);
            await Observe(a,s,saved,t);
            var result=new EngineeringIterationResultV1(c.OperationId,saved.Body.Kind,c.ObjectId,saved.Reference.Version,saved.Reference.Digest);
            await journal.Store(EngineeringIterationValuesV1.Owner,EngineeringIterationValuesV1.Contract,c.OperationId,a.ActorId,bytes,result,[c.ObjectId],new{c.Action,c.Reason,actor=a.ActorId,result},t);return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(EngineeringIterationValuesV1.Owner,c.OperationId,t);return original==null?action:"query";});
    }
    private async Task<EngineeringIterationResultV1> Disclose(OperationsAccessV1 a,EngineeringIterationStoreV1 s,MainOwnerOperationJournalV1.Original o,Guid id,byte[]? bytes,CancellationToken ct)
    {
        if(o.Actor!=a.ActorId || o.Contract!=EngineeringIterationValuesV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(o.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null && !MainOwnerOperationJournalV1.Matches(o,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<EngineeringIterationResultV1>(o.Result);
        if(o.State!="COMMITTED" || result==null || result.OperationId!=id || !o.Objects.SequenceEqual(new[]{result.ObjectId}))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        var value=await Observe(a,s,await s.Read(result.ObjectId,Revision(result.Version),ct),ct);
        if(value.Reference.Digest!=result.Digest || value.Body.Kind!=result.Kind)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);return result;
    }
    public Task<EngineeringIterationOperationV1> OperationAsync(Guid id,bool close,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",close,async(a,t)=>
        {
            a.RequireEngineeringIterationFields(false);var s=new EngineeringIterationStoreV1(db);await s.Fence(false,t);
            var j=new MainOwnerOperationJournalV1(db);var o=await j.Read(EngineeringIterationValuesV1.Owner,id,t);
            if(o!=null)
            {
                if(o.Actor!=a.ActorId || o.Contract!=EngineeringIterationValuesV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                return o.State=="CLOSED"?new(id,"CLOSED"):new EngineeringIterationOperationV1(id,"COMMITTED",await Disclose(a,s,o,id,null,t));
            }
            if(!close)return new EngineeringIterationOperationV1(id,"UNKNOWN");
            var result=new EngineeringIterationOperationV1(id,"CLOSED");await j.Store(EngineeringIterationValuesV1.Owner,EngineeringIterationValuesV1.Contract,id,a.ActorId,null,result,[],new{action="close-uncommitted-iteration",actor=a.ActorId},t);
            var closed=await j.Read(EngineeringIterationValuesV1.Owner,id,t);
            if(closed==null || closed.State!="CLOSED" || closed.Actor!=a.ActorId || closed.Contract!=EngineeringIterationValuesV1.Contract)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
