using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly Guid ActualEnvironment=Guid.Parse("d3c68ff7-3eed-412e-aa70-fa5af56a57e1");
    private static ToolActualUsageOwnerV1 ActualUsage(Harness h,CP6Context? db=null,ToolActualRuntimeV1? runtime=null)
        =>new(db??h.Db,RegistrationAuthority(h,db),runtime??new(ActualEnvironment,"SIMULATION"));
    private static async Task<Guid> ActualSetup(Harness h)
    {
        var id=await MaintenanceSetup(h);
        foreach(var action in new[]{"actual-record","actual-policy","actual-recognize","actual-apply"})await EngineeringAction(h,"wms-plate-mold",action);
        return id;
    }
    private static async Task<Harness> ActualReviewer(Harness h,int role=72)
    {
        var user=new CP6.Entity.DomainModels.Sys.Sys_User{UserName="actual-reviewer-"+Guid.NewGuid().ToString("N")[..8],Password=new CP6.Core.Services.Sys.BCryptPasswordHasher(4).Hash("Price1!Password"),RoleId=role};
        if(role!=72)
        {
            h.Db.Sys_Roles.Add(new(){RoleId=role,RoleName="actual-independent-reviewer"});
            foreach(var grant in await h.Db.Sys_RoleActions.AsNoTracking().Where(x=>x.RoleId==72).ToArrayAsync())h.Db.Sys_RoleActions.Add(new(){RoleId=role,MenuId=grant.MenuId,ActionCode=grant.ActionCode});
            h.Db.Sys_RoleDataScopes.Add(new(){RoleId=role,ResourceKey="wms-plate-mold",ScopeType=5});h.Db.Sys_RoleDataScopes.Add(new(){RoleId=role,ResourceKey="space-site",ScopeType=5});h.Db.WmsRoleScopes.Add(new(){RoleId=role,WarehouseCd="*"});
        }
        h.Db.Sys_Users.Add(user);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        return h with{User=user.Id,Http=new Microsoft.AspNetCore.Http.DefaultHttpContext{User=CurrentPermissionContextTests.Principal(user.Id,h.Tenant)}};
    }
    private static async Task<ToolActualResultV1> ActualPolicy(ToolActualUsageOwnerV1 owner,Guid id)
    {var c=await owner.ContextAsync(id);return await owner.PolicyAsync(id,c.Etag,new(Guid.NewGuid(),"ENABLED",c.Site,true,DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddDays(1),"Authorized independent recognition of original local site records"));}
    private static async Task<(ToolActualResultV1 Evidence,ToolActualResultV1 Occurrence,ToolActualResultV1 Observation,ToolActualEvidenceCommandV1 Input)> ActualObserve(ToolActualUsageOwnerV1 owner,Guid id,int shots=300,DateTimeOffset? at=null)
    {
        var input=new ToolActualEvidenceCommandV1(Guid.NewGuid(),"SHOP-"+Guid.NewGuid().ToString("N"),"LINE-1","1",shots,at??DateTimeOffset.UtcNow,"Original local record e\u0301; counter observed on the actual tool");
        var evidence=await owner.EvidenceAsync(id,(await owner.ContextAsync(id)).Etag,input);
        var occurrence=await owner.RegisterAsync(id,(await owner.ContextAsync(id)).Etag,new(Guid.NewGuid(),evidence.Fact,shots,input.OccurredAt,"Register this recorded physical occurrence"));
        var observation=await owner.ObserveAsync(id,(await owner.ContextAsync(id)).Etag,new(Guid.NewGuid(),occurrence.Fact,shots,input.OccurredAt,"Record already happened; this is not a use permit"));
        return(evidence,occurrence,observation,input);
    }
    private static async Task<ToolActualResultV1> ActualRecognize(ToolActualUsageOwnerV1 owner,Guid id,ToolActualRefV1 observation,ToolActualRefV1 evidence,Guid? cycle=null)
    {
        var c=await owner.ContextAsync(id);var target=cycle==null?c.Cycles.Single(x=>x.Current):c.Cycles.Single(x=>x.Reference.Id==cycle);
        return await owner.RecognizeAsync(id,observation.Id,c.Etag,new(Guid.NewGuid(),observation.Digest,"ACCEPT_FACT",target.Reference,[evidence],"Independent inspection confirms this actual record and its exact original cycle"));
    }
    private static async Task<(ToolActualResultV1 Result,ToolActualApplyV1 Command,string Etag)> ActualApply(ToolActualUsageOwnerV1 owner,Guid id,ToolActualRefV1 observation,ToolActualRefV1 decision)
    {
        var app=await owner.ApplicationContextAsync(id,observation.Id);var c=await owner.ContextAsync(id);var cmd=new ToolActualApplyV1(Guid.NewGuid(),app.Revision,app.ContextRef,decision);
        return(await owner.ApplyAsync(id,observation.Id,c.Etag,cmd),cmd,c.Etag);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_actual_usage_HTTP_registered_instance_context()
    {
        await using var h = await Seed(); await GrantRegistration(h);
        var command = await NewRegistered(h); await Registrations(h).CommandAsync(command, null);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string,string> { ["Wms__ToolActual__EnvironmentId"] = "d3c68ff7-3eed-412e-aa70-fa5af56a57e1", ["Wms__ToolActual__Mode"] = "SIMULATION" });
        var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get,
            $"/api/wms/specials/v1/tools/{command.ObjectId}/actual-usage-context"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_actual_usage_original_occurrence_observation_recognition_and_application_add_exactly_once()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);
        var original=(await Registrations(h).ReadAsync(id))!.OriginalJson;var o=await ActualObserve(owner,id,1100);
        var before=await owner.ContextAsync(id);Assert.Equal("0",before.Cycles.Single().Shots);Assert.Equal("0",before.LifetimeShots);Assert.Equal("ACTUAL_RECONCILIATION_REQUIRED",before.Knowledge);Assert.Equal("RECORDED_OBSERVATION",o.Observation.Outcome);
        var waiting=await ActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Evidence.Fact);Assert.Equal("NEEDS_EVIDENCE",waiting.Outcome);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ActualApply(owner,id,o.Observation.Fact,waiting.Fact))).Status);
        await ActualPolicy(owner,id);Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ActualRecognize(owner,id,o.Observation.Fact,o.Evidence.Fact))).Status);
        var decision=await ActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Evidence.Fact);Assert.Equal("ACCEPT_FACT",decision.Outcome);
        var applied=await ActualApply(owner,id,o.Observation.Fact,decision.Fact);Assert.Equal("APPLIED_ACTUAL_WITH_RESTRICTIONS",applied.Result.Outcome);Assert.False(applied.Result.IsUsePermit);
        var c=await owner.ContextAsync(id);Assert.Equal("1100",c.Cycles.Single().Shots);Assert.Equal("1100",c.LifetimeShots);Assert.Equal("RESTRICTED",c.CurrentEligibility);Assert.Single(c.Uses);Assert.Equal("APPLIED",c.Observations.Single().ApplicationState);
        var native=await ToolInstances(h).ReadAsync(id);Assert.Equal(1100,native.Source.GetProperty("usedShots").GetInt32());Assert.Equal(2,native.Source.GetProperty("status").GetInt32());
        var registration=(await Registrations(h).ReadAsync(id))!;Assert.Equal(original,registration.OriginalJson);Assert.Equal("1100",registration.CurrentCycleShots);Assert.Equal("1100",registration.LifetimeShots);Assert.Equal("RESTRICTED",registration.CurrentEligibility);
        Assert.Equal(applied.Result,await owner.ApplyAsync(id,o.Observation.Fact.Id,applied.Etag,applied.Command));Assert.Equal(applied.Result,(await owner.OperationAsync(applied.Command.OperationId,false)).Result);
        var alias=await ActualApply(owner,id,o.Observation.Fact,decision.Fact);Assert.Equal(applied.Result.Fact,alias.Result.Fact);Assert.Single((await owner.ContextAsync(id)).Uses);
        Assert.Equal(0,await h.Db.StockTransactions.CountAsync());Assert.Equal(0,await h.Db.Stocks.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_actual_usage_source_revision_and_new_operation_after_reset_keep_same_occurrence_and_use()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);var o=await ActualObserve(owner,id,700);
        await ActualPolicy(owner,id);var decision=await ActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Evidence.Fact);var used=await ActualApply(owner,id,o.Observation.Fact,decision.Fact);
        var reset=await MaintenanceResetReady(h,id);await Maintenance(h).ResetAsync(id,reset.Context.Maintenance.Single().MaintenanceId,reset.Context.Etag,reset.Command);
        var c=await owner.ContextAsync(id);Assert.Equal("0",c.Cycles.Single(x=>x.Current).Shots);Assert.Equal("700",c.LifetimeShots);
        var revision=await owner.EvidenceAsync(id,c.Etag,o.Input with{OperationId=Guid.NewGuid(),Previous=o.Evidence.Fact,Evidence="Additional legible original note"});
        var again=await owner.RegisterAsync(id,(await owner.ContextAsync(id)).Etag,new(Guid.NewGuid(),revision.Fact,700,o.Input.OccurredAt,"same recorded occurrence"));Assert.Equal(o.Occurrence.Fact,again.Fact);
        var observation=await owner.ObserveAsync(id,(await owner.ContextAsync(id)).Etag,new(Guid.NewGuid(),again.Fact,700,o.Input.OccurredAt,"same already happened event"));Assert.Equal(o.Observation.Fact,observation.Fact);
        var alias=await ActualApply(owner,id,observation.Fact,decision.Fact);Assert.Equal(used.Result.Fact,alias.Result.Fact);
        c=await owner.ContextAsync(id);Assert.Single(c.Uses);Assert.Equal("700",c.LifetimeShots);Assert.Equal("0",c.Cycles.Single(x=>x.Current).Shots);
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.EvidenceAsync(id,c.Etag,o.Input with{OperationId=Guid.NewGuid(),Shots=701}))).Status);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("DELETE FROM cp6_tool_actual_v1.facts"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_actual_usage_late_old_cycle_recognition_changes_historical_cycle_and_lifetime_only()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);var o=await ActualObserve(owner,id,125);
        var old=(await owner.ContextAsync(id)).Cycles.Single().Reference.Id;var reset=await MaintenanceResetReady(h,id);await Maintenance(h).ResetAsync(id,reset.Context.Maintenance.Single().MaintenanceId,reset.Context.Etag,reset.Command);
        await ActualPolicy(owner,id);var pending=await ActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Evidence.Fact);Assert.Equal("NEEDS_EVIDENCE",pending.Outcome);
        var accepted=await ActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Evidence.Fact,old);var before=await ToolInstances(h).ReadAsync(id);
        await ActualApply(owner,id,o.Observation.Fact,accepted.Fact);var after=await ToolInstances(h).ReadAsync(id);Assert.Equal(before.Reference,after.Reference);
        var c=await owner.ContextAsync(id);Assert.Equal("125",c.Cycles.Single(x=>!x.Current).Shots);Assert.Equal("0",c.Cycles.Single(x=>x.Current).Shots);Assert.Equal("125",c.LifetimeShots);
        Assert.Equal("125",(await Maintenance(h).ContextAsync(id)).Cycle.LifetimeShots);Assert.Equal("0",(await Maintenance(h).ContextAsync(id)).Cycle.Shots);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_actual_usage_current_policy_original_issuer_and_new_decision_are_required_without_changing_history()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var reviewer=await ActualReviewer(h,73);var owner=ActualUsage(h);var inspector=ActualUsage(reviewer);
        var o=await ActualObserve(owner,id);await ActualPolicy(owner,id);var decision=await ActualRecognize(inspector,id,o.Observation.Fact,o.Evidence.Fact);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.MenuId==443&&x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("UNAVAILABLE",(await inspector.ContextAsync(id)).PolicyKnowledge);
        var revoked=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ActualApply(inspector,id,o.Observation.Fact,decision.Fact));
        Assert.Equal(412,revoked.Status);Assert.Equal("TOOL_ACTUAL_ISSUER_CHANGED",revoked.Message);
        Assert.Equal(decision,(await inspector.OperationAsync(decision.OperationId,false)).Result);
        Assert.Empty((await inspector.ContextAsync(id)).Uses);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_actual_usage_changed_policy_or_cycle_and_new_needs_evidence_decision_cannot_apply_old_acceptance()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);var inspector=ActualUsage(reviewer);var o=await ActualObserve(owner,id);
        await ActualPolicy(owner,id);var decision=await ActualRecognize(inspector,id,o.Observation.Fact,o.Evidence.Fact);
        await ActualPolicy(owner,id);Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ActualApply(owner,id,o.Observation.Fact,decision.Fact))).Status);
        decision=await ActualRecognize(inspector,id,o.Observation.Fact,o.Evidence.Fact);var c=await inspector.ContextAsync(id);
        await inspector.RecognizeAsync(id,o.Observation.Fact.Id,c.Etag,new(Guid.NewGuid(),o.Observation.Fact.Digest,"NEEDS_EVIDENCE",null,[],"Subsequent independent review requests original log"));
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ActualApply(owner,id,o.Observation.Fact,decision.Fact))).Status);Assert.Empty((await owner.ContextAsync(id)).Uses);
        c=await owner.ContextAsync(id);await h.Db.Space_Sites.Where(x=>x.Id==c.Site.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Enable,false));
        Assert.Equal("UNAVAILABLE",(await owner.ContextAsync(id)).PolicyKnowledge);Assert.Single((await owner.ContextAsync(id)).Observations);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_actual_usage_legacy_unknown_cycle_and_cross_tool_evidence_never_invent_counts()
    {
        await using var h=await Seed();await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);
        var legacy=await LegacyRegistration(h);await Registrations(h).CommandAsync(legacy.Command,legacy.View.Etag);var id=legacy.Command.ObjectId;
        var o=await ActualObserve(owner,id,12);await ActualPolicy(owner,id);var d=await ActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Evidence.Fact);Assert.Equal("NEEDS_EVIDENCE",d.Outcome);
        var c=await owner.ContextAsync(id);Assert.Null(c.LifetimeShots);Assert.Equal("500",c.Cycles.Single().Shots);Assert.Null(c.Cycles.Single().Number);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ActualApply(owner,id,o.Observation.Fact,d.Fact))).Status);
        var other=await NewRegistered(h);await Registrations(h).CommandAsync(other,null);
        // Different Site namespaces are not the same occurrence; reusing an exact foreign reference is denied.
        var otherContext=await owner.ContextAsync(other.ObjectId);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.RegisterAsync(other.ObjectId,otherContext.Etag,new(Guid.NewGuid(),o.Evidence.Fact,12,o.Input.OccurredAt,"wrong tool")))).Status);
        Assert.Equal("UNKNOWN",(await Registrations(h).ReadAsync(id))!.LifetimeKnowledge);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_actual_usage_runtime_binding_is_host_owned_and_cannot_mix_environment_or_mode()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var owner=ActualUsage(h);await ActualObserve(owner,id);
        Assert.Equal("TOOL_ACTUAL_RUNTIME_UNCONFIGURED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ActualUsage(h,runtime:new(Guid.Empty,"")).ContextAsync(id))).Message);
        Assert.Equal("TOOL_ACTUAL_RUNTIME_MISMATCH",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ActualUsage(h,runtime:new(Guid.NewGuid(),"SIMULATION")).ContextAsync(id))).Message);
        Assert.Equal("TOOL_ACTUAL_RUNTIME_MISMATCH",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ActualUsage(h,runtime:new(ActualEnvironment,"PRODUCTION")).ContextAsync(id))).Message);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("UPDATE cp6_tool_actual_v1.environment SET mode='PRODUCTION'"));
        Assert.Single((await owner.ContextAsync(id)).Observations);
    }
    private static async Task<bool> ActualApplicationWritten(System.Data.Common.DbCommand command,Guid tool)
    {
        if(!command.CommandText.Contains("SELECT body_json AS"))return false;
        await using var probe=command.Connection!.CreateCommand();probe.Transaction=command.Transaction;probe.CommandText="SELECT EXISTS(SELECT FROM cp6_tool_actual_v1.facts WHERE tool_id=@tool AND kind='APPLICATION')";
        probe.Parameters.Add(new Npgsql.NpgsqlParameter("tool",tool));return (bool)(await probe.ExecuteScalarAsync())!;
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_actual_usage_final_same_transaction_fault_rolls_back_use_application_native_and_original_journal()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);
        var o=await ActualObserve(owner,id);await ActualPolicy(owner,id);var d=await ActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Evidence.Fact);
        var app=await owner.ApplicationContextAsync(id,o.Observation.Fact.Id);var c=await owner.ContextAsync(id);var cmd=new ToolActualApplyV1(Guid.NewGuid(),app.Revision,app.ContextRef,d.Fact);
        hook.Before=async command=>{if(!await ActualApplicationWritten(command,id))return;hook.Before=null;await using var writer=command.Connection!.CreateCommand();writer.Transaction=command.Transaction;
            writer.CommandText="UPDATE public.\"T_PlateMoldStock\" SET \"UsedShots\"=9 WHERE \"Id\"=@id";writer.Parameters.Add(new Npgsql.NpgsqlParameter("id",id));await writer.ExecuteNonQueryAsync();};
        Assert.Equal("TOOL_INSTANCE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ApplyAsync(id,o.Observation.Fact.Id,c.Etag,cmd))).Message);
        c=await owner.ContextAsync(id);Assert.Empty(c.Uses);Assert.Equal("0",c.LifetimeShots);Assert.Equal("0",c.Cycles.Single().Shots);Assert.Null(c.Observations.Single().Application);Assert.Equal("UNKNOWN",(await owner.OperationAsync(cmd.OperationId,false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_actual_usage_peer_writer_waits_and_lost_commit_ack_recovers_original_after_write_revoked()
    {
        var hook=new CommandHook();var fault=new CommitFault();await using var h=await Seed(hook,fault);var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);
        var o=await ActualObserve(owner,id);await ActualPolicy(owner,id);var d=await ActualRecognize(ActualUsage(reviewer),id,o.Observation.Fact,o.Evidence.Fact);var app=await owner.ApplicationContextAsync(id,o.Observation.Fact.Id);var c=await owner.ContextAsync(id);var cmd=new ToolActualApplyV1(Guid.NewGuid(),app.Revision,app.ContextRef,d.Fact);Task<int>? peerWrite=null;
        hook.Before=async command=>
        {
            if(!await ActualApplicationWritten(command,id))return;hook.Before=null;
            peerWrite=Task.Run(async()=>{await using var peer=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});return await peer.PlateMoldStocks.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.UsedShots,9));});
            await using var observer=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE 'UPDATE %T_PlateMoldStock%') AS \"Value\"").SingleAsync(deadline.Token))await Task.Delay(20,deadline.Token);
            Assert.False(peerWrite.IsCompleted);
        };
        fault.Armed=true;Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ApplyAsync(id,o.Observation.Fact.Id,c.Etag,cmd))).Message);Assert.Equal(1,await peerWrite!);
        var original=(await owner.OperationAsync(cmd.OperationId,false)).Result!;await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&x.ActionCode=="actual-apply").ExecuteDeleteAsync();
        Assert.Equal(original,await owner.ApplyAsync(id,o.Observation.Fact.Id,c.Etag,cmd));Assert.Equal("RECONCILIATION_REQUIRED",(await owner.ContextAsync(id)).Knowledge);Assert.Single((await owner.ContextAsync(id)).Uses);
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ApplyAsync(id,Guid.NewGuid(),c.Etag,cmd))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_actual_usage_original_key_close_unicode_conflict_and_current_read_scope_remain_independent()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var owner=ActualUsage(h);var c=await owner.ContextAsync(id);
        var cmd=new ToolActualEvidenceCommandV1(Guid.NewGuid(),"DOC-e\u0301","L","1",10,DateTimeOffset.UtcNow,"Original source e\u0301");
        Assert.Equal(428,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.EvidenceAsync(id,null,cmd))).Status);Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.EvidenceAsync(id,"*",cmd))).Status);
        var result=await owner.EvidenceAsync(id,c.Etag,cmd);Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.EvidenceAsync(id,c.Etag,cmd with{DocumentKey="DOC-é"}))).Status);
        var closed=Guid.NewGuid();await owner.OperationAsync(closed,true);Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.EvidenceAsync(id,c.Etag,cmd with{OperationId=closed}))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&x.ActionCode=="actual-record").ExecuteDeleteAsync();Assert.Equal(result,await owner.EvidenceAsync(id,c.Etag,cmd));
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-plate-mold").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));await h.Db.PlateMoldStocks.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"someone-else"));
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.OperationAsync(cmd.OperationId,false))).Status);
        await using var other=await Seed();await ActualSetup(other);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ActualUsage(other).ContextAsync(id))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_actual_usage_actual_HTTP_separate_actors_source_observe_recognize_apply_and_closed_public_inputs()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["Wms__ToolActual__EnvironmentId"]=ActualEnvironment.ToString(),["Wms__ToolActual__Mode"]="SIMULATION"});
        var cookies=await Login(host,h);
        var reviewerName=await h.Db.Sys_Users.Where(x=>x.Id==reviewer.User).Select(x=>x.UserName).SingleAsync();
        using var reviewerLogin=await host.Send(new HttpRequestMessage(HttpMethod.Post,"api/auth/login")
        {Content=JsonContent.Create(new{UserName=reviewerName,Password="Price1!Password",TenantCode=h.Tenant.ToString("N")})});
        Assert.Equal(HttpStatusCode.OK,reviewerLogin.StatusCode);
        var reviewCookies=reviewerLogin.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0].Split('=',2)).Where(x=>x[1].Length>0).ToDictionary(x=>x[0],x=>x[1]);
        Assert.NotEqual(cookies["cp6_at"],reviewCookies["cp6_at"]);var root=$"/api/wms/specials/v1/tools/{id}";
        async Task<ToolActualResultV1> Send(string route,object body,Dictionary<string,string>? identity=null)
        {
            var c=await owner.ContextAsync(id);var request=OperationsRequest(identity??cookies,HttpMethod.Post,root+route);request.Content=JsonContent.Create(body);request.Headers.TryAddWithoutValidation("If-Match",c.Etag);
            using var response=await host.Send(request);var text=await response.Content.ReadAsStringAsync();Assert.True(response.IsSuccessStatusCode,response.StatusCode+":"+text);
            return JsonDocument.Parse(text).RootElement.GetProperty("data").Deserialize<ToolActualResultV1>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        var input=new ToolActualEvidenceCommandV1(Guid.NewGuid(),"HTTP-LOG","L-1","1",40,DateTimeOffset.UtcNow,"Actual signed-off shop record text");
        var bad=OperationsRequest(cookies,HttpMethod.Post,root+"/actual-evidence",input,csrf:false);bad.Headers.TryAddWithoutValidation("If-Match",(await owner.ContextAsync(id)).Etag);using(var denied=await host.Send(bad))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        var injected=OperationsRequest(cookies,HttpMethod.Post,root+"/actual-evidence",new{input.OperationId,input.DocumentKey,input.LineKey,input.EventOrdinal,input.Shots,input.OccurredAt,input.Evidence,approved=true});injected.Headers.TryAddWithoutValidation("If-Match",(await owner.ContextAsync(id)).Etag);using(var denied=await host.Send(injected))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        var evidence=await Send("/actual-evidence",input);var occurrence=await Send("/use-occurrences/register",new ToolActualRegisterV1(Guid.NewGuid(),evidence.Fact,40,input.OccurredAt,"Register original event"));
        var observation=await Send("/observations",new ToolActualObservationV1(Guid.NewGuid(),occurrence.Fact,40,input.OccurredAt,"Real use without current qualification evidence"));var c=await owner.ContextAsync(id);
        await Send("/actual-recognition-policies",new ToolActualPolicyCommandV1(Guid.NewGuid(),"ENABLED",c.Site,true,DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddHours(1),"Authorized independent fact recognition"));
        c=await owner.ContextAsync(id);var decision=await Send($"/observations/{observation.Fact.Id}/recognition-decisions",new ToolActualRecognitionV1(Guid.NewGuid(),observation.Fact.Digest,"ACCEPT_FACT",c.Cycles.Single().Reference,[evidence.Fact],"Independent record and physical cycle checked"),reviewCookies);
        var app=await owner.ApplicationContextAsync(id,observation.Fact.Id);var result=await Send($"/observations/{observation.Fact.Id}/apply",new ToolActualApplyV1(Guid.NewGuid(),app.Revision,app.ContextRef,decision.Fact));Assert.Equal("APPLIED_ACTUAL_WITH_RESTRICTIONS",result.Outcome);Assert.Equal("40",(await owner.ContextAsync(id)).LifetimeShots);
        foreach(var path in new[]{root+"/actual-evidence-options?kind=USE",root+"/use-occurrence-options",$"/api/wms/specials/v1/tools/use-occurrences/{occurrence.Fact.Id}?recordVersion=1",$"/api/wms/specials/v1/tools/observations/{observation.Fact.Id}",$"/api/wms/specials/v1/tools/recognition-decisions/{decision.Fact.Id}"})
        {using var read=await host.Send(OperationsRequest(cookies,HttpMethod.Get,path));Assert.Equal(HttpStatusCode.OK,read.StatusCode);Assert.True(read.Headers.CacheControl!.NoStore);}
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_actual_usage_multi_cycle_counters_preserve_maintenance_originals_and_reject_overflow_atomically()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var reviewer=await ActualReviewer(h);var owner=ActualUsage(h);var inspector=ActualUsage(reviewer);
        await ActualPolicy(owner,id);var first=await ActualObserve(owner,id,100);var decision=await ActualRecognize(inspector,id,first.Observation.Fact,first.Evidence.Fact);
        await ActualApply(owner,id,first.Observation.Fact,decision.Fact);
        var reset=await MaintenanceResetReady(h,id);var released=await Maintenance(h).ResetAsync(id,reset.Context.Maintenance.Single().MaintenanceId,reset.Context.Etag,reset.Command);
        var savedReset=await Maintenance(h).FactAsync(id,released.Fact.Id);
        var next=await ActualObserve(owner,id,200);decision=await ActualRecognize(inspector,id,next.Observation.Fact,next.Evidence.Fact);await ActualApply(owner,id,next.Observation.Fact,decision.Fact);
        var c=await owner.ContextAsync(id);Assert.Equal("200",c.Cycles.Single(x=>x.Current).Shots);Assert.Equal("100",c.Cycles.Single(x=>!x.Current).Shots);Assert.Equal("300",c.LifetimeShots);
        Assert.Equal(JsonSerializer.Serialize(savedReset),JsonSerializer.Serialize(await Maintenance(h).FactAsync(id,released.Fact.Id)));Assert.Equal(released,(await Maintenance(h).OperationAsync(reset.Command.OperationId,false)).Result);
        var big=await ActualObserve(owner,id,int.MaxValue);decision=await ActualRecognize(inspector,id,big.Observation.Fact,big.Evidence.Fact);
        var overflow=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>ActualApply(owner,id,big.Observation.Fact,decision.Fact));Assert.Equal(422,overflow.Status);Assert.Equal("TOOL_ACTUAL_COUNTER_OVERFLOW",overflow.Message);
        c=await owner.ContextAsync(id);Assert.Equal(2,c.Uses.Length);Assert.Equal("200",c.Cycles.Single(x=>x.Current).Shots);Assert.Equal("300",c.LifetimeShots);Assert.Null(c.Observations.Single(x=>x.Original.Reference==big.Observation.Fact).Application);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_actual_usage_invalid_original_unicode_is_rejected_before_freeze_without_recording_a_replacement()
    {
        await using var h=await Seed();var id=await ActualSetup(h);var owner=ActualUsage(h);var c=await owner.ContextAsync(id);
        var cmd=new ToolActualEvidenceCommandV1(Guid.NewGuid(),"DOC","L","1",1,DateTimeOffset.UtcNow,"Original text");
        foreach(var input in new[]{cmd with{Evidence="\ud800"},cmd with{DocumentKey="\udc00"},cmd with{LineKey="\ud800"},cmd with{EventOrdinal="\udc00"}})
            Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.EvidenceAsync(id,c.Etag,input))).Status);
        Assert.Empty((await owner.ContextAsync(id)).Evidence);Assert.Equal("UNKNOWN",(await owner.OperationAsync(cmd.OperationId,false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_actual_usage_new_tenant_capabilities_do_not_grant_record_policy_recognize_or_apply()
    {
        await using var h=await Seed();await GrantRegistration(h);var codes=new[]{"actual-record","actual-policy","actual-recognize","actual-apply"};
        Assert.False(await h.Db.Sys_MenuActions.AnyAsync(x=>codes.Contains(x.ActionCode)));
        CP6.WebApi.Seed.WmsPermissionSeed.EnsureSeeded(h.Db);h.Db.ChangeTracker.Clear();
        Assert.Equal(4,await h.Db.Sys_MenuActions.CountAsync(x=>x.MenuId==443&&codes.Contains(x.ActionCode)));
        Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x=>codes.Contains(x.ActionCode)));
    }
}
