using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<NativeTimeoutOwnerV2> TimeoutOwner(Harness h,PersonalFormOwnerV2 form,DateTimeOffset now,Guid? serviceId=null)
    {
        var options=new NativeTimeoutOptionsV2{Enabled=true,ServiceId=serviceId??Guid.NewGuid(),HostKey="isolated-postgres-test-host"};
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_native_timeout_v2.registrations(tenant_id,environment_id,service_id,host_key,enabled)
            VALUES({h.Tenant},{ScopeFence(h).EnvironmentId},{options.ServiceId},{options.HostKey},true)
            """);
        return new(h.Db,ScopeFence(h),options,form,Decisions(h)){Clock=_=>Task.FromResult(now)};
    }
    private async Task TimeoutZone(Harness h)=>await h.Db.Sys_Tenants.Where(t=>t.Id==h.Tenant).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.TimeZoneId,"America/New_York"));
    [CurrentPermissionPostgresFact]
    public async Task NativeTimeout_frozen_utc_duration_reminds_once_per_occurrence_without_a_human_vote()
    {
        await using var h=await Seed();await TimeoutZone(h);
        var f=await NativeFormSetup(h,configure:g=>{g.Nodes.Single(n=>n.Id=="a").TimeoutHours=1;g.Nodes.Single(n=>n.Id=="a").TimeoutAction="remind";});
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={f.Task}").SingleAsync();
        var created=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT created_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={f.Task}").SingleAsync();
        Assert.Equal(TimeSpan.FromHours(1),due-created);
        var before=await f.Owner.TaskContextAsync(f.Task);var worker=await TimeoutOwner(h,f.Owner,due.AddSeconds(1));
        var claim=Assert.Single(await worker.ClaimAsync());Assert.Empty(await worker.ClaimAsync());
        var result=await worker.ExecuteAsync(claim);Assert.Equal("COMPLETED",result.State);Assert.Equal(result,await worker.ExecuteAsync(claim));
        Assert.Equal(before.Task.TaskRowVersion,result.OriginalTaskVersion);Assert.Equal(before.Task.InstanceRowVersion,result.OriginalInstanceVersion);
        Assert.Equal(claim.Epoch,result.ClaimEpoch);
        Assert.Equal(before.Task,(await f.Owner.TaskContextAsync(f.Task)).Task);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.votes WHERE task_id={f.Task}").SingleAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.outbox WHERE kind='TIMEOUT_REMINDER' AND task_id={f.Task}").SingleAsync());
        Assert.Empty(await worker.ClaimAsync());worker.Clock=_=>Task.FromResult(due.AddHours(1).AddSeconds(2));
        var next=Assert.Single(await worker.ClaimAsync());Assert.Equal(2,next.Occurrence);Assert.NotEqual(claim.ExecutionId,next.ExecutionId);
        Assert.Equal("COMPLETED",(await worker.ExecuteAsync(next)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeTimeout_expired_claim_cannot_commit_and_system_approval_is_not_a_human_vote()
    {
        await using var h=await Seed();await TimeoutZone(h);
        var f=await NativeFormSetup(h,configure:g=>{g.Nodes.Single(n=>n.Id=="a").TimeoutHours=1;g.Nodes.Single(n=>n.Id=="a").TimeoutAction="approve";});
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={f.Task}").SingleAsync();
        var worker=await TimeoutOwner(h,f.Owner,due.AddSeconds(1));var first=Assert.Single(await worker.ClaimAsync());
        worker.Clock=_=>Task.FromResult(due.AddSeconds(62));var second=Assert.Single(await worker.ClaimAsync());
        Assert.Equal(first.ExecutionId,second.ExecutionId);Assert.Equal(first.Epoch+1,second.Epoch);
        Assert.Equal("TIMEOUT_LEASE_LOST",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>worker.ExecuteAsync(first))).Message);
        Assert.Equal("COMPLETED",(await worker.ExecuteAsync(second)).State);
        Assert.Equal(FlowInstanceStatus.Approved,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.votes WHERE task_id={f.Task}").SingleAsync());
        Assert.Contains(await h.Db.Wf_FlowFormTos.AsNoTracking().ToArrayAsync(),t=>t.ActualHandlerId==second.ServiceId&&t.ExpectedHandlerId==h.User);
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeTimeout_human_winner_skips_effect_and_revoked_service_cannot_use_a_claim()
    {
        await using var h=await Seed();await TimeoutZone(h);
        var f=await NativeFormSetup(h,configure:g=>{g.Nodes.Single(n=>n.Id=="a").TimeoutHours=1;g.Nodes.Single(n=>n.Id=="a").TimeoutAction="reject";});
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={f.Task}").SingleAsync();
        var worker=await TimeoutOwner(h,f.Owner,due.AddSeconds(1));var claim=Assert.Single(await worker.ClaimAsync());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_native_timeout_v2.registrations SET enabled=false WHERE service_id={claim.ServiceId}");
        Assert.Equal("TIMEOUT_SERVICE_NOT_AUTHORIZED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>worker.ExecuteAsync(claim))).Message);
        var view=await f.Owner.TaskContextAsync(f.Task);var vote=new ApprovalTaskDecisionInputV2(Guid.NewGuid(),"approve","human acted first",view.Task.TaskRowVersion,view.Task.InstanceRowVersion);
        await f.Owner.DecideTaskAsync(f.Task,vote,ifMatch:"\""+vote.TaskRowVersion+"\"");
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_native_timeout_v2.registrations SET enabled=true WHERE service_id={claim.ServiceId}");
        Assert.Equal("TIMEOUT_LEASE_LOST",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>worker.ExecuteAsync(claim))).Message);
        Assert.Equal(FlowInstanceStatus.Approved,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.votes WHERE task_id={f.Task}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeTimeout_lost_commit_recovers_same_system_result_and_single_notification()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);await TimeoutZone(h);
        var f=await NativeFormSetup(h,configure:g=>{g.Nodes.Single(n=>n.Id=="a").TimeoutHours=1;g.Nodes.Single(n=>n.Id=="a").TimeoutAction="remind";});
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={f.Task}").SingleAsync();
        var worker=await TimeoutOwner(h,f.Owner,due.AddSeconds(1));var claim=Assert.Single(await worker.ClaimAsync());fault.Armed=true;
        Assert.Equal("TIMEOUT_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>worker.ExecuteAsync(claim))).Message);
        var original=await worker.ExecuteAsync(claim);Assert.Equal("COMPLETED",original.State);Assert.Equal(claim.ExecutionId,original.ExecutionId);
        Assert.Equal(original,await worker.ExecuteAsync(claim));
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.outbox WHERE kind='TIMEOUT_REMINDER' AND task_id={f.Task}").SingleAsync());
        var diagnostics=(await NativeInbox(h,f.Owner).DetailAsync(f.Instance)).Timeout!;
        Assert.Equal("OBSERVED_RUNNING",diagnostics.RuntimeState);Assert.Equal(original,Assert.Single(diagnostics.Deadlines).LastResult);
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeTimeout_APP_automatic_approval_is_blocked_without_human_evidence_and_alerted_once()
    {
        await using var h=await Seed();await TimeoutZone(h);
        await StartSetup(h,StartSchema.Replace("\"countersign\":\"all\"","\"countersign\":\"all\",\"timeoutHours\":1,\"timeoutAction\":\"approve\""));
        await Starter(h).RunAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync();
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={task.Id}").SingleAsync();
        var worker=await TimeoutOwner(h,PersonalForm(h),due.AddSeconds(1));var claim=Assert.Single(await worker.ClaimAsync());
        var result=await worker.ExecuteAsync(claim);Assert.Equal("BLOCKED_POLICY",result.State);Assert.Equal("BUSINESS_REQUIRES_HUMAN_EVIDENCE",result.Reason);
        Assert.Equal(result,await worker.ExecuteAsync(claim));Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE task_id={task.Id}").SingleAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_native_tasks_v2.notification_outbox WHERE kind='TIMEOUT_BLOCKED' AND task_id={task.Id}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeTimeout_escalation_keeps_actual_service_identity_and_new_principal_can_handle()
    {
        await using var h=await Seed();await TimeoutZone(h);Guid target=Guid.Empty;
        // The existing fixture creates the human role first; grant a second eligible
        // principal before the publication freezes the escalation policy.
        var f=await NativeFormSetup(h,configure:g=>{var n=g.Nodes.Single(n=>n.Id=="a");n.TimeoutHours=1;n.TimeoutAction="escalate";n.EscalateTo=target;},
            beforePublish:async()=>{target=await GrantDelegate(h,"timeout-target");});
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={f.Task}").SingleAsync();
        var worker=await TimeoutOwner(h,f.Owner,due.AddSeconds(1));var claim=Assert.Single(await worker.ClaimAsync());
        Assert.Equal("COMPLETED",(await worker.ExecuteAsync(claim)).State);
        Assert.Equal(target,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).AssigneeId);
        var deadline=Assert.Single((await NativeInbox(h,f.Owner).DetailAsync(f.Instance)).Timeout!.Deadlines);
        Assert.Equal(2,deadline.Revision);Assert.Null(deadline.DueAt);Assert.Equal("NONE",deadline.Policy.EscalationReset);
        Assert.Contains(await h.Db.Wf_FlowFormTos.AsNoTracking().ToArrayAsync(),t=>t.Status==FlowFormToStatus.Transferred&&t.ActualHandlerId==claim.ServiceId&&t.ExpectedHandlerId==h.User);
        TaskActor(h,target);var owner=PersonalForm(h with{User=target});var c=await owner.TaskContextAsync(f.Task);
        var input=new ApprovalTaskDecisionInputV2(Guid.NewGuid(),"approve","new principal human vote",c.Task.TaskRowVersion,c.Task.InstanceRowVersion);
        Assert.Equal(FlowInstanceStatus.Approved,(await owner.DecideTaskAsync(f.Task,input,ifMatch:"\""+input.TaskRowVersion+"\"")).Task!.InstanceStatus);
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeTimeout_error_edge_cancels_its_token_only_and_creates_real_recovery_task()
    {
        await using var h=await Seed();await TimeoutZone(h);
        var f=await NativeFormSetup(h,true,configure:g=>{
            var n=g.Nodes.Single(n=>n.Id=="a");n.TimeoutHours=1;n.TimeoutAction="errorEdge";
            g.Nodes.Add(new(){Id="recovery",Type="approval",ApproverStrategy="Specified",ApproverUserId=h.User});
            g.Edges.Add(new(){From="a",To="recovery",IsError=true});g.Edges.Add(new(){From="recovery",To="join"});});
        var timed=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.NodeId=="a");
        var sibling=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.NodeId=="b");
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={timed.Id}").SingleAsync();
        var worker=await TimeoutOwner(h,f.Owner,due.AddSeconds(1));var claim=Assert.Single(await worker.ClaimAsync());
        Assert.Equal("COMPLETED",(await worker.ExecuteAsync(claim)).State);
        Assert.Equal(3,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==timed.Id)).Status);
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==sibling.Id)).Status);
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.NodeId=="recovery")).Status);
        Assert.Empty((await NativeInbox(h,f.Owner).DetailAsync(f.Instance)).Votes);
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeTimeout_human_completion_after_claim_has_one_vote_and_a_skipped_timeout_receipt()
    {
        await using var h=await Seed();await TimeoutZone(h);
        var f=await NativeFormSetup(h,configure:g=>{var n=g.Nodes.Single(n=>n.Id=="a");n.TimeoutHours=1;n.TimeoutAction="reject";});
        var due=await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={f.Task}").SingleAsync();
        var worker=await TimeoutOwner(h,f.Owner,due.AddSeconds(1));var claim=Assert.Single(await worker.ClaimAsync());
        var c=await f.Owner.TaskContextAsync(f.Task);var vote=new ApprovalTaskDecisionInputV2(Guid.NewGuid(),"approve","human winner",c.Task.TaskRowVersion,c.Task.InstanceRowVersion);
        await f.Owner.DecideTaskAsync(f.Task,vote,ifMatch:"\""+vote.TaskRowVersion+"\"");
        var result=await worker.ExecuteAsync(claim);Assert.Equal("SKIPPED_ALREADY_HANDLED",result.State);Assert.Null(result.CommandId);
        Assert.Equal(1,(await NativeInbox(h,f.Owner).DetailAsync(f.Instance)).Votes.Length);Assert.Equal(result,await worker.ExecuteAsync(claim));
    }
}
