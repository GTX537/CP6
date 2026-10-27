using System.Text.Json;
using System.Data.Common;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string StartSchema = """
        {"start":"s","nodes":[{"id":"s","type":"start"},{"id":"a","type":"approval","approverStrategy":"Role","approverRoleId":73,"countersign":"all"},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"a"},{"from":"a","to":"e"}]}
        """;
    private async Task<(IntakeFixture Intake, Guid Approver)> StartSetup(Harness h, string schema = StartSchema, bool approver = true)
    {
        var f = await IntakeSetup(h); var reviewer = Guid.NewGuid();
        if(approver)
        {
            h.Db.Sys_Roles.Add(new(){RoleId=73,RoleName="actual approval role"});
            h.Db.Sys_Users.Add(new(){Id=reviewer,UserName="actual-reviewer",Password="unused-fixture",RoleId=73});
            await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        }
        var head=await f.Flows.ReadHeadAsync("binding-flow");
        var draft=await f.Flows.CreateNextDraftAsync("binding-flow",NextDraftCommand(head.Version,f.Version));
        var edit=await f.Flows.SaveAsync("binding-flow",AuthoringCommand("runtime-edit",draft.ResourceVersion,"runtime",schema));
        var pub=await f.Flows.PublishAsync("binding-flow",PublicationCommand(await Preview(f.Flows,"binding-flow",edit),"runtime-publish"));
        var version=pub.Result!.Publication!.VersionId;
        var binding=await f.Binding.SaveAsync(f.Bound.Result.BindingId,BindingEtag(f.Bound),BindingBytes(
            BindingInput(version,f.ReceiveAt.AddSeconds(-1)) with{BizType="WMS_TOOL_DISCARD",SubjectKind="TOOL_DISPOSITION_REQUEST"}));
        f=f with{Version=version,Bound=binding};await f.Owner.SubmitAsync(f.Payload);return(f,reviewer);
    }
    private static ApprovalStartOwnerV2 Starter(Harness h)=>new(h.Db,ScopeFence(h));
    private sealed class StartWriteHook : DbCommandInterceptor
    {
        internal Func<DbCommand,Task>? Before;
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,CommandEventData eventData,
            InterceptionResult<int> result,CancellationToken cancellationToken=default)
        {if(Before!=null)await Before(command);return result;}
    }

    [CurrentPermissionPostgresFact]
    public async Task App_start_native_engine_graph_and_completion_are_atomic_pinned_and_original_ack_stays_immutable()
    {
        await using var h=await Seed();var(f,reviewer)=await StartSetup(h);var e=IntakeBody(f);
        var original=await f.Owner.OriginalAsync(e.CommandId);Assert.Equal("DISPATCH_PENDING",(await f.Owner.CurrentAsync(e.SubmissionId)).State);
        var started=await Starter(h).RunAsync(h.Tenant);Assert.Equal("RUNNING",started.State);Assert.NotNull(started.InstanceId);
        var instance=await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync();Assert.Equal(f.Version,instance.FlowDefVersionId);
        Assert.Equal(h.User,instance.StarterId);Assert.Equal(FlowInstanceStatus.Running,instance.Status);Assert.Null(instance.BizId);Assert.Null(instance.BizType);
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync();Assert.Equal(reviewer,task.AssigneeId);Assert.NotEqual(h.User,task.AssigneeId);
        Assert.Equal(FlowTaskStatus.Pending,task.Status);Assert.Equal(started.InstanceId,task.InstanceId);
        Assert.Equal(task.TokenId,(await h.Db.Wf_FlowTokens.AsNoTracking().SingleAsync()).Id);
        Assert.Single(await h.Db.Wf_FlowHistories.AsNoTracking().Where(x=>x.Action=="submit").ToArrayAsync());
        Assert.Single(await h.Db.Wf_FlowFormTos.AsNoTracking().ToArrayAsync());Assert.Single(await h.Db.Wf_FlowDatas.AsNoTracking().ToArrayAsync());
        var current=await f.Owner.CurrentAsync(e.SubmissionId);Assert.Equal("RUNNING",current.State);Assert.Equal(f.Version,current.FlowDefVersionId);Assert.Equal(e.Subject,current.Subject);
        Assert.Equal(original,await f.Owner.OriginalAsync(e.CommandId));Assert.Equal(original,(await f.Owner.SubmitAsync(f.Payload)).Ack);
        var duplicate=e with{CommandId=Guid.NewGuid(),SubmissionId=Guid.NewGuid(),OwnerOutboxId=Guid.NewGuid(),Reason="same accepted intent"};
        await f.Owner.SubmitAsync(ApprovalCanonicalV2.Bytes(duplicate));var alias=await f.Owner.CurrentAsync(duplicate.SubmissionId);
        Assert.Equal(instance.Id,alias.InstanceId);Assert.Equal(e.SubmissionId,alias.Alias!.CanonicalSubmissionId);
        Assert.Equal("IDLE",(await Starter(h).RunAsync(h.Tenant)).State);Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        Assert.Empty(await h.Db.Wf_FlowDefVersions.ToArrayAsync()); // No fake legacy definition was manufactured.
    }
    [CurrentPermissionPostgresFact]
    public async Task App_start_later_publication_and_binding_disable_cannot_change_accepted_pin()
    {
        await using var h=await Seed();var(f,_)=await StartSetup(h);
        await f.Binding.DisableAsync(f.Bound.Result.BindingId,BindingEtag(f.Bound),BindingBytes(new ApprovalBindingDisableV2(Guid.NewGuid(),"future intake off")));
        var head=await f.Flows.ReadHeadAsync("binding-flow");
        var draft=await f.Flows.CreateNextDraftAsync("binding-flow",NextDraftCommand(head.Version,f.Version,"after-draft"));
        var changed=await f.Flows.SaveAsync("binding-flow",AuthoringCommand("after-intake",draft.ResourceVersion,"different",AuthoringSchema));
        var next=await f.Flows.PublishAsync("binding-flow",PublicationCommand(await Preview(f.Flows,"binding-flow",changed),"after-publish"));
        var activation=(await f.Flows.ActivationPreviewAsync("binding-flow")).Current;
        await f.Flows.SetActivationAsync("binding-flow",ActivationCommand(activation,activation.PublishedVersionId!.Value,false,"disable-after-intake"));
        Assert.Equal("RUNNING",(await Starter(h).RunAsync(h.Tenant)).State);
        var actual=await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync();Assert.Equal(f.Version,actual.FlowDefVersionId);
        Assert.NotEqual(next.Result!.Publication!.VersionId,actual.FlowDefVersionId);Assert.Single(await h.Db.Wf_FlowTasks.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task App_start_two_native_connections_and_lost_commit_ack_recover_one_original_instance()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var(f,_)=await StartSetup(h);var e=IntakeBody(f);fault.Armed=true;
        Assert.Equal("START_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Starter(h).RunAsync(h.Tenant))).Message);
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=h with{Db=db};
        Assert.Equal("IDLE",(await Starter(peer).RunAsync(h.Tenant)).State);
        var current=await f.Owner.CurrentAsync(e.SubmissionId);Assert.Equal("RUNNING",current.State);Assert.Equal((await db.Wf_FlowInstances.SingleAsync()).Id,current.InstanceId);
        Assert.Single(await db.Wf_FlowInstances.ToArrayAsync());Assert.Single(await db.Wf_FlowTasks.ToArrayAsync());
        // A separate original intent exercises simultaneous first claims, not just sequential replay.
        await using var other=await Seed();var(otherF,_)=await StartSetup(other);
        await using var db2=server.Context(new TenantContext{CurrentTenantId=other.Tenant});var peer2=other with{Db=db2};
        var results=await Task.WhenAll(Starter(other).RunAsync(other.Tenant),Starter(peer2).RunAsync(other.Tenant));
        Assert.Equal(new[]{"IDLE","RUNNING"},results.Select(x=>x.State).Order());Assert.Single(await db2.Wf_FlowInstances.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task App_start_failure_after_actual_engine_save_rolls_back_graph_then_retries_same_id()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var(f,_)=await StartSetup(h);var e=IntakeBody(f);
        hook.Before=command=>{
            if(command.CommandText.Contains("INSERT INTO cp6_approval_start_v2.starts"))throw new InvalidOperationException("injected before completion");
            return Task.CompletedTask;
        };
        Assert.Equal("injected before completion",(await Assert.ThrowsAsync<InvalidOperationException>(()=>Starter(h).RunAsync(h.Tenant))).Message);
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());
        Assert.Empty(await h.Db.Wf_FlowTokens.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowHistories.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowFormTos.ToArrayAsync());
        Assert.Equal("DISPATCH_PENDING",(await f.Owner.CurrentAsync(e.SubmissionId)).State);
        hook.Before=null;var retry=await Starter(h).RunAsync(h.Tenant);Assert.Equal(e.SubmissionId,retry.SubmissionId);Assert.Equal(retry.InstanceId,(await f.Owner.CurrentAsync(e.SubmissionId)).InstanceId);
    }
    [CurrentPermissionPostgresFact]
    public async Task App_start_missing_role_and_no_human_path_suspend_never_approve_or_invent_a_reviewer()
    {
        await using var h=await Seed();var(f,_)=await StartSetup(h,approver:false);
        Assert.Equal("SUSPENDED",(await Starter(h).RunAsync(h.Tenant)).State);Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());
        Assert.Single(await h.Db.Wf_FlowTokens.Where(x=>x.Status==FlowTokenStatus.Active).ToArrayAsync());
        Assert.Contains(await h.Db.Wf_FlowHistories.ToArrayAsync(),x=>x.Action=="suspend"&&x.ActorId==Guid.Empty);
        await using var automatic=await Seed();var(autoF,_)=await StartSetup(automatic,AuthoringSchema);
        Assert.Equal("SUSPENDED",(await Starter(automatic).RunAsync(automatic.Tenant)).State);
        Assert.Empty(await automatic.Db.Wf_FlowTasks.ToArrayAsync());Assert.DoesNotContain(await automatic.Db.Wf_FlowHistories.ToArrayAsync(),x=>x.Action=="approve");
    }
    [CurrentPermissionPostgresFact]
    public async Task App_start_current_service_query_requires_scope_and_native_runtime_rejects_legacy_mutations()
    {
        await using var h=await Seed();var(f,reviewer)=await StartSetup(h);var e=IntakeBody(f);await Starter(h).RunAsync(h.Tenant);
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync();var engine=new FlowEngine(h.Db,new ApproverResolver(h.Db));
        Assert.Equal("APP_DECISION_OWNER_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>engine.ActAsync(task.Id,reviewer,true))).Message);
        Assert.Equal("APP_DECISION_OWNER_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>engine.AddSignAsync(task.Id,reviewer,h.User,"after"))).Message);
        Assert.Equal("APP_DECISION_OWNER_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>new TaskCenterService(h.Db).WithdrawAsync(task.InstanceId,h.User))).Message);
        var instance=await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync();
        Assert.Equal("APP_DECISION_OWNER_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>new ApprovalDispatcher([]).OnInstanceFinishedAsync(instance,true,reviewer,null))).Message);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Wf_FlowTasks.Where(x=>x.Id==task.Id).ExecuteUpdateAsync(x=>x.SetProperty(t=>t.Status,FlowTaskStatus.Approved)));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Wf_FlowTasks.Where(x=>x.Id==task.Id).ExecuteUpdateAsync(x=>x.SetProperty(t=>t.InstanceId,Guid.NewGuid())));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_approval_start_v2.starts WHERE tenant_id={h.Tenant}"));
        Assert.Equal("APP_START_SCOPE_MISMATCH",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Starter(h).RunAsync(Guid.NewGuid()))).Message);
        f.Authority.Identity=f.Authority.Identity with{CanSubmit=false};Assert.Equal("RUNNING",(await f.Owner.CurrentAsync(e.SubmissionId)).State);
        f.Authority.Identity=f.Authority.Identity with{Principal=Guid.NewGuid()};Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.CurrentAsync(e.SubmissionId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task App_start_native_authority_change_after_graph_save_aborts_instance_and_completion()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var(f,reviewer)=await StartSetup(h);
        hook.Before=async command=>{
            if(!command.CommandText.Contains("INSERT INTO cp6_approval_start_v2.starts"))return;hook.Before=null;
            await using var change=command.Connection!.CreateCommand();change.Transaction=command.Transaction;
            change.CommandText="UPDATE public.\"Sys_Users\" SET \"Enable\"=false WHERE \"Id\"=@id";change.Parameters.Add(new Npgsql.NpgsqlParameter("id",reviewer));await change.ExecuteNonQueryAsync();
        };
        Assert.Equal("SCOPE_AUTHORITY_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Starter(h).RunAsync(h.Tenant))).Message);
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());Assert.True(await h.Db.Sys_Users.AnyAsync(x=>x.Id==reviewer&&x.Enable));
    }
    [CurrentPermissionPostgresFact]
    public async Task App_start_database_rejects_orphan_native_instance_without_original_completion()
    {
        await using var h=await Seed();var(f,_)=await StartSetup(h);var e=IntakeBody(f);
        await using(var tx=await h.Db.Database.BeginTransactionAsync())
        {
            h.Db.Wf_FlowInstances.Add(new(){Id=e.SubmissionId,FlowKey="binding-flow",FlowDefVersionId=f.Version,StarterId=h.User,Creator=FlowEngine.ApprovalRuntimeV2});
            await h.Db.SaveChangesAsync();await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>tx.CommitAsync());
        }
        h.Db.ChangeTracker.Clear();Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
        Assert.Equal("RUNNING",(await Starter(h).RunAsync(h.Tenant)).State);
    }
    [Fact]
    public void App_start_exact_conditions_preserve_numeric_snapshot_and_never_silently_take_fallback()
    {
        Assert.True(FlowEngine.ExactCondition("nested.value > 9007199254740992", "{\"nested\":{\"value\":9007199254740993}}"));
        Assert.Throws<ScopeAuthorizationExceptionV1>(()=>FlowEngine.ExactCondition("absent > 1","{}"));
    }

    [CurrentPermissionPostgresFact]
    public async Task App_start_parallel_tasks_and_serial_stage_plan_use_existing_kernel_in_the_same_transaction()
    {
        var parallel="""
            {"start":"s","nodes":[{"id":"s","type":"start"},{"id":"p","type":"parallelSplit"},{"id":"a","type":"approval","approverStrategy":"Role","approverRoleId":73},{"id":"b","type":"approval","approverStrategy":"Role","approverRoleId":73},{"id":"j","type":"parallelJoin"},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"p"},{"from":"p","to":"a"},{"from":"p","to":"b"},{"from":"a","to":"j"},{"from":"b","to":"j"},{"from":"j","to":"e"}]}
            """;
        await using var h=await Seed();var(f,_)=await StartSetup(h,parallel);Assert.Equal("RUNNING",(await Starter(h).RunAsync(h.Tenant)).State);
        var tasks=await h.Db.Wf_FlowTasks.AsNoTracking().ToArrayAsync();Assert.Equal(2,tasks.Length);Assert.Equal(2,tasks.Select(x=>x.TokenId).Distinct().Count());
        Assert.Equal(2,await h.Db.Wf_FlowTokens.CountAsync(x=>x.Status==FlowTokenStatus.Active));
        Assert.Equal(2,await h.Db.Wf_FlowFormTos.CountAsync());Assert.Equal(2,await h.Db.Wf_FlowDatas.CountAsync());
        var serial="""
            {"start":"s","nodes":[{"id":"s","type":"start"},{"id":"a","type":"approval","stages":[{"kind":"fixed","approverStrategy":"Role","approverRoleId":73,"countersign":"all"},{"kind":"fixed","approverStrategy":"Role","approverRoleId":73,"countersign":"veto"}]},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"a"},{"from":"a","to":"e"}]}
            """;
        await using var other=await Seed();await StartSetup(other,serial);Assert.Equal("RUNNING",(await Starter(other).RunAsync(other.Tenant)).State);
        Assert.Equal(0,(await other.Db.Wf_FlowTasks.SingleAsync()).StageIndex);
        var token=await other.Db.Wf_FlowTokens.SingleAsync();Assert.Equal(2,JsonDocument.Parse(token.StagePlanJson!).RootElement.GetArrayLength());
    }
    [CurrentPermissionPostgresFact]
    public async Task App_start_revision_identity_does_not_collide_with_legacy_id_or_active_business_key()
    {
        await using var h=await Seed();var(f,_)=await StartSetup(h);var e=IntakeBody(f);
        var legacy=new Wf_FlowInstance{Id=e.SubmissionId,FlowKey="legacy",StarterId=h.User,BizType=e.Subject.BizType,BizId=e.Subject.SubjectId,Status=FlowInstanceStatus.Running};
        h.Db.Wf_FlowInstances.Add(legacy);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var current=await Starter(h).RunAsync(h.Tenant);Assert.Equal("RUNNING",current.State);Assert.NotEqual(legacy.Id,current.InstanceId);
        Assert.Equal(2,await h.Db.Wf_FlowInstances.CountAsync());
        h.Db.Wf_FlowInstances.Add(new(){FlowKey="other-legacy",StarterId=h.User,BizType=legacy.BizType,BizId=legacy.BizId,Status=FlowInstanceStatus.Running});
        await Assert.ThrowsAsync<DbUpdateException>(()=>h.Db.SaveChangesAsync());h.Db.ChangeTracker.Clear();
        Assert.Equal(current.InstanceId,(await f.Owner.CurrentAsync(e.SubmissionId)).InstanceId);
    }
    [CurrentPermissionPostgresFact]
    public async Task App_start_disabled_role_cannot_create_tasks_and_missing_runtime_guard_is_unavailable()
    {
        await using var h=await Seed();var(f,_)=await StartSetup(h);
        await h.Db.Sys_Roles.Where(x=>x.RoleId==73).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.Enable,false));
        Assert.Equal("SUSPENDED",(await Starter(h).RunAsync(h.Tenant)).State);Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Wf_FlowTask\" DISABLE TRIGGER approval_runtime_guard");
        try {Assert.Equal("APP_START_STORE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.CurrentAsync(IntakeBody(f).SubmissionId))).Message);}
        finally {await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Wf_FlowTask\" ENABLE TRIGGER approval_runtime_guard");}
    }
    [CurrentPermissionPostgresFact]
    public async Task App_start_cannot_adopt_unrelated_orphan_task_under_the_deterministic_instance_identity()
    {
        await using var h=await Seed();var(f,reviewer)=await StartSetup(h);var e=IntakeBody(f);
        h.Db.Wf_FlowTasks.Add(new(){InstanceId=FlowEngine.ApprovalInstanceId(e),NodeId="unrelated",AssigneeId=reviewer});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("APP_ENGINE_ID_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Starter(h).RunAsync(h.Tenant))).Message);
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());Assert.Equal("DISPATCH_PENDING",(await f.Owner.CurrentAsync(e.SubmissionId)).State);
        Assert.Equal("unrelated",(await h.Db.Wf_FlowTasks.SingleAsync()).NodeId);
    }
}
