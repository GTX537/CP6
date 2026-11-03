using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task NativeTimeoutPolicy_missing_original_receipt_rolls_back_the_system_effect_at_commit()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);await TimeoutZone(h);
        var f=await NativeFormSetup(h,configure:g=>{var n=g.Nodes.Single(n=>n.Id=="a");n.TimeoutHours=1;n.TimeoutAction="approve";});
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={f.Task}").SingleAsync();
        var worker=await TimeoutOwner(h,f.Owner,due.AddSeconds(1));var claim=Assert.Single(await worker.ClaimAsync());
        hook.Before=command=>{if(command.CommandText.StartsWith("INSERT INTO cp6_native_timeout_v2.results",StringComparison.Ordinal))command.CommandText="SELECT 1";return Task.CompletedTask;};
        var rejection=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>worker.ExecuteAsync(claim));
        Assert.Equal("23514",rejection.SqlState);Assert.Contains("TIMEOUT_COMMAND_RESULT_ATOMIC_REQUIRED",rejection.MessageText);
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.commands WHERE id={claim.ExecutionId}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeTimeoutPolicy_new_publication_and_tenant_timezone_never_rewrite_accepted_deadline()
    {
        await using var h=await Seed();await TimeoutZone(h);FlowSchema? graph=null;
        var f=await NativeFormSetup(h,configure:g=>{graph=g;var n=g.Nodes.Single(n=>n.Id=="a");n.TimeoutHours=1;n.TimeoutAction=" ReMiNd ";});
        var original=Assert.Single((await NativeInbox(h,f.Owner).DetailAsync(f.Instance)).Timeout!.Deadlines);
        Assert.Equal("UNPROVEN",(await NativeInbox(h,f.Owner).DetailAsync(f.Instance)).Timeout!.RuntimeState);
        var head=await f.Flow.ReadHeadAsync("native-inbox");var draft=await f.Flow.CreateNextDraftAsync("native-inbox",NextDraftCommand(head.Version,original.Policy.FlowVersionId));
        var node=graph!.Nodes.Single(n=>n.Id=="a");node.TimeoutAction="reject";node.TimeoutHours=24;
        var saved=await f.Flow.SaveAsync("native-inbox",AuthoringCommand("new-timeout",draft.ResourceVersion,"new policy",JsonSerializer.Serialize(graph,PersonalFormOwnerV2.Json)));
        var publication=(await f.Flow.PublishAsync("native-inbox",PublicationCommand(await Preview(f.Flow,"native-inbox",saved),"new-timeout-publication"))).Result!.Publication!;
        Assert.NotEqual(original.Policy.FlowVersionId,publication.VersionId);
        await h.Db.Sys_Tenants.Where(t=>t.Id==h.Tenant).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.TimeZoneId,"Asia/Shanghai"));
        var worker=await TimeoutOwner(h,f.Owner,original.DueAt!.Value.AddSeconds(1));var claim=Assert.Single(await worker.ClaimAsync());
        Assert.Equal("COMPLETED",(await worker.ExecuteAsync(claim)).State);
        var observed=Assert.Single((await NativeInbox(h,f.Owner).DetailAsync(f.Instance)).Timeout!.Deadlines);
        Assert.Equal(original.DueAt,observed.DueAt);Assert.Equal(original.Policy,observed.Policy);
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);Assert.Equal("remind",observed.Policy.Action);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeTimeoutPolicy_current_target_revoke_blocks_upgrade_without_losing_task_or_original_receipt()
    {
        await using var h=await Seed();await TimeoutZone(h);Guid target=Guid.Empty;
        var f=await NativeFormSetup(h,configure:g=>{var n=g.Nodes.Single(n=>n.Id=="a");n.TimeoutHours=1;n.TimeoutAction="escalate";n.EscalateTo=target;},
            beforePublish:async()=>{target=await GrantDelegate(h,"revoked-timeout-target");});
        var before=await f.Owner.TaskContextAsync(f.Task);
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={f.Task}").SingleAsync();
        var worker=await TimeoutOwner(h,f.Owner,due.AddSeconds(1));var claim=Assert.Single(await worker.ClaimAsync());
        await h.Db.Sys_Users.Where(u=>u.Id==target).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Enable,false));
        var result=await worker.ExecuteAsync(claim);Assert.Equal("BLOCKED_TARGET",result.State);Assert.Equal("TARGET_INELIGIBLE",result.Reason);
        Assert.Equal(before.Task,(await f.Owner.TaskContextAsync(f.Task)).Task);Assert.Equal(result,await worker.ExecuteAsync(claim));
        Assert.False((await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).TimeoutHandled);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.outbox WHERE command_id={claim.ExecutionId} AND kind='TIMEOUT_BLOCKED'").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeTimeoutPolicy_missing_writer_guard_rejects_claim_without_producing_effect()
    {
        await using var h=await Seed();await TimeoutZone(h);
        var f=await NativeFormSetup(h,configure:g=>{var n=g.Nodes.Single(n=>n.Id=="a");n.TimeoutHours=1;n.TimeoutAction="approve";});
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={f.Task}").SingleAsync();
        var worker=await TimeoutOwner(h,f.Owner,due.AddSeconds(1));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_native_timeout_v2.results DISABLE TRIGGER result_guard");
        try{Assert.Equal("TIMEOUT_WRITER_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>worker.ClaimAsync())).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_native_timeout_v2.results ENABLE TRIGGER result_guard");}
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_native_timeout_v2.claims WHERE task_id={f.Task}").SingleAsync());
    }
}
