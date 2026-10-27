using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<(Guid Mid,ToolMaintenanceResultV1 Report,ToolActualResultV1 Occurrence,ToolActualResultV1 Observation)> MaintenanceObserveActual(Harness h,Guid id,string outcome="PASS",Guid? mid=null)
    {
        var maintenance=Maintenance(h);var owner=ActualUsage(h);var maintenanceId=mid??(await MaintenanceStart(maintenance,id)).MaintenanceId!.Value;
        var report=await MaintenanceReport(maintenance,id,maintenanceId,null,outcome);var source=(await maintenance.FactAsync(id,report.Fact.Id)).Fact.Body;
        var occurrence=await owner.RegisterMaintenanceAsync(id,(await owner.MaintenanceContextAsync(id)).Etag,
            new(Guid.NewGuid(),maintenanceId,report.Fact,outcome,source.GetProperty("occurredAt").GetDateTimeOffset(),"Register the original immutable result"));
        var observation=await owner.ObserveMaintenanceAsync(id,(await owner.MaintenanceContextAsync(id)).Etag,
            new(Guid.NewGuid(),occurrence.Fact,outcome,source.GetProperty("occurredAt").GetDateTimeOffset(),"Record actual maintenance outcome","MAINTENANCE_RESULT_ACTUAL"));
        return(maintenanceId,report,occurrence,observation);
    }
    private static async Task<ToolActualResultV1> MaintenanceActualPolicy(ToolActualUsageOwnerV1 owner,Guid id)
    {var c=await owner.MaintenanceContextAsync(id);return await owner.MaintenanceRecognitionPolicyAsync(id,c.Etag,new(Guid.NewGuid(),"ENABLED",c.Site,["DIMENSION","SURFACE"],DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddDays(1),"Independent review of original maintenance records"));}
    private static async Task<ToolActualResultV1> MaintenanceActualRecognize(ToolActualUsageOwnerV1 owner,Guid id,ToolActualRefV1 observation,ToolMaintenanceRefV1 evidence,string outcome="ACCEPT_FACT")
        =>await owner.RecognizeMaintenanceAsync(id,observation.Id,(await owner.MaintenanceContextAsync(id)).Etag,new(Guid.NewGuid(),observation.Digest,outcome,evidence,"Independent physical result and original report verified"));
    private static async Task<(ToolActualResultV1 Result,ToolActualApplyV1 Input,string Etag)> MaintenanceActualApply(ToolActualUsageOwnerV1 owner,Guid id,ToolActualRefV1 observation,ToolActualRefV1 decision)
    {
        var app=await owner.MaintenanceApplicationContextAsync(id,observation.Id);var c=await owner.MaintenanceContextAsync(id);var input=new ToolActualApplyV1(Guid.NewGuid(),app.Revision,app.ContextRef,decision);
        return(await owner.ApplyMaintenanceAsync(id,observation.Id,c.Etag,input),input,c.Etag);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_observation_without_reset_policy_completes_then_independent_master_gate_resets_once()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);var m=Maintenance(h);
        var used=await ActualObserve(owner,id,250);await ActualPolicy(owner,id);var ud=await ActualRecognize(ActualUsage(reviewer),id,used.Observation.Fact,used.Evidence.Fact);await ActualApply(owner,id,used.Observation.Fact,ud.Fact);
        var o=await MaintenanceObserveActual(h,id);var before=await m.ContextAsync(id);Assert.Equal("OPEN",before.Maintenance.Single().Status);Assert.Equal("MISSING",before.PolicyKnowledge);
        var waiting=await MaintenanceActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Report.Fact);Assert.Equal("NEEDS_EVIDENCE",waiting.Outcome);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>MaintenanceActualApply(owner,id,o.Observation.Fact,waiting.Fact))).Status);
        await MaintenanceActualPolicy(owner,id);Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>MaintenanceActualRecognize(owner,id,o.Observation.Fact,o.Report.Fact))).Status);
        var accepted=await MaintenanceActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Report.Fact);var applied=await MaintenanceActualApply(owner,id,o.Observation.Fact,accepted.Fact);
        Assert.Equal("COMPLETED_PASS_PENDING_RESET",applied.Result.Outcome);Assert.False(applied.Result.IsUsePermit);
        var c=await m.ContextAsync(id);Assert.Equal("MISSING",c.PolicyKnowledge);Assert.Equal("COMPLETED_PASS_PENDING_RESET",c.Maintenance.Single().Status);
        Assert.Equal("250",c.Cycle.Shots);Assert.Equal("250",c.Cycle.LifetimeShots);Assert.Equal(before.Cycle.Reference,c.Cycle.Reference);Assert.Equal(before.Native,c.Native);
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>MaintenanceStart(m,id))).Status);
        var completion=c.Maintenance.Single().Completion!.Reference;Assert.Equal("Main.Wms.ToolMaintenance.v1",completion.Owner);
        Assert.Equal(applied.Result,await owner.ApplyMaintenanceAsync(id,o.Observation.Fact.Id,applied.Etag,applied.Input));
        Assert.Equal(applied.Result.Fact,(await MaintenanceActualApply(owner,id,o.Observation.Fact,accepted.Fact)).Result.Fact);
        var p=await MaintenancePublish(m,id);c=await m.ContextAsync(id);
        var q=await new ToolMaintenanceMasterOwnerV1(m).AssessAsync(id,o.Mid,c.Etag,new(Guid.NewGuid(),completion,p.Fact,"ELIGIBLE",DateTimeOffset.UtcNow.AddHours(1),"Independent current release qualification"));
        c=await m.ContextAsync(id);var reset=new ToolMaintenanceResetV1(Guid.NewGuid(),completion,p.Fact,q.Fact,c.Cycle.Reference);var result=await m.ResetAsync(id,o.Mid,c.Etag,reset);
        Assert.Equal(result,await m.ResetAsync(id,o.Mid,c.Etag,reset));c=await m.ContextAsync(id);Assert.Equal("2",c.Cycle.Number);Assert.Equal("0",c.Cycle.Shots);Assert.Equal("250",c.Cycle.LifetimeShots);
        Assert.Equal(applied.Result.Fact,(await MaintenanceActualApply(owner,id,o.Observation.Fact,accepted.Fact)).Result.Fact);
        Assert.Single(c.Maintenance);Assert.Equal("COMPLETED_PASS_RELEASED",c.Maintenance.Single().Status);Assert.Equal("RESTRICTED",c.CurrentEligibility);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_observation_repair_and_unserviceable_keep_explicit_successors_and_no_reset()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);var m=Maintenance(h);await MaintenanceActualPolicy(owner,id);
        var initial=await m.ContextAsync(id);var priorPolicy=await m.PolicyAsync(id,initial.Etag,MaintenancePolicy(initial)with{AllowUnserviceableRepair=true});
        var o=await MaintenanceObserveActual(h,id,"REPAIR_REQUIRED");var d=await MaintenanceActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Report.Fact);await MaintenanceActualApply(owner,id,o.Observation.Fact,d.Fact);
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>MaintenanceStart(m,id))).Status);
        var next=await MaintenanceStart(m,id,o.Mid);var n=await MaintenanceObserveActual(h,id,"UNSERVICEABLE",next.MaintenanceId);d=await MaintenanceActualRecognize(ActualUsage(reviewer),id,n.Observation.Fact,n.Report.Fact);await MaintenanceActualApply(owner,id,n.Observation.Fact,d.Fact);
        Assert.Equal("TOOL_REPAIR_POLICY_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>MaintenanceStart(m,id,n.Mid,priorPolicy.Fact))).Message);
        var c=await m.ContextAsync(id);Assert.Equal(new[]{"UNSERVICEABLE","REPAIR_REQUIRED"},c.Maintenance.Select(x=>x.Status));Assert.Equal("1",c.Cycle.Number);
        var p=await MaintenancePublish(m,id);Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>MaintenanceStart(m,id,n.Mid,p.Fact))).Status);
        c=await m.ContextAsync(id);var repair=await m.PolicyAsync(id,c.Etag,MaintenancePolicy(c)with{AllowUnserviceableRepair=true});await MaintenanceStart(m,id,n.Mid,repair.Fact);
        Assert.Equal("OPEN",(await m.ContextAsync(id)).Maintenance.First().Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_observation_duplicate_original_is_stable_and_different_report_cannot_complete_twice()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);var o=await MaintenanceObserveActual(h,id);var second=await MaintenanceObserveActual(h,id,mid:o.Mid);
        var source=(await Maintenance(h).FactAsync(id,o.Report.Fact.Id)).Fact.Body;var at=source.GetProperty("occurredAt").GetDateTimeOffset();
        var same=await owner.RegisterMaintenanceAsync(id,(await owner.MaintenanceContextAsync(id)).Etag,new(Guid.NewGuid(),o.Mid,o.Report.Fact,"PASS",at,"Same original with another operation key"));Assert.Equal(o.Occurrence.Fact,same.Fact);
        var sameObs=await owner.ObserveMaintenanceAsync(id,(await owner.MaintenanceContextAsync(id)).Etag,new(Guid.NewGuid(),same.Fact,"PASS",at,"Same event","MAINTENANCE_RESULT_ACTUAL"));Assert.Equal(o.Observation.Fact,sameObs.Fact);
        var current=(await owner.MaintenanceContextAsync(id)).Etag;
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.RegisterMaintenanceAsync(id,current,new(Guid.NewGuid(),o.Mid,o.Report.Fact,"REPAIR_REQUIRED",at,"Changed assertion")))).Status);
        await MaintenanceActualPolicy(owner,id);var d=await MaintenanceActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Report.Fact);await MaintenanceActualApply(owner,id,o.Observation.Fact,d.Fact);
        d=await MaintenanceActualRecognize(ActualUsage(reviewer),id,second.Observation.Fact,second.Report.Fact);
        Assert.Equal("TOOL_MAINTENANCE_COMPLETION_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>MaintenanceActualApply(owner,id,second.Observation.Fact,d.Fact))).Message);
        Assert.Null((await owner.MaintenanceContextAsync(id)).Observations.Single(x=>x.Original.Reference==second.Observation.Fact).Application);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_observation_existing_normal_completion_is_linked_without_second_native_fact()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var m=Maintenance(h);var owner=ActualUsage(h);
        var ready=await MaintenanceResetReady(h,id);var original=ready.Context.Maintenance.Single();var report=original.Reports.Single();var body=report.Fact.Body;
        var occurrence=await owner.RegisterMaintenanceAsync(id,(await owner.MaintenanceContextAsync(id)).Etag,new(Guid.NewGuid(),original.MaintenanceId,report.Reference,"PASS",body.GetProperty("occurredAt").GetDateTimeOffset(),"Original normal completion evidence"));
        var observation=await owner.ObserveMaintenanceAsync(id,(await owner.MaintenanceContextAsync(id)).Etag,new(Guid.NewGuid(),occurrence.Fact,"PASS",body.GetProperty("occurredAt").GetDateTimeOffset(),"Already happened","MAINTENANCE_RESULT_ACTUAL"));
        await MaintenanceActualPolicy(owner,id);var d=await MaintenanceActualRecognize(ActualUsage(reviewer),id,observation.Fact,report.Reference);await MaintenanceActualApply(owner,id,observation.Fact,d.Fact);
        Assert.Equal(original.Completion!.Reference,(await m.ContextAsync(id)).Maintenance.Single().Completion!.Reference);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tool_maintenance_v1.facts WHERE kind='COMPLETE' AND tool_id={id}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_observation_latest_policy_decision_and_original_issuer_remain_required()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var reviewer=await ActualReviewer(h,73);var owner=ActualUsage(h);var inspector=ActualUsage(reviewer);var o=await MaintenanceObserveActual(h,id);
        await MaintenanceActualPolicy(owner,id);var d=await MaintenanceActualRecognize(inspector,id,o.Observation.Fact,o.Report.Fact);
        await MaintenanceActualPolicy(owner,id);Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>MaintenanceActualApply(owner,id,o.Observation.Fact,d.Fact))).Status);
        d=await MaintenanceActualRecognize(inspector,id,o.Observation.Fact,o.Report.Fact);await MaintenanceActualRecognize(inspector,id,o.Observation.Fact,o.Report.Fact,"REJECT_ASSERTION");
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>MaintenanceActualApply(owner,id,o.Observation.Fact,d.Fact))).Status);
        d=await MaintenanceActualRecognize(inspector,id,o.Observation.Fact,o.Report.Fact);await h.Db.Sys_RoleActions.Where(x=>x.RoleId==73&&x.MenuId==443&&x.ActionCode=="actual-recognize").ExecuteDeleteAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>MaintenanceActualApply(owner,id,o.Observation.Fact,d.Fact));
        Assert.Equal("OPEN",(await Maintenance(h).ContextAsync(id)).Maintenance.Single().Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_observation_final_fault_rolls_back_complete_open_lease_application_and_journal()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);var o=await MaintenanceObserveActual(h,id);await MaintenanceActualPolicy(owner,id);
        var d=await MaintenanceActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Report.Fact);var c=await owner.MaintenanceContextAsync(id);var app=await owner.MaintenanceApplicationContextAsync(id,o.Observation.Fact.Id);var cmd=new ToolActualApplyV1(Guid.NewGuid(),app.Revision,app.ContextRef,d.Fact);
        hook.Before=async command=>{if(!command.CommandText.Contains("SELECT body_json AS"))return;await using var probe=command.Connection!.CreateCommand();probe.Transaction=command.Transaction;probe.CommandText="SELECT EXISTS(SELECT FROM cp6_tool_actual_v1.facts WHERE kind='M_APPLICATION' AND tool_id=@tool)";probe.Parameters.Add(new Npgsql.NpgsqlParameter("tool",id));if(!(bool)(await probe.ExecuteScalarAsync())!)return;hook.Before=null;
            await using var writer=command.Connection.CreateCommand();writer.Transaction=command.Transaction;writer.CommandText="UPDATE public.\"T_PlateMoldStock\" SET \"UsedShots\"=9 WHERE \"Id\"=@id";writer.Parameters.Add(new Npgsql.NpgsqlParameter("id",id));await writer.ExecuteNonQueryAsync();};
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ApplyMaintenanceAsync(id,o.Observation.Fact.Id,c.Etag,cmd));
        Assert.Equal("OPEN",(await Maintenance(h).ContextAsync(id)).Maintenance.Single().Status);Assert.Equal("UNKNOWN",(await owner.OperationAsync(cmd.OperationId,false)).State);
        Assert.Null((await owner.MaintenanceContextAsync(id)).Observations.Single().Application);Assert.Equal("0",(await Maintenance(h).ContextAsync(id)).Cycle.Shots);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_observation_actual_HTTP_dual_actor_closed_union_and_public_original_routes()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);var m=Maintenance(h);
        var start=await MaintenanceStart(m,id);var report=await MaintenanceReport(m,id,start.MaintenanceId!.Value,null);var at=(await m.FactAsync(id,report.Fact.Id)).Fact.Body.GetProperty("occurredAt").GetDateTimeOffset();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["Wms__ToolActual__EnvironmentId"]=ActualEnvironment.ToString(),["Wms__ToolActual__Mode"]="SIMULATION"});
        var cookies=await Login(host,h);var name=await h.Db.Sys_Users.Where(x=>x.Id==reviewer.User).Select(x=>x.UserName).SingleAsync();
        using var login=await host.Send(new HttpRequestMessage(HttpMethod.Post,"api/auth/login"){Content=JsonContent.Create(new{UserName=name,Password="Price1!Password",TenantCode=h.Tenant.ToString("N")})});Assert.Equal(HttpStatusCode.OK,login.StatusCode);
        var other=login.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0].Split('=',2)).Where(x=>x[1].Length>0).ToDictionary(x=>x[0],x=>x[1]);var root=$"/api/wms/specials/v1/tools/{id}";
        async Task<ToolActualResultV1> Send(string route,object body,Dictionary<string,string>? actor=null)
        {
            var request=OperationsRequest(actor??cookies,HttpMethod.Post,root+route,body);request.Headers.TryAddWithoutValidation("If-Match",(await owner.MaintenanceContextAsync(id)).Etag);
            using var response=await host.Send(request);var text=await response.Content.ReadAsStringAsync();Assert.True(response.IsSuccessStatusCode,response.StatusCode+":"+text);
            return JsonDocument.Parse(text).RootElement.GetProperty("data").Deserialize<ToolActualResultV1>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        var occurrence=await Send("/maintenance-occurrences/register",new ToolMaintenanceOccurrenceRegisterV1(Guid.NewGuid(),start.MaintenanceId.Value,report.Fact,"PASS",at,"Native original result"));
        var observation=await Send("/observations",new ToolMaintenanceActualObservationV1(Guid.NewGuid(),occurrence.Fact,"PASS",at,"Actual maintenance recorded","MAINTENANCE_RESULT_ACTUAL"));
        var bad=OperationsRequest(cookies,HttpMethod.Post,root+"/observations",new{operationId=Guid.NewGuid(),occurrenceSelectionRef=occurrence.Fact,outcome="PASS",occurredAt=at,reason="invalid mixed union",kind="MAINTENANCE_RESULT_ACTUAL",shots=1});bad.Headers.TryAddWithoutValidation("If-Match",(await owner.MaintenanceContextAsync(id)).Etag);
        using(var response=await host.Send(bad))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        var noCsrf=OperationsRequest(cookies,HttpMethod.Post,root+"/observations",new{},csrf:false);using(var response=await host.Send(noCsrf))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        var c=await owner.MaintenanceContextAsync(id);await Send("/maintenance-recognition-policies",new ToolMaintenanceActualPolicyV1(Guid.NewGuid(),"ENABLED",c.Site,["DIMENSION","SURFACE"],DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddHours(1),"Authorized original result recognition"));
        var d=await Send($"/observations/{observation.Fact.Id}/recognition-decisions",new ToolMaintenanceActualRecognitionV1(Guid.NewGuid(),observation.Fact.Digest,"ACCEPT_FACT",report.Fact,"Independent actual report verified"),other);
        var app=await owner.MaintenanceApplicationContextAsync(id,observation.Fact.Id);var applied=await Send($"/observations/{observation.Fact.Id}/apply",new ToolActualApplyV1(Guid.NewGuid(),app.Revision,app.ContextRef,d.Fact));Assert.Equal("COMPLETED_PASS_PENDING_RESET",applied.Outcome);
        foreach(var path in new[]{root+"/actual-maintenance-context",root+"/actual-evidence-options?kind=MAINTENANCE",root+"/maintenance-occurrence-options?maintenanceId="+start.MaintenanceId,
            $"/api/wms/specials/v1/tools/maintenance-occurrences/{occurrence.Fact.Id}",$"/api/wms/specials/v1/tools/observations/{observation.Fact.Id}",$"/api/wms/specials/v1/tools/recognition-decisions/{d.Fact.Id}",root+$"/observations/{observation.Fact.Id}/application-context"})
        {using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,path));Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl!.NoStore);}
        Assert.Equal("MISSING",(await m.ContextAsync(id)).PolicyKnowledge);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_observation_unknown_legacy_counts_stay_unknown_and_bad_checklist_cannot_be_recognized()
    {
        await using var h=await Seed();await ActualSetup(h);var reviewer=await ActualReviewer(h);var legacy=await LegacyRegistration(h);await Registrations(h).CommandAsync(legacy.Command,legacy.View.Etag);var id=legacy.Command.ObjectId;
        var owner=ActualUsage(h);var o=await MaintenanceObserveActual(h,id);var c=await owner.MaintenanceContextAsync(id);
        await owner.MaintenanceRecognitionPolicyAsync(id,c.Etag,new(Guid.NewGuid(),"ENABLED",c.Site,["DIMENSION","SURFACE","CRACK"],DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddHours(1),"Requires missing crack inspection"));
        Assert.Equal("NEEDS_EVIDENCE",(await MaintenanceActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Report.Fact)).Outcome);
        await MaintenanceActualPolicy(owner,id);var d=await MaintenanceActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Report.Fact);await MaintenanceActualApply(owner,id,o.Observation.Fact,d.Fact);
        var mc=await Maintenance(h).ContextAsync(id);Assert.Null(mc.Cycle.Number);Assert.Null(mc.Cycle.LifetimeShots);Assert.Equal("500",mc.Cycle.Shots);Assert.Equal("COMPLETED_PASS_PENDING_RESET",mc.Maintenance.Single().Status);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("DELETE FROM cp6_tool_actual_v1.facts WHERE kind='M_APPLICATION'"));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_tool_actual_v1.facts DISABLE TRIGGER tool_actual_validate_maintenance");
        try { Assert.Equal("TOOL_ACTUAL_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.MaintenanceContextAsync(id))).Message); }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_tool_actual_v1.facts ENABLE TRIGGER tool_actual_validate_maintenance"); }
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_observation_lost_ack_recovers_original_after_write_revocation_and_close_is_terminal()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);var o=await MaintenanceObserveActual(h,id);await MaintenanceActualPolicy(owner,id);
        var d=await MaintenanceActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Report.Fact);var app=await owner.MaintenanceApplicationContextAsync(id,o.Observation.Fact.Id);var c=await owner.MaintenanceContextAsync(id);var cmd=new ToolActualApplyV1(Guid.NewGuid(),app.Revision,app.ContextRef,d.Fact);
        fault.Armed=true;Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ApplyMaintenanceAsync(id,o.Observation.Fact.Id,c.Etag,cmd))).Message);
        var result=(await owner.OperationAsync(cmd.OperationId,false)).Result!;await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&x.ActionCode=="actual-apply").ExecuteDeleteAsync();
        Assert.Equal(result,await owner.ApplyMaintenanceAsync(id,o.Observation.Fact.Id,c.Etag,cmd));Assert.Equal("COMPLETED_PASS_PENDING_RESET",result.Outcome);
        var closed=Guid.NewGuid();await owner.OperationAsync(closed,true);Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ApplyMaintenanceAsync(id,o.Observation.Fact.Id,c.Etag,cmd with{OperationId=closed}))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&x.ActionCode=="query").ExecuteDeleteAsync();Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.OperationAsync(cmd.OperationId,false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_observation_peer_native_writer_waits_until_completion_commits()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);var o=await MaintenanceObserveActual(h,id);await MaintenanceActualPolicy(owner,id);
        var d=await MaintenanceActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Report.Fact);Task<int>? peerWrite=null;
        hook.Before=async command=>
        {
            if(!command.CommandText.Contains("SELECT body_json AS"))return;
            await using var probe=command.Connection!.CreateCommand();probe.Transaction=command.Transaction;probe.CommandText="SELECT EXISTS(SELECT FROM cp6_tool_actual_v1.facts WHERE kind='M_APPLICATION' AND tool_id=@id)";probe.Parameters.Add(new Npgsql.NpgsqlParameter("id",id));if(!(bool)(await probe.ExecuteScalarAsync())!)return;hook.Before=null;
            peerWrite=Task.Run(async()=>{await using var peer=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});return await peer.PlateMoldStocks.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.UsedShots,7));});
            await using var observer=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE 'UPDATE %T_PlateMoldStock%') AS \"Value\"").SingleAsync(deadline.Token))await Task.Delay(20,deadline.Token);
            Assert.False(peerWrite.IsCompleted);
        };
        var result=await MaintenanceActualApply(owner,id,o.Observation.Fact,d.Fact);Assert.NotNull(peerWrite);Assert.Equal(1,await peerWrite!);
        Assert.Equal("COMPLETED_PASS_PENDING_RESET",result.Result.Outcome);Assert.Equal(result.Result,(await owner.OperationAsync(result.Input.OperationId,false)).Result);
        Assert.Equal("RECONCILIATION_REQUIRED",(await Maintenance(h).ContextAsync(id)).Cycle.Knowledge);
    }
}
