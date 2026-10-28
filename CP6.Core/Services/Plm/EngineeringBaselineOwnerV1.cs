using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringBaselineValuesV1;
namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringBaselineOwnerV1(CP6Context db,OperationsOwnerV1 owner,ScopeAuthorizationFenceV1 scope,
    QualitySpecificationRuntimeV1 runtime,EngineeringSourceCheckService checks,IterationReviewOwnerV1 reviews,EngineeringOutputRelationsOwnerV1 relations)
{
    internal bool Owns(CP6Context context)=>ReferenceEquals(db,context) && owner.Owns(db) && checks.Owns(db) && reviews.Owns(db) && relations.Owns(db);
    internal async Task<EngineeringBaselineViewV1> Source(OperationsAccessV1 root, EngineeringSourceRefV1 reference, bool current, CancellationToken ct)
    {
        if (!Owns(db) || reference.Owner!="Main.Plm.EngineeringBaseline.v1") throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
        var a=await root.SourceAsync(OperationsOwnerV1.EngineeringBaseline,ct);
        var value=await View(a,await Store(a,false,ct),reference.Id,ct,current);
        if(value.Reference!=reference)throw Error("ENGINEERING_BASELINE_REFERENCE_CHANGED",412);
        if(current && value.SourceStatus!="CURRENT")throw Error("ENGINEERING_BASELINE_SOURCES_CHANGED",412);
        return value;
    }
    private OperationsOwnerV1 Authority=>owner.Owns(db) && checks.Owns(db) && reviews.Owns(db) && relations.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private async Task<EngineeringBaselineStoreV1> Store(OperationsAccessV1 a,bool write,CancellationToken ct)
    {
        if(runtime.EvidenceMode is not("REAL" or "DEMO"))throw Error("ENGINEERING_BASELINE_ENVIRONMENT_UNAVAILABLE",503);
        await scope.Root(ct);var lease=await scope.AcquireAsync(ct);a.AtCommit(t=>scope.ValidateAsync(lease,t));
        var s=new EngineeringBaselineStoreV1(db,scope.EnvironmentId);await s.Fence(write,ct);return s;
    }
    private async Task<EngineeringBaselineViewV1> View(OperationsAccessV1 a,EngineeringBaselineStoreV1 s,Guid id,CancellationToken ct,bool observeQualification=true)
    {
        await a.RequireEngineeringBaseline(scope.EnvironmentId,id,ct);a.AtCommit(t=>a.RequireEngineeringBaseline(scope.EnvironmentId,id,t));
        var v=await s.Read(id,ct);if(v.Original.EvidenceMode!=runtime.EvidenceMode)throw Error("ENGINEERING_BASELINE_MODE_MISMATCH",403);
        // Preserve the fixed original. Current authorization is mandatory, current qualification is a separate status.
        var check=await checks.CandidateSource(a,v.Original.Command.SourceCheck,ct);
        var review=await reviews.FreezeSource(a,v.Original.Command.IterationId,v.Original.Command.ReviewSubmissionId,v.Original.Command.SourceCandidate,false,ct);
        var changes=check.Changes.ToList();
        if(observeQualification)
        {
            try{await a.EngineeringObservation((observation,t)=>reviews.FreezeSource(observation,v.Original.Command.IterationId,
                v.Original.Command.ReviewSubmissionId,v.Original.Command.SourceCandidate,true,t),ct);}
            catch(OrderPriceCorrectionException ex)when(ex.Status is 403 or 412 or 422){changes.Add(ex.Message);}
        }
        if(review.Package!=v.Original.Review.Package || check.OriginalJson!=v.Original.CheckOriginal)throw Error("ENGINEERING_BASELINE_ORIGINAL_UNAVAILABLE",503);
        if(v.Original.OutputRelations is {} r)await relations.Source(a,r,false,ct);
        return v with{SourceStatus=changes.Count==0?"CURRENT":"CHANGED",Changes=changes.Distinct().ToArray()};
    }
    public Task<QualitySpecificationScopeV1> ScopeAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringBaseline,"query",false,
        async(a,t)=>{_ = await Store(a,false,t);return new QualitySpecificationScopeV1(db.CurrentTenantId,a.ActorId,scope.EnvironmentId,runtime.EvidenceMode);},ct);
    public Task<EngineeringBaselineViewV1[]> ListAsync(Guid? revision=null,int page=1,CancellationToken ct=default)
    {
        if(page is <1 or >100000 || revision==Guid.Empty)throw Error("ENGINEERING_BASELINE_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringBaseline,"query",false,async(a,t)=>{
            var s=await Store(a,false,t);var rows=a.EngineeringBaselines(scope.EnvironmentId);if(revision!=null)rows=rows.Where(x=>x.RevisionId==revision);
            var ids=await rows.OrderBy(x=>x.Id).Skip((page-1)*20).Take(20).Select(x=>x.Id).ToArrayAsync(t);
            var result=new List<EngineeringBaselineViewV1>();foreach(var id in ids)result.Add(await View(a,s,id,t));return result.ToArray();
        },ct);
    }
    public Task<EngineeringBaselineViewV1> ReadAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringBaseline,"query",false,
        async(a,t)=>await View(a,await Store(a,false,t),id,t),ct);
    public Task<EngineeringFreezeChoiceV1[]> ChoicesAsync(Guid iteration,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringBaseline,"query",false,
        async(a,t)=>{_ = await Store(a,false,t);return await reviews.FreezeChoices(a,iteration,t);},ct);
    public Task<EngineeringFreezeResultV1> FreezeAsync(EngineeringFreezeCommandV1 input,CancellationToken ct=default)
    {
        Command(input);var c=OperationsOwnerV1.Freeze(input);var bytes=MainOwnerOperationJournalV1.Encode(c);
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringBaseline,"freeze",true,async(a,t)=>{
            var s=await Store(a,true,t);if(original!=null)return await Disclose(a,s,original,c.OperationId,bytes,t);
            await a.RequireEngineeringFreeze(c.Revision.Id,t);a.AtCommit(token=>a.RequireEngineeringFreeze(c.Revision.Id,token));
            var existing=await EngineeringBaselineStoreV1.Heads(db,scope.EnvironmentId).SingleOrDefaultAsync(x=>x.CandidateId==c.EngineeringCandidate.Id
                && x.CandidateVersion==c.EngineeringCandidate.Version && x.ConfigurationId==c.Configuration.Id && x.ConfigurationVersion==c.Configuration.Version,t);
            EngineeringFreezeResultV1 result;
            if(existing!=null)
            {
                var query=await a.SourceAsync(OperationsOwnerV1.EngineeringBaseline,t);var saved=await View(query,s,existing.Id,t);
                if(Hash(Bytes(saved.Original.Command with{OperationId=c.OperationId,Reason=c.Reason}))!=Hash(Bytes(c)))throw Error("ENGINEERING_BASELINE_ALREADY_FROZEN",409);
                result=new(c.OperationId,saved.Reference,existing.OperationId,"ACCEPTED",saved.Original.Manifest.Digest,saved.OutputBinding?.BodyDigest,existing.IntentId);
            }
            else
            {
                var review=await reviews.FreezeSource(a,c.IterationId,c.ReviewSubmissionId,c.SourceCandidate,true,t);
                var definition=review.Candidate.Original.Definition;
                if(review.RoundId!=c.ReviewRoundId || review.Receipt.ReceiptId!=c.ApplicationReceiptId || review.Package.Digest!=c.ReviewPackageDigest
                    || definition.EngineeringRevision!=c.Revision || definition.EngineeringCandidate!=c.EngineeringCandidate || definition.SourceCheck!=c.SourceCheck)
                    throw Error("ENGINEERING_FREEZE_REVIEW_BINDING_MISMATCH",422);
                var actual=await checks.HoldExact(a,c.SourceCheck,t);var check=await checks.CandidateSource(a,c.SourceCheck,t);
                if(check.Freshness!="CURRENT" || actual.Command.Revision!=c.Revision || actual.Command.Candidate!=c.EngineeringCandidate
                    || actual.Command.ExpectedEtag!=c.ExpectedEtag || actual.Input.Configuration?.Reference!=c.Configuration
                    || actual.Manifest==null || actual.Manifest.Digest!=c.TechnicalManifestDigest || definition.TechnicalSourceOriginal!=check.OriginalJson)
                    throw Error("ENGINEERING_FREEZE_TECHNICAL_BINDING_MISMATCH",422);
                var revisions=new EngineeringRevisionStoreV1(db);await revisions.Fence(true,t);var revision=await revisions.Read(c.Revision.Id,Revision(c.Revision.Version),t);
                if(!revision.IsCurrent || revision.Reference!=c.Revision || revision.CandidateRef!=c.EngineeringCandidate || revision.Etag!=c.ExpectedEtag)
                    throw Error("ENGINEERING_FREEZE_CANDIDATE_CHANGED",412);
                OutputRelationsVersionV1? relation=null;
                if(actual.Command.OutputRelations is {} r)relation=(await relations.Source(a,r,true,t)).Version;
                if(actual.PolicyBody.EngineeringChecks!.Manufacturing=="INTERNAL" && relation==null)throw Error("ENGINEERING_FREEZE_OUTPUT_RELATIONS_REQUIRED",422);
                var o=new EngineeringBaselineOriginalV1(Schema,scope.EnvironmentId,db.CurrentTenantId,Guid.NewGuid(),"1",a.ActorId,DateTimeOffset.UtcNow,
                    runtime.EvidenceMode,c,revision,actual,check.OriginalJson,review,actual.Manifest,relation?.Reference,relation?.Original.WireOriginal,
                    relation==null?"NOT_APPLICABLE_BY_APPROVED_POLICY":"APPLIES");
                result=await s.Save(o,relation,t);
            }
            await journal.Store(Journal,Contract,c.OperationId,a.ActorId,bytes,result,[result.Baseline.Id],new{c.Reason,result.Baseline,c.SourceCandidate,c.EngineeringCandidate,c.Configuration},t);
            return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(Journal,c.OperationId,t);return original==null?"freeze":"query";});
    }
    private async Task<EngineeringFreezeResultV1> Disclose(OperationsAccessV1 a,EngineeringBaselineStoreV1 s,MainOwnerOperationJournalV1.Original o,Guid id,byte[]? input,CancellationToken ct)
    {
        if(o.Actor!=a.ActorId || o.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(o.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null && !MainOwnerOperationJournalV1.Matches(o,input))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var r=JsonSerializer.Deserialize<EngineeringFreezeResultV1>(o.Result);
        if(r==null || r.OperationId!=id || !o.Objects.SequenceEqual(new[]{r.Baseline.Id}))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        if((await View(a,s,r.Baseline.Id,ct)).Reference!=r.Baseline)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);return r;
    }
    private async Task<EngineeringFreezeOperationV1> Original(OperationsAccessV1 a,EngineeringBaselineStoreV1 s,Guid id,CancellationToken ct)
    {
        var o=await new MainOwnerOperationJournalV1(db).Read(Journal,id,ct);if(o==null)return new(id,"UNKNOWN");
        if(o.Actor!=a.ActorId || o.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        return o.State=="CLOSED"?new(id,"CLOSED"):new(id,"COMMITTED",await Disclose(a,s,o,id,null,ct));
    }
    public Task<EngineeringFreezeOperationV1> OperationAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringBaseline,"query",false,
        async(a,t)=>await Original(a,await Store(a,false,t),id,t),ct);
    public Task<EngineeringFreezeCancellationV1> CancelAsync(EngineeringFreezeCancelV1 input,CancellationToken ct=default)
    {
        if(input==null || input.OperationId==Guid.Empty || input.TargetOperationId==Guid.Empty || input.OperationId==input.TargetOperationId || input.ExpectedState!="UNKNOWN")
            throw Error("ENGINEERING_FREEZE_CANCEL_INVALID",400);
        EngineeringRevisionValuesV1.Text(input.Reason);var c=OperationsOwnerV1.Freeze(input);var bytes=MainOwnerOperationJournalV1.Encode(c);
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringBaseline,"cancel",true,async(a,t)=>{
            var s=await Store(a,true,t);var query=await a.SourceAsync(OperationsOwnerV1.EngineeringBaseline,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=CancelJournal)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(!MainOwnerOperationJournalV1.Matches(original,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
                var saved=JsonSerializer.Deserialize<EngineeringFreezeCancellationV1>(original.Result)??throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
                await Original(query,s,saved.TargetOperationId,t);return saved;
            }
            var state=await Original(query,s,c.TargetOperationId,t);
            if(state.State=="UNKNOWN")
            {state=new(c.TargetOperationId,"CLOSED");await journal.Store(Journal,Contract,c.TargetOperationId,a.ActorId,null,state,[],new{c.OperationId,c.Reason},t);}
            var result=new EngineeringFreezeCancellationV1(c.OperationId,c.TargetOperationId,state);
            await journal.Store(CancelJournal,CancelJournal,c.OperationId,a.ActorId,bytes,result,state.Result==null?[]:[state.Result.Baseline.Id],new{c.Reason},t);return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(CancelJournal,c.OperationId,t);return original==null?"cancel":"query";});
    }
    public Task<EngineeringBaselineDiffV1> DiffAsync(Guid before,Guid after,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringBaseline,"query",false,async(a,t)=>{
        // Diff compares immutable originals. It still checks every current source read right,
        // but does not need the separate, expensive current professional-qualification status.
        var s=await Store(a,false,t);var x=await View(a,s,before,t,false);var y=before==after?x:await View(a,s,after,t,false);
        if(x.Original.Command.Revision.Id!=y.Original.Command.Revision.Id)throw Error("ENGINEERING_BASELINE_DIFF_PARENT_MISMATCH",422);
        var result=new List<EngineeringWorkingChangeV1>();
        void Compare(string path,object? l,object? r){if(Hash(Bytes(l!))!=Hash(Bytes(r!)))result.Add(new(path,JsonSerializer.SerializeToElement(l,EngineeringProductStoreV1.Json),JsonSerializer.SerializeToElement(r,EngineeringProductStoreV1.Json)));}
        Compare("composition",x.Original.Revision.Draft,y.Original.Revision.Draft);Compare("manifest",x.Original.Manifest,y.Original.Manifest);
        Compare("review",x.Original.Review,y.Original.Review);Compare("outputRelations",x.Original.OutputRelationsOriginal,y.Original.OutputRelationsOriginal);
        return new EngineeringBaselineDiffV1(x.Reference,y.Reference,result.ToArray());
    },ct);
    public Task<EngineeringBaselineUsageV1[]> WhereUsedAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringBaseline,"query",false,async(a,t)=>{
        var s=await Store(a,false,t);await View(a,s,id,t);return new[]{new EngineeringBaselineUsageV1("Main.Plm.EngineeringRevision.v1","COMPLETE_FOR_NATIVE_SUCCESSORS",await s.Successors(id,t),"Only native successor originals are included"),
            new EngineeringBaselineUsageV1("REL/VER/WO/SCHED","UNKNOWN",[],"Downstream authoritative reference readers have not been adopted")};
    },ct);
}
