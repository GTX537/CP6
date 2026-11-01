using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes.Scheduling;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record RebindFixture(ArrangementFixture F,QueueHeadV1 Queue,RebindSimulationV1 Source,SchedPlanV1 Plan,RebindWorkV1 Work);
    private async Task<RebindFixture> RebindSetup(Harness h,string[]? deferred=null,string execution="REGISTERED")
    {
        var(f,q)=await QueueSetup(h);await EngineeringAction(h,"mes-planning-board","work.reconcile");var root=f.Openings[0];var op=root.Operations[0];
        var choice=new RebindChoiceV1(SchedReference("RES-CHOICE"),SchedReference("RES"),SchedReference("WO-REQUIREMENT"),SchedReference("PLM-TECHNICAL"),Span(0,2000),true,Now().AddHours(1),"RES-REBIND-1");
        var source=new RebindSimulationV1("SIMULATED",root.RootId,op.OperationId,[root.Observation.Guards[0].ClaimRef],[new(SchedReference("EXEC-WORK"),execution)],[choice],deferred??[]);
        await f.Owner.OpenRebindSimulationAsync(source);var c=await f.Owner.RebindContextAsync(root.RootId,op.OperationId);
        var input=Change(c.Capture,op.OperationId,24,26) with{Kind="RESOURCE_CHANGE",ResourceChoiceRef=choice.ChoiceRef};var plan=await f.Owner.PrepareAsync(input);
        Assert.True(plan.Eligible);var reply=await f.Owner.ApplyRebindAsync(Apply(plan));return new(f,q,source,plan,reply.Work);
    }
    private static RebindActionV1 RebindAction(RebindWorkV1 w)=>new(Guid.NewGuid(),w.Revision,"模拟生产验收换机");
    private static async Task<RebindWorkV1> RebindStep(RebindFixture f,RebindWorkV1 w)
    {await f.F.Owner.ContinueRebindAsync(w.WorkId,RebindAction(w));return await f.F.Owner.RebindWorkAsync(w.WorkId);}
    private static SchedulingStoreV1 RebindStore(Harness h)=>new(h.Db,ScopeFence(h).EnvironmentId);
    [CurrentPermissionPostgresFact]
    public async Task Rebind_commits_full_root_claim_and_binding_once_then_migrates_exact_membership()
    {
        await using var h=await Seed();var f=await RebindSetup(h);var w=f.Work;
        Assert.Equal("REBIND_OWNER_TRANSPORT_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>SchedOwner(h,false).ContinueRebindAsync(w.WorkId,RebindAction(w)))).Message);
        Assert.Equal(w.WorkId,(await SchedOwner(h,false).RebindWorkAsync(w.WorkId)).WorkId);
        for(var n=0;n<4;n++)w=await RebindStep(f,w);
        Assert.Equal("ASSIGNMENT_COMMITTED_CLEANUP_PENDING",w.State);Assert.True(w.NoNewExecution);Assert.NotNull(w.BindingResult);
        var root=(await f.F.Owner.CaptureAsync(w.RootId)).Root;var original=f.Plan.Capture.Root;var op=root.Operations.Single(o=>o.OperationId==w.OperationId);
        Assert.Equal("11",root.Expected.Revision);Assert.Equal("5",root.Expected.ControlEpoch);Assert.Equal("4",root.Expected.BindingRegistryRevision);
        Assert.Equal(original.Expected.EvidenceVersion,root.Expected.EvidenceVersion);Assert.Equal(original.Expected.SnapshotId,root.Expected.SnapshotId);
        Assert.Equal(f.Plan.Rebind!.NewBindingId,op.AssignmentRef!.Id);Assert.Equal("6",op.ScheduleRevision);Assert.Equal(original.Operations.Single(o=>o.OperationId!=w.OperationId),root.Operations.Single(o=>o.OperationId!=w.OperationId));
        Assert.Equal(original.Expected,f.Plan.RootApplyGroup.OriginalWoExpected);Assert.Equal("ADOPTED",(await RebindStore(h).RebindClaim(f.Plan.Rebind.NewClaimId,default))!.State);
        Assert.Equal(f.Plan.RootApplyGroup.ChangedOperations.Single().NewInterval,(await f.F.Owner.CaptureAsync(w.RootId)).Observation.Guards.Single(g=>g.OperationId==w.OperationId).UseInterval);
        var pending=await f.F.Owner.QueueAsync(f.Queue.SiteId,f.Queue.ResourceRef.Id);Assert.Contains(pending.Items,i=>i.Member.OperationId==w.OperationId&&i.State=="ASSIGNMENT_SUPERSEDED");
        w=await RebindStep(f,w);Assert.Equal("RELEASED",(await RebindStore(h).RebindClaim(f.Source.OldClaimRefs[0].Id,default))!.State);
        var migration=await f.F.Owner.MigrateRebindQueueAsync(w.WorkId);Assert.Equal("COMPLETED",migration.State);
        var old=await f.F.Owner.QueueAsync(f.Queue.SiteId,f.Queue.ResourceRef.Id);Assert.DoesNotContain(old.Items,i=>i.Member.OperationId==w.OperationId);
        var target=await f.F.Owner.QueueAsync(f.Queue.SiteId,f.Source.Choices[0].ResourceRef.Id);Assert.Single(target.Items);Assert.Equal(f.Plan.Rebind.NewQueueItemId,target.Items[0].Member.ItemId);Assert.Equal("WAITING",target.Items[0].State);
        Assert.Equal(Digest(migration),Digest(await f.F.Owner.MigrateRebindQueueAsync(w.WorkId)));Assert.Single(await f.F.Owner.HistoryAsync(w.RootId));
        var same=await f.F.Owner.ApplyRebindAsync(Apply(f.Plan));Assert.Equal(w.WorkId,same.Work.WorkId);Assert.True(same.Replayed);
        Assert.Equal("COMPLETED",(await f.F.Owner.RebindWorkAsync(w.WorkId)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Rebind_unknown_original_phase_is_looked_up_without_resend_and_late_reserve_is_closed_after_stop()
    {
        await using var h=await Seed();var f=await RebindSetup(h,["RESERVE_NEW"]);var w=await RebindStep(f,await RebindStep(f,f.Work));w=await RebindStep(f,w);
        Assert.Equal("RESERVE_NEW",w.PendingPhase);var request=await RebindStore(h).RebindRequest(w.WorkId,"RESERVE_NEW",default);
        for(var n=0;n<2;n++)w=await RebindStep(f,w);Assert.Null(await RebindStore(h).RebindClaim(f.Plan.Rebind!.NewClaimId,default));
        Assert.Equal(request.Key,(await RebindStore(h).RebindRequest(w.WorkId,"RESERVE_NEW",default)).Key);
        w=(await f.F.Owner.StopRebindAsync(w.WorkId,RebindAction(w))).Work;Assert.True(w.Stopped);
        await f.F.Owner.DeliverRebindSimulationAsync(w.WorkId);w=await f.F.Owner.ReconcileRebindAsync(w.WorkId);Assert.Equal("PREPARED_NO_EXECUTION",(await RebindStore(h).RebindClaim(f.Plan.Rebind.NewClaimId,default))!.State);
        w=await RebindStep(f,w);Assert.Equal("STOPPED_NO_BINDING_CHANGE",w.State);Assert.Equal("CLOSED_UNUSED",(await RebindStore(h).RebindClaim(f.Plan.Rebind.NewClaimId,default))!.State);
        Assert.Equal(f.Plan.Rebind.Context.OldBindingRef,(await f.F.Owner.CaptureAsync(w.RootId)).Root.Operations.Single(o=>o.OperationId==w.OperationId).AssignmentRef);Assert.Empty(await f.F.Owner.HistoryAsync(w.RootId));
        Assert.True(await RebindStore(h).RebindStopped(w.WorkId,default));
    }
    [CurrentPermissionPostgresFact]
    public async Task Rebind_unrelated_root_change_pauses_without_adopting_latest_five_domains()
    {
        await using var h=await Seed();var f=await RebindSetup(h);var w=await RebindStep(f,f.Work);var before=w.Accepted;
        var c=await f.F.Owner.CaptureAsync(w.RootId);var time=await f.F.Owner.PrepareAsync(Change(c,w.OperationId,23,26));await f.F.Owner.ApplyAsync(Apply(time));
        w=await RebindStep(f,w);Assert.Equal("PAUSED_STALE",w.State);Assert.Equal(before,w.Accepted);Assert.Null(await RebindStore(h).RebindClaim(f.Plan.Rebind!.NewClaimId,default));
        w=await RebindStep(f,w);Assert.Equal("PAUSED_STALE",w.State);
        w=(await f.F.Owner.StopRebindAsync(w.WorkId,RebindAction(w))).Work;w=await RebindStep(f,w);Assert.Equal("STOPPED_NO_BINDING_CHANGE",w.State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Rebind_EXEC_unknown_cannot_reserve_and_actuals_are_not_erased()
    {
        await using var h=await Seed();var f=await RebindSetup(h,execution:"MAY_EXECUTE");var w=await RebindStep(f,f.Work);
        Assert.Equal("REBIND_OLD_EXECUTION_UNRESOLVED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>RebindStep(f,w))).Message);
        w=await f.F.Owner.RebindWorkAsync(w.WorkId);Assert.Equal("CLOSE_OLD",w.PendingPhase);Assert.Null(await RebindStore(h).RebindClaim(f.Plan.Rebind!.NewClaimId,default));
        var observation=f.Plan.Capture.Observation;await f.F.Owner.PublishSimulationAsync(NewObservation(observation,observation.Guards.Select(g=>g.OperationId==w.OperationId?g with{ExecutionState="STARTED",ActualInterval=Span(24,25)}:g).ToArray()));
        Assert.Equal("REBIND_OLD_EXECUTION_UNRESOLVED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.F.Owner.DeliverRebindSimulationAsync(w.WorkId))).Message);
        Assert.Equal(Span(24,25),(await f.F.Owner.CaptureAsync(w.RootId)).Observation.Guards.Single(g=>g.OperationId==w.OperationId).ActualInterval);
        Assert.Equal("REBIND_EXECUTION_ACTUAL_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.F.Owner.ResolveRebindExecutionSimulationAsync(w.WorkId,f.Source.OldExecutions[0] with{State="CLOSED_NO_EXECUTION"}))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Rebind_binding_receipt_failure_rolls_back_all_participants_and_original_inbox_remains_recoverable()
    {
        var hook=new CreationWriteHook();await using var h=await Seed(hook);var f=await RebindSetup(h,["COMMIT_BINDING"]);var w=f.Work;
        for(var n=0;n<4;n++)w=await RebindStep(f,w);Assert.Equal("COMMIT_BINDING",w.PendingPhase);
        var before=(await f.F.Owner.CaptureAsync(w.RootId)).Root;var fired=false;
        hook.Before=c=>{if(c.CommandText.Contains("INSERT INTO cp6_scheduling_v1.rebind_receipts")){fired=true;throw new IOException("simulated lost owner receipt storage");}return Task.CompletedTask;};
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.F.Owner.DeliverRebindSimulationAsync(w.WorkId))).Status);Assert.True(fired);hook.Before=null;
        Assert.Equal(Digest(before),Digest((await f.F.Owner.CaptureAsync(w.RootId)).Root));Assert.Equal("PREPARED_NO_EXECUTION",(await RebindStore(h).RebindClaim(f.Plan.Rebind!.NewClaimId,default))!.State);
        Assert.Empty(await f.F.Owner.HistoryAsync(w.RootId));Assert.Null(await RebindStore(h).RebindMigration(w.WorkId,default));
        await f.F.Owner.DeliverRebindSimulationAsync(w.WorkId);w=await f.F.Owner.ReconcileRebindAsync(w.WorkId);Assert.NotNull(w.BindingResult);Assert.Single(await f.F.Owner.HistoryAsync(w.RootId));
    }
    [CurrentPermissionPostgresFact]
    public async Task Rebind_stop_wins_before_commit_and_commit_wins_before_stop_without_closing_adopted_claim()
    {
        await using var h=await Seed();var f=await RebindSetup(h,["COMMIT_BINDING"]);var w=f.Work;for(var n=0;n<4;n++)w=await RebindStep(f,w);
        w=(await f.F.Owner.StopRebindAsync(w.WorkId,RebindAction(w))).Work;await f.F.Owner.DeliverRebindSimulationAsync(w.WorkId);w=await f.F.Owner.ReconcileRebindAsync(w.WorkId);w=await RebindStep(f,w);
        Assert.Equal("STOPPED_NO_BINDING_CHANGE",w.State);Assert.Empty(await f.F.Owner.HistoryAsync(w.RootId));
        await using var other=await Seed();var second=await RebindSetup(other);var committed=second.Work;for(var n=0;n<4;n++)committed=await RebindStep(second,committed);
        var stopped=(await second.F.Owner.StopRebindAsync(committed.WorkId,RebindAction(committed))).Work;Assert.NotNull(stopped.BindingResult);Assert.False(stopped.Stopped);
        Assert.Equal("ADOPTED",(await RebindStore(other).RebindClaim(second.Plan.Rebind!.NewClaimId,default))!.State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Rebind_revocation_blocks_new_business_but_query_and_conservation_keep_original_registered_scope()
    {
        await using var h=await Seed();var f=await RebindSetup(h,["RESERVE_NEW"]);var w=f.Work;for(var n=0;n<3;n++)w=await RebindStep(f,w);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==301&&x.ActionCode=="change.apply").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.F.Owner.DeliverRebindSimulationAsync(w.WorkId))).Status);
        Assert.Equal("RESERVE_NEW",(await f.F.Owner.ReconcileRebindAsync(w.WorkId)).PendingPhase);
        w=await f.F.Owner.ConserveRebindSimulationAsync(w.WorkId);Assert.True(w.Stopped);await f.F.Owner.DeliverRebindSimulationAsync(w.WorkId);w=await f.F.Owner.ReconcileRebindAsync(w.WorkId);
        Assert.Equal("STOPPED_CLEANUP_PENDING",w.State);Assert.Equal("PREPARED_NO_EXECUTION",(await RebindStore(h).RebindClaim(f.Plan.Rebind!.NewClaimId,default))!.State);
        w=await f.F.Owner.ConserveRebindSimulationAsync(w.WorkId);Assert.Equal("CLOSE_UNUSED",w.PendingPhase);
        await f.F.Owner.DeliverRebindSimulationAsync(w.WorkId);w=await f.F.Owner.ReconcileRebindAsync(w.WorkId);Assert.Equal("STOPPED_NO_BINDING_CHANGE",w.State);
        Assert.Equal("CLOSED_UNUSED",(await RebindStore(h).RebindClaim(f.Plan.Rebind.NewClaimId,default))!.State);
        await using var other=await Seed();await ArrangementSetup(other);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>SchedOwner(other).RebindWorkAsync(w.WorkId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Rebind_actual_HTTP_contract_rejects_expected_replacement_and_uses_original_command_lookup()
    {
        await using var h=await Seed();var f=await RebindSetup(h);var settings=await DocumentHostSettings(new());settings["SchedulingSimulation__Enabled"]="true";
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var cookies=await Login(host,h);const string route="/api/mes/scheduling";
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route+$"/operations/{f.Work.OperationId}/rebind-context"))){Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("SIMULATED_EXEC_RES",Decode<RebindContextV1>(await response.Content.ReadAsByteArrayAsync()).DataKind);}
        var forged=JsonSerializer.SerializeToNode(RebindAction(f.Work),SchedulingValuesV1.Json)!.AsObject();forged["woExpected"]=JsonSerializer.SerializeToNode(f.Work.Accepted,SchedulingValuesV1.Json);
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,route+$"/rebind-works/{f.Work.WorkId}/continue",forged)))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        var input=RebindAction(f.Work);
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,route+$"/rebind-works/{f.Work.WorkId}/continue",input))){Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl?.NoStore);}
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route+"/commands/"+input.CommandId))){Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("rebindReply",await response.Content.ReadAsStringAsync());}
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Post,route+$"/change-plans/{f.Plan.PlanId}/apply",Apply(f.Plan)))){Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(f.Work.WorkId,Decode<RebindReplyV1>(await response.Content.ReadAsByteArrayAsync()).Work.WorkId);}
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==301&&x.ActionCode=="command.read").ExecuteDeleteAsync();
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route+$"/rebind-works/{f.Work.WorkId}")))Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==301&&x.ActionCode=="work.read").ExecuteDeleteAsync();
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route+$"/rebind-works/{f.Work.WorkId}")))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task Rebind_concurrent_stop_and_binding_have_one_decision_and_never_close_an_adopted_claim()
    {
        await using var h=await Seed();var f=await RebindSetup(h,["COMMIT_BINDING"]);var w=f.Work;for(var n=0;n<4;n++)w=await RebindStep(f,w);
        await using var peerDb=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=SchedOwner(h with{Db=peerDb});
        var actions=await Task.WhenAll(
            Task.Run(async()=>{await f.F.Owner.DeliverRebindSimulationAsync(w.WorkId);return "delivery";}),
            Task.Run(async()=>{await peer.StopRebindAsync(w.WorkId,RebindAction(w));return "stop";}));
        w=await f.F.Owner.ReconcileRebindAsync(w.WorkId);var claim=(await RebindStore(h).RebindClaim(f.Plan.Rebind!.NewClaimId,default))!;
        if(w.BindingResult!=null){Assert.False(w.Stopped);Assert.Equal("ADOPTED",claim.State);Assert.Single(await f.F.Owner.HistoryAsync(w.RootId));}
        else{Assert.True(w.Stopped);w=await RebindStep(f,w);Assert.Equal("STOPPED_NO_BINDING_CHANGE",w.State);Assert.Equal("CLOSED_UNUSED",(await RebindStore(h).RebindClaim(claim.Reference.Id,default))!.State);Assert.Empty(await f.F.Owner.HistoryAsync(w.RootId));}
    }
    [CurrentPermissionPostgresFact]
    public async Task Rebind_migration_preserves_current_unrelated_order_and_rolls_back_both_heads_on_failure()
    {
        var hook=new CreationWriteHook();await using var h=await Seed(hook);var f=await RebindSetup(h);var w=f.Work;for(var n=0;n<4;n++)w=await RebindStep(f,w);
        var store=RebindStore(h);var old=(await store.Queue(f.Queue.SiteId,f.Queue.ResourceRef.Id,default))!;
        var unrelated=old.Members.Where(m=>m.OperationId!=w.OperationId).Reverse().ToArray();var changed=old with{Revision=Next(old.Revision),Members=unrelated.Append(f.Plan.Rebind!.Context.OldMember).ToArray()};
        await store.SaveQueue(changed,new{kind="SIMULATED_CONCURRENT_UNRELATED_REORDER"},default);
        var fired=false;hook.Before=c=>{if(c.CommandText.Contains("INSERT INTO cp6_scheduling_v1.rebind_migrations")){fired=true;throw new IOException("migration receipt write fault");}return Task.CompletedTask;};
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.F.Owner.MigrateRebindQueueAsync(w.WorkId))).Status);Assert.True(fired);hook.Before=null;
        Assert.Equal(Digest(changed),Digest(await store.Queue(f.Queue.SiteId,f.Queue.ResourceRef.Id,default)));Assert.Null(await store.Queue(f.Queue.SiteId,f.Source.Choices[0].ResourceRef.Id,default));
        await f.F.Owner.MigrateRebindQueueAsync(w.WorkId);Assert.Equal(unrelated,(await store.Queue(f.Queue.SiteId,f.Queue.ResourceRef.Id,default))!.Members);
        w=await f.F.Owner.RebindWorkAsync(w.WorkId);w=await RebindStep(f,w);Assert.Equal("COMPLETED",w.State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Rebind_current_source_change_is_a_persisted_rejection_and_missing_old_member_is_not_migration_success()
    {
        await using var h=await Seed();var f=await RebindSetup(h);var w=f.Work;for(var n=0;n<4;n++)w=await RebindStep(f,w);var store=RebindStore(h);
        var head=(await store.Queue(f.Queue.SiteId,f.Queue.ResourceRef.Id,default))!;await store.SaveQueue(head with{Revision=Next(head.Revision),Members=head.Members.Where(m=>m.OperationId!=w.OperationId).ToArray()},new{kind="SIMULATED_MEMBERSHIP_LOSS"},default);
        Assert.Equal("REBIND_OLD_MEMBERSHIP_NOT_PROVEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.F.Owner.MigrateRebindQueueAsync(w.WorkId))).Message);
        Assert.Equal("PENDING",(await store.RebindMigration(w.WorkId,default))!.State);
        await using var other=await Seed();var next=await RebindSetup(other,["COMMIT_BINDING"]);var pending=next.Work;for(var n=0;n<4;n++)pending=await RebindStep(next,pending);
        var current=await next.F.Owner.CaptureAsync(pending.RootId);await next.F.Owner.PublishSimulationAsync(NewObservation(current.Observation,current.Observation.Guards.Select(g=>g.OperationId==pending.OperationId?g with{PurposeAllowed=false}:g).ToArray()));
        var receipt=await next.F.Owner.DeliverRebindSimulationAsync(pending.WorkId);Assert.Equal("PAUSED_REJECTED",receipt.Outcome);Assert.Equal("SCHED_SOURCE_CUT_CHANGED",receipt.FailureCode);
        pending=await next.F.Owner.ReconcileRebindAsync(pending.WorkId);Assert.Equal("PAUSED_REJECTED",pending.State);Assert.Empty(await next.F.Owner.HistoryAsync(pending.RootId));
        Assert.Equal("PREPARED_NO_EXECUTION",(await RebindStore(other).RebindClaim(next.Plan.Rebind!.NewClaimId,default))!.State);
    }

    [CurrentPermissionPostgresFact]
    public async Task Rebind_original_EXEC_resolution_and_RES_reservation_conflict_are_actual_owner_effects()
    {
        await using var h=await Seed();var first=await RebindSetup(h,execution:"UNKNOWN");var w=await RebindStep(first,first.Work);
        Assert.Equal("REBIND_OLD_EXECUTION_UNRESOLVED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>RebindStep(first,w))).Message);
        w=await first.F.Owner.RebindWorkAsync(w.WorkId);var original=(await RebindStore(h).RebindRequest(w.WorkId,"CLOSE_OLD",default)).Key;
        await first.F.Owner.ResolveRebindExecutionSimulationAsync(w.WorkId,first.Source.OldExecutions[0] with{State="CLOSED_NO_EXECUTION"});
        var proof=await first.F.Owner.DeliverRebindSimulationAsync(w.WorkId);Assert.Equal(original,proof.Key);w=await first.F.Owner.ReconcileRebindAsync(w.WorkId);w=await RebindStep(first,w);
        Assert.Equal("READY_TO_COMMIT",w.State);
        var root=first.F.Openings[1];var op=root.Operations[0];
        var source=first.Source with{RootId=root.RootId,OperationId=op.OperationId,OldClaimRefs=[root.Observation.Guards[0].ClaimRef],OldExecutions=[new(SchedReference("EXEC-WORK"),"REGISTERED")]};
        await first.F.Owner.OpenRebindSimulationAsync(source);var c=await first.F.Owner.RebindContextAsync(root.RootId,op.OperationId);
        var plan=await first.F.Owner.PrepareAsync(Change(c.Capture,op.OperationId,24,26) with{Kind="RESOURCE_CHANGE",ResourceChoiceRef=source.Choices[0].ChoiceRef});
        var reply=await first.F.Owner.ApplyRebindAsync(Apply(plan));var second=new RebindFixture(first.F,first.Queue,source,plan,reply.Work);
        var other=second.Work;for(var n=0;n<3;n++)other=await RebindStep(second,other);
        Assert.Equal("PAUSED_REJECTED",other.State);Assert.Equal("REBIND_RESOURCE_ALREADY_RESERVED",other.Receipts.Last().FailureCode);Assert.Null(await RebindStore(h).RebindClaim(second.Plan.Rebind!.NewClaimId,default));
        other=(await second.F.Owner.StopRebindAsync(other.WorkId,RebindAction(other))).Work;other=await RebindStep(second,other);Assert.Equal("STOPPED_NO_BINDING_CHANGE",other.State);
    }

}
