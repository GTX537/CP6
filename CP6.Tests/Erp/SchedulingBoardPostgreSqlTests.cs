using System.Net;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes.Scheduling;
using CP6.Entity.DomainModels.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static BoardQueryV1 BoardQuery(ArrangementFixture f)=>new(f.Site.Id,Span(0,2000),"America/New_York",null,null,null,["ANY"]);
    private static async Task<BoardSourceV1[]> BoardSources(ArrangementFixture f)
    {
        var process=SchedReference("PLM-PROCESS");var group=SchedReference("WORKGROUP");var sources=f.Openings.Select(o=>new BoardSourceV1("SIMULATED",SchedReference("BOARD-SOURCE"),o.RootId,o.SiteRef,o.ZoneId,"RELEASED",null,
            o.Operations.Select(p=>new BoardOperationSourceV1(p.OperationId,process,group,null)).ToArray())).ToArray();
        foreach(var s in sources)await f.Owner.PublishBoardSimulationAsync(s);return sources;
    }
    [CurrentPermissionPostgresFact]
    public async Task Board_capture_filters_KPI_and_pages_share_one_cut_despite_new_execution()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h,operations:25);var sources=await BoardSources(f);
        var query=BoardQuery(f) with{ProcessRef=sources[0].Operations[0].ProcessRef,WorkgroupRef=sources[0].Operations[0].WorkgroupRef,ResourceRef=f.Openings[0].Operations[0].ResourceRef};
        var first=await f.Owner.BoardAsync(query);Assert.Equal(20,first.Bars.Length);Assert.Equal("25",first.Kpi.Denominator.Value);Assert.Equal("0",first.Kpi.CompletionRate.Value);
        Assert.Equal(20,first.NextCursor);Assert.Equal(20,first.Bars.Select(b=>b.OccurrenceRef!.Id).Distinct().Count());
        var original=f.Openings[0].Observation;await f.Owner.PublishSimulationAsync(NewObservation(original,original.Guards.Select(g=>g with{ExecutionState="COMPLETED",ActualInterval=Span(24,26)}).ToArray()));
        var last=await f.Owner.BoardAsync(query,first.CaptureId,20);Assert.Equal(5,last.Bars.Length);Assert.Null(last.NextCursor);Assert.Equal(first.QueryScopeHash,last.QueryScopeHash);Assert.Equal(first.Kpi,last.Kpi);Assert.All(last.Bars,b=>Assert.Equal("NOT_STARTED",b.State));
        var fresh=await f.Owner.BoardAsync(query);Assert.Equal("100",fresh.Kpi.CompletionRate.Value);Assert.NotEqual(first.CaptureId,fresh.CaptureId);
        Assert.Equal("BOARD_CAPTURE_SCOPE_MISMATCH",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.BoardAsync(query with{Statuses=["STARTED"]},first.CaptureId))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Board_partial_scope_hides_totals_and_conflict_identity_and_rechecks_capture_visibility()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h,2,sharedResource:true,foreignFrom:1);await BoardSources(f);
        var store=new SchedulingStoreV1(h.Db,ScopeFence(h).EnvironmentId);var peer=await store.Root(f.Openings[1].RootId,default);
        await store.SetRoot(peer with{Operations=peer.Operations.Select((o,n)=>o with{Interval=f.Openings[0].Operations[n].Interval}).ToArray()},default);
        var query=BoardQuery(f);var whole=await f.Owner.BoardAsync(query);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="mes-planning-board").ExecuteUpdateAsync(x=>x.SetProperty(s=>s.ScopeType,1));
        var part=await f.Owner.BoardAsync(query);Assert.Equal("PARTIAL",part.Coverage);Assert.Null(part.Kpi.TotalOrders.Value);Assert.Null(part.Kpi.Denominator.Value);Assert.Equal(2,part.Bars.Length);
        Assert.Contains(part.Bars.SelectMany(b=>b.Conflicts),c=>c.Coverage=="OPAQUE_UNAVAILABLE"&&c.OperationId==null);
        var json=System.Text.Encoding.UTF8.GetString(Bytes(part));Assert.DoesNotContain(peer.RootId.ToString(),json);Assert.DoesNotContain(peer.Operations[0].OperationId.ToString(),json);
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.BoardAsync(query,whole.CaptureId))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.BoardDetailAsync(peer.Operations[0].OperationId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Board_unplanned_unknown_actual_and_complete_empty_are_distinct()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h);await BoardSources(f);var store=new SchedulingStoreV1(h.Db,ScopeFence(h).EnvironmentId);var root=await store.Root(f.Openings[0].RootId,default);
        await store.SetRoot(root with{Operations=root.Operations.Select(o=>o with{Interval=null}).ToArray()},default);
        var unplanned=await f.Owner.BoardAsync(BoardQuery(f));Assert.Equal(2,unplanned.Bars.Length);Assert.All(unplanned.Bars,b=>{Assert.Null(b.PlannedInterval);Assert.Null(b.ActualInterval);Assert.Equal("NOT_OCCURRED",b.ActualCoverage);});
        var empty=await f.Owner.BoardAsync(BoardQuery(f) with{Statuses=["COMPLETED"]});Assert.Empty(empty.Bars);Assert.Equal("0",empty.Kpi.TotalOrders.Value);Assert.Null(empty.Kpi.CompletionRate.Value);Assert.Equal("N/A",empty.Kpi.CompletionRate.Coverage);
        await f.Owner.PublishSimulationAsync(NewObservation(f.Openings[0].Observation,f.Openings[0].Observation.Guards.Select(g=>g with{ExecutionState="UNKNOWN"}).ToArray()));
        var unknown=await f.Owner.BoardAsync(BoardQuery(f));Assert.Equal("UNKNOWN",unknown.Coverage);Assert.Null(unknown.Kpi.CompletionRate.Value);Assert.All(unknown.Bars,b=>Assert.Equal("UNKNOWN",b.ActualCoverage));
    }
    [CurrentPermissionPostgresFact]
    public async Task Board_legacy_inspection_has_no_fabricated_snapshot_occurrence_quantity_or_write_context()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h);await BoardSources(f);
        var wo=new WorkOrder{WorkOrderNo="BL"+Guid.NewGuid().ToString("N")[..18],ProductCd="P",Status=6,Creator="price-owner"};
        var row=new WorkOrderProcess{WorkOrderNo=wo.WorkOrderNo,ProcessCd="P",TaskCd="L",Creator="price-owner",PlanStartTime=new(2026,11,1,1,30,0),PlanEndTime=new(2026,11,1,3,30,0),GoodQty=9};
        h.Db.WorkOrders.Add(wo);h.Db.WorkOrderProcesses.Add(row);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var source=new BoardSourceV1("SIMULATED",SchedReference("BOARD-SOURCE"),wo.Id,f.Site,"America/New_York","COMPLETED",null,[new(row.Id,SchedReference("PROCESS"),null,null)]);await f.Owner.PublishBoardSimulationAsync(source);
        var detail=await f.Owner.BoardDetailAsync(row.Id);Assert.Equal("LEGACY_INSPECTED",detail.Operation.WorkOrderState);Assert.Equal("LEGACY_READ_ONLY",detail.Operation.Mode);Assert.Null(detail.Operation.PlannedInterval);Assert.Null(detail.Operation.GoodQty);
        Assert.Null(detail.Operation.OccurrenceRef);Assert.Null(detail.Operation.SnapshotRef);Assert.Null(detail.RootScheduleContext);Assert.False(detail.CompleteForRoot);
        Assert.Equal("UNKNOWN",detail.Operation.Coverage);Assert.NotEqual("CLOSED",detail.Operation.WorkOrderState);
    }
    [CurrentPermissionPostgresFact]
    public async Task Board_expired_capture_source_predecessor_and_invalid_query_fail_at_boundary()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h);var sources=await BoardSources(f);var query=BoardQuery(f);var page=await f.Owner.BoardAsync(query);
        var store=new SchedulingStoreV1(h.Db,ScopeFence(h).EnvironmentId);var doc=await store.Arrangement(page.CaptureId,"board-capture",default);var old=Decode<BoardCaptureV1>(doc.Body) with{CaptureId=Guid.NewGuid(),ExpiresAt=Now().AddMinutes(-1)};
        await store.SaveArrangement(old.CaptureId,f.Site.Id,"board-capture",doc.ActorId,old,default);
        Assert.Equal("BOARD_CAPTURE_EXPIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.BoardAsync(query,old.CaptureId))).Message);
        Assert.Equal("BOARD_STATUS_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.BoardAsync(query with{Statuses=["ANY","STARTED"]}))).Message);
        Assert.Equal("BOARD_WINDOW_TOO_LARGE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.BoardAsync(query with{Window=Span(0,2400)}))).Message);
        Assert.Equal("BOARD_SOURCE_PREDECESSOR_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PublishBoardSimulationAsync(sources[0] with{WorkOrderState="COMPLETED"}))).Message);
        Assert.Equal("BOARD_SOURCE_MANIFEST_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.PublishBoardSimulationAsync(sources[0] with{Operations=[sources[0].Operations[0]]}))).Message);
        Assert.Equal("SCHED_SIMULATION_DISABLED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>SchedOwner(h,false).PublishBoardSimulationAsync(sources[0]))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Board_detail_exact_occurrences_and_context_authorized_factory_defaults()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h);var sources=await BoardSources(f);var context=await f.Owner.BoardContextAsync();var site=Assert.Single(context.Sites);
        Assert.Equal(f.Site,site.SiteRef);Assert.Equal(0,TimeZoneInfo.ConvertTime(site.DefaultWindow.Start,TimeZoneInfo.FindSystemTimeZoneById(site.ZoneId)).Hour);
        Assert.Single(site.Processes);var first=await f.Owner.BoardDetailAsync(f.Openings[0].Operations[0].OperationId);var second=await f.Owner.BoardDetailAsync(f.Openings[0].Operations[1].OperationId);
        Assert.NotEqual(first.Operation.OccurrenceRef,second.Operation.OccurrenceRef);Assert.Equal(sources[0].Operations[0].ProcessRef,first.Operation.ProcessRef);Assert.True(first.CompleteForRoot);Assert.Equal(2,first.RootScheduleContext!.Operations.Length);
        Assert.Equal(f.Openings[0].Expected,first.Operation.WoExpected);Assert.Equal("5",first.Operation.ScheduleRevision);
    }
    [CurrentPermissionPostgresFact]
    public async Task Board_date_state_reference_filters_delays_and_unknown_resource_occupancy_are_explicit()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h);var sources=await BoardSources(f);var s=sources[0];
        await f.Owner.PublishBoardSimulationAsync(s with{Reference=s.Reference with{Version="2",Digest="delivery-source-v2"},WorkOrderState="IN_PROGRESS",DeliveryInstant=SchedDay.AddHours(25).ToString("yyyy-MM-ddTHH:mm:sszzz")});
        var o=f.Openings[0].Observation;await f.Owner.PublishSimulationAsync(NewObservation(o,o.Guards.Select((g,n)=>n==0?g with{ExecutionState="COMPLETED",ActualInterval=Span(24,27)}:g with{ExecutionState="STARTED"}).ToArray()));
        var query=BoardQuery(f);var all=await f.Owner.BoardAsync(query);Assert.Equal("50",all.Kpi.CompletionRate.Value);Assert.Equal("1",all.Kpi.DelayedOrders.Value);Assert.Equal("1",all.Kpi.InProgressOrders.Value);Assert.Contains("RESOURCE_OCCUPANCY_UNKNOWN",all.Warnings);
        var filtered=await f.Owner.BoardAsync(query with{Statuses=["COMPLETED"],Window=Span(26,27)});Assert.Single(filtered.Bars);Assert.Equal("100",filtered.Kpi.CompletionRate.Value);Assert.Equal("0.083333",filtered.Kpi.AvgDelayDays.Value);
        Assert.Equal("BOARD_FILTER_REFERENCE_NOT_VISIBLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Owner.BoardAsync(query with{ProcessRef=s.Operations[0].ProcessRef! with{Version="9"}}))).Message);
        var empty=await f.Owner.BoardAsync(query with{Window=Span(100,110)});Assert.Empty(empty.Bars);Assert.Equal("0",empty.Kpi.TotalOrders.Value);
    }
    [CurrentPermissionPostgresFact]
    public async Task Board_cancelled_legacy_operation_does_not_inflate_order_delay_or_completion_denominator()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h);await BoardSources(f);
        var wo=new WorkOrder{WorkOrderNo="BC"+Guid.NewGuid().ToString("N")[..18],ProductCd="P",Status=3,Creator="price-owner"};
        var done=new WorkOrderProcess{WorkOrderNo=wo.WorkOrderNo,ProcessCd="P",TaskCd="D",Creator="price-owner",ProcessStatus=2,ActualStartTime=new(2026,11,2,9,0,0),ActualEndTime=new(2026,11,2,11,0,0)};
        var cancelled=new WorkOrderProcess{WorkOrderNo=wo.WorkOrderNo,ProcessCd="P",TaskCd="C",Creator="price-owner",ProcessStatus=9,PlanStartTime=new(2026,11,10,9,0,0),PlanEndTime=new(2026,11,10,11,0,0)};
        h.Db.WorkOrders.Add(wo);h.Db.WorkOrderProcesses.AddRange(done,cancelled);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var process=SchedReference("PROCESS");
        await f.Owner.PublishBoardSimulationAsync(new("SIMULATED",SchedReference("BOARD-SOURCE"),wo.Id,f.Site,"America/New_York","IN_PROGRESS","2026-11-02T10:00:00-05:00",[new(done.Id,process,null,null),new(cancelled.Id,process,null,null)]));
        var page=await f.Owner.BoardAsync(BoardQuery(f) with{ProcessRef=process});Assert.Equal(2,page.Bars.Length);Assert.Equal("1",page.Kpi.CancelledCount.Value);Assert.Equal("1",page.Kpi.Denominator.Value);Assert.Equal("100",page.Kpi.CompletionRate.Value);Assert.Equal("0.041667",page.Kpi.AvgDelayDays.Value);
    }
    [CurrentPermissionPostgresFact]
    public async Task Board_actual_HTTP_filters_capture_paging_offset_rejection_and_operation_detail()
    {
        await using var h=await Seed();var f=await ArrangementSetup(h,operations:25);await BoardSources(f);var settings=await DocumentHostSettings(new());settings["SchedulingSimulation__Enabled"]="true";
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var cookies=await Login(host,h);const string route="/api/mes/scheduling";
        var query=$"siteId={f.Site.Id}&from=2026-11-01T00:00:00Z&to=2026-12-01T00:00:00Z&zoneId=America%2FNew_York&statuses=ANY";BoardPageV1 page;
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route+"/board?"+query))){Assert.Equal(HttpStatusCode.OK,r.StatusCode);page=Decode<BoardPageV1>(await r.Content.ReadAsByteArrayAsync());Assert.Equal(20,page.Bars.Length);Assert.True(r.Headers.CacheControl?.NoStore);}
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route+"/board?"+query+$"&captureId={page.CaptureId}&cursor=20"))){Assert.Equal(HttpStatusCode.OK,r.StatusCode);var next=Decode<BoardPageV1>(await r.Content.ReadAsByteArrayAsync());Assert.Equal(5,next.Bars.Length);Assert.Equal(page.Kpi,next.Kpi);}
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route+"/board?"+query.Replace("2026-11-01T00:00:00Z","2026-11-01T01:30:00"))))Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);
        using(var r=await host.Send(OperationsRequest(cookies,HttpMethod.Get,route+"/operations/"+page.Bars[0].OperationId))){Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Equal(page.Bars[0].OccurrenceRef,Decode<BoardDetailV1>(await r.Content.ReadAsByteArrayAsync()).Operation.OccurrenceRef);}
    }
}
