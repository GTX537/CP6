using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task NativeTimeoutRace_two_worker_connections_claim_one_original_slot()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);await TimeoutZone(h);
        var f=await NativeFormSetup(h,configure:g=>{var n=g.Nodes.Single(n=>n.Id=="a");n.TimeoutHours=1;n.TimeoutAction="remind";});
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={f.Task}").SingleAsync();
        var service=Guid.NewGuid();var worker=await TimeoutOwner(h,f.Owner,due.AddSeconds(1),service);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hook.Before=async command=>{if(command.CommandText.Contains("INSERT INTO cp6_native_timeout_v2.claims")){entered.TrySetResult();await release.Task;}};
        var first=worker.ClaimAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var other=h with{Db=peer};
        var rival=new NativeTimeoutOwnerV2(peer,ScopeFence(other),new(){Enabled=true,ServiceId=service,HostKey="isolated-postgres-test-host"},PersonalForm(other),Decisions(other))
            {Clock=_=>Task.FromResult(due.AddSeconds(1))};
        var second=rival.ClaimAsync();
        try{await Task.Delay(100);Assert.False(second.IsCompleted);}finally{release.TrySetResult();}
        Assert.Single(await first);Assert.Empty(await second);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_native_timeout_v2.claims WHERE task_id={f.Task}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeTimeoutRace_suspended_before_sign_defers_original_slot_until_parent_resumes()
    {
        await using var h=await Seed();await TimeoutZone(h);
        var f=await NativeFormSetup(h,configure:g=>{var n=g.Nodes.Single(n=>n.Id=="a");n.TimeoutHours=1;n.TimeoutAction="remind";});
        await NativeOperationPermissions(h);var target=await GrantDelegate(h);
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={f.Task}").SingleAsync();
        var worker=await TimeoutOwner(h,f.Owner,due.AddSeconds(1));var claim=Assert.Single(await worker.ClaimAsync());
        var context=await f.Owner.TaskContextAsync(f.Task);
        var added=await f.Owner.AddSignTaskAsync(f.Task,PersonalCommand(new NativeAddSignInputV2(f.Task,target,"before","before sign after claim",context.Task.TaskRowVersion),"before-timeout",context.Task.InstanceRowVersion));
        Assert.Equal("DEFERRED_STATE",(await worker.ExecuteAsync(claim)).State);Assert.Empty(await worker.ClaimAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_native_timeout_v2.results WHERE execution_id={claim.ExecutionId}").SingleAsync());
        var child=added.Result!.Value.GetProperty("task").GetProperty("taskId").GetGuid();
        TaskActor(h,target);var reviewer=PersonalForm(h with{User=target});var c=await reviewer.TaskContextAsync(child);
        var vote=NativeVote(c,"before sign complete","approve");
        await DecideNative(reviewer,child,vote);TaskActor(h,h.User);
        var resumed=Assert.Single(await worker.ClaimAsync());Assert.Equal(claim.ExecutionId,resumed.ExecutionId);Assert.Equal(claim.Occurrence,resumed.Occurrence);
        Assert.Equal("COMPLETED",(await worker.ExecuteAsync(resumed)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeTimeoutRace_worker_and_human_two_connections_have_one_terminal_result()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);await TimeoutZone(h);
        var f=await NativeFormSetup(h,configure:g=>{var n=g.Nodes.Single(n=>n.Id=="a");n.TimeoutHours=1;n.TimeoutAction="reject";});
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={f.Task}").SingleAsync();
        var worker=await TimeoutOwner(h,f.Owner,due.AddSeconds(1));var claim=Assert.Single(await worker.ClaimAsync());
        var c=await f.Owner.TaskContextAsync(f.Task);var vote=NativeVote(c,"concurrent human","approve");
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hook.Before=async command=>{if(command.CommandText.Contains("INSERT INTO cp6_personal_form_v2.outbox")){entered.TrySetResult();await release.Task;}};
        var executing=worker.ExecuteAsync(claim);await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var human=PersonalForm(h with{Db=peer});
        var competing=Record.ExceptionAsync(()=>DecideNative(human,f.Task,vote));
        try{await Task.Delay(100);Assert.False(competing.IsCompleted);}finally{release.TrySetResult();}
        Assert.Equal("COMPLETED",(await executing).State);Assert.IsType<ScopeAuthorizationExceptionV1>(await competing);
        Assert.Equal(2,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);
        Assert.Empty((await NativeInbox(h,f.Owner).DetailAsync(f.Instance)).Votes);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_native_timeout_v2.results WHERE execution_id={claim.ExecutionId}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeTimeoutRace_expiry_at_final_boundary_rolls_back_task_notification_and_receipt()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);await TimeoutZone(h);
        var f=await NativeFormSetup(h,configure:g=>{var n=g.Nodes.Single(n=>n.Id=="a");n.TimeoutHours=1;n.TimeoutAction="approve";});
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={f.Task}").SingleAsync();
        var worker=await TimeoutOwner(h,f.Owner,due.AddSeconds(1));var claim=Assert.Single(await worker.ClaimAsync());
        hook.Before=command=>{if(command.CommandText.Contains("INSERT INTO cp6_personal_form_v2.outbox"))worker.Clock=_=>Task.FromResult(due.AddSeconds(62));return Task.CompletedTask;};
        Assert.Equal("TIMEOUT_LEASE_LOST",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>worker.ExecuteAsync(claim))).Message);
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.commands WHERE id={claim.ExecutionId}").SingleAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.outbox WHERE command_id={claim.ExecutionId}").SingleAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_native_timeout_v2.results WHERE execution_id={claim.ExecutionId}").SingleAsync());
        hook.Before=null;var retry=Assert.Single(await worker.ClaimAsync());Assert.Equal(claim.ExecutionId,retry.ExecutionId);
        Assert.Equal("COMPLETED",(await worker.ExecuteAsync(retry)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeTimeoutRace_APP_error_edge_cannot_forge_a_terminal_business_decision()
    {
        await using var h=await Seed();await TimeoutZone(h);
        var schema=StartSchema.Replace("\"countersign\":\"all\"","\"countersign\":\"all\",\"timeoutHours\":1,\"timeoutAction\":\"errorEdge\"")
            .Replace("{\"from\":\"a\",\"to\":\"e\"}","{\"from\":\"a\",\"to\":\"e\"},{\"from\":\"a\",\"to\":\"e\",\"isError\":true}");
        await StartSetup(h,schema);await Starter(h).RunAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync();
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={task.Id}").SingleAsync();
        var worker=await TimeoutOwner(h,PersonalForm(h),due.AddSeconds(1));var claim=Assert.Single(await worker.ClaimAsync());
        var result=await worker.ExecuteAsync(claim);Assert.Equal("BLOCKED_POLICY",result.State);Assert.Equal("ERROR_EDGE_REQUIRES_BUSINESS_TERMINAL_AUTHORITY",result.Reason);
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);Assert.Equal(0,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE task_id={task.Id}").SingleAsync());
    }
}
