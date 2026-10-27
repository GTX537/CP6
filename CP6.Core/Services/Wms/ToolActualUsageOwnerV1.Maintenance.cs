using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;
using static CP6.Core.Services.Wms.ToolActualUsageStoreV1;
namespace CP6.Core.Services.Wms;

public sealed partial class ToolActualUsageOwnerV1
{
    private static ToolActualFactViewV1 MaintenanceObserved(Context c,Guid observation)
        =>c.Facts.SingleOrDefault(f=>f.Fact.Kind=="M_OBSERVATION"&&f.Reference.Id==observation)??throw Error("TOOL_ACTUAL_OBSERVATION_NOT_FOUND",404);
    private static ToolActualObservationViewV1 MaintenanceObservation(ToolActualFactViewV1 original,ToolActualFactViewV1[] facts)
    {
        var decisions=facts.Where(f=>f.Fact.Kind=="M_RECOGNITION"&&f.Fact.SubjectId==original.Reference.Id).ToArray();
        var application=facts.SingleOrDefault(f=>f.Fact.Kind=="M_APPLICATION"&&f.Fact.SubjectId==original.Reference.Id);
        return new(original,application!=null?"APPLIED":decisions.Length==0?"UNAPPLIED":"WAITING_EVIDENCE",application==null?"1":"2",decisions,application);
    }
    private static string MaintenanceApplicationContext(Context c,ToolActualFactViewV1 original)
        =>Hash(JsonSerializer.Serialize(new{c.Etag,original.Reference,Revision=MaintenanceObservation(original,c.Facts).ApplicationRevision},ToolActualUsageStoreV1.Json));
    private async Task<ToolActualFactViewV1> RequireMaintenanceRecognitionPolicy(Context c,ToolActualRefV1? expected,CancellationToken ct)
    {
        var policy=(await c.Store.Facts(c.Native.Reference.Id,ct)).LastOrDefault(f=>f.Fact.Kind=="M_POLICY")??throw Error("TOOL_ACTUAL_POLICY_UNAVAILABLE",503);
        if(expected!=null&&policy.Reference!=expected)throw Error("TOOL_ACTUAL_POLICY_CHANGED",412);
        var p=Body<ToolMaintenanceActualPolicyV1>(policy);var now=DateTimeOffset.UtcNow;
        if(p.State!="ENABLED"||now<p.EffectiveFrom||now>=p.EffectiveTo)throw Error("TOOL_ACTUAL_POLICY_UNAVAILABLE",503);
        if(!c.Registration.CurrentSite.Enabled||p.Site!=c.Registration.CurrentSite.Reference)throw Error("TOOL_SITE_CHANGED",412);
        await CurrentIssuer(policy,"actual-policy",ct);return policy;
    }
    private async Task<(ToolActualFactViewV1? Policy,string Knowledge)> MaintenanceRecognitionPolicyKnowledge(Context c,CancellationToken ct)
    {
        try{return(await RequireMaintenanceRecognitionPolicy(c,null,ct),"CURRENT");}
        catch(OrderPriceCorrectionException e)when(e.Status is 403 or 404 or 412||e.Message=="TOOL_ACTUAL_POLICY_UNAVAILABLE")
        {return(c.Facts.LastOrDefault(f=>f.Fact.Kind=="M_POLICY"),"UNAVAILABLE");}
        catch(CurrentPermissionUnavailableException){return(c.Facts.LastOrDefault(f=>f.Fact.Kind=="M_POLICY"),"UNAVAILABLE");}
    }
    public Task<ToolMaintenanceActualContextV1> MaintenanceContextAsync(Guid id,Guid? maintenanceId=null,int page=1,CancellationToken ct=default)
    {
        if(page is <1 or >100000)throw Error("TOOL_ACTUAL_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>
        {
            var c=await Prepare(a,id,false,t);var(p,k)=await MaintenanceRecognitionPolicyKnowledge(c,t);
            var occurrences=c.Facts.Where(f=>f.Fact.Kind=="M_OCCURRENCE"&&(maintenanceId==null||Body<ToolMaintenanceOccurrenceBodyV1>(f).Identity.SourceDocumentId==maintenanceId)).ToArray();
            var keys=occurrences.Select(f=>f.Fact.IdentityKey).ToHashSet(StringComparer.Ordinal);
            var evidence=c.Maintenance.Where(f=>f.Fact.Kind=="REPORT"&&(maintenanceId==null||f.Fact.MaintenanceId==maintenanceId)).Reverse().Skip((page-1)*20).Take(20).ToArray();
            return new ToolMaintenanceActualContextV1(id,c.Scope,c.Etag,c.Registration.CurrentSite.Reference,
                Unreconciled(c.Facts)?"ACTUAL_RECONCILIATION_REQUIRED":"NO_RECORDED_ACTUAL_RESTRICTION",p,p==null?"MISSING":k,evidence,
                occurrences.Reverse().Skip((page-1)*20).Take(20).ToArray(),c.Facts.Where(f=>f.Fact.Kind=="M_OBSERVATION"&&keys.Contains(f.Fact.IdentityKey)).Reverse().Skip((page-1)*20).Take(20).Select(f=>MaintenanceObservation(f,c.Facts)).ToArray(),
                ToolMaintenanceStoreV1.Entries(c.Maintenance).Where(x=>maintenanceId==null||x.MaintenanceId==maintenanceId).Reverse().Skip((page-1)*20).Take(20).ToArray(),page);
        },ct);
    }
    public Task<ToolActualResultV1> RegisterMaintenanceAsync(Guid id,string? etag,ToolMaintenanceOccurrenceRegisterV1 input,CancellationToken ct=default)
    {
        Text(input.Reason,500);var cmd=OperationsOwnerV1.Freeze(input)with{OccurredAt=input.OccurredAt.ToUniversalTime()};
        return Write(id,etag,cmd.OperationId,"actual-record",cmd,async(c,t)=>
        {
            var report=ToolMaintenanceStoreV1.Exact(c.Maintenance,cmd.SourceEvidenceSelectionRef,"REPORT",cmd.MaintenanceId);
            var source=ToolMaintenanceStoreV1.Body<ToolMaintenanceReportCommandV1>(report);
            if(source.Outcome!=cmd.ReportedOutcome||source.OccurredAt!=cmd.OccurredAt)throw Error("OCCURRENCE_CONTENT_CONFLICT",409);
            var key=Hash(JsonSerializer.Serialize(new{c.Scope,cmd.MaintenanceId,report.Reference.Owner,DocumentId=report.Reference.Id,LineId="RESULT",EventOrdinal="1"},ToolActualUsageStoreV1.Json));
            var existing=c.Facts.SingleOrDefault(f=>f.Fact.Kind=="M_OCCURRENCE"&&f.Fact.IdentityKey==key);if(existing!=null)return existing;
            var identity=new ToolMaintenanceOccurrenceIdentityV1("Main.Wms.ToolMaintenanceOccurrence.v1","WMS_TOOL_MAINTENANCE",cmd.MaintenanceId,"RESULT",Guid.NewGuid());
            return await Save(c,"M_OCCURRENCE",cmd.MaintenanceId,key,new ToolMaintenanceOccurrenceBodyV1(identity,report.Reference,"RESULT","1",source.Outcome,source.OccurredAt,cmd.Reason),t);
        },ct,target:cmd.MaintenanceId);
    }
    public Task<ToolActualResultV1> ObserveMaintenanceAsync(Guid id,string? etag,ToolMaintenanceActualObservationV1 input,CancellationToken ct=default)
    {
        Text(input.Reason,500);var cmd=OperationsOwnerV1.Freeze(input)with{OccurredAt=input.OccurredAt.ToUniversalTime()};
        if(cmd.Kind!="MAINTENANCE_RESULT_ACTUAL")throw Error("TOOL_ACTUAL_KIND_INVALID",400);
        return Write(id,etag,cmd.OperationId,"actual-record",cmd,async(c,t)=>
        {
            var occurrence=Exact(c.Facts,cmd.OccurrenceSelectionRef,"M_OCCURRENCE");var source=Body<ToolMaintenanceOccurrenceBodyV1>(occurrence);
            if(source.Outcome!=cmd.Outcome||source.OccurredAt!=cmd.OccurredAt)throw Error("OCCURRENCE_CONTENT_CONFLICT",409);
            var existing=c.Facts.SingleOrDefault(f=>f.Fact.Kind=="M_OBSERVATION"&&f.Fact.IdentityKey==occurrence.Fact.IdentityKey);if(existing!=null)return existing;
            return await Save(c,"M_OBSERVATION",occurrence.Reference.Id,occurrence.Fact.IdentityKey,
                new ToolMaintenanceObservedBodyV1(occurrence.Reference,source.Identity,source.EvidenceRef,source.Outcome,source.OccurredAt,cmd.Reason),t);
        },ct);
    }
    public Task<ToolActualResultV1> MaintenanceRecognitionPolicyAsync(Guid id,string? etag,ToolMaintenanceActualPolicyV1 input,CancellationToken ct=default)
    {
        Text(input.Reason,2000);if(input.RequiredChecks==null||input.RequiredChecks.Length is <1 or >30)throw Error("TOOL_ACTUAL_POLICY_INVALID",400);
        foreach(var check in input.RequiredChecks)Text(check,80);
        var cmd=OperationsOwnerV1.Freeze(input)with{EffectiveFrom=input.EffectiveFrom.ToUniversalTime(),EffectiveTo=input.EffectiveTo.ToUniversalTime()};
        if(cmd.State is not("ENABLED" or "REVOKED")||cmd.Site==null||cmd.EffectiveTo<=cmd.EffectiveFrom||cmd.RequiredChecks.Distinct(StringComparer.Ordinal).Count()!=cmd.RequiredChecks.Length)throw Error("TOOL_ACTUAL_POLICY_INVALID",400);
        return Write(id,etag,cmd.OperationId,"actual-policy",cmd,async(c,t)=>
        {
            if(cmd.Site!=c.Registration.CurrentSite.Reference||cmd.State=="ENABLED"&&!c.Registration.CurrentSite.Enabled)throw Error("TOOL_SITE_CHANGED",412);
            return await Save(c,"M_POLICY",null,Hash("maintenance-recognition-policy:"+id),cmd,t);
        },ct);
    }
    public Task<ToolActualResultV1> RecognizeMaintenanceAsync(Guid id,Guid observation,string? etag,ToolMaintenanceActualRecognitionV1 input,CancellationToken ct=default)
    {
        Text(input.Reason,2000);var cmd=OperationsOwnerV1.Freeze(input);
        if(cmd.Outcome is not("ACCEPT_FACT" or "NEEDS_EVIDENCE" or "REJECT_ASSERTION"))throw Error("TOOL_ACTUAL_RECOGNITION_INVALID",400);
        return Write(id,etag,cmd.OperationId,"actual-recognize",cmd,async(c,t)=>
        {
            var original=MaintenanceObserved(c,observation);var observed=Body<ToolMaintenanceObservedBodyV1>(original);
            if(original.Reference.Digest!=cmd.ExpectedObservationDigest)throw Error("TOOL_ACTUAL_OBSERVATION_CHANGED",412);
            if(MaintenanceObservation(original,c.Facts).Application!=null)throw Error("TOOL_ACTUAL_ALREADY_APPLIED",409);
            if(cmd.EvidenceRef!=observed.EvidenceRef)throw Error("TOOL_ACTUAL_EVIDENCE_MISMATCH",422);
            var evidence=ToolMaintenanceStoreV1.Exact(c.Maintenance,cmd.EvidenceRef,"REPORT",observed.Identity.SourceDocumentId);
            var report=ToolMaintenanceStoreV1.Body<ToolMaintenanceReportCommandV1>(evidence);var(p,k)=await MaintenanceRecognitionPolicyKnowledge(c,t);var outcome=cmd.Outcome;
            if(outcome=="ACCEPT_FACT")
            {
                if(c.Access.ActorId==original.Fact.ActorId||c.Access.ActorId==evidence.Fact.ActorId)throw Error("TOOL_ACTUAL_INDEPENDENT_RECOGNITION_REQUIRED",403);
                if(k!="CURRENT"||report.Outcome!=observed.Outcome||report.OccurredAt!=observed.OccurredAt
                    ||!Body<ToolMaintenanceActualPolicyV1>(p!).RequiredChecks.Order(StringComparer.Ordinal).SequenceEqual(report.Checks.Select(x=>x.Code).Order(StringComparer.Ordinal))
                    ||report.Outcome=="PASS"&&report.Checks.Any(x=>x.Result!="PASS"))outcome="NEEDS_EVIDENCE";
            }
            if(outcome=="ACCEPT_FACT")c.Access.AtCommit(async token=>{await RequireMaintenanceRecognitionPolicy(c,p!.Reference,token);});
            return await Save(c,"M_RECOGNITION",observation,original.Fact.IdentityKey,new ToolMaintenanceRecognizedBodyV1(original.Reference,observed.Identity,evidence.Reference,
                outcome,observed.Outcome,observed.OccurredAt,outcome=="ACCEPT_FACT"?p!.Reference:null,cmd.Reason),t);
        },ct,target:observation);
    }
    public Task<ToolMaintenanceActualApplicationContextV1> MaintenanceApplicationContextAsync(Guid id,Guid observation,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>
        {
            var c=await Prepare(a,id,false,t);var original=MaintenanceObserved(c,observation);var observed=Body<ToolMaintenanceObservedBodyV1>(original);
            var(p,k)=await MaintenanceRecognitionPolicyKnowledge(c,t);var missing=new List<string>();if(k!="CURRENT")missing.Add("CURRENT_MAINTENANCE_RECOGNITION_POLICY");
            var decision=c.Facts.LastOrDefault(f=>f.Fact.Kind=="M_RECOGNITION"&&f.Fact.SubjectId==observation);
            if(decision==null||Body<ToolMaintenanceRecognizedBodyV1>(decision).Outcome!="ACCEPT_FACT")missing.Add("ACTUAL_RECOGNITION_DECISION");
            return new ToolMaintenanceActualApplicationContextV1(original.Reference,MaintenanceObservation(original,c.Facts).ApplicationRevision,MaintenanceApplicationContext(c,original),p,missing.ToArray(),
                ToolMaintenanceStoreV1.Exact(c.Maintenance,observed.EvidenceRef,"REPORT",observed.Identity.SourceDocumentId));
        },ct);
    public Task<ToolActualResultV1> ApplyMaintenanceAsync(Guid id,Guid observation,string? etag,ToolActualApplyV1 input,CancellationToken ct=default)
    {
        var cmd=OperationsOwnerV1.Freeze(input);
        return Write(id,etag,cmd.OperationId,"actual-apply",cmd,async(c,t)=>
        {
            var original=MaintenanceObserved(c,observation);var previous=MaintenanceObservation(original,c.Facts).Application;
            if(previous!=null){if(Body<ToolMaintenanceActualApplicationV1>(previous).Decision!=cmd.DecisionRef)throw Error("OCCURRENCE_CONTENT_CONFLICT",409);return previous;}
            if(cmd.ExpectedObservationApplicationRevision!="1"||cmd.ApplicationContextRef!=MaintenanceApplicationContext(c,original))throw Error("TOOL_ACTUAL_APPLICATION_CHANGED",412);
            var decision=Exact(c.Facts,cmd.DecisionRef,"M_RECOGNITION");var d=Body<ToolMaintenanceRecognizedBodyV1>(decision);var observed=Body<ToolMaintenanceObservedBodyV1>(original);
            if(d.Observation!=original.Reference||d.Outcome!="ACCEPT_FACT"||d.Policy==null||d.Identity!=observed.Identity||d.EvidenceRef!=observed.EvidenceRef
                ||d.MaintenanceOutcome!=observed.Outcome||d.OccurredAt!=observed.OccurredAt
                ||c.Facts.LastOrDefault(f=>f.Fact.Kind=="M_RECOGNITION"&&f.Fact.SubjectId==observation)?.Reference!=decision.Reference)throw Error("TOOL_ACTUAL_DECISION_CHANGED",412);
            await RequireMaintenanceRecognitionPolicy(c,d.Policy,t);await CurrentIssuer(decision,"actual-recognize",t);
            c.Access.AtCommit(async token=>{await RequireMaintenanceRecognitionPolicy(c,d.Policy,token);await CurrentIssuer(decision,"actual-recognize",token);});
            var completion=await new ToolMaintenanceOwnerV1(db,owner).CompleteActualOwnedAsync(c.Access,id,observed,original.Reference,decision.Reference,d.Policy,t);
            return await Save(c,"M_APPLICATION",observation,original.Fact.IdentityKey,new ToolMaintenanceActualApplicationV1(original.Reference,decision.Reference,completion.Reference,observed.Outcome),t);
        },ct,target:observation);
    }
}
