using System.Globalization;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;
public sealed partial class SchedulingOwnerV1
{
    private static readonly string[] BoardStates=["NOT_STARTED","STARTED","COMPLETED","SUSPENDED","CANCELLED","UNKNOWN"];
    private static readonly string[] BoardOrderStates=["DRAFT","CONFIRMED","RELEASED","IN_PROGRESS","COMPLETED","SUSPENDED","CANCELLED","UNKNOWN"];
    private static TimeZoneInfo BoardZone(string id)
    {
        try{return TimeZoneInfo.FindSystemTimeZoneById(id);}
        catch(TimeZoneNotFoundException){throw Error("SCHED_ZONE_INVALID");}
        catch(InvalidTimeZoneException){throw Error("SCHED_ZONE_INVALID");}
    }
    private static DateTimeOffset? BoardInstant(string? value)
    {try{return value==null?null:SchedInstantConverterV1.Parse(value);}catch(System.Text.Json.JsonException){throw Error("SCHED_OFFSET_TIME_REQUIRED");}}
    public Task<SchedRefV1> PublishBoardSimulationAsync(BoardSourceV1 input,CancellationToken ct=default)
    {
        if(!simulation.Value.Enabled)throw Error("SCHED_SIMULATION_DISABLED",404);
        if(input?.DataKind!="SIMULATED"||input.RootId==Guid.Empty||input.Operations is not{Length:>0 and <=500}
            ||input.Operations.Any(x=>x==null||x.OperationId==Guid.Empty)||!BoardOrderStates.Contains(input.WorkOrderState)
            ||string.IsNullOrWhiteSpace(input.ZoneId))throw Error("BOARD_SOURCE_INVALID");
        Reference(input.Reference);Reference(input.SiteRef);BoardZone(input.ZoneId);BoardInstant(input.DeliveryInstant);
        foreach(var op in input.Operations)foreach(var r in new[]{op.ProcessRef,op.WorkgroupRef,op.LegacyResourceRef})if(r!=null)Reference(r);
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"simulate-source",true,async(a,t)=>
        {
            await Lease(a,true,t);a.RequireSchedulingSimulation();await Visible(a,input.RootId,t);
            var wo=await db.WorkOrders.AsNoTracking().SingleAsync(w=>w.Id==input.RootId,t);
            var ops=await db.WorkOrderProcesses.AsNoTracking().Where(o=>o.WorkOrderNo==wo.WorkOrderNo&&!o.IsDeleted).Select(o=>o.Id).ToArrayAsync(t);
            if(!ops.Order().SequenceEqual(input.Operations.Select(o=>o.OperationId).Order()))throw Error("BOARD_SOURCE_MANIFEST_CHANGED",409);
            if(await Store.RootIds(null).ContainsAsync(input.RootId,t))
            {var adopted=await Store.Root(input.RootId,t);if(!SameResource(adopted.SiteRef,input.SiteRef)||adopted.ZoneId!=input.ZoneId||input.Operations.Any(o=>o.LegacyResourceRef!=null))throw Error("BOARD_ADOPTED_SOURCE_MISMATCH",409);}
            var old=(await Store.BoardSources(t)).SingleOrDefault(s=>s.RootId==input.RootId);
            if(old!=null)
            {if(old==input||Digest(old)==Digest(input))return old.Reference;
             if(old.Reference.Id!=input.Reference.Id||old.Reference.Owner!=input.Reference.Owner||Next(old.Reference.Version)!=input.Reference.Version)throw Error("BOARD_SOURCE_PREDECESSOR_CHANGED",409);}
            else if(input.Reference.Version!="1")throw Error("BOARD_SOURCE_PREDECESSOR_CHANGED",409);
            await Store.SaveArrangement(Guid.NewGuid(),input.SiteRef.Id,"board-source",a.ActorId,input,t);
            await Store.SetBoardSource(input,t);return input.Reference;
        },ct);
    }
    public Task<BoardContextV1> BoardContextAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.Planning,"query",false,async(a,t)=>
    {
        await Lease(a,false,t);var visible=await (await a.PlanningOrders(t)).Select(w=>w.Id).ToArrayAsync(t);
        var sources=(await Store.BoardSources(t)).Where(s=>visible.Contains(s.RootId)).ToArray();var roots=new List<SchedRootV1>();
        foreach(var id in await Store.RootIds(null).Where(id=>visible.Contains(id)).ToArrayAsync(t))roots.Add(await Store.Root(id,t));
        var definitions=roots.Select(r=>(r.SiteRef,r.ZoneId)).Concat(sources.Select(s=>(s.SiteRef,s.ZoneId))).GroupBy(s=>s.SiteRef.Id);var sites=new List<BoardSiteV1>();
        foreach(var site in definitions)
        {
            if(site.Select(s=>s.ZoneId).Distinct().Count()!=1)throw Error("BOARD_SITE_ZONE_CONFLICT",409);
            var zone=BoardZone(site.First().ZoneId);var day=TimeZoneInfo.ConvertTime(Now(),zone).Date;
            DateTimeOffset Local(DateTime d)
            {if(zone.IsAmbiguousTime(d)||zone.IsInvalidTime(d))throw Error("BOARD_BUSINESS_DAY_OFFSET_REQUIRED",422);return new(d,zone.GetUtcOffset(d));}
            var members=sources.Where(s=>s.SiteRef.Id==site.Key).SelectMany(s=>s.Operations).ToArray();
            sites.Add(new(site.First().SiteRef,zone.Id,new(Local(day),Local(day.AddDays(14))),members.Where(o=>o.ProcessRef!=null).Select(o=>o.ProcessRef!).Distinct().ToArray(),
                members.Where(o=>o.WorkgroupRef!=null).Select(o=>o.WorkgroupRef!).Distinct().ToArray(),
                roots.Where(r=>r.SiteRef.Id==site.Key).SelectMany(r=>r.Operations).Where(o=>o.ResourceRef!=null).Select(o=>o.ResourceRef!)
                    .Concat(members.Where(o=>o.LegacyResourceRef!=null).Select(o=>o.LegacyResourceRef!)).Distinct().ToArray()));
        }
        return new BoardContextV1(db.CurrentTenantId,scope.EnvironmentId,a.ActorId,sites.OrderBy(s=>s.SiteRef.Id).ToArray());
    },ct);
    public Task<BoardPageV1> BoardAsync(BoardQueryV1 query,Guid? captureId=null,int cursor=0,int pageSize=20,CancellationToken ct=default)
    {
        if(query==null||query.SiteId==Guid.Empty||string.IsNullOrWhiteSpace(query.ZoneId)||query.Statuses is not{Length:>0}||cursor<0||pageSize is not(20 or 50 or 100)
            ||captureId==null&&cursor!=0||query.Sort is not("START_ASC" or "PRIORITY_DESC" or "WO_ASC"))throw Error("BOARD_QUERY_INVALID");
        Interval(query.Window);BoardZone(query.ZoneId);if(query.Window.End-query.Window.Start>TimeSpan.FromDays(93))throw Error("BOARD_WINDOW_TOO_LARGE");
        if(query.Statuses.Any(s=>s!="ANY"&&!BoardStates.Contains(s))||query.Statuses.Contains("ANY")&&query.Statuses.Length!=1)throw Error("BOARD_STATUS_REQUIRED");
        foreach(var r in new[]{query.ProcessRef,query.WorkgroupRef,query.ResourceRef})if(r!=null)Reference(r);
        query=query with{Statuses=query.Statuses.Distinct().Order(StringComparer.Ordinal).ToArray()};
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"query",true,async(a,t)=>
        {
            await Lease(a,true,t);BoardCaptureV1 capture;
            if(captureId!=null)
            {
                var d=await Store.Arrangement(captureId.Value,"board-capture",t);capture=Decode<BoardCaptureV1>(d.Body);
                if(capture.ActorId!=a.ActorId)throw Error("BOARD_CAPTURE_ACTOR_MISMATCH",403);
                if(Digest(capture.Query)!=Digest(query))throw Error("BOARD_CAPTURE_SCOPE_MISMATCH",409);
                if(capture.ExpiresAt<=Now())throw Error("BOARD_CAPTURE_EXPIRED",409);
                foreach(var id in capture.VisibleRoots)await Visible(a,id,t);
            }
            else
            {
                var visible=await (await a.PlanningOrders(t)).Select(w=>w.Id).ToArrayAsync(t);var roots=await Store.Roots(query.SiteId,t);
                var sources=(await Store.BoardSources(t)).Where(s=>s.SiteRef.Id==query.SiteId).ToArray();
                var allIds=roots.Select(r=>r.RootId).Concat(sources.Select(s=>s.RootId)).Distinct().ToArray();var allowed=allIds.Where(visible.Contains).ToArray();
                if(allowed.Length==0)throw Error("BOARD_SITE_NOT_VISIBLE",404);
                if(roots.Where(r=>allowed.Contains(r.RootId)).Any(r=>r.ZoneId!=query.ZoneId)||sources.Where(s=>allowed.Contains(s.RootId)).Any(s=>s.ZoneId!=query.ZoneId))throw Error("BOARD_SITE_ZONE_MISMATCH",409);
                var now=Now();var all=await BoardProjection(roots,sources,now,t);
                var rows=all.Where(b=>allowed.Contains(b.WoId)).ToArray();
                foreach(var pair in new[]{(query.ProcessRef,rows.Select(r=>r.ProcessRef)),(query.WorkgroupRef,rows.Select(r=>r.WorkgroupRef)),(query.ResourceRef,rows.Select(r=>r.ResourceRef))})
                    if(pair.Item1!=null&&!pair.Item2.Contains(pair.Item1))throw Error("BOARD_FILTER_REFERENCE_NOT_VISIBLE",404);
                var selected=rows.Where(b=>(query.ProcessRef==null||b.ProcessRef==query.ProcessRef)&&(query.WorkgroupRef==null||b.WorkgroupRef==query.WorkgroupRef)
                    &&(query.ResourceRef==null||b.ResourceRef==query.ResourceRef)&&(query.Statuses.Contains("ANY")||query.Statuses.Contains(b.State))
                    &&(b.PlannedInterval==null&&b.ActualInterval==null||b.PlannedInterval!=null&&Overlap(b.PlannedInterval,query.Window)||b.ActualInterval!=null&&Overlap(b.ActualInterval,query.Window))).ToArray();
                var coverage=allowed.Length<allIds.Length?"PARTIAL":rows.Any(r=>r.Coverage!="COMPLETE")?"UNKNOWN":"COMPLETE";
                selected=selected.Select(b=>b with{Conflicts=BoardConflicts(b,all,allowed)}).ToArray();
                IOrderedEnumerable<BoardBarV1> sorted=query.Sort switch
                {"WO_ASC"=>selected.OrderBy(b=>b.WorkOrderNo,StringComparer.Ordinal),"PRIORITY_DESC"=>selected.OrderByDescending(b=>b.Priority switch{"EXPEDITE"=>3,"URGENT"=>2,_=>1}),_=>selected.OrderBy(b=>b.PlannedInterval?.Start??DateTimeOffset.MaxValue)};
                selected=sorted.ThenBy(b=>b.WoId).ThenBy(b=>b.RouteSortOrder).ThenBy(b=>b.OperationId).ToArray();
                var warnings=new List<string>();if(coverage!="COMPLETE")warnings.Add(coverage=="PARTIAL"?"PARTIAL_AUTHORIZATION_TOTALS_UNAVAILABLE":"SOURCE_COVERAGE_UNKNOWN");
                if(all.Any(b=>b.State is "STARTED" or "UNKNOWN"&&b.ActualInterval==null&&selected.Any(s=>SameResource(s.ResourceRef,b.ResourceRef))))warnings.Add("RESOURCE_OCCUPANCY_UNKNOWN");
                capture=new(Guid.NewGuid(),a.ActorId,query,Digest(new{db.CurrentTenantId,scope.EnvironmentId,a.ActorId,query,visibleRoots=allowed.Order().ToArray()}),now,now.AddMinutes(30),allowed,selected,BoardKpi(selected,coverage,now),coverage,warnings.ToArray());
                await Store.SaveArrangement(capture.CaptureId,query.SiteId,"board-capture",a.ActorId,capture,t);
            }
            if(cursor>capture.Bars.Length)throw Error("BOARD_CURSOR_INVALID");
            return new BoardPageV1(capture.CaptureId,capture.QueryScopeHash,capture.AsOf,capture.ExpiresAt,capture.Query.ZoneId,capture.Bars.Skip(cursor).Take(pageSize).ToArray(),
                cursor+pageSize<capture.Bars.Length?cursor+pageSize:null,capture.Kpi,capture.Coverage,capture.Warnings);
        },ct);
    }
    public Task<BoardDetailV1> BoardDetailAsync(Guid operation,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.Planning,"query",false,async(a,t)=>
    {
        await Lease(a,false,t);var op=await db.WorkOrderProcesses.AsNoTracking().SingleOrDefaultAsync(o=>o.Id==operation&&!o.IsDeleted,t)??throw Error("SCHED_NOT_VISIBLE",404);
        var wo=await (await a.PlanningOrders(t)).SingleOrDefaultAsync(w=>w.WorkOrderNo==op.WorkOrderNo,t)??throw Error("SCHED_NOT_VISIBLE",404);
        var adopted=await Store.RootIds(null).ContainsAsync(wo.Id,t)?await Store.Root(wo.Id,t):null;
        var source=(await Store.BoardSources(t)).SingleOrDefault(s=>s.RootId==wo.Id);if(adopted==null&&source==null)throw Error("BOARD_SOURCE_NOT_AVAILABLE",409);
        var now=Now();var rows=await BoardProjection(adopted==null?[]:[adopted],source==null?[]:[source],now,t);
        return new BoardDetailV1(rows.Single(b=>b.OperationId==operation),adopted,adopted!=null,adopted?.ZoneId??source!.ZoneId,now);
    },ct);
    private async Task<BoardBarV1[]> BoardProjection(SchedRootV1[] roots,BoardSourceV1[] sources,DateTimeOffset now,CancellationToken ct)
    {
        // The legacy tables have independent writers; one short shared cut keeps their two reads coherent.
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"T_WorkOrder\",public.\"T_WorkOrderProcess\" IN SHARE MODE",ct);
        var ids=roots.Select(r=>r.RootId).Concat(sources.Select(s=>s.RootId)).Distinct().ToArray();
        var native=await db.WorkOrders.AsNoTracking().Where(w=>ids.Contains(w.Id)&&!w.IsDeleted).ToArrayAsync(ct);var numbers=native.Select(w=>w.WorkOrderNo).ToArray();
        var nativeOps=await db.WorkOrderProcesses.AsNoTracking().Where(o=>numbers.Contains(o.WorkOrderNo)&&!o.IsDeleted).ToArrayAsync(ct);var result=new List<BoardBarV1>();
        foreach(var wo in native)
        {
            var root=roots.SingleOrDefault(r=>r.RootId==wo.Id);var source=sources.SingleOrDefault(s=>s.RootId==wo.Id);var observation=root==null?null:await Store.Observation(wo.Id,ct);
            var zone=BoardZone(root?.ZoneId??source!.ZoneId);var delivery=source==null?null:BoardInstant(source.DeliveryInstant);
            foreach(var row in nativeOps.Where(o=>o.WorkOrderNo==wo.WorkOrderNo))
            {
                var op=root?.Operations.Single(o=>o.OperationId==row.Id);var guard=observation?.Guards.Single(g=>g.OperationId==row.Id);var info=source?.Operations.SingleOrDefault(o=>o.OperationId==row.Id);
                var plan=op!=null?op.Interval:LegacyInterval(row.PlanStartTime,row.PlanEndTime,zone);
                var actual=guard!=null?guard.ActualInterval:LegacyInterval(row.ActualStartTime,row.ActualEndTime,zone);
                var state=guard?.ExecutionState??(row.ProcessStatus switch{0=>"NOT_STARTED",1=>"STARTED",2=>"COMPLETED",3=>"SUSPENDED",9=>"CANCELLED",_=>"UNKNOWN"});
                var orderState=root!=null?source?.WorkOrderState??"UNKNOWN":wo.Status switch{0=>"DRAFT",1=>"CONFIRMED",2=>"RELEASED",3=>"IN_PROGRESS",4=>"COMPLETED",5=>"SUSPENDED",6=>"LEGACY_INSPECTED",9=>"CANCELLED",_=>"UNKNOWN"};
                var coverage=info==null||state=="UNKNOWN"||orderState=="UNKNOWN"||root==null&&(row.PlanStartTime!=null||row.PlanEndTime!=null)&&plan==null?"UNKNOWN":"COMPLETE";
                var end=state=="COMPLETED"?actual?.End:plan?.End>now?plan.End:now;
                result.Add(new(row.Id,wo.Id,wo.WorkOrderNo,wo.ProductName??wo.ProductCd,op?.ProcessLabel??row.ProcessCd,row.SortOrder,
                    op?.OccurrenceRef,root?.SnapshotRef,root?.Expected,op?.ScheduleRevision,root?.QueueHeads??[],info?.ProcessRef,info?.WorkgroupRef,op?.ResourceRef??info?.LegacyResourceRef,op?.AssignmentRef,
                    plan,actual,actual!=null?"COMPLETE":state=="NOT_STARTED"?"NOT_OCCURRED":"UNKNOWN",delivery,op?.PlanQty,op?.GoodQty,op?.DefectQty,state,orderState,
                    root?.Schedule.Priority??(wo.Priority switch{>=3=>"EXPEDITE",2=>"URGENT",_=>"NORMAL"}),coverage,root==null?"LEGACY_READ_ONLY":"SCHEDULING_V1",state!="CANCELLED"&&delivery!=null&&end>delivery,[]));
            }
        }
        return result.ToArray();
    }
    private static SchedIntervalV1? LegacyInterval(DateTime? start,DateTime? end,TimeZoneInfo zone)
    {
        if(start==null||end==null||start>=end)return null;
        var a=DateTime.SpecifyKind(start.Value,DateTimeKind.Unspecified);var b=DateTime.SpecifyKind(end.Value,DateTimeKind.Unspecified);
        if(zone.IsAmbiguousTime(a)||zone.IsAmbiguousTime(b)||zone.IsInvalidTime(a)||zone.IsInvalidTime(b))return null;
        var interval=new SchedIntervalV1(new(a,zone.GetUtcOffset(a)),new(b,zone.GetUtcOffset(b)));return interval.Start<interval.End?interval:null;
    }
    private static BoardConflictV1[] BoardConflicts(BoardBarV1 bar,BoardBarV1[] all,Guid[] visible)
    {
        if(bar.PlannedInterval==null||bar.ResourceRef==null)return [];
        return all.Where(other=>other.OperationId!=bar.OperationId&&SameResource(other.ResourceRef,bar.ResourceRef))
            .Select(other=>(other,interval:other.State is "STARTED" or "COMPLETED"?other.ActualInterval:other.PlannedInterval))
            .Where(p=>p.interval!=null&&Overlap(bar.PlannedInterval,p.interval)).Select(p=>new BoardConflictV1(visible.Contains(p.other.WoId)?p.other.OperationId:null,p.interval!,visible.Contains(p.other.WoId)?"VISIBLE":"OPAQUE_UNAVAILABLE")).Distinct().ToArray();
    }
    private static BoardKpiV1 BoardKpi(BoardBarV1[] bars,string coverage,DateTimeOffset now)
    {
        BoardMetricV1 Metric(decimal? value,string unit,string state="COMPLETE")=>new(coverage=="COMPLETE"?value?.ToString(CultureInfo.InvariantCulture):null,unit,coverage=="COMPLETE"?state:coverage);
        var groups=bars.GroupBy(b=>b.WoId).ToArray();var done=bars.Count(b=>b.State=="COMPLETED");var cancelled=bars.Count(b=>b.State=="CANCELLED");var unknown=bars.Count(b=>b.State=="UNKNOWN");
        // Cancelled and unknown operations are reported separately, outside the completion denominator.
        var denominator=bars.Length-cancelled-unknown;var late=groups.Where(g=>g.Any(b=>b.Delayed)).ToArray();
        var delays=late.Select(g=>g.Where(b=>b.State!="CANCELLED").Max(b=>b.State=="COMPLETED"?b.ActualInterval?.End:b.PlannedInterval?.End>now?b.PlannedInterval.End:now)-g.First().DeliveryDate).Where(x=>x!=null).Select(x=>(decimal)x!.Value.Ticks/TimeSpan.TicksPerDay).ToArray();
        var delayUnknown=bars.Any(b=>b.State=="COMPLETED"&&b.DeliveryDate!=null&&b.ActualInterval==null);
        return new(Metric(groups.Length,"orders"),Metric(groups.Count(g=>g.First().WorkOrderState=="IN_PROGRESS"),"orders"),Metric(groups.Count(g=>g.First().WorkOrderState=="COMPLETED"),"orders"),
            Metric(delayUnknown?null:late.Length,"orders",delayUnknown?"UNKNOWN":"COMPLETE"),Metric(denominator==0?null:decimal.Round(done*100m/denominator,4),"percent",denominator==0?"N/A":"COMPLETE"),
            Metric(delayUnknown||delays.Length==0?null:decimal.Round(delays.Average(),6),"days",delayUnknown?"UNKNOWN":delays.Length==0?"N/A":"COMPLETE"),
            Metric(denominator,"operations"),Metric(done,"operations"),Metric(cancelled,"operations"),Metric(unknown,"operations"),"BOARD_FILTERED_OPS_V1");
    }
}
