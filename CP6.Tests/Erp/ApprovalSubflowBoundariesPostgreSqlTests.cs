using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task App_subflow_empty_collection_or_runtime_input_error_uses_explicit_route_without_invented_children(bool invalid)
    {
        var schema=JsonSerializer.Serialize(new {start="s",nodes=new object[]{new{id="s",type="start"},
            new{id="call",type="subFlow",subFlowKey="child",subCollectionVar=invalid?"obligations.version":"obligations.local"},
            new{id="a",type="approval",approverStrategy="Role",approverRoleId=73},new{id="error",type="approval",approverStrategy="Role",approverRoleId=73},new{id="e",type="end"}},
            edges=new object[]{new{from="s",to="call"},new{from="call",to="a"},new{from="call",to="error",isError=true},new{from="a",to="e"},new{from="error",to="e"}}});
        await using var h=await Seed();var f=await SubflowSetup(h,schema);var started=await Starter(h).RunAsync(h.Tenant);
        Assert.Equal("RUNNING",started.State);Assert.Single(await h.Db.Wf_FlowInstances.ToArrayAsync());
        Assert.All(await h.Db.Wf_FlowTasks.ToArrayAsync(),x=>Assert.Equal(invalid?"error":"a",x.NodeId));
        foreach(var who in new[]{f.First,f.Second}) {var actor=DecisionActor(h,who);await Vote(actor,await DecisionInput(actor));}
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_runtime_v2.children WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(f.Intake.Payload,await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task App_subflow_missing_graph_guard_blocks_start_and_legacy_promotion_cannot_bypass_owner()
    {
        await using var h=await Seed();var f=await SubflowSetup(h);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_approval_runtime_v2.children DISABLE TRIGGER runtime_child_complete");
        try
        {
            Assert.Equal("APP_START_STORE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Starter(h).RunAsync(h.Tenant))).Message);
            Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
        }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_approval_runtime_v2.children ENABLE TRIGGER runtime_child_complete"); }
        var started=await Starter(h).RunAsync(h.Tenant);Assert.Equal(FlowEngine.ApprovalInstanceId(IntakeBody(f.Intake)),started.InstanceId);
        h.Db.ChangeTracker.Clear();var legacy=new Wf_FlowInstance{Id=Guid.NewGuid(),FlowKey="unmanaged",StarterId=h.User,Creator="legacy",Status=FlowInstanceStatus.Running};
        h.Db.Wf_FlowInstances.Add(legacy);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Wf_FlowInstances.Where(x=>x.Id==legacy.Id).ExecuteUpdateAsync(x=>x.SetProperty(i=>i.Creator,FlowEngine.ApprovalRuntimeV2)));
        Assert.NotEqual(FlowEngine.ApprovalRuntimeV2,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(x=>x.Id==legacy.Id)).Creator);
    }

    private static string SubflowCollection(string policy) => JsonSerializer.Serialize(new {start="s",
        nodes=new object[]{new{id="s",type="start"},new{id="sub",type="subFlow",subFlowKey="child",
            subCollectionVar="obligations.unknownOwners",subCompletionPolicy=policy,subVarsOutJson="{\"reviewedOwner\":\"$.item\"}"},new{id="e",type="end"}},
        edges=new[]{new{from="s",to="sub"},new{from="sub",to="e"}}});

    [CurrentPermissionPostgresFact]
    public async Task App_subflow_any_completes_root_and_cancels_other_children_without_changing_frozen_subject()
    {
        await using var h=await Seed();var f=await SubflowSetup(h,SubflowCollection("any"));var start=await Starter(h).RunAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var children=await h.Db.Wf_FlowInstances.AsNoTracking().Where(x=>x.ParentInstanceId==start.InstanceId).OrderBy(x=>x.SubIndex).ToArrayAsync();Assert.Equal(4,children.Length);
        foreach(var who in new[]{f.First,f.Second})
        {
            var actor=DecisionActor(h,who);var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x=>x.InstanceId==children[2].Id && x.AssigneeId==who);
            var result=await Vote(actor,await SubflowInput(actor,task.Id));Assert.Equal(who==f.Second,result.Result.DecisionId!=null);
        }
        var root=await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(x=>x.Id==start.InstanceId);Assert.Equal(1,root.Status);
        using var vars=JsonDocument.Parse(root.VarsJson);Assert.True(vars.RootElement.TryGetProperty("reviewedOwner",out var mapped));Assert.Equal(JsonValueKind.String,mapped.ValueKind);
        Assert.Equal(3,await h.Db.Wf_FlowInstances.CountAsync(x=>x.ParentInstanceId==root.Id && x.Status==3));
        Assert.False(await h.Db.Wf_FlowTasks.AnyAsync(x=>x.Status==0||x.Status==4));Assert.False(await h.Db.Wf_FlowTokens.AnyAsync(x=>x.Status==0));
        var original=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}").SingleAsync();
        Assert.Equal(f.Intake.Payload,original);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task App_subflow_all_rejection_cascades_and_two_connections_cannot_double_complete()
    {
        await using var h=await Seed();var f=await SubflowSetup(h,SubflowCollection("all"));await Starter(h).RunAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().Where(x=>x.AssigneeId==f.First).OrderBy(x=>x.Id).FirstAsync();
        var actor=DecisionActor(h,f.First);var command=await SubflowInput(actor,task.Id,"reject");
        await using var peerDb=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=actor with{Db=peerDb};
        var results=await Task.WhenAll(Vote(actor,command),Vote(peer,command));Assert.Equal(results[0],results[1]);Assert.NotNull(results[0].Result.DecisionId);
        Assert.False(await h.Db.Wf_FlowTasks.AnyAsync(x=>x.Status==0||x.Status==4));Assert.False(await h.Db.Wf_FlowTokens.AnyAsync(x=>x.Status==0));
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task App_subflow_child_current_authority_closed_command_legacy_writes_and_cross_tenant_are_enforced()
    {
        await using var h=await Seed();var f=await SubflowSetup(h);await Starter(h).RunAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var actor=DecisionActor(h,f.First);var command=await DecisionInput(actor);var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x=>x.Id==command.Task);
        var closed=await Decisions(actor).CloseAsync(task.Id,command.Input.CommandId);Assert.Equal("CLOSED",closed.State);
        Assert.Equal("COMMAND_CLOSED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Vote(actor,command))).Message);
        Assert.Equal("APP_DECISION_OWNER_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>new FlowEngine(h.Db,new ApproverResolver(h.Db)).ActAsync(task.Id,f.First,true))).Message);
        h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Wf_FlowTasks.Where(x=>x.Id==task.Id).ExecuteUpdateAsync(x=>x.SetProperty(t=>t.Status,1)));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_approval_runtime_v2.children WHERE tenant_id={h.Tenant}"));
        await using var foreign=await Seed();
        await RegisterScope(foreign);await TaskPermission(foreign,foreign.User);await EngineeringAction(foreign,"oa-inbox","query");
        Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Decisions(foreign).ContextAsync(task.Id))).Status);
        await h.Db.Sys_Users.Where(x=>x.Id==f.First).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Enable,false));
        await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Decisions(actor).ContextAsync(task.Id));
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x=>x.Id==task.Id)).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task App_subflow_terminal_failure_rolls_back_child_parent_and_outbox_then_unknown_commit_recovers()
    {
        var hook=new StartWriteHook();var fault=new CommitFault();await using var h=await Seed(hook,fault);
        var f=await SubflowSetup(h,nested:true);await Starter(h).RunAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var one=DecisionActor(h,f.First);await Vote(one,await DecisionInput(one));
        var two=DecisionActor(h,f.Second);var command=await DecisionInput(two);
        hook.Before=sql=>{if(sql.CommandText.Contains("INSERT INTO cp6_approval_decision_v2.decisions"))throw new InvalidOperationException("terminal persistence failed");return Task.CompletedTask;};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Vote(two,command));
        Assert.All(await h.Db.Wf_FlowInstances.AsNoTracking().ToArrayAsync(),x=>Assert.Equal(0,x.Status));
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x=>x.Id==command.Task)).Status);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync());
        var child=await h.Db.Wf_FlowInstances.AsNoTracking().Where(x=>x.ParentInstanceId!=null).Select(x=>x.Id).FirstAsync();
        hook.Before=async sql=>
        {
            if(!sql.CommandText.Contains("INSERT INTO cp6_approval_decision_v2.decisions"))return;
            hook.Before=null;
            // Simulate a missed child-state write after native execution but before final commit.
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Wf_FlowInstance\" SET \"Status\"=0 WHERE \"Id\"={child}");
        };
        Assert.NotNull(await Record.ExceptionAsync(()=>Vote(two,command)));
        Assert.All(await h.Db.Wf_FlowInstances.AsNoTracking().ToArrayAsync(),x=>Assert.Equal(0,x.Status));
        hook.Before=null;fault.Armed=true;
        Assert.Equal("OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Vote(two,command))).Message);
        var original=await Decisions(two).OriginalAsync(command.Input.CommandId);Assert.NotNull(original!.Result.DecisionId);Assert.Equal(original,await Vote(two,command));
        Assert.All(await h.Db.Wf_FlowInstances.AsNoTracking().ToArrayAsync(),x=>Assert.Equal(1,x.Status));
    }

    [CurrentPermissionPostgresFact]
    public async Task App_subflow_child_http_decision_preserves_five_field_wire_and_original_recovery()
    {
        await using var h=await Seed();var f=await SubflowSetup(h,nested:true);var start=await Starter(h).RunAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var password=new BCryptPasswordHasher(4).Hash("Price1!Password");await h.Db.Sys_Users.Where(x=>x.Id==f.First).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Password,password));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString()});
        using var login=await host.Send(new HttpRequestMessage(HttpMethod.Post,"api/auth/login"){Content=JsonContent.Create(new{UserName="child-reviewer-1",Password="Price1!Password",TenantCode=h.Tenant.ToString("N")})});
        Assert.Equal(HttpStatusCode.OK,login.StatusCode);
        var cookies=login.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0].Split('=',2)).Where(x=>x[1].Length>0).ToDictionary(x=>x[0],x=>x[1]);
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x=>x.AssigneeId==f.First);Assert.NotEqual(start.InstanceId,task.InstanceId);
        using var context=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/oa/tasks/{task.Id}/decision-context"));Assert.Equal(HttpStatusCode.OK,context.StatusCode);
        var view=JsonSerializer.Deserialize<ApprovalTaskDecisionViewV2>(await context.Content.ReadAsStringAsync(),ApprovalBindingOwnerV2.Json)!;
        var input=new ApprovalTaskDecisionInputV2(Guid.NewGuid(),"reject","Reject the pinned child review",view.TaskRowVersion,view.InstanceRowVersion);
        using var request=OperationsRequest(cookies,HttpMethod.Post,$"/api/oa/tasks/{task.Id}/decision");request.Headers.TryAddWithoutValidation("If-Match",context.Headers.ETag!.ToString());request.Content=JsonContent.Create(input);
        using var response=await host.Send(request);Assert.True(response.StatusCode==HttpStatusCode.OK,await response.Content.ReadAsStringAsync());
        var result=JsonSerializer.Deserialize<ApprovalTaskDecisionResultV2>(await response.Content.ReadAsStringAsync(),ApprovalBindingOwnerV2.Json)!;Assert.NotNull(result.Result.DecisionId);
        using var read=await host.Send(OperationsRequest(cookies,HttpMethod.Get,result.QueryRef));Assert.Equal(HttpStatusCode.OK,read.StatusCode);
        Assert.Equal(result,JsonSerializer.Deserialize<ApprovalTaskDecisionResultV2>(await read.Content.ReadAsStringAsync(),ApprovalBindingOwnerV2.Json));
        var payload=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        Assert.Equal(start.InstanceId,ApprovalDecisionWireV2.Parse(payload).Value.InstanceId);
    }
}
