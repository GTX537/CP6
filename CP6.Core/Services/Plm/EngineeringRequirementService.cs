using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Pub;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;
public sealed partial class EngineeringRequirementService(CP6Context db,OperationsOwnerV1 owner,
    FileContentOwnerV1? files = null, ControlledDocumentOwnerV1? documents = null, OrderSourceReader? sales = null,
    EngineeringIntakeOriginalsV1? intakeOriginals = null)
{
    private OperationsOwnerV1 Authority=>owner.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<EngineeringProductScopeV1> ScopeAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,"query",false,
        (a,_)=>Task.FromResult(new EngineeringProductScopeV1(db.CurrentTenantId,a.ActorId)),ct,readOnlyAuthorityFence:true);
    public Task<EngineeringRevisionCreationContextV1> CreationContextAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,"add",false,
        async(a,t)=>{await new EngineeringRequirementStoreV1(db).Fence(false,t);a.RequireRequirementFields(true);return await a.EngineeringRequirementCreationContext(t);},ct,readOnlyAuthorityFence:true);
    public Task<EngineeringRequirementSnapshotV1[]> ListAsync(string? kind=null,string? search=null,int page=1,CancellationToken ct=default)
        => List(kind, search, page, "query", ct);

    public Task<EngineeringRequirementSnapshotV1[]> ExportAsync(string? kind, string? search, int page, CancellationToken ct=default)
        => List(kind, search, page, "export", ct);

    private Task<EngineeringRequirementSnapshotV1[]> List(string? kind, string? search, int page, string action, CancellationToken ct)
    {
        if(kind!=null && kind is not("SOURCE" or "REQUIREMENT") || search?.Length>256 || page is <1 or >100000)throw Error("REQUIREMENT_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,action,false,async(a,t)=>
        {
            var s=new EngineeringRequirementStoreV1(db);await s.Fence(false,t);var rows=a.EngineeringRequirements();
            if (action=="export")
            {
                var query = await a.SourceAsync(OperationsOwnerV1.EngineeringRequirement, t);
                var queryIds = query.EngineeringRequirements().Select(row => row.Id);
                rows = rows.Where(row => queryIds.Contains(row.Id));
            }
            if(kind!=null)rows=rows.Where(x=>x.Kind==kind);if(!string.IsNullOrEmpty(search))rows=rows.Where(x=>x.Name.Contains(search));
            var ordered=rows.OrderBy(x=>x.Name).ThenBy(x=>x.Id).Select(x=>x.Id);
            var result=new List<EngineeringRequirementSnapshotV1>();
            var skip=(page-1)*20;
            const int batchSize=40;
            for (var offset=0; result.Count<20; offset+=batchSize)
            {
                var ids=await ordered.Skip(offset).Take(batchSize).ToArrayAsync(t);
                foreach(var id in ids)
                {
                    EngineeringRequirementSnapshotV1 value;
                    try { value=await Observe(a,s,await s.Read(id,null,t),t); }
                    catch(OrderPriceCorrectionException e) when (
                        e.Status is 403 or 404 or 409 or 412 ||
                        e.Message is "COMMERCIAL_HISTORY_UNPROVEN" or "UNPROVEN_G03" or "REQUIREMENT_CRM_ACT_AUTHORITY_UNPROVEN"
                            or "SALES_SOURCE_ORIGINAL_UNPROVEN" or "FILE_CONTENT_INTEGRITY_UNAVAILABLE"
                            or "ENGINEERING_INTAKE_SOURCE_UNPROVEN")
                    { continue; }
                    if (skip>0) { skip--; continue; }
                    result.Add(value);
                    if (result.Count==20) break;
                }
                if (ids.Length<batchSize) break;
            }
            return result.ToArray();
        },ct);
    }
    public Task<EngineeringRequirementSnapshotV1> ReadAsync(Guid id,string? version=null,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("REQUIREMENT_IDENTITY_INVALID",400);var v=version==null?(long?)null:Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,"query",false,async(a,t)=>
        {var s=new EngineeringRequirementStoreV1(db);await s.Fence(false,t);await a.RequireEngineeringRequirement(id,t);return await Observe(a,s,await s.Read(id,v,t),t);},ct);
    }
    // 所有披露路径均核对准确历史依赖及其当前对象、字段和来源授权。
    private async Task<EngineeringRequirementSnapshotV1> Observe(OperationsAccessV1 a,EngineeringRequirementStoreV1 s,EngineeringRequirementSnapshotV1 value,CancellationToken ct)
    {
        var seen=new HashSet<EngineeringSourceRefV1>();var missing=EngineeringRequirementValuesV1.Missing(value.Body);
        RequirementCoverageV1? coverage=null;
        async Task Visit(EngineeringRequirementSnapshotV1 v,int depth)
        {
            if(!seen.Add(v.Reference))return;if(depth>128 || seen.Count>4096)throw Error("REQUIREMENT_GRAPH_LIMIT",422);
            await a.RequireEngineeringRequirement(v.Reference.Id,ct);a.AtCommit(t=>a.RequireEngineeringRequirement(v.Reference.Id,t));
            await ObserveOrigin(a,v,ct);
            if(v.Body.Requirement is not {} r)return;
            foreach(var unit in r.Criteria.Select(x=>x.Evaluation?.Unit).OfType<UomProfileRefV1>().Distinct())
            {
                var ua=await a.SourceAsync(OperationsOwnerV1.UomProfile,ct);await ua.RequireUomProfile(unit.Id,ct);ua.AtCommit(t=>ua.RequireUomProfile(unit.Id,t));
                var us=new UomProfileStoreV1(s.Context);await us.Fence(false,ct);var uv=await us.Read(unit.Id,Revision(unit.Version),ct);
                if(uv.Reference!=unit)throw Error("REQUIREMENT_UNIT_REFERENCE_CHANGED",412);
                if(!uv.IsCurrent || uv.Profile.Enabled!=true)missing.Add("UNIT_SOURCE_CHANGED:"+unit.Id);
            }
            var source=await Exact(a,s,r.Source,"SOURCE",ct);
            await Visit(source,depth);
            if (r.SourceReviews is { Length: >0 } reviews)
            {
                missing.Add("REQUIREMENT_SOURCE_REEVALUATION_REQUIRED");
                var reviewSources = reviews.SelectMany(review => new[] { review.PreviousSource, review.IncomingSource }).Distinct();
                foreach (var reference in reviewSources.Where(reference => !seen.Contains(reference)))
                    await Visit(await Exact(a,s,reference,"SOURCE",ct),depth);
            }
            if(!source.IsCurrent)missing.Add("SOURCE_VERSION_CHANGED:"+v.Reference.Id);
            if(source.Body.Source!.Kind=="UNRESOLVED")missing.Add("SOURCE_UNRESOLVED:"+v.Reference.Id);
            if(source.Body.Source.Kind=="OWNER_ORIGINAL")
                foreach(var criterion in r.Criteria.Where(c=>c.SourceClauseIds is not { Length: >0 }))
                    missing.Add("CRITERION_SOURCE_BASIS_REQUIRED:"+criterion.Id);
            foreach (var question in r.Clarifications ?? [])
            {
                var questionSource=await Exact(a,s,question.Source,"SOURCE",ct);
                await Visit(questionSource,depth);
                if (question.Reply is { } reply) await ReadOrigin(a,reply,ct);
            }
            foreach(var link in r.Relations)
            {
                var target=await Exact(a,s,link.Target,"REQUIREMENT",ct);
                if(!target.IsCurrent)missing.Add("RELATION_VERSION_CHANGED:"+link.Id);
                await Visit(target,depth+1);
            }
            if (r.Decomposition is { } review)
            {
                var assessed=await ReadCoverage(a,s,r,review,target=>Visit(target,depth+1),ct);
                if (v.Reference==value.Reference) coverage=assessed;
                foreach (var clause in assessed.Clauses.Where(x=>x.State!="COVERED"))
                    missing.Add("PARENT_CLAUSE_COVERAGE_PENDING:"+clause.ClauseId);
            }
        }
        await Visit(value,0);return value with{Missing=missing.Distinct(StringComparer.Ordinal).ToArray(),Coverage=coverage};
    }
    private static async Task<EngineeringRequirementSnapshotV1> Exact(OperationsAccessV1 a,EngineeringRequirementStoreV1 s,EngineeringSourceRefV1 r,string kind,CancellationToken ct)
    {
        EngineeringRequirementValuesV1.Ref(r);await a.RequireEngineeringRequirement(r.Id,ct);a.AtCommit(t=>a.RequireEngineeringRequirement(r.Id,t));
        var value=await s.Read(r.Id,Revision(r.Version),ct);
        if(value.Reference!=r || value.Body.Kind!=kind)throw Error("REQUIREMENT_REFERENCE_CHANGED",412);return value;
    }
    private static async Task ValidateSources(OperationsAccessV1 a,EngineeringRequirementStoreV1 s,EngineeringRequirementCommandV1 c,
        EngineeringRequirementSnapshotV1? old,CancellationToken ct)
    {
        if(c.Body.Requirement is not {} r)return;
        var source=await Exact(a,s,r.Source,"SOURCE",ct);
        if(r.ClauseIds.Any(id=>!source.Body.Source!.Clauses.Any(x=>x.Id==id)))throw Error("REQUIREMENT_CLAUSE_NOT_FOUND",422);
        await ValidateClarifications(a,s,r,old?.Body.Requirement,ct);
        if (r.Decomposition is { } review && (review.Parent.Id!=c.ObjectId || old==null ||
            review.Parent!=old.Reference && review.Parent!=old.Body.Requirement?.Decomposition?.Parent))
            throw Error("REQUIREMENT_COVERAGE_PARENT_CHANGED",412);
        // Backwards exact-version edges must not prove the new version through an older version of its own logical identity.
        var seen=new HashSet<EngineeringSourceRefV1>();
        async Task Walk(EngineeringSourceRefV1 target,int depth)
        {
            if(target.Id==c.ObjectId)throw Error("REQUIREMENT_RELATION_CYCLE",422);
            if(!seen.Add(target))return;if(depth>128 || seen.Count>4096)throw Error("REQUIREMENT_GRAPH_LIMIT",422);
            var v=await Exact(a,s,target,"REQUIREMENT",ct);
            foreach(var l in v.Body.Requirement!.Relations.Where(x=>x.Kind!="CONFLICT"))await Walk(l.Target,depth+1);
        }
        foreach(var link in r.Relations)
        {
            if(link.Target.Id==c.ObjectId)throw Error("REQUIREMENT_RELATION_CYCLE",422);
            var target=await Exact(a,s,link.Target,"REQUIREMENT",ct);
            if(link.Coverage?.Any(pair=>!target.Body.Requirement!.ClauseIds.Contains(pair.ParentClauseId,StringComparer.Ordinal))==true)
                throw Error("REQUIREMENT_COVERAGE_CLAUSE_INVALID",422);
            if(link.Kind!="CONFLICT")await Walk(link.Target,0);
        }
    }
    public Task<EngineeringRequirementResultV1> CommandAsync(EngineeringRequirementCommandV1 input,CancellationToken ct=default)
    {
        var c=OperationsOwnerV1.Freeze(input);
        var bytes=ValidateCommand(c);
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;var action=c.Action=="CREATE"?"add":"edit";
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,action,true,async(a,t)=>
        {
            var s=new EngineeringRequirementStoreV1(db);await s.Fence(true,t);
            if(original!=null)return await Disclose(a,s,original,c.OperationId,bytes,t);
            var saved=await WriteWithinAsync(a,s,c,bytes,t);
            return Result(c,saved);
        },ct,actionAtFence:async t=>{original=await journal.Read(EngineeringRequirementValuesV1.Owner,c.OperationId,t);return original==null?action:"query";});
    }

    internal static byte[] ValidateCommand(EngineeringRequirementCommandV1 c)
    {
        if(c==null || c.OperationId==Guid.Empty || c.ObjectId==Guid.Empty || c.Action is not("CREATE" or "REVISE"))throw Error("REQUIREMENT_COMMAND_INVALID",400);
        EngineeringRequirementValuesV1.Text(c.Reason,true);EngineeringRequirementValuesV1.Body(c.Body);
        if(c.Action=="CREATE") {if(c.ExpectedVersion!=null || c.ExpectedEtag!=null)throw Error("REQUIREMENT_COMMAND_INVALID",400);}
        else {Revision(c.ExpectedVersion);if(c.ExpectedEtag==null || c.ExpectedEtag==Guid.Empty || c.DepartmentId!=null || c.ExpectedAuthorityVersion!=null)throw Error("REQUIREMENT_COMMAND_INVALID",400);}
        var bytes=MainOwnerOperationJournalV1.Encode(c);if(bytes.Length>2097152)throw Error("REQUIREMENT_SIZE_LIMIT",400);
        return bytes;
    }

    // 使用同一 Owner 写入用例。事务、权限门与原操作身份由调用入口持有。
    internal async Task<EngineeringRequirementSnapshotV1> WriteWithinAsync(OperationsAccessV1 access, EngineeringRequirementStoreV1 store,
        EngineeringRequirementCommandV1 command, byte[] bytes, CancellationToken ct, bool fromEngineeringIntake = false)
    {
        if (!Authority.Owns(store.Context)) throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
        access.RequireRequirementFields(true);
        OperationsAccessV1.NewEngineeringRequirementBinding? ticket=null;
        EngineeringRequirementSnapshotV1? old=null;
        var confirmsSource=command.Body.Source?.Clauses.Any(x=>x.Terms!=null)==true;
        if(command.Action=="CREATE")
            ticket=await access.BeginEngineeringRequirementCreation(command.ObjectId,command.ExpectedAuthorityVersion,command.DepartmentId,ct);
        else
        {
            await access.RequireEngineeringRequirement(command.ObjectId,ct);
            old=await store.Read(command.ObjectId,null,ct);
            if(old.Reference.Version!=command.ExpectedVersion || old.Etag!=command.ExpectedEtag)throw Error("REQUIREMENT_VERSION_CHANGED",412);
            await RequirementEvaluationStoreV1.GuardRevision(db,command.ObjectId,ct);
            // 历史依赖仍沿原 Owner 授权，原件登记只能通过明确的后继版继续。
            await Observe(access,store,old,ct);
            if (old.Body.Source?.Origin != null && command.Body.Source?.Origin == null)
                throw Error("REQUIREMENT_ORIGIN_REMOVAL_FORBIDDEN",422);
            confirmsSource|=old.Body.Source?.Clauses.Any(x=>x.Terms!=null)==true;
        }
        if (!fromEngineeringIntake && command.Body.Requirement is { } definition)
        {
            var prior = old?.Body.Requirement?.SourceReviews;
            if (definition.SourceReviews is null && prior is not null)
                command = command with { Body = command.Body with { Requirement = definition with { SourceReviews = prior } } };
            else if (definition.SourceReviews is not null &&
                (prior is null || !EngineeringProductStoreV1.Encode(definition.SourceReviews).AsSpan().SequenceEqual(EngineeringProductStoreV1.Encode(prior))))
                throw Error("REQUIREMENT_SOURCE_REVIEW_OWNER_REQUIRED",422);
        }
        await ValidateSources(access,store,command,old,ct);
        var saved=await store.Append(command,access.ActorId,ct);
        if(ticket!=null)await access.CompleteEngineeringRequirementCreation(ticket,ct);
        if(confirmsSource)
        {
            var sourceAuthority=await access.ActionAsync(OperationsOwnerV1.EngineeringRequirement,"source-confirm",ct);
            sourceAuthority.RequireRequirementFields(true);
            await sourceAuthority.RequireEngineeringRequirement(command.ObjectId,ct);
            sourceAuthority.AtCommit(token=>sourceAuthority.RequireEngineeringRequirement(command.ObjectId,token));
        }
        await Observe(access,store,saved,ct);
        var result=Result(command,saved);
        await new MainOwnerOperationJournalV1(db).Store(EngineeringRequirementValuesV1.Owner,EngineeringRequirementValuesV1.Contract,
            command.OperationId,access.ActorId,bytes,result,[command.ObjectId],
            new{command.Action,command.Reason,actor=access.ActorId,result},ct);
        return saved;
    }

    private static EngineeringRequirementResultV1 Result(EngineeringRequirementCommandV1 command, EngineeringRequirementSnapshotV1 saved)
        => new(command.OperationId,saved.Body.Kind,command.ObjectId,saved.Reference.Version,saved.Reference.Digest);
    private async Task<EngineeringRequirementResultV1> Disclose(OperationsAccessV1 a,EngineeringRequirementStoreV1 s,MainOwnerOperationJournalV1.Original original,Guid id,byte[]? bytes,CancellationToken ct)
    {
        if(original.Actor!=a.ActorId || original.Contract!=EngineeringRequirementValuesV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null && !MainOwnerOperationJournalV1.Matches(original,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<EngineeringRequirementResultV1>(original.Result);
        if(result==null || result.OperationId!=id || !original.Objects.SequenceEqual(new[]{result.ObjectId}))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        var v=await Observe(a,s,await s.Read(result.ObjectId,Revision(result.Version),ct),ct);
        if(v.Reference.Digest!=result.Digest || v.Body.Kind!=result.Kind)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);return result;
    }
    public Task<EngineeringRequirementOperationV1> OperationAsync(Guid id,bool close,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,"query",close,async(a,t)=>
        {
            a.RequireRequirementFields(false);var s=new EngineeringRequirementStoreV1(db);await s.Fence(false,t);
            var j=new MainOwnerOperationJournalV1(db);var o=await j.Read(EngineeringRequirementValuesV1.Owner,id,t);
            if(o!=null)
            {
                if(o.Actor!=a.ActorId || o.Contract!=EngineeringRequirementValuesV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                return o.State=="CLOSED"?new(id,"CLOSED"):new EngineeringRequirementOperationV1(id,"COMMITTED",await Disclose(a,s,o,id,null,t));
            }
            if(!close)return new EngineeringRequirementOperationV1(id,"UNKNOWN");
            var result=new EngineeringRequirementOperationV1(id,"CLOSED");await j.Store(EngineeringRequirementValuesV1.Owner,EngineeringRequirementValuesV1.Contract,id,a.ActorId,null,result,[],new{action="close-uncommitted-requirement",actor=a.ActorId},t);return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
