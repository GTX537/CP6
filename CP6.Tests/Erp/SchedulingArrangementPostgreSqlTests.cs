using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes.Scheduling;
using CP6.Entity.DomainModels.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record ArrangementFixture(SchedulingOwnerV1 Owner,SchedRefV1 Site,SchedOpeningV1[] Openings,SchedPlanningInputV1[] Inputs);
    private async Task<ArrangementFixture> ArrangementSetup(Harness h,int roots=1,int operations=2,bool sharedResource=false,decimal? legacy=null,bool missingResource=false,int? foreignFrom=null)
    {
        await GrantPlanning(h);await RegisterScope(h);
        foreach(var action in new[]{"plan.prepare","change.apply","command.read","history.read","simulate-source","arrange.preview","preview.read","arrange.apply","work.read"})await EngineeringAction(h,"mes-planning-board",action);
        await EngineeringAction(h,"mes-work-order","change.schedule");var owner=SchedOwner(h);var site=SchedReference("SITE");var shared=SchedReference("RES");
        var openings=new List<SchedOpeningV1>();var inputs=new List<SchedPlanningInputV1>();
        for(var r=0;r<roots;r++)
        {
            var wo=new WorkOrder{WorkOrderNo="AR"+Guid.NewGuid().ToString("N")[..18],ProductCd="P1",Creator=foreignFrom!=null&&r>=foreignFrom?"other-owner":"price-owner",Status=1,ProductionQty=1};
            var rows=Enumerable.Range(0,operations).Select(n=>new WorkOrderProcess{WorkOrderNo=wo.WorkOrderNo,ProcessCd="P",TaskCd=n.ToString(),Creator="price-owner",SortOrder=n,LeadTime=n==0?legacy:null}).ToArray();
            h.Db.WorkOrders.Add(wo);h.Db.WorkOrderProcesses.AddRange(rows);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
            var snapshot=SchedReference("WO-SNAPSHOT");var resource=sharedResource&&r<2?shared:SchedReference("RES");var unit=SchedReference("MASTER-UOM");var basis=SchedReference("PLM-QUANTITY");
            var ops=rows.Select((p,n)=>new SchedOperationV1(p.Id,SchedReference("WO-OCCURRENCE"),n,"step "+n,"5",Span(24+r*operations*3+n*3,26+r*operations*3+n*3),missingResource?null:SchedReference("WO-ASSIGNMENT"),missingResource?null:resource,new("1",unit,basis),null,null)).ToArray();
            var guards=ops.Select((o,n)=>new SchedGuardV1(o.OperationId,SchedReference("SOURCE-WINDOW"),Span(0,2000),SchedReference("RES-CLAIM"),Span(0,2000),true,true,"NOT_STARTED",null,n==0?[]:[ops[n-1].OccurrenceRef],[])).ToArray();
            var opening=new SchedOpeningV1("SIMULATED",wo.Id,site,"America/New_York",snapshot,new("10","4","7","3",snapshot.Id),new(new(ops.First().Interval!.Start,ops.Last().Interval!.End),"NORMAL","opening"),ops,new("SIMULATED",SchedReference("SCHED-SOURCE"),wo.Id,guards));
            await owner.OpenSimulationAsync(opening);openings.Add(opening);
            var input=new SchedPlanningInputV1("SIMULATED",SchedReference("SCHED-PLANNING"),wo.Id,null,[],ops.Select(o=>new SchedPlanningOperationV1(o.OperationId,null,true,true,true)).ToArray());
            await owner.PublishPlanningSimulationAsync(input);inputs.Add(input);
        }
        return new(owner,site,openings.ToArray(),inputs.ToArray());
    }
    private static ArrangementRequestV1 ArrangementRequest(ArrangementCaptureV1 c,int hour=8)=>new(c.SiteId,c.CaptureId,c.Roots.SelectMany(r=>r.Capture.Root.Operations).Select(o=>o.OperationId).ToArray(),SchedDay.AddHours(hour),120,ArrangementAlgorithmV1.Algorithm,c.Roots.SelectMany(r=>r.Capture.Root.Operations).Where(o=>o.ResourceRef!=null).Select(o=>o.ResourceRef!).Distinct().ToArray(),true,"模拟生产验收完整批次");
    private static async Task<ArrangementPreviewV1> Arrange(ArrangementFixture f,int hour=8)=>await f.Owner.PrepareArrangementAsync(ArrangementRequest(await f.Owner.ArrangementCaptureAsync(f.Site.Id),hour));
    private static ArrangementApplyV1 ArrangementApply(ArrangementPreviewV1 p)=>new(Guid.NewGuid(),p.PreviewId,p.Revision,p.Digest,p.AtomicGroups.Where(g=>g.Eligible).Select(g=>g.Group.GroupId).ToArray(),p.ManifestDigest,p.Input.Reason);
    private static SchedPlanningInputV1 PlanningNext(SchedPlanningInputV1 x)=>x with{Reference=x.Reference with{Version=Next(x.Reference.Version),Digest=Digest(x)}};

    [CurrentPermissionPostgresFact]
    public async Task Arrangement_stable_manifest_legacy_duration_complete_root_and_original_application()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h,legacy:1.3m);var capture=await f.Owner.ArrangementCaptureAsync(f.Site.Id);var input=ArrangementRequest(capture);
        var one=await f.Owner.PrepareArrangementAsync(input);var two=await f.Owner.PrepareArrangementAsync(input);Assert.Equal(one.ManifestDigest,two.ManifestDigest);
        Assert.Equal(TimeSpan.FromHours(10),one.Items[0].NewInterval!.End-one.Items[0].NewInterval!.Start);Assert.Equal("LEGACY_ESTIMATE",one.Items[0].DurationBasis);
        Assert.Equal(TimeSpan.FromHours(2),one.Items[1].NewInterval!.End-one.Items[1].NewInterval!.Start);Assert.Single(one.AtomicGroups);Assert.Equal(2,one.AtomicGroups[0].Group.ChangedOperations.Length);
        var command=ArrangementApply(one);var result=await f.Owner.ApplyArrangementAsync(command);Assert.Equal(1,result.CommittedCount);Assert.Equal("COMMITTED",result.Outcome);
        var current=await f.Owner.CaptureAsync(f.Openings[0].RootId);Assert.Equal("11",current.Root.Expected.Revision);Assert.All(current.Root.Operations,o=>Assert.Equal("6",o.ScheduleRevision));
        Assert.Equal("4",current.Root.Expected.ControlEpoch);Assert.Equal(f.Openings[0].RootSchedule.Interval!.End,current.Root.Schedule.Interval!.End);
        var replay=await f.Owner.ApplyArrangementAsync(command);Assert.True(replay.Replayed);Assert.Equal(result.Groups[0].Result!.ApplicationRef,replay.Groups[0].Result!.ApplicationRef);
        var other=await f.Owner.ApplyArrangementAsync(command with{CommandId=Guid.NewGuid()});Assert.Equal(result.Groups[0].Result!.ApplicationRef,other.Groups[0].Result!.ApplicationRef);
        Assert.Equal("SCHED_COMMAND_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyArrangementAsync(command with{Reason="different"}))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==301&&x.ActionCode=="arrange.apply").ExecuteDeleteAsync();
        Assert.Equal(1,(await f.Owner.ApplyArrangementAsync(command)).CommittedCount);Assert.Contains("COMMITTED",JsonSerializer.Serialize(await f.Owner.CommandAsync(command.CommandId),SchedulingValuesV1.Json));
    }
    [CurrentPermissionPostgresFact]
    public async Task Arrangement_unknown_constraints_unassigned_locked_and_technical_duration_are_explicit()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h);var input=f.Inputs[0];
        await f.Owner.PublishPlanningSimulationAsync(PlanningNext(input) with{Operations=input.Operations.Select((o,n)=>n==0?o with{CalendarKnown=false}:o).ToArray()});
        var unknown=await Arrange(f);Assert.Contains(unknown.Items,i=>i.ReasonCodes.Contains("UNKNOWN_CONSTRAINT"));Assert.All(unknown.AtomicGroups,g=>Assert.False(g.Eligible));
        var current=PlanningNext(PlanningNext(input)) with{Operations=input.Operations.Select(o=>o with{TechnicalDuration=new(SchedReference("DURATION"),"5400000000")}).ToArray()};
        await f.Owner.PublishPlanningSimulationAsync(current);var technical=await Arrange(f);Assert.All(technical.Items,i=>Assert.Equal(TimeSpan.FromMinutes(90),i.NewInterval!.End-i.NewInterval!.Start));
        var observation=NewObservation(f.Openings[0].Observation,f.Openings[0].Observation.Guards.Select((g,n)=>n==0?g with{ExecutionState="STARTED",ActualInterval=Span(8,10)}:g).ToArray());await f.Owner.PublishSimulationAsync(observation);
        var locked=await Arrange(f);Assert.Contains(locked.Items,i=>i.Inclusion=="EXCLUDED"&&i.ReasonCodes.Contains("EXECUTION_LOCKED"));
        await using var other=await Seed();var unassigned=await ArrangementSetup(other,missingResource:true);var missing=await Arrange(unassigned);Assert.All(missing.Items,i=>Assert.Contains("UNASSIGNED",i.ReasonCodes));Assert.Empty(missing.AtomicGroups);
    }
    [CurrentPermissionPostgresFact]
    public async Task Arrangement_started_member_rejects_whole_root_and_waits_dependents_while_independent_root_commits()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h,3);await f.Owner.PublishPlanningSimulationAsync(PlanningNext(f.Inputs[1]) with{PredecessorRootIds=[f.Openings[0].RootId]});
        var p=await Arrange(f);var first=f.Openings[0];await f.Owner.PublishSimulationAsync(NewObservation(first.Observation,first.Observation.Guards.Select((g,n)=>n==1?g with{ExecutionState="STARTED",ActualInterval=Span(8,12)}:g).ToArray()));
        var result=await f.Owner.ApplyArrangementAsync(ArrangementApply(p));Assert.Equal(3,result.SubmittedCount);Assert.Equal(1,result.CommittedCount);Assert.Equal(1,result.RejectedCount);Assert.Equal(1,result.WaitingCount);
        Assert.Equal("10",(await f.Owner.CaptureAsync(first.RootId)).Root.Expected.Revision);Assert.Empty(await f.Owner.HistoryAsync(first.RootId));
        var stopped=await f.Owner.StopArrangementAsync(result.BatchId);Assert.Equal(1,stopped.CancelledCount);Assert.Equal(1,stopped.CommittedCount);
        Assert.Equal(1,(await f.Owner.ContinueArrangementAsync(result.BatchId)).CancelledCount);
    }
    [CurrentPermissionPostgresFact]
    public async Task Arrangement_mid_group_failure_preserves_unknown_and_recovers_same_group_without_repeating_independent_success()
    {
        var hook=new CreationWriteHook();await using var h=await Seed(hook);var f=await ArrangementSetup(h,3);await f.Owner.PublishPlanningSimulationAsync(PlanningNext(f.Inputs[1]) with{PredecessorRootIds=[f.Openings[0].RootId]});var p=await Arrange(f);var fired=false;
        hook.Before=c=>{if(!fired&&c.CommandText.Contains("INSERT INTO cp6_scheduling_v1.applications")&&c.Parameters.Cast<System.Data.Common.DbParameter>().Any(v=>Equals(v.Value,f.Openings[0].RootId))){fired=true;throw new IOException("failure after root write before receipt");}return Task.CompletedTask;};
        var command=ArrangementApply(p);var result=await f.Owner.ApplyArrangementAsync(command);Assert.True(fired);Assert.Equal(1,result.UnknownCount);Assert.Equal(1,result.CommittedCount);Assert.Equal(1,result.WaitingCount);
        Assert.Equal("10",(await f.Owner.CaptureAsync(f.Openings[0].RootId)).Root.Expected.Revision);Assert.Empty(await f.Owner.HistoryAsync(f.Openings[0].RootId));
        var original=await f.Owner.ApplyArrangementAsync(command);Assert.Equal(1,original.UnknownCount);
        hook.Before=null;var recovered=await f.Owner.ContinueArrangementAsync(result.BatchId);Assert.Equal(3,recovered.CommittedCount);Assert.Equal(0,recovered.UnknownCount);
        foreach(var opening in f.Openings){Assert.Equal("11",(await f.Owner.CaptureAsync(opening.RootId)).Root.Expected.Revision);Assert.Single(await f.Owner.HistoryAsync(opening.RootId));}
        Assert.Equal(result.Groups.Single(g=>g.State=="COMMITTED").Result!.ApplicationRef,recovered.Groups.Single(g=>g.GroupId==result.Groups.Single(x=>x.State=="COMMITTED").GroupId).Result!.ApplicationRef);
    }
    [CurrentPermissionPostgresFact]
    public async Task Arrangement_manifest_500_paginates_but_applies_all_confirmed_members_once()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h,operations:500);var p=await Arrange(f);Assert.Equal(500,p.ManifestCount);var page=await f.Owner.ArrangementPreviewAsync(p.PreviewId);Assert.Equal(20,page.Items.Length);Assert.Equal(20,page.NextCursor);Assert.Equal(p.ManifestDigest,page.ManifestDigest);
        var last=await f.Owner.ArrangementPreviewAsync(p.PreviewId,480);Assert.Equal(20,last.Items.Length);Assert.Null(last.NextCursor);
        var applied=await f.Owner.ApplyArrangementAsync(ArrangementApply(p));Assert.Equal(500,applied.Groups.Single().Result!.OperationResults.Length);
        Assert.Equal("11",(await f.Owner.CaptureAsync(f.Openings[0].RootId)).Root.Expected.Revision);
    }
    [CurrentPermissionPostgresFact]
    public async Task Arrangement_shared_resource_vacate_dependency_and_final_cross_root_conflict_are_enforced()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h,2,sharedResource:true);var preview=await Arrange(f);Assert.All(preview.AtomicGroups,g=>Assert.True(g.Eligible));
        var first=f.Openings[0];var capture=await f.Owner.CaptureAsync(first.RootId);
        var competing=await f.Owner.PrepareAsync(Change(capture,first.Operations[0].OperationId,8,10));await f.Owner.ApplyAsync(Apply(competing));
        var result=await f.Owner.ApplyArrangementAsync(ArrangementApply(preview));Assert.True(result.RejectedCount>=1);Assert.DoesNotContain(result.Groups,g=>g.State=="UNKNOWN");
        // A second root cannot occupy the first root's committed interval.
        var peer=f.Openings[1];var peerCapture=await f.Owner.CaptureAsync(peer.RootId);var sameTime=await f.Owner.PrepareAsync(Change(peerCapture,peer.Operations[0].OperationId,8,10));
        Assert.Equal("RESOURCE_INTERVAL_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyAsync(Apply(sameTime)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Arrangement_vacates_exact_predecessor_group_before_overlapping_old_slots_and_preserves_unchanged_operation()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h,2,sharedResource:true);
        // Make the earlier native root sort first; priority is a controlled current root input in this fixture.
        var store=new SchedulingStoreV1(h.Db,ScopeFence(h).EnvironmentId);var root=await store.Root(f.Openings[0].RootId,default);await store.SetRoot(root with{Schedule=root.Schedule with{Priority="URGENT"}},default);
        var p=await Arrange(f,24);Assert.Contains(p.AtomicGroups,g=>g.PredecessorGroupIds.Length>0);
        var result=await f.Owner.ApplyArrangementAsync(ArrangementApply(p));Assert.Equal(2,result.CommittedCount);Assert.Equal(0,result.WaitingCount);
        var first=await f.Owner.CaptureAsync(f.Openings[0].RootId);Assert.Equal("5",first.Root.Operations.Single(o=>o.OperationId==f.Openings[0].Operations[0].OperationId).ScheduleRevision);
        Assert.Equal("6",first.Root.Operations.Single(o=>o.OperationId==f.Openings[0].Operations[1].OperationId).ScheduleRevision);
    }
    [CurrentPermissionPostgresFact]
    public async Task Arrangement_concurrent_commands_share_one_original_group_and_current_planning_cut_is_final()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h);var p=await Arrange(f);
        await using var peerDb=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=SchedOwner(h with{Db=peerDb});
        var results=await Task.WhenAll(f.Owner.ApplyArrangementAsync(ArrangementApply(p)),peer.ApplyArrangementAsync(ArrangementApply(p)));
        Assert.All(results,r=>Assert.Equal(1,r.CommittedCount));Assert.Equal(results[0].Groups[0].Result!.ApplicationRef,results[1].Groups[0].Result!.ApplicationRef);Assert.Single(await f.Owner.HistoryAsync(f.Openings[0].RootId));
        var next=await Arrange(f,10);await f.Owner.PublishPlanningSimulationAsync(PlanningNext(f.Inputs[0]));
        var rejected=await f.Owner.ApplyArrangementAsync(ArrangementApply(next));Assert.Equal("SCHED_PLANNING_CUT_CHANGED",rejected.Groups.Single().ErrorCode);Assert.Equal("11",(await f.Owner.CaptureAsync(f.Openings[0].RootId)).Root.Expected.Revision);
        var expired=next with{PreviewId=Guid.NewGuid(),ExpiresAt=Now().AddMinutes(-1)};await new SchedulingStoreV1(h.Db,ScopeFence(h).EnvironmentId).SaveArrangement(expired.PreviewId,f.Site.Id,"preview",h.User,expired,default);
        Assert.Equal("SCHED_PREVIEW_EXPIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyArrangementAsync(ArrangementApply(expired)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Arrangement_partial_capture_exposes_only_opaque_busy_intervals_on_visible_resources()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h,3,sharedResource:true,foreignFrom:1);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="mes-planning-board").ExecuteUpdateAsync(x=>x.SetProperty(s=>s.ScopeType,1));
        var c=await f.Owner.ArrangementCaptureAsync(f.Site.Id);Assert.Equal("PARTIAL",c.Coverage);Assert.Single(c.Roots);Assert.NotEmpty(c.Unavailable);
        var bytes=System.Text.Encoding.UTF8.GetString(Bytes(c));Assert.DoesNotContain(f.Openings[1].RootId.ToString(),bytes);Assert.DoesNotContain(f.Openings[2].Operations[0].ResourceRef!.Id.ToString(),bytes);
        Assert.All(c.Unavailable,b=>Assert.True(SameResource(b.ResourceRef,f.Openings[0].Operations[0].ResourceRef)));
    }
    [CurrentPermissionPostgresFact]
    public async Task Arrangement_site_above_500_accepts_a_bounded_selection_and_keeps_other_roots_out_of_the_manifest()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h,2,operations:300);var capture=await f.Owner.ArrangementCaptureAsync(f.Site.Id);Assert.Equal(600,capture.Roots.Sum(r=>r.Capture.Root.Operations.Length));
        var input=ArrangementRequest(capture) with{OperationIds=f.Openings[0].Operations.Select(o=>o.OperationId).ToArray()};
        var preview=await f.Owner.PrepareArrangementAsync(input);Assert.Equal(300,preview.ManifestCount);Assert.Single(preview.AtomicGroups);
        var result=await f.Owner.ApplyArrangementAsync(ArrangementApply(preview));Assert.Equal(1,result.CommittedCount);
        Assert.Equal("10",(await f.Owner.CaptureAsync(f.Openings[1].RootId)).Root.Expected.Revision);
    }
    [CurrentPermissionPostgresFact]
    public async Task Arrangement_actual_HTTP_captures_previews_applies_and_recovers_batch_without_candidate_rewrite()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h);var settings=await DocumentHostSettings(new());settings["SchedulingSimulation__Enabled"]="true";
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var cookies=await Login(host,h);const string route="/api/mes/scheduling";
        ArrangementCaptureV1 capture;using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route+"/arrangement-captures?siteId="+f.Site.Id))){Assert.Equal(HttpStatusCode.OK,r.StatusCode);capture=Decode<ArrangementCaptureV1>(await r.Content.ReadAsByteArrayAsync());}
        ArrangementPreviewV1 preview;using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Post,route+"/arrangement-previews",ArrangementRequest(capture)))){Assert.Equal(HttpStatusCode.OK,r.StatusCode);preview=Decode<ArrangementPreviewV1>(await r.Content.ReadAsByteArrayAsync());}
        var command=ArrangementApply(preview);var forged=JsonSerializer.SerializeToNode(command,SchedulingValuesV1.Json)!.AsObject();forged["candidates"]=new System.Text.Json.Nodes.JsonArray();
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Post,route+"/arrangement-previews/"+preview.PreviewId+"/apply",forged)))Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Post,route+"/arrangement-previews/"+preview.PreviewId+"/apply",command))){Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Equal(1,Decode<ArrangementBatchResultV1>(await r.Content.ReadAsByteArrayAsync()).CommittedCount);Assert.True(r.Headers.CacheControl?.NoStore);}
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route+"/commands/"+command.CommandId))){Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Contains("COMMITTED",await r.Content.ReadAsStringAsync());}
    }
}
