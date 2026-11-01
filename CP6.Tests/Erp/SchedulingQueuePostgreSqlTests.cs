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
    private async Task<(ArrangementFixture Fixture,QueueHeadV1 Head)> QueueSetup(Harness h,int operations=2,int? foreignFrom=null)
    {
        var f=await ArrangementSetup(h,2,operations,sharedResource:true,foreignFrom:foreignFrom);
        foreach(var action in new[]{"queue.read","queue.prepare","queue.apply"})await EngineeringAction(h,"mes-planning-board",action);
        return(f,await f.Owner.InitializeQueueSimulationAsync(f.Site.Id,f.Openings[0].Operations[0].ResourceRef!));
    }
    private static async Task<QueuePlanV1> QueuePlanFor(ArrangementFixture f,QueueHeadV1 head)
    {
        var c=await f.Owner.QueueAsync(head.SiteId,head.ResourceRef.Id,pageSize:100);var move=c.Items.First(i=>i.Member.RootId!=c.Items[0].Member.RootId);
        return await f.Owner.PrepareQueueAsync(head.ResourceRef.Id,new(head.ResourceRef,c.QueueRevision,c.CaptureId,move.Member.ItemId,"BEFORE",c.Items[0].Member.ItemId,null,c.ManifestDigest,"模拟生产验收队列顺序"));
    }
    private static SchedApplyV1 QueueApply(QueuePlanV1 p)=>new(Guid.NewGuid(),p.PlanId,p.Revision,p.Digest,p.Input.Reason);
    [CurrentPermissionPostgresFact]
    public async Task Queue_reorders_whole_manifest_without_changing_route_or_WO_and_recovers_original_result()
    {
        await using var h=await Seed();var(f,head)=await QueueSetup(h);
        var p=await QueuePlanFor(f,head);var command=QueueApply(p);var result=await f.Owner.ApplyQueueAsync(command);Assert.Equal("2",result.QueueRef.Version);
        var view=await f.Owner.QueueAsync(head.SiteId,head.ResourceRef.Id);Assert.Equal(p.NewOrder,view.Items.Select(i=>i.Member.ItemId));Assert.All(view.Items,i=>Assert.Equal("NO_EXECUTION_PERMISSION",i.ExecutionGateSummary));
        foreach(var opening in f.Openings){var c=await f.Owner.CaptureAsync(opening.RootId);Assert.Equal(opening.Expected,c.Root.Expected);Assert.Equal(opening.Operations.Select(o=>o.ScheduleRevision),c.Root.Operations.OrderBy(o=>o.RouteSortOrder).Select(o=>o.ScheduleRevision));Assert.Contains(c.Root.QueueHeads,q=>q==result.QueueRef);}
        foreach(var op in f.Openings.SelectMany(o=>o.Operations))Assert.Equal(op.RouteSortOrder,await h.Db.WorkOrderProcesses.Where(x=>x.Id==op.OperationId).Select(x=>x.SortOrder).SingleAsync());
        Assert.Equal(2,(await f.Owner.QueueHistoryAsync(head.SiteId,head.ResourceRef.Id)).Length);
        var same=await f.Owner.ApplyQueueAsync(command with{CommandId=Guid.NewGuid()});Assert.Equal(result.QueueRef,same.QueueRef);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==301&&x.ActionCode=="queue.apply").ExecuteDeleteAsync();Assert.True((await f.Owner.ApplyQueueAsync(command)).Replayed);
        Assert.Contains("queueReply",JsonSerializer.Serialize(await f.Owner.CommandAsync(command.CommandId),SchedulingValuesV1.Json));
        Assert.Equal("SCHED_COMMAND_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyQueueAsync(command with{Reason="other"}))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Queue_current_EXEC_lock_late_fact_history_duplicate_and_old_version_remain_consistent()
    {
        await using var h=await Seed();var(f,head)=await QueueSetup(h);var p=await QueuePlanFor(f,head);var move=p.Capture.Items.Single(i=>i.Member.ItemId==p.Input.MoveItemId);
        var fact=new QueueExecutionEventV1("SIMULATED",move.Member.RootId,move.Member.OperationId,SchedReference("EXEC"),"STARTED",Span(8,9),SchedDay.AddHours(8));
        var started=await f.Owner.PublishQueueExecutionSimulationAsync(fact);Assert.True(started.ReassessRequired);
        Assert.Equal("ITEM_LOCKED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyQueueAsync(QueueApply(p)))).Message);
        var view=await f.Owner.QueueAsync(head.SiteId,head.ResourceRef.Id);Assert.Contains(view.Items,i=>i.State=="LOCKED_IN_EXECUTION"&&i.ReassessRequired);Assert.Single(await f.Owner.QueueHistoryAsync(head.SiteId,head.ResourceRef.Id));
        var completed=fact with{FactRef=fact.FactRef with{Version="3",Digest="complete"},State="COMPLETED"};await f.Owner.PublishQueueExecutionSimulationAsync(completed);
        Assert.Equal("DUPLICATE",(await f.Owner.PublishQueueExecutionSimulationAsync(completed)).Disposition);
        Assert.Equal("OLDER_RECORDED_NOT_PROJECTED",(await f.Owner.PublishQueueExecutionSimulationAsync(fact with{FactRef=fact.FactRef with{Version="2",Digest="late"}})).Disposition);
        var done=await f.Owner.QueueAsync(head.SiteId,head.ResourceRef.Id);Assert.Equal("2",done.QueueRevision);Assert.DoesNotContain(done.Items,i=>i.Member.ItemId==move.Member.ItemId);Assert.Equal(2,(await f.Owner.QueueHistoryAsync(head.SiteId,head.ResourceRef.Id)).Length);
        Assert.Equal("QUEUE_EXECUTION_IDENTITY_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PublishQueueExecutionSimulationAsync(completed with{ActualInterval=Span(8,10)}))).Message);
        var c=await f.Owner.CaptureAsync(fact.RootId);Assert.Equal("COMPLETED",c.Observation.Guards.Single(g=>g.OperationId==fact.OperationId).ExecutionState);
        var other=c.Root.Operations.First(o=>o.OperationId!=fact.OperationId);
        Assert.Equal("QUEUE_EXECUTION_STREAM_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PublishQueueExecutionSimulationAsync(fact with{OperationId=other.OperationId,FactRef=fact.FactRef with{Version="4"}}))).Message);
        Assert.Equal("SCHED_EXECUTION_FACT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PublishSimulationAsync(NewObservation(c.Observation,c.Observation.Guards.Select(g=>g.OperationId==fact.OperationId?g with{ExecutionState="NOT_STARTED"}:g).ToArray())))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Queue_partial_scope_and_window_are_readable_but_cannot_reorder_hidden_members()
    {
        await using var h=await Seed();var(f,head)=await QueueSetup(h,foreignFrom:1);
        var whole=await f.Owner.QueueAsync(head.SiteId,head.ResourceRef.Id);Assert.True(whole.CompleteForWrite);
        var window=await f.Owner.QueueAsync(head.SiteId,head.ResourceRef.Id,window:Span(24,26));Assert.False(window.CompleteForWrite);Assert.Null(window.TotalCount);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="mes-planning-board").ExecuteUpdateAsync(x=>x.SetProperty(s=>s.ScopeType,1));
        var part=await f.Owner.QueueAsync(head.SiteId,head.ResourceRef.Id);Assert.Equal(2,part.Items.Length);Assert.False(part.CompleteForWrite);Assert.Null(part.TotalCount);Assert.Equal(new[]{1,2},part.Items.Select(i=>i.QueueOrdinal));
        Assert.DoesNotContain(f.Openings[1].RootId.ToString(),System.Text.Encoding.UTF8.GetString(Bytes(part)));
        var input=new QueuePlanRequestV1(head.ResourceRef,part.QueueRevision,part.CaptureId,part.Items[0].Member.ItemId,"END",null,null,part.ManifestDigest,"partial");
        Assert.Equal("QUEUE_COMPLETE_SCOPE_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PrepareQueueAsync(head.ResourceRef.Id,input))).Message);
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.QueueAsync(head.SiteId,head.ResourceRef.Id,whole.CaptureId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Queue_concurrent_plans_CAS_only_one_wins_and_time_plan_observes_queue_predecessor()
    {
        await using var h=await Seed();var(f,head)=await QueueSetup(h);var first=await QueuePlanFor(f,head);var second=await QueuePlanFor(f,head);
        var root=await f.Owner.CaptureAsync(f.Openings[0].RootId);var time=await f.Owner.PrepareAsync(Change(root,f.Openings[0].Operations[0].OperationId,8,10));
        await using var peerDb=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=SchedOwner(h with{Db=peerDb});
        async Task<string> Try(SchedulingOwnerV1 o,QueuePlanV1 p){try{return(await o.ApplyQueueAsync(QueueApply(p))).Outcome;}catch(OrderPriceCorrectionException e){return e.Message;}}
        var results=await Task.WhenAll(Try(f.Owner,first),Try(peer,second));Assert.Contains("COMMITTED",results);Assert.Contains("QUEUE_CHANGED",results);
        Assert.Equal("QUEUE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyAsync(Apply(time)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Queue_failed_receipt_write_rolls_back_head_root_versions_and_audit_then_same_key_recovers()
    {
        var hook=new CreationWriteHook();await using var h=await Seed(hook);var(f,head)=await QueueSetup(h);var p=await QueuePlanFor(f,head);var command=QueueApply(p);var fired=false;
        hook.Before=c=>{if(c.CommandText.Contains("INSERT INTO cp6_scheduling_v1.queue_applications")){fired=true;throw new IOException("queue application fault");}return Task.CompletedTask;};
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyQueueAsync(command))).Status);Assert.True(fired);hook.Before=null;
        Assert.Equal("1",(await f.Owner.QueueAsync(head.SiteId,head.ResourceRef.Id)).QueueRevision);Assert.Single(await f.Owner.QueueHistoryAsync(head.SiteId,head.ResourceRef.Id));
        foreach(var opening in f.Openings)Assert.All((await f.Owner.CaptureAsync(opening.RootId)).Root.QueueHeads,q=>Assert.Equal("1",q.Version));
        Assert.Contains("NOT_OBSERVED",JsonSerializer.Serialize(await f.Owner.CommandAsync(command.CommandId),SchedulingValuesV1.Json));
        Assert.Equal("2",(await f.Owner.ApplyQueueAsync(command)).QueueRef.Version);Assert.Equal(2,(await f.Owner.QueueHistoryAsync(head.SiteId,head.ResourceRef.Id)).Length);
    }
    [CurrentPermissionPostgresFact]
    public async Task Queue_position_technical_predecessor_resource_and_expired_capture_fail_at_boundary()
    {
        await using var h=await Seed();var(f,head)=await QueueSetup(h);var c=await f.Owner.QueueAsync(head.SiteId,head.ResourceRef.Id);
        var last=c.Items[1];var input=new QueuePlanRequestV1(head.ResourceRef,c.QueueRevision,c.CaptureId,last.Member.ItemId,"BEFORE",c.Items[0].Member.ItemId,null,c.ManifestDigest,"technical");
        Assert.Equal("QUEUE_ROUTE_PREDECESSOR_VIOLATED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PrepareQueueAsync(head.ResourceRef.Id,input))).Message);
        Assert.Equal("QUEUE_POSITION_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PrepareQueueAsync(head.ResourceRef.Id,input with{BeforeItemId=null}))).Message);
        var foreign=SchedReference("RES");Assert.Equal("QUEUE_CAPTURE_MISMATCH",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PrepareQueueAsync(foreign.Id,input with{ResourceRef=foreign}))).Message);
        var store=new SchedulingStoreV1(h.Db,ScopeFence(h).EnvironmentId);var captured=Decode<QueueCaptureV1>((await store.Arrangement(c.CaptureId,"queue-capture",default)).Body);var expired=captured with{CaptureId=Guid.NewGuid(),ExpiresAt=Now().AddMinutes(-1)};
        await store.SaveArrangement(expired.CaptureId,head.SiteId,"queue-capture",h.User,expired,default);
        Assert.Equal("QUEUE_CAPTURE_EXPIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.QueueAsync(head.SiteId,head.ResourceRef.Id,expired.CaptureId))).Message);
        var locked=c.Items[1];await f.Owner.PublishQueueExecutionSimulationAsync(new("SIMULATED",locked.Member.RootId,locked.Member.OperationId,SchedReference("EXEC"),"STARTED",Span(8,9),SchedDay.AddHours(8)));
        Assert.Equal("QUEUE_LOCKED_ANCHOR",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>QueuePlanFor(f,head))).Message);
        await using var other=await Seed();await QueueSetup(other);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>SchedOwner(other).QueueAsync(head.SiteId,head.ResourceRef.Id))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Queue_empty_capture_still_checks_original_object_scope_after_revocation()
    {
        await using var h=await Seed();var(f,head)=await QueueSetup(h,foreignFrom:1);
        foreach(var opening in f.Openings)
        foreach(var op in opening.Operations)await f.Owner.PublishQueueExecutionSimulationAsync(new("SIMULATED",opening.RootId,op.OperationId,SchedReference("EXEC"),"COMPLETED",Span(8,9),SchedDay.AddHours(9)));
        var empty=await f.Owner.QueueAsync(head.SiteId,head.ResourceRef.Id);Assert.Empty(empty.Items);Assert.Equal(0,empty.TotalCount);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="mes-planning-board").ExecuteUpdateAsync(x=>x.SetProperty(s=>s.ScopeType,1));
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.QueueAsync(head.SiteId,head.ResourceRef.Id,empty.CaptureId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Queue_missing_membership_does_not_reject_an_authoritative_execution_fact()
    {
        await using var h=await Seed();var(f,head)=await QueueSetup(h);var missing=head.Members[^1];var store=new SchedulingStoreV1(h.Db,ScopeFence(h).EnvironmentId);
        // Explicitly simulated asynchronous membership lag; no cross-resource migration success is claimed.
        await store.SaveQueue(head with{Revision="2",Members=head.Members[..^1]},new{kind="SIMULATED_MEMBERSHIP_LAG"},default);
        var fact=new QueueExecutionEventV1("SIMULATED",missing.RootId,missing.OperationId,SchedReference("EXEC"),"STARTED",Span(8,9),SchedDay.AddHours(8));
        var result=await f.Owner.PublishQueueExecutionSimulationAsync(fact);Assert.Equal("PROJECTED",result.Disposition);Assert.True(result.ReassessRequired);
        Assert.Equal("STARTED",(await f.Owner.CaptureAsync(missing.RootId)).Observation.Guards.Single(g=>g.OperationId==missing.OperationId).ExecutionState);
        Assert.Equal("QUEUE_MEMBERSHIP_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.QueueAsync(head.SiteId,head.ResourceRef.Id))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Queue_actual_HTTP_paging_plan_apply_original_read_and_rejects_client_members()
    {
        await using var h=await Seed();var(f,head)=await QueueSetup(h,25);var settings=await DocumentHostSettings(new());settings["SchedulingSimulation__Enabled"]="true";
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var cookies=await Login(host,h);const string route="/api/mes/scheduling";QueueViewV1 page;
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route+$"/resources/{head.ResourceRef.Id}/queue?siteId={head.SiteId}"))){Assert.Equal(HttpStatusCode.OK,r.StatusCode);page=Decode<QueueViewV1>(await r.Content.ReadAsByteArrayAsync());Assert.Equal(20,page.Items.Length);Assert.Equal(50,page.TotalCount);}
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route+$"/resources/{head.ResourceRef.Id}/queue?siteId={head.SiteId}&windowFrom=2026-11-01T01:30:00&windowTo=2026-11-01T03:30:00")))Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route+$"/resources/{head.ResourceRef.Id}/queue?siteId={head.SiteId}&captureId={page.CaptureId}&cursor=40"))){Assert.Equal(HttpStatusCode.OK,r.StatusCode);var last=Decode<QueueViewV1>(await r.Content.ReadAsByteArrayAsync());Assert.Equal(page.ManifestDigest,last.ManifestDigest);Assert.Equal(10,last.Items.Length);}
        var p=await QueuePlanFor(f,head);var command=QueueApply(p);var forged=JsonSerializer.SerializeToNode(command,SchedulingValuesV1.Json)!.AsObject();forged["members"]=new System.Text.Json.Nodes.JsonArray();
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Post,route+$"/queue-plans/{p.PlanId}/apply",forged)))Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Post,route+$"/queue-plans/{p.PlanId}/apply",command))){Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Equal("2",Decode<QueueReplyV1>(await r.Content.ReadAsByteArrayAsync()).QueueRef.Version);Assert.True(r.Headers.CacheControl?.NoStore);}
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route+"/commands/"+command.CommandId))){Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Contains("queueReply",await r.Content.ReadAsStringAsync());}
    }
}
