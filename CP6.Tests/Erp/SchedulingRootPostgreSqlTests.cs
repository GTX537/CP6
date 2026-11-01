using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Mes.Scheduling;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 SchedAuthority(Harness h)
    {
        var catalog=new AuthorizationCatalogStoreV1();foreach(var key in new[]{"mes-planning-board","mes-plan-achievement","mes-work-order","mes-production-result"})
        {catalog.RegisterFields(key,[]);catalog.RegisterScope(key,key,[1,2,3,4,5],1);}catalog.Seal();
        return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private static SchedulingOwnerV1 SchedOwner(Harness h,bool enabled=true)=>new(h.Db,SchedAuthority(h),ScopeFence(h),Options.Create(new SchedulingSimulationOptionsV1{Enabled=enabled}));
    private sealed record SchedFixture(SchedulingOwnerV1 Owner,SchedOpeningV1 Opening);
    private static readonly DateTimeOffset SchedDay=new(2026,11,2,0,0,0,TimeSpan.Zero);
    private static SchedIntervalV1 Span(int from,int to)=>new(SchedDay.AddHours(from),SchedDay.AddHours(to));
    private static SchedRefV1 SchedReference(string owner)=>Ref(owner,Guid.NewGuid(),"1",new{kind="SIMULATED"});
    private async Task<SchedFixture> SchedSetup(Harness h)
    {
        await GrantPlanning(h);await RegisterScope(h);
        foreach(var action in new[]{"plan.prepare","change.apply","command.read","history.read","simulate-source"})await EngineeringAction(h,"mes-planning-board",action);
        await EngineeringAction(h,"mes-work-order","change.schedule");
        var root=new WorkOrder{WorkOrderNo="SC"+Guid.NewGuid().ToString("N")[..18],ProductCd="P1",Creator="price-owner",Status=1,ProductionQty=200.12345678m};
        var a=new WorkOrderProcess{WorkOrderNo=root.WorkOrderNo,ProcessCd="P",TaskCd="A",Creator="price-owner",SortOrder=1};
        var b=new WorkOrderProcess{WorkOrderNo=root.WorkOrderNo,ProcessCd="P",TaskCd="B",Creator="price-owner",SortOrder=2};
        h.Db.WorkOrders.Add(root);h.Db.WorkOrderProcesses.AddRange(a,b);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var snapshot=SchedReference("WO-SNAPSHOT");var resource=SchedReference("RES");var uom=SchedReference("MASTER-UOM");var basis=SchedReference("PLM-QUANTITY");
        SchedOperationV1 Op(WorkOrderProcess p,string revision,int from,int to)=>new(p.Id,SchedReference("WO-OCCURRENCE"),p.SortOrder,"same process / "+p.TaskCd,revision,Span(from,to),SchedReference("WO-ASSIGNMENT"),resource,new("200.12345678",uom,basis),null,null);
        var ops=new[]{Op(a,"5",8,10),Op(b,"8",14,16)};
        var observation=new SchedObservationV1("SIMULATED",SchedReference("SCHED-SOURCE"),root.Id,ops.Select((o,i)=>new SchedGuardV1(o.OperationId,SchedReference("SOURCE-WINDOW"),Span(0,48),SchedReference("RES-CLAIM"),Span(0,48),true,true,"NOT_STARTED",null,i==0?[]:[ops[0].OccurrenceRef],[])).ToArray());
        var opening=new SchedOpeningV1("SIMULATED",root.Id,SchedReference("SITE"),"America/New_York",snapshot,new("10","4","7","3",snapshot.Id),new(Span(8,16),"NORMAL","模拟期初"),ops,observation);
        var owner=SchedOwner(h);await owner.OpenSimulationAsync(opening);return new(owner,opening);
    }
    private static SchedApplyV1 Apply(SchedPlanV1 p)=>new(Guid.NewGuid(),p.PlanId,p.Revision,p.Digest,p.Input.Reason);
    private static SchedChangeRequestV1 Change(SchedCaptureV1 c,Guid operation,int from=9,int to=11)=>new(c.CaptureId,operation,c.Root.Operations.Single(o=>o.OperationId==operation).ScheduleRevision,c.Root.Expected,"TIME_ONLY",SchedDay.AddHours(from),SchedDay.AddHours(to),null,null,"生产验收模拟资料改期");
    private static async Task<SchedPlanV1> Plan(SchedFixture f,int index=0,int from=9,int to=11)=>await f.Owner.PrepareAsync(Change(await f.Owner.CaptureAsync(f.Opening.RootId),f.Opening.Operations[index].OperationId,from,to));
    private static SchedObservationV1 NewObservation(SchedObservationV1 source,SchedGuardV1[] guards)=>source with{Reference=source.Reference with{Version=Next(source.Reference.Version),Digest=Digest(guards)},Guards=guards};

    [CurrentPermissionPostgresFact]
    public async Task Scheduling_root_time_only_preserves_buffer_full_manifest_versions_actuals_and_original_result()
    {
        await using var h=await Seed();var f=await SchedSetup(h);var plan=await Plan(f);
        Assert.True(plan.Eligible);Assert.Equal(Span(8,16),plan.RootApplyGroup.NewRootSchedule.Interval);Assert.Single(plan.RootApplyGroup.UnchangedOperationRefs);
        var command=Apply(plan);var reply=await f.Owner.ApplyAsync(command);var after=await f.Owner.CaptureAsync(f.Opening.RootId);
        Assert.Equal(new("11","4","7","3",f.Opening.SnapshotRef.Id),after.Root.Expected);
        Assert.Equal("6",after.Root.Operations.Single(x=>x.OperationId==f.Opening.Operations[0].OperationId).ScheduleRevision);
        Assert.Equal("8",after.Root.Operations.Single(x=>x.OperationId==f.Opening.Operations[1].OperationId).ScheduleRevision);
        Assert.All(after.Root.Operations,x=>{Assert.Null(x.GoodQty);Assert.Null(x.DefectQty);Assert.Equal("200.12345678",x.PlanQty!.Value);});
        var repeated=await f.Owner.ApplyAsync(command);Assert.True(repeated.Replayed);Assert.Equal(Digest(reply.Result),Digest(repeated.Result));
        var samePlan=await f.Owner.ApplyAsync(command with{CommandId=Guid.NewGuid()});Assert.Equal(reply.Result.ApplicationRef,samePlan.Result.ApplicationRef);
        Assert.Single(await f.Owner.HistoryAsync(f.Opening.RootId));Assert.Equal("SCHED_COMMAND_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyAsync(command with{Reason="different"}))).Message);
        Assert.Equal(0,await h.Db.WorkOrderProcesses.SumAsync(p=>p.GoodQty));
    }
    [CurrentPermissionPostgresFact]
    public async Task Scheduling_root_expands_only_outward_and_rejects_source_or_predecessor_violation()
    {
        await using var h=await Seed();var f=await SchedSetup(h);var plan=await Plan(f,1,15,18);Assert.Equal(Span(8,18),plan.RootApplyGroup.NewRootSchedule.Interval);
        var reply=await f.Owner.ApplyAsync(Apply(plan));Assert.Equal("11",reply.Result.NewWoExpected.Revision);
        var blocked=await Plan(f,0,20,22);Assert.False(blocked.Eligible);Assert.Contains(blocked.Gates,x=>x.Code=="ROUTE_PREDECESSOR_VIOLATED");
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyAsync(Apply(blocked)));
        var sourceBlocked=await Plan(f,1,49,50);Assert.False(sourceBlocked.Eligible);Assert.Contains(sourceBlocked.Gates,x=>x.Code=="SOURCE_WINDOW_VIOLATED");
        Assert.Single(await f.Owner.HistoryAsync(f.Opening.RootId));
    }
    [CurrentPermissionPostgresFact]
    public async Task Scheduling_started_or_revoked_claim_after_prepare_rejects_whole_group()
    {
        await using var h=await Seed();var f=await SchedSetup(h);var plan=await Plan(f);
        var source=NewObservation(f.Opening.Observation,f.Opening.Observation.Guards.Select((g,i)=>i==0?g with{ExecutionState="STARTED"}:g).ToArray());
        await f.Owner.PublishSimulationAsync(source);
        Assert.Equal("OPERATION_STARTED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyAsync(Apply(plan)))).Message);
        source=NewObservation(source,source.Guards.Select((g,i)=>i==0?g with{ExecutionState="NOT_STARTED",ClaimValid=false}:g).ToArray());await f.Owner.PublishSimulationAsync(source);
        Assert.Equal("RESOURCE_CLAIM_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyAsync(Apply(plan)))).Message);
        var current=await f.Owner.CaptureAsync(f.Opening.RootId);Assert.Equal(f.Opening.Expected,current.Root.Expected);Assert.Empty(await f.Owner.HistoryAsync(f.Opening.RootId));
        Assert.Equal("SCHED_SOURCE_PREDECESSOR_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PublishSimulationAsync(f.Opening.Observation))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Scheduling_each_wo_domain_and_unchanged_member_are_final_compare_inputs()
    {
        await using var h=await Seed();var f=await SchedSetup(h);var plan=await Plan(f);var store=new SchedulingStoreV1(h.Db,ScopeFence(h).EnvironmentId);var root=plan.Capture.Root;
        var versions=new[]{root.Expected with{Revision="12"},root.Expected with{ControlEpoch="5"},root.Expected with{EvidenceVersion="8"},root.Expected with{BindingRegistryRevision="4"},root.Expected with{SnapshotId=Guid.NewGuid()}};
        foreach(var v in versions)
        {await store.SetRoot(root with{Expected=v},default);Assert.Equal("WO_CUT_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyAsync(Apply(plan)))).Message);}
        var altered=root with{Operations=root.Operations.Select(o=>o.OperationId==f.Opening.Operations[1].OperationId?o with{ScheduleRevision="9"}:o).ToArray()};
        await store.SetRoot(altered,default);Assert.Equal("SCHED_ROOT_MEMBERS_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyAsync(Apply(plan)))).Message);
        await store.SetRoot(root with{QueueHeads=[SchedReference("SCHED-QUEUE")]},default);
        Assert.Equal("QUEUE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyAsync(Apply(plan)))).Message);
        await store.SetRoot(root,default);Assert.Equal("COMMITTED",(await f.Owner.ApplyAsync(Apply(plan))).Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task Scheduling_mid_transaction_failure_rolls_back_root_operations_receipt_and_audit()
    {
        var hook=new CreationWriteHook();await using var h=await Seed(hook);var f=await SchedSetup(h);var plan=await Plan(f);var fired=false;
        hook.Before=c=>{if(c.CommandText.Contains("INSERT INTO cp6_scheduling_v1.commands")){fired=true;throw new IOException("after actual WO/SCHED writes");}return Task.CompletedTask;};
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyAsync(Apply(plan)));Assert.True(fired);hook.Before=null;
        var current=await f.Owner.CaptureAsync(f.Opening.RootId);Assert.Equal(f.Opening.Expected,current.Root.Expected);Assert.Empty(await f.Owner.HistoryAsync(f.Opening.RootId));
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_scheduling_v1.audit_outbox WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Scheduling_lost_response_after_revoke_recovers_original_but_new_write_requires_both_owners()
    {
        await using var h=await Seed();var f=await SchedSetup(h);var plan=await Plan(f);var command=Apply(plan);var committed=await f.Owner.ApplyAsync(command);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==301&&x.ActionCode=="change.apply"||x.MenuId==302&&x.ActionCode=="change.schedule").ExecuteDeleteAsync();
        Assert.True((await f.Owner.ApplyAsync(command)).Replayed);
        var recovered=JsonSerializer.SerializeToElement(await f.Owner.CommandAsync(command.CommandId),SchedulingValuesV1.Json);Assert.Equal("FOUND",recovered.GetProperty("status").GetString());
        var next=await Plan(f,0,9,12);await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyAsync(Apply(next)));
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==302&&x.ActionCode=="query").ExecuteDeleteAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.CommandAsync(command.CommandId));
        Assert.Single(await h.Db.Database.SqlQuery<Guid>($"SELECT plan_id AS \"Value\" FROM cp6_scheduling_v1.applications WHERE tenant_id={h.Tenant}").ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Scheduling_two_prepared_plans_race_one_root_and_legacy_raw_writes_are_fenced()
    {
        await using var h=await Seed();var f=await SchedSetup(h);var one=await Plan(f);var two=await Plan(f,1,15,18);
        await using var peerDb=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=SchedOwner(h with{Db=peerDb});
        async Task<bool> Run(SchedulingOwnerV1 o,SchedPlanV1 p){try{await o.ApplyAsync(Apply(p));return true;}catch(OrderPriceCorrectionException e)when(e.Message=="WO_CUT_CHANGED"){return false;}}
        Assert.Single(await Task.WhenAll(Run(f.Owner,one),Run(peer,two)),x=>x);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_WorkOrderProcess\" SET \"PlanStartTime\"=LOCALTIMESTAMP WHERE \"TenantId\"={h.Tenant} AND \"Id\"={f.Opening.Operations[0].OperationId}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_WorkOrder\" SET \"Priority\"=3 WHERE \"TenantId\"={h.Tenant} AND \"Id\"={f.Opening.RootId}"));
        var legacy=new PlanningBoardService(h.Db,SchedAuthority(h));
        Assert.Equal("MIGRATED_SCOPE_NEW_FLOW_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>legacy.RescheduleAsync(new(){Id=f.Opening.Operations[0].OperationId,PlanStartTime=DateTime.Today,PlanEndTime=DateTime.Today.AddDays(1)},"forged"))).Message);
        Assert.Single(await f.Owner.HistoryAsync(f.Opening.RootId));
    }
    [CurrentPermissionPostgresFact]
    public async Task Scheduling_independent_wo_action_foreign_tenant_and_unmapped_roots_fail_at_their_boundary()
    {
        await using var h=await Seed();var f=await SchedSetup(h);var plan=await Plan(f);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==302&&x.ActionCode=="change.schedule").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyAsync(Apply(plan)))).Message);
        await using var other=await Seed();await GrantPlanning(other);await RegisterScope(other);
        Assert.Equal("SCHED_NOT_VISIBLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>SchedOwner(other).CaptureAsync(f.Opening.RootId))).Message);
        Assert.Equal("SCHED_SIMULATION_DISABLED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>SchedOwner(h,false).OpenSimulationAsync(f.Opening))).Message);
        var fresh=await f.Owner.CaptureAsync(f.Opening.RootId);Assert.Equal("10",fresh.Root.Expected.Revision);
    }
    [CurrentPermissionPostgresFact]
    public async Task Scheduling_unplanned_root_requires_all_members_and_offset_instants_keep_microseconds()
    {
        await using var h=await Seed();var f=await SchedSetup(h);var store=new SchedulingStoreV1(h.Db,ScopeFence(h).EnvironmentId);var root=await store.Root(f.Opening.RootId,default);
        await store.SetRoot(root with{Schedule=root.Schedule with{Interval=null}},default);
        var plan=await Plan(f);Assert.Equal(Span(9,16),plan.RootApplyGroup.NewRootSchedule.Interval);
        await store.SetRoot(root with{Schedule=root.Schedule with{Interval=null},Operations=root.Operations.Select(o=>o.OperationId==f.Opening.Operations[1].OperationId?o with{Interval=null}:o).ToArray()},default);
        Assert.Equal("ROOT_ENVELOPE_UNRESOLVED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Plan(f))).Message);
        var first=new SchedIntervalV1(DateTimeOffset.Parse("2026-11-01T01:30:00.000001-04:00"),DateTimeOffset.Parse("2026-11-01T01:30:00.000001-05:00"));
        Interval(first);var roundtrip=Decode<SchedIntervalV1>(Bytes(first));Assert.Equal(TimeSpan.FromHours(1),roundtrip.End-roundtrip.Start);Assert.Equal(10,roundtrip.Start.Ticks%TimeSpan.TicksPerSecond);
        Assert.Throws<JsonException>(()=>Decode<SchedIntervalV1>(System.Text.Encoding.UTF8.GetBytes("{\"start\":\"2026-11-01T01:30:00\",\"end\":\"2026-11-01T03:00:00Z\"}")));
    }
    [CurrentPermissionPostgresFact]
    public async Task Scheduling_reason_text_that_looks_like_a_time_does_not_alias_an_original_command()
    {
        await using var h=await Seed();var f=await SchedSetup(h);var capture=await f.Owner.CaptureAsync(f.Opening.RootId);
        var plan=await f.Owner.PrepareAsync(Change(capture,f.Opening.Operations[0].OperationId) with{Reason="2026-11-01T01:30:00-04:00"});
        var command=Apply(plan);await f.Owner.ApplyAsync(command);
        Assert.Equal("SCHED_COMMAND_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.ApplyAsync(command with{Reason="2026-11-01T05:30:00Z"}))).Message);
        Assert.Single(await f.Owner.HistoryAsync(f.Opening.RootId));
    }
    [CurrentPermissionPostgresFact]
    public async Task Scheduling_actual_HTTP_prepares_applies_recovers_and_rejects_machine_or_extra_input()
    {
        await using var h=await Seed();var f=await SchedSetup(h);var settings=await DocumentHostSettings(new());settings["SchedulingSimulation__Enabled"]="true";
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var cookies=await Login(host,h);const string root="/api/mes/scheduling";
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+"/context")))
        {Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Contains(f.Opening.RootId.ToString(),await r.Content.ReadAsStringAsync());}
        SchedCaptureV1 capture;using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+"/roots/"+f.Opening.RootId+"/capture")))
        {Assert.Equal(HttpStatusCode.OK,r.StatusCode);capture=Decode<SchedCaptureV1>(await r.Content.ReadAsByteArrayAsync());Assert.True(r.Headers.CacheControl?.NoStore);}
        var input=Change(capture,f.Opening.Operations[0].OperationId);var forged=JsonSerializer.SerializeToNode(input,SchedulingValuesV1.Json)!.AsObject();forged["machineCd"]="FORGED";
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/change-plans",forged)))Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);
        forged.Remove("machineCd");forged["proposedStart"]="2026-11-01T01:30:00";
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/change-plans",forged)))Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);
        SchedPlanV1 plan;using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/change-plans",input)))
        {Assert.Equal(HttpStatusCode.OK,r.StatusCode);plan=Decode<SchedPlanV1>(await r.Content.ReadAsByteArrayAsync());}
        var command=Apply(plan);using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/change-plans/"+plan.PlanId+"/apply",command)))
        {Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Equal("11",Decode<SchedReplyV1>(await r.Content.ReadAsByteArrayAsync()).Result.NewWoExpected.Revision);}
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+"/commands/"+command.CommandId)))
        {Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Contains("COMMITTED",await r.Content.ReadAsStringAsync());}
    }
}
