using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;
using static CP6.Core.Services.Wms.ToolActualUsageStoreV1;
namespace CP6.Core.Services.Wms;

/// <summary>Actual occurrence recognition never retroactively grants a production use permit.</summary>
public sealed class ToolActualUsageOwnerV1(CP6Context db, OperationsOwnerV1 owner, ToolActualRuntimeV1 runtime)
{
    private const string Contract="Main.Wms.ToolActualUsage.commands.v1",Journal="Main.Wms.ToolActualUsage.operations.v1";
    private OperationsOwnerV1 Authority=>owner.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private sealed record Context(OperationsAccessV1 Access,OperationsAccessV1 Query,ToolInstanceStoreV1 Tools,ToolActualUsageStoreV1 Store,
        ToolInstanceViewV1 Native,ToolRegistrationViewV1 Registration,ToolRegistrationOriginalV1 Original,ToolMaintenanceCycleV1 Cycle,
        ToolActualScopeV1 Scope,ToolActualFactViewV1[] Facts,ToolActualCycleV1[] Cycles)
    {
        internal string Etag=>"\"ta1-"+Hash(JsonSerializer.Serialize(new{Native.Reference,Last=Facts.LastOrDefault()?.Reference,Scope},ToolActualUsageStoreV1.Json))+"\"";
        internal bool Coherent=>Registration.CycleKnowledge!="RECONCILIATION_REQUIRED";
    }
    private async Task<Context> Prepare(OperationsAccessV1 a,Guid id,bool write,CancellationToken ct)
    {
        if(id==Guid.Empty)throw Error("TOOL_INSTANCE_ID_INVALID",400);
        var query=await a.SourceAsync(OperationsOwnerV1.ToolInstance,ct);var tools=new ToolInstanceStoreV1(db);await tools.Fence(ct,write);
        var maintenance=new ToolMaintenanceStoreV1(db);await maintenance.Fence(write,ct);
        var store=new ToolActualUsageStoreV1(db);await store.Fence(write,ct);await store.Runtime(runtime,write,ct);
        var native=await tools.Read(query,id,null,ct);var registration=await new ToolRegistrationOwnerV1(db,owner).ReadOwnedAsync(query,id,ct)??throw Error("LEGACY_MAPPING_REQUIRED",409);
        var original=JsonSerializer.Deserialize<ToolRegistrationOriginalV1>(registration.OriginalJson)??throw Error("TOOL_REGISTRATION_UNAVAILABLE",503);
        var facts=await store.Facts(id,ct);var cycle=(await ToolMaintenanceStoreV1.Project(db,original,ct)).Cycle;
        var scope=new ToolActualScopeV1(runtime.EnvironmentId,db.CurrentTenantId,registration.CurrentSite.Reference.Id,runtime.Mode);
        if(facts.Any(f=>f.Fact.Scope!=scope))throw Error("TOOL_ACTUAL_RUNTIME_MISMATCH",503);
        return new(a,query,tools,store,native,registration,original,cycle,scope,facts,
            await ToolActualUsageStoreV1.Cycles(db,original,await maintenance.Facts(id,ct),facts,ct));
    }
    private static void Text(string? text,int max)
    {
        if(string.IsNullOrWhiteSpace(text)||text.Length>max)throw Error("TOOL_ACTUAL_INPUT_INVALID",400);
        try{new UTF8Encoding(false,true).GetByteCount(text);}catch(EncoderFallbackException){throw Error("TOOL_ACTUAL_INPUT_INVALID",400);}
    }
    private static void Actual(int shots,DateTimeOffset at)
    {if(shots<=0||at==default||at>DateTimeOffset.UtcNow)throw Error("TOOL_ACTUAL_FACT_INVALID",422);}
    private async Task CurrentIssuer(ToolActualFactViewV1 fact,string action,CancellationToken ct)
    {
        var witnesses=fact.Fact.Authorities;
        if(!witnesses.Any(x=>x.Resource==OperationsOwnerV1.ToolInstance&&x.Action==action)
            ||!witnesses.Any(x=>x.Resource==OperationsOwnerV1.ToolInstance&&x.Action=="query")
            ||!witnesses.Any(x=>x.Resource==OperationsOwnerV1.PhysicalSite&&x.Action=="query")
            ||witnesses.Select(x=>(x.Resource,x.Action)).Distinct().Count()!=witnesses.Length)throw Error("TOOL_ACTUAL_UNAVAILABLE",503);
        foreach(var witness in witnesses)
        {
            var actual=await Authority.ToolActualReceiptAuthorityAsync(witness.Resource,witness.Action,fact.Fact.ActorId,ct);
            if(actual.Source.SourcePermissionVersion!=witness.PermissionVersion||actual.OrganizationVersion!=witness.OrganizationVersion)throw Error("TOOL_ACTUAL_ISSUER_CHANGED",412);
            var access=new OperationsAccessV1(db,owner,actual);
            if(witness.Resource==OperationsOwnerV1.ToolInstance)await access.RequireToolInstance(db,fact.Fact.ToolId,ct);
            if(witness.Resource==OperationsOwnerV1.PhysicalSite)
            {var site=await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,access,fact.Fact.Site.Id,ct);if(site.Reference!=fact.Fact.Site||!site.Enabled)throw Error("TOOL_SITE_CHANGED",412);}
            await access.ValidateFinalAsync(ct);
        }
    }
    private async Task<ToolActualFactViewV1> RequirePolicy(Context c,ToolActualRefV1? expected,CancellationToken ct)
    {
        var facts=await c.Store.Facts(c.Native.Reference.Id,ct);var policy=facts.LastOrDefault(f=>f.Fact.Kind=="POLICY")??throw Error("TOOL_ACTUAL_POLICY_UNAVAILABLE",503);
        if(expected!=null&&policy.Reference!=expected)throw Error("TOOL_ACTUAL_POLICY_CHANGED",412);
        var body=Body<ToolActualPolicyCommandV1>(policy);var now=DateTimeOffset.UtcNow;
        if(body.State!="ENABLED"||!body.AllowLocalAttestation||now<body.EffectiveFrom||now>=body.EffectiveTo)throw Error("TOOL_ACTUAL_POLICY_UNAVAILABLE",503);
        if(!c.Registration.CurrentSite.Enabled||body.Site!=c.Registration.CurrentSite.Reference)throw Error("TOOL_SITE_CHANGED",412);
        await CurrentIssuer(policy,"actual-policy",ct);return policy;
    }
    private async Task<(ToolActualFactViewV1? Policy,string Knowledge)> PolicyKnowledge(Context c,CancellationToken ct)
    {
        try{return(await RequirePolicy(c,null,ct),"CURRENT");}
        catch(OrderPriceCorrectionException e)when(e.Status is 403 or 404 or 412||e.Message=="TOOL_ACTUAL_POLICY_UNAVAILABLE")
        {return(c.Facts.LastOrDefault(f=>f.Fact.Kind=="POLICY"),"UNAVAILABLE");}
        catch(CurrentPermissionUnavailableException){return(c.Facts.LastOrDefault(f=>f.Fact.Kind=="POLICY"),"UNAVAILABLE");}
    }
    public Task<ToolActualContextV1> ContextAsync(Guid id,int page=1,CancellationToken ct=default)
    {
        if(page is <1 or >100000)throw Error("TOOL_ACTUAL_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>
        {
            var c=await Prepare(a,id,false,t);var(p,k)=await PolicyKnowledge(c,t);
            ToolActualFactViewV1[] Items(string kind)=>c.Facts.Where(f=>f.Fact.Kind==kind).Reverse().Skip((page-1)*20).Take(20).ToArray();
            return new ToolActualContextV1(db.CurrentTenantId,id,c.Scope,c.Etag,c.Native.Reference,c.Registration.CurrentSite.Reference,
                !c.Coherent?"RECONCILIATION_REQUIRED":Unreconciled(c.Facts)?"ACTUAL_RECONCILIATION_REQUIRED":"NO_RECORDED_ACTUAL_RESTRICTION",
                Unreconciled(c.Facts)?"RESTRICTED":"UNKNOWN",c.Cycles,c.Coherent?c.Cycle.LifetimeKnowledge:"UNKNOWN",c.Coherent?c.Cycle.LifetimeShots:null,
                p,p==null?"MISSING":k,Items("EVIDENCE"),Items("OCCURRENCE"),Items("OBSERVATION").Select(f=>Observation(f,c.Facts)).ToArray(),Items("USE"),Page:page);
        },ct);
    }
    private async Task<ToolActualFactViewV1> Save(Context c,string kind,Guid? subject,string key,object body,CancellationToken ct,
        string revision="1",ToolInstanceViewV1? after=null,bool nativeAlreadyChanged=false)
    {
        var witnesses=c.Access.Authorities.DistinctBy(x=>(x.Resource,x.Action)).Select(x=>new ToolMaintenanceAuthorityV1(x.Resource,x.Action,x.Source.SourcePermissionVersion,x.OrganizationVersion)).ToArray();
        return await c.Store.Append(new(c.Scope,c.Native.Reference.Id,Guid.NewGuid(),kind,subject,key,revision,c.Access.ActorId,DateTimeOffset.UtcNow,
            nativeAlreadyChanged?after!.Reference:c.Native.Reference,(after??c.Native).Reference,c.Registration.CurrentSite.Reference,witnesses,
            JsonSerializer.SerializeToElement(body,ToolActualUsageStoreV1.Json)),ct);
    }
    private Task<ToolActualResultV1> Write<T>(Guid id,string? etag,Guid operation,string action,T command,
        Func<Context,CancellationToken,Task<ToolActualFactViewV1>> change,CancellationToken ct,Guid? target=null)
    {
        if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract=Contract,id,etag,action,target,command});var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,action,true,async(a,t)=>
        {
            var c=await Prepare(a,id,true,t);if(original!=null)return await Disclose(c,operation,original,bytes,t);
            if(string.IsNullOrEmpty(etag))throw Error("IF_MATCH_REQUIRED",428);if(etag!=c.Etag)throw Error("TOOL_ACTUAL_CHANGED",412);
            await a.RequireToolInstance(db,id,t);var fact=await change(c,t);
            var outcome=fact.Fact.Kind switch{"OCCURRENCE"=>"REGISTERED_ACTUAL_OCCURRENCE","OBSERVATION"=>"RECORDED_OBSERVATION","APPLICATION"=>"APPLIED_ACTUAL_WITH_RESTRICTIONS",
                "RECOGNITION"=>Body<ToolActualRecognizedBodyV1>(fact).Outcome,"POLICY"=>Body<ToolActualPolicyCommandV1>(fact).State,_=>"RECORDED_LOCAL_ATTESTATION"};
            var result=new ToolActualResultV1(operation,id,fact.Fact.Kind,fact.Reference,outcome);
            await journal.Store(Journal,Contract,operation,a.ActorId,bytes,result,[id],new{action,a.ActorId,fact.Reference,fact.Fact.NativeBefore,fact.Fact.NativeAfter},t);
            var isNew=!c.Facts.Any(f=>f.Reference==fact.Reference);var expectedNative=isNew?fact.Fact.NativeAfter:c.Native.Reference;
            var expectedHead=isNew?fact.Reference:c.Facts.LastOrDefault()?.Reference;
            await c.Tools.HoldExactAsync(c.Query,expectedNative,t);
            a.AtCommit(async token=>
            {
                if((await c.Store.Facts(id,token)).LastOrDefault()?.Reference!=expectedHead)throw Error("TOOL_ACTUAL_CHANGED",412);
                var site=await c.Query.SourceAsync(OperationsOwnerV1.PhysicalSite,token);
                if((await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,site,c.Scope.SiteId,token)).Reference!=c.Registration.CurrentSite.Reference)throw Error("TOOL_SITE_CHANGED",412);
                if((await c.Tools.Read(c.Query,id,null,token)).Reference!=expectedNative)throw Error("TOOL_INSTANCE_CHANGED",412);
                await a.RequireToolInstance(db,id,token);await c.Store.Runtime(runtime,false,token);
            });return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(Journal,operation,t);return original==null?action:"query";});
    }
    public Task<ToolActualResultV1> EvidenceAsync(Guid id,string? etag,ToolActualEvidenceCommandV1 input,CancellationToken ct=default)
    {
        Text(input.DocumentKey,100);Text(input.LineKey,80);Text(input.EventOrdinal,80);Text(input.Evidence,4000);
        var cmd=OperationsOwnerV1.Freeze(input) with{OccurredAt=input.OccurredAt.ToUniversalTime()};Text(cmd.DocumentKey,100);Text(cmd.LineKey,80);Text(cmd.EventOrdinal,80);Text(cmd.Evidence,4000);Actual(cmd.Shots,cmd.OccurredAt);
        return Write(id,etag,cmd.OperationId,"actual-record",cmd,async(c,t)=>
        {
            var document=await c.Store.Document(c.Scope.SiteId,cmd.DocumentKey,t);
            var key=Hash(JsonSerializer.Serialize(new{c.Scope,owner=EvidenceOwner,document,line=cmd.LineKey,ordinal=cmd.EventOrdinal},ToolActualUsageStoreV1.Json));
            if(await c.Store.ForeignIdentity(key,id,t))throw Error("OCCURRENCE_CONTENT_CONFLICT",409);
            var existing=c.Facts.LastOrDefault(f=>f.Fact.Kind=="EVIDENCE"&&f.Fact.IdentityKey==key);var revision=1L;
            if(existing!=null)
            {
                var old=Body<ToolActualEvidenceBodyV1>(existing);
                if(old.Shots!=cmd.Shots||old.OccurredAt!=cmd.OccurredAt)throw Error("OCCURRENCE_CONTENT_CONFLICT",409);
                if(cmd.Previous==null&&old.Evidence==cmd.Evidence)return existing;
                if(cmd.Previous!=existing.Reference)throw Error("TOOL_ACTUAL_EVIDENCE_CHANGED",412);
                revision=checked(Revision(existing.Fact.Revision)+1);
            }
            else if(cmd.Previous!=null)throw Error("TOOL_ACTUAL_EVIDENCE_CHANGED",412);
            return await Save(c,"EVIDENCE",document,key,new ToolActualEvidenceBodyV1(document,cmd.DocumentKey,cmd.LineKey,cmd.EventOrdinal,cmd.Shots,cmd.OccurredAt,cmd.Evidence,cmd.Previous),t,revision.ToString(CultureInfo.InvariantCulture));
        },ct);
    }
    public Task<ToolActualResultV1> RegisterAsync(Guid id,string? etag,ToolActualRegisterV1 input,CancellationToken ct=default)
    {
        Text(input.Reason,500);
        var cmd=OperationsOwnerV1.Freeze(input)with{OccurredAt=input.OccurredAt.ToUniversalTime()};Text(cmd.Reason,500);Actual(cmd.ReportedShots,cmd.OccurredAt);
        return Write(id,etag,cmd.OperationId,"actual-record",cmd,async(c,t)=>
        {
            var evidence=Exact(c.Facts,cmd.SourceEvidenceSelectionRef,"EVIDENCE");var source=Body<ToolActualEvidenceBodyV1>(evidence);
            if(source.Shots!=cmd.ReportedShots||source.OccurredAt!=cmd.OccurredAt)throw Error("OCCURRENCE_CONTENT_CONFLICT",409);
            var existing=c.Facts.SingleOrDefault(f=>f.Fact.Kind=="OCCURRENCE"&&f.Fact.IdentityKey==evidence.Fact.IdentityKey);if(existing!=null)return existing;
            var identity=new ToolUseOccurrenceIdentityV1(OccurrenceOwner,"WMS_TOOL_ACTUAL",source.DocumentId,source.LineKey,Guid.NewGuid());
            return await Save(c,"OCCURRENCE",evidence.Reference.Id,evidence.Fact.IdentityKey,new ToolActualOccurrenceBodyV1(identity,evidence.Reference,source.Shots,source.OccurredAt,cmd.Reason),t);
        },ct);
    }
    public Task<ToolActualResultV1> ObserveAsync(Guid id,string? etag,ToolActualObservationV1 input,CancellationToken ct=default)
    {
        Text(input.Reason,500);
        var cmd=OperationsOwnerV1.Freeze(input)with{OccurredAt=input.OccurredAt.ToUniversalTime()};Text(cmd.Reason,500);Actual(cmd.Shots,cmd.OccurredAt);
        return Write(id,etag,cmd.OperationId,"actual-record",cmd,async(c,t)=>
        {
            var occurrence=Exact(c.Facts,cmd.OccurrenceSelectionRef,"OCCURRENCE");var source=Body<ToolActualOccurrenceBodyV1>(occurrence);
            if(source.Shots!=cmd.Shots||source.OccurredAt!=cmd.OccurredAt)throw Error("OCCURRENCE_CONTENT_CONFLICT",409);
            var existing=c.Facts.SingleOrDefault(f=>f.Fact.Kind=="OBSERVATION"&&f.Fact.IdentityKey==occurrence.Fact.IdentityKey);if(existing!=null)return existing;
            return await Save(c,"OBSERVATION",occurrence.Reference.Id,occurrence.Fact.IdentityKey,new ToolActualObservedBodyV1(occurrence.Reference,source.Identity,source.Shots,source.OccurredAt,cmd.Reason),t);
        },ct);
    }
    public Task<ToolActualResultV1> PolicyAsync(Guid id,string? etag,ToolActualPolicyCommandV1 input,CancellationToken ct=default)
    {
        Text(input.Reason,2000);
        var cmd=OperationsOwnerV1.Freeze(input)with{EffectiveFrom=input.EffectiveFrom.ToUniversalTime(),EffectiveTo=input.EffectiveTo.ToUniversalTime()};Text(cmd.Reason,2000);
        if(cmd.State is not("ENABLED" or "REVOKED")||cmd.Site==null||cmd.EffectiveTo<=cmd.EffectiveFrom)throw Error("TOOL_ACTUAL_POLICY_INVALID",400);
        return Write(id,etag,cmd.OperationId,"actual-policy",cmd,async(c,t)=>
        {
            if(cmd.Site!=c.Registration.CurrentSite.Reference||cmd.State=="ENABLED"&&!c.Registration.CurrentSite.Enabled)throw Error("TOOL_SITE_CHANGED",412);
            return await Save(c,"POLICY",null,Hash("policy:"+id),cmd,t);
        },ct);
    }
    private static ToolActualFactViewV1 Observed(Context c,Guid id)=>c.Facts.SingleOrDefault(f=>f.Fact.Kind=="OBSERVATION"&&f.Reference.Id==id)??throw Error("TOOL_ACTUAL_OBSERVATION_NOT_FOUND",404);
    private static string ApplicationRevision(Context c,ToolActualFactViewV1 observation)=>Observation(observation,c.Facts).ApplicationRevision;
    private static string ApplicationContext(Context c,ToolActualFactViewV1 observation)=>Hash(JsonSerializer.Serialize(new{c.Etag,observation.Reference,Revision=ApplicationRevision(c,observation),c.Cycles,Site=c.Registration.CurrentSite.Reference},ToolActualUsageStoreV1.Json));
    public Task<ToolActualApplicationContextV1> ApplicationContextAsync(Guid id,Guid observation,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>
        {
            var c=await Prepare(a,id,false,t);var original=Observed(c,observation);var(p,k)=await PolicyKnowledge(c,t);var missing=new List<string>();
            if(k!="CURRENT")missing.Add("CURRENT_RECOGNITION_POLICY");if(!c.Coherent)missing.Add("NATIVE_CYCLE_RECONCILIATION");if(c.Cycles.All(x=>x.Number==null))missing.Add("KNOWN_CYCLE");
            var latest=c.Facts.LastOrDefault(x=>x.Fact.Kind=="RECOGNITION"&&x.Fact.SubjectId==observation);
            if(latest==null||Body<ToolActualRecognizedBodyV1>(latest).Outcome!="ACCEPT_FACT")missing.Add("ACTUAL_RECOGNITION_DECISION");
            return new ToolActualApplicationContextV1(original.Reference,ApplicationRevision(c,original),ApplicationContext(c,original),c.Cycles,p,missing.ToArray(),
                c.Facts.Where(f=>f.Fact.Kind=="EVIDENCE"&&f.Fact.IdentityKey==original.Fact.IdentityKey).ToArray());
        },ct);
    public Task<ToolActualResultV1> RecognizeAsync(Guid id,Guid observation,string? etag,ToolActualRecognitionV1 input,CancellationToken ct=default)
    {
        Text(input.Reason,2000);
        var cmd=OperationsOwnerV1.Freeze(input);Text(cmd.Reason,2000);
        if(cmd.Outcome is not("ACCEPT_FACT" or "NEEDS_EVIDENCE" or "REJECT_ASSERTION")||cmd.EvidenceRefs==null||cmd.EvidenceRefs.Length>20||cmd.EvidenceRefs.Any(x=>x==null))throw Error("TOOL_ACTUAL_RECOGNITION_INVALID",400);
        return Write(id,etag,cmd.OperationId,"actual-recognize",cmd,async(c,t)=>
        {
            var original=Observed(c,observation);if(original.Reference.Digest!=cmd.ExpectedObservationDigest)throw Error("TOOL_ACTUAL_OBSERVATION_CHANGED",412);
            if(Observation(original,c.Facts).Application!=null)throw Error("TOOL_ACTUAL_ALREADY_APPLIED",409);
            var observed=Body<ToolActualObservedBodyV1>(original);var source=Exact(c.Facts,observed.OccurrenceRef,"OCCURRENCE");var evidence=Exact(c.Facts,Body<ToolActualOccurrenceBodyV1>(source).EvidenceRef,"EVIDENCE");
            var(p,k)=await PolicyKnowledge(c,t);var outcome=cmd.Outcome;ToolActualCycleV1? cycle=null;
            if(outcome=="ACCEPT_FACT")
            {
                cycle=c.Cycles.SingleOrDefault(x=>x.Reference==cmd.RecognizedCycleRef&&x.Number!=null&&x.StartedAt<=observed.OccurredAt&&(x.ClosedAt==null||observed.OccurredAt<x.ClosedAt));
                var selected=cmd.EvidenceRefs.Select(r=>Exact(c.Facts,r,"EVIDENCE")).ToArray();
                if(selected.Any(f=>f.Fact.IdentityKey!=original.Fact.IdentityKey))throw Error("TOOL_ACTUAL_EVIDENCE_MISMATCH",422);
                if(c.Access.ActorId==original.Fact.ActorId||c.Access.ActorId==evidence.Fact.ActorId||selected.Any(f=>f.Fact.ActorId==c.Access.ActorId))throw Error("TOOL_ACTUAL_INDEPENDENT_RECOGNITION_REQUIRED",403);
                if(!c.Coherent||cycle==null||k!="CURRENT"||selected.Length==0) { outcome="NEEDS_EVIDENCE";cycle=null; }
            }
            var qualification="UNPROVEN";
            var status=await db.Database.SqlQuery<int?>($"""
                SELECT (source_json::jsonb->>'status')::integer AS "Value" FROM cp6_tool_instance_v1.versions
                WHERE tenant_id={db.CurrentTenantId} AND object_id={id} AND observed_at<={observed.OccurredAt} AND source_present ORDER BY revision DESC LIMIT 1
                """).SingleOrDefaultAsync(t);if(status is 1 or 2 or 3)qualification="VIOLATION";
            if(outcome=="ACCEPT_FACT")c.Access.AtCommit(async token=>{await RequirePolicy(c,p!.Reference,token);});
            return await Save(c,"RECOGNITION",observation,original.Fact.IdentityKey,new ToolActualRecognizedBodyV1(original.Reference,observed.Identity,outcome,
                outcome=="ACCEPT_FACT"?p!.Reference:null,cycle?.Reference,observed.Shots,observed.OccurredAt,cmd.EvidenceRefs,cmd.Reason,qualification),t);
        },ct,target:observation);
    }
    public Task<ToolActualResultV1> ApplyAsync(Guid id,Guid observation,string? etag,ToolActualApplyV1 input,CancellationToken ct=default)
    {
        var cmd=OperationsOwnerV1.Freeze(input);
        return Write(id,etag,cmd.OperationId,"actual-apply",cmd,async(c,t)=>
        {
            var original=Observed(c,observation);var application=Observation(original,c.Facts).Application;
            if(application!=null)
            {if(Body<ToolActualApplicationBodyV1>(application).Decision!=cmd.DecisionRef)throw Error("OCCURRENCE_CONTENT_CONFLICT",409);return application;}
            if(cmd.ExpectedObservationApplicationRevision!=ApplicationRevision(c,original)||cmd.ApplicationContextRef!=ApplicationContext(c,original))throw Error("TOOL_ACTUAL_APPLICATION_CHANGED",412);
            if(!c.Coherent)throw Error("TOOL_CYCLE_RECONCILIATION_REQUIRED",409);
            var decision=Exact(c.Facts,cmd.DecisionRef,"RECOGNITION");var d=Body<ToolActualRecognizedBodyV1>(decision);
            if(decision.Fact.SubjectId!=observation||d.Observation!=original.Reference||d.Outcome!="ACCEPT_FACT"||d.TargetCycle==null
                ||c.Facts.LastOrDefault(f=>f.Fact.Kind=="RECOGNITION"&&f.Fact.SubjectId==observation)?.Reference!=decision.Reference)throw Error("TOOL_ACTUAL_DECISION_CHANGED",412);
            var cycle=c.Cycles.SingleOrDefault(x=>x.Reference==d.TargetCycle&&x.Number!=null&&x.StartedAt<=d.OccurredAt&&(x.ClosedAt==null||d.OccurredAt<x.ClosedAt))??throw Error("TOOL_ACTUAL_CYCLE_CHANGED",412);
            await RequirePolicy(c,d.Policy,t);await CurrentIssuer(decision,"actual-recognize",t);
            c.Access.AtCommit(async token=>{await RequirePolicy(c,d.Policy,token);await CurrentIssuer(decision,"actual-recognize",token);});
            var observed=Body<ToolActualObservedBodyV1>(original);if(d.Shots!=observed.Shots||d.OccurredAt!=observed.OccurredAt||d.Identity!=observed.Identity)throw Error("OCCURRENCE_CONTENT_CONFLICT",409);
            int nextTarget;string? lifetime;
            try { nextTarget=checked(int.Parse(cycle.Shots,CultureInfo.InvariantCulture)+d.Shots);
                lifetime=c.Cycle.LifetimeShots==null?null:checked(long.Parse(c.Cycle.LifetimeShots,CultureInfo.InvariantCulture)+d.Shots).ToString(CultureInfo.InvariantCulture); }
            catch(OverflowException){throw Error("TOOL_ACTUAL_COUNTER_OVERFLOW",422);}
            var nextCycle=c.Cycle with{Shots=cycle.Current?nextTarget:c.Cycle.Shots,LifetimeShots=lifetime,ProjectionRevision=(c.Facts.Count(f=>f.Fact.Kind=="USE")+2L).ToString(CultureInfo.InvariantCulture)};
            var after=c.Native;
            if(cycle.Current)
            {
                var row=await db.PlateMoldStocks.SingleAsync(x=>x.Id==id,t);row.UsedShots=nextTarget;
                var actualTime=d.OccurredAt.UtcDateTime;if(row.LastUsedAt==null||row.LastUsedAt<actualTime)row.LastUsedAt=actualTime;
                if(row.Status==0&&row.MaxShots.HasValue&&nextTarget>=row.MaxShots.Value)row.Status=2;
                row.Modifier=c.Access.Actor;row.ModifyDate=DateTime.Now;await db.SaveChangesAsync(t);after=await c.Tools.Read(c.Query,id,null,t);
            }
            var use=await Save(c,"USE",observation,original.Fact.IdentityKey,new ToolActualUseBodyV1(original.Reference,decision.Reference,observed.OccurrenceRef,observed.Identity,
                cycle.Reference,d.Shots,d.OccurredAt,c.Cycle,nextCycle,cycle.Shots,nextTarget.ToString(CultureInfo.InvariantCulture),d.QualificationAtOccurrence),t,after:after);
            return await Save(c,"APPLICATION",observation,original.Fact.IdentityKey,new ToolActualApplicationBodyV1(original.Reference,decision.Reference,use.Reference,"2"),t,after:after,nativeAlreadyChanged:true);
        },ct,target:observation);
    }
    private async Task<ToolActualResultV1> Disclose(Context c,Guid operation,MainOwnerOperationJournalV1.Original original,byte[]? bytes,CancellationToken ct)
    {
        if(original.Actor!=c.Access.ActorId||original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null&&!MainOwnerOperationJournalV1.Matches(original,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<ToolActualResultV1>(original.Result);
        if(original.State!="COMMITTED"||result==null||result.OperationId!=operation||result.ObjectId!=c.Native.Reference.Id||original.Objects.Length!=1||original.Objects[0]!=result.ObjectId)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        var fact=Exact(c.Facts,result.Fact,result.Kind);await c.Tools.Read(c.Query,result.ObjectId,Revision(fact.Fact.NativeAfter.Version),ct);return result;
    }
    public Task<ToolActualOperationV1> OperationAsync(Guid operation,bool close,CancellationToken ct=default)
    {
        if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",close,async(a,t)=>
        {
            var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(Journal,operation,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId||original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new ToolActualOperationV1(operation,"CLOSED");
                if(original.Objects.Length!=1)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
                var c=await Prepare(a,original.Objects[0],close,t);return new(operation,"COMMITTED",await Disclose(c,operation,original,null,t));
            }
            if(!close)return new(operation,"UNKNOWN");var result=new ToolActualOperationV1(operation,"CLOSED");
            await journal.Store(Journal,Contract,operation,a.ActorId,null,result,[],new{action="close-actual-usage-operation"},t);return result;
        },ct,readOnlyAuthorityFence:!close);
    }
    public Task<ToolActualFactViewV1> FactAsync(Guid id,Guid factId,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>
    {var c=await Prepare(a,id,false,t);var f=c.Facts.SingleOrDefault(x=>x.Reference.Id==factId)??throw Error("TOOL_ACTUAL_FACT_NOT_FOUND",404);await c.Tools.Read(c.Query,id,Revision(f.Fact.NativeBefore.Version),t);await c.Tools.Read(c.Query,id,Revision(f.Fact.NativeAfter.Version),t);return f;},ct);
    public Task<object> LookupAsync(Guid factId,string kind,string? version=null,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>
        {
            var tool=await db.Database.SqlQuery<Guid>($"SELECT tool_id AS \"Value\" FROM cp6_tool_actual_v1.facts WHERE tenant_id={db.CurrentTenantId} AND record_id={factId} AND kind={kind}").SingleOrDefaultAsync(t);
            if(tool==Guid.Empty)throw Error("TOOL_ACTUAL_FACT_NOT_FOUND",404);var c=await Prepare(a,tool,false,t);
            var f=c.Facts.Single(x=>x.Reference.Id==factId);if(version!=null&&version!=f.Reference.Version)throw Error("TOOL_ACTUAL_REFERENCE_CHANGED",412);
            await c.Tools.Read(c.Query,tool,Revision(f.Fact.NativeAfter.Version),t);
            return kind=="OBSERVATION"?(object)Observation(f,c.Facts):f;
        },ct);
}
