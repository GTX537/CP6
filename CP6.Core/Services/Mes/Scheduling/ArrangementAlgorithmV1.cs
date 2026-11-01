using System.Globalization;
using CP6.Core.Services.Erp;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;
internal static class ArrangementAlgorithmV1
{
    internal const string Algorithm="LEGACY_PRIORITY_DUE_CONTIGUOUS_V1";
    internal static string Resource(SchedRefV1 r)=>r.Owner+"/"+r.Id;
    internal static int Priority(string value)=>value switch{"EXPEDITE"=>3,"URGENT"=>2,_=>1};
    internal static ArrangementPreviewV1 Prepare(ArrangementCaptureV1 capture,ArrangementRequestV1 input)
    {
        var selected=input.OperationIds.ToHashSet();var items=new List<ArrangementItemV1>();var busy=capture.Unavailable.ToList();
        var selectedRoots=capture.Roots.Where(r=>r.Capture.Root.Operations.Any(o=>selected.Contains(o.OperationId))).ToArray();
        foreach(var r in capture.Roots.Except(selectedRoots))
        foreach(var op in r.Capture.Root.Operations.Where(o=>o.ResourceRef!=null))
        {
            var guard=r.Capture.Observation.Guards.Single(g=>g.OperationId==op.OperationId);
            var interval=guard.ExecutionState is "STARTED" or "COMPLETED"?guard.ActualInterval:op.Interval;
            if(interval!=null||guard.ExecutionState!="NOT_STARTED")busy.Add(new(op.ResourceRef!,interval));
        }
        var rows=selectedRoots.SelectMany(r=>r.Capture.Root.Operations.Select(o=>new{Root=r,Operation=o,Guard=r.Capture.Observation.Guards.Single(g=>g.OperationId==o.OperationId),Facts=r.Planning?.Operations.Single(x=>x.OperationId==o.OperationId)}))
            .OrderByDescending(x=>Priority(x.Root.Capture.Root.Schedule.Priority)).ThenBy(x=>x.Root.Planning?.DeliveryDate?.Value??DateTimeOffset.MaxValue)
            .ThenBy(x=>x.Operation.RouteSortOrder).ThenBy(x=>x.Root.Capture.Root.RootId).ThenBy(x=>x.Operation.OperationId).ToArray();
        foreach(var row in rows)
        {
            var op=row.Operation;var why=!selected.Contains(op.OperationId)?"NOT_SELECTED":op.ResourceRef==null?"UNASSIGNED":
                !input.ResourceScopeRefs.Contains(op.ResourceRef)?"OUTSIDE_RESOURCE_SCOPE":row.Guard.ExecutionState is "STARTED" or "COMPLETED"?"EXECUTION_LOCKED":null;
            if(why==null)continue;
            items.Add(new(row.Root.Capture.Root.RootId,op.OperationId,op.OccurrenceRef,op.ResourceRef,op.Interval,null,"NOT_APPLICABLE",Array.IndexOf(rows,row)+1,"EXCLUDED",[why]));
            if(op.ResourceRef!=null)
            {
                var interval=row.Guard.ExecutionState is "STARTED" or "COMPLETED"?row.Guard.ActualInterval:op.Interval;
                // Unknown occupied duration is an unknown constraint, never an empty busy interval.
                if(interval!=null||row.Guard.ExecutionState!="NOT_STARTED")busy.Add(new(op.ResourceRef,interval));
            }
        }
        var cursors=new Dictionary<string,DateTimeOffset>();
        foreach(var row in rows)
        {
            var op=row.Operation;if(items.Any(i=>i.OperationId==op.OperationId))continue;var reasons=new List<string>();var basis="EXPLICIT_DEFAULT";SchedIntervalV1? proposed=null;
            if(row.Facts is not{CalendarKnown:true,MaintenanceKnown:true,PredecessorsKnown:true})reasons.Add("UNKNOWN_CONSTRAINT");
            if(row.Guard.ExecutionState=="UNKNOWN")reasons.Add("EXECUTION_UNKNOWN");
            var resource=op.ResourceRef!;var occupied=busy.Where(b=>SameResource(b.ResourceRef,resource)).ToArray();
            if(occupied.Any(b=>b.Interval==null))reasons.Add("UNKNOWN_BUSY_INTERVAL");
            if(reasons.Count==0)
            {
                try
                {
                    TimeSpan duration;
                    if(row.Facts!.TechnicalDuration is {} technical){duration=TimeSpan.FromTicks(checked(long.Parse(technical.Microseconds,CultureInfo.InvariantCulture)*10));basis="TECHNICAL:"+technical.Reference.Id;}
                    else if(row.Root.LegacyLeadTimes[op.OperationId] is {} legacy&&decimal.Parse(legacy,CultureInfo.InvariantCulture)>0)
                    {var hours=decimal.Truncate(Math.Max(1,decimal.Parse(legacy,CultureInfo.InvariantCulture)*8));duration=TimeSpan.FromTicks(checked((long)(hours*TimeSpan.TicksPerHour)));basis="LEGACY_ESTIMATE";}
                    else duration=TimeSpan.FromMinutes(input.DefaultDurationMinutes);
                    var from=cursors.GetValueOrDefault(Resource(resource),input.BaseTime);
                    if(from<row.Guard.SourceWindow.Start)from=row.Guard.SourceWindow.Start;if(from<row.Guard.UseInterval.Start)from=row.Guard.UseInterval.Start;
                    var constraints=occupied.Where(b=>b.Interval!=null).Select(b=>b.Interval!).Concat(row.Guard.UnavailableIntervals).OrderBy(b=>b.Start).ToArray();
                    foreach(var b in constraints)if(Overlap(new(from,from+duration),b))from=b.End;
                    proposed=new(from,from+duration);cursors[Resource(resource)]=proposed.End;busy.Add(new(resource,proposed));
                }
                catch(OverflowException){reasons.Add("DURATION_OUT_OF_RANGE");}
                catch(ArgumentOutOfRangeException){reasons.Add("TIME_RANGE_EXCEEDED");}
            }
            items.Add(new(row.Root.Capture.Root.RootId,op.OperationId,op.OccurrenceRef,resource,op.Interval,proposed,basis,Array.IndexOf(rows,row)+1,reasons.Count==0?"INCLUDED":"BLOCKED",reasons.ToArray()));
        }
        for(var i=0;i<items.Count;i++)if(items[i].Inclusion=="INCLUDED"&&items[i].NewInterval==items[i].OldInterval)
            items[i]=items[i] with{Inclusion="EXCLUDED",ReasonCodes=["UNCHANGED_INTERVAL"]};
        var groups=new List<ArrangementGroupV1>();
        foreach(var root in selectedRoots.OrderBy(r=>r.Capture.Root.RootId))
        {
            var chosen=items.Where(i=>i.RootId==root.Capture.Root.RootId&&i.Inclusion!="EXCLUDED").ToArray();if(chosen.Length==0)continue;
            var changes=chosen.Where(i=>i.NewInterval!=null).Select(i=>{var op=root.Capture.Root.Operations.Single(o=>o.OperationId==i.OperationId);return new SchedOperationChangeV1(op.OperationId,op.OccurrenceRef,op.ScheduleRevision,op.Interval,i.NewInterval!,op.AssignmentRef,root.Capture.Observation.Guards.Single(g=>g.OperationId==op.OperationId).SourceWindowRef);}).ToArray();
            var reasons=chosen.SelectMany(i=>i.ReasonCodes).ToList();RootApplyGroupV1 group;
            try{group=WorkOrderScheduleOwnerV1.Prepare(root.Capture,changes,null,input.Reason);}
            catch(OrderPriceCorrectionException e)when(e.Message=="ROOT_ENVELOPE_UNRESOLVED")
            {
                for(var n=0;n<items.Count;n++)if(items[n].RootId==root.Capture.Root.RootId&&items[n].Inclusion!="EXCLUDED")
                    items[n]=items[n] with{Inclusion="BLOCKED",ReasonCodes=items[n].ReasonCodes.Concat(new[]{e.Message}).ToArray()};
                continue;
            }
            reasons.AddRange(WorkOrderScheduleOwnerV1.Gates(root.Capture.Root,root.Capture.Observation,changes).Select(g=>g.Code));
            if(root.Planning?.PredecessorRootIds.Any(id=>!capture.Roots.Any(r=>r.Capture.Root.RootId==id))==true)reasons.Add("DEPENDENCY_NOT_CAPTURED");
            groups.Add(new(group,root.Capture,root.Planning?.Reference,[],reasons.Count==0&&changes.Length>0,reasons.Distinct().ToArray()));
        }
        for(var i=0;i<groups.Count;i++)
        {
            var g=groups[i];var roots=capture.Roots.Single(r=>r.Capture.Root.RootId==g.Group.RootWoId).Planning?.PredecessorRootIds??[];
            var dependencies=new HashSet<Guid>();var missing=false;
            foreach(var root in roots){var dependency=groups.SingleOrDefault(x=>x.Group.RootWoId==root);if(dependency==null)missing=true;else dependencies.Add(dependency.Group.GroupId);}
            // A proposed slot cannot overwrite a different root's current slot until that exact original group vacates it.
            foreach(var change in g.Group.ChangedOperations)
            {
                var op=g.Capture.Root.Operations.Single(o=>o.OperationId==change.OperationId);
                foreach(var other in capture.Roots.Where(r=>r.Capture.Root.RootId!=g.Group.RootWoId))
                foreach(var occupying in other.Capture.Root.Operations.Where(o=>SameResource(o.ResourceRef,op.ResourceRef)&&o.Interval!=null&&Overlap(o.Interval,change.NewInterval)))
                {
                    var predecessor=groups.SingleOrDefault(x=>x.Group.RootWoId==other.Capture.Root.RootId);
                    if(predecessor?.Group.ChangedOperations.Any(c=>c.OperationId==occupying.OperationId&&!Overlap(c.NewInterval,change.NewInterval))==true)dependencies.Add(predecessor.Group.GroupId);
                    else missing=true;
                }
            }
            groups[i]=g with{PredecessorGroupIds=dependencies.Order().ToArray(),Eligible=g.Eligible&&!missing,ReasonCodes=missing?g.ReasonCodes.Concat(new[]{"DEPENDENCY_OR_OCCUPANCY_BLOCKED"}).ToArray():g.ReasonCodes};
        }
        bool Cycle(Guid id,HashSet<Guid> stack)
        {if(!stack.Add(id))return true;var result=groups.Single(g=>g.Group.GroupId==id).PredecessorGroupIds.Any(p=>Cycle(p,stack));stack.Remove(id);return result;}
        for(var i=0;i<groups.Count;i++)if(Cycle(groups[i].Group.GroupId,[]))groups[i]=groups[i] with{Eligible=false,ReasonCodes=groups[i].ReasonCodes.Concat(new[]{"DEPENDENCY_CYCLE"}).ToArray()};
        var ordered=items.OrderBy(i=>i.Rank).ToArray();var manifest=Digest(ordered.OrderBy(i=>i.OperationId).ToArray());var id=Guid.NewGuid();
        return new(id,"1",Digest(new{input,capture.CaptureId,manifest,groups}),capture.ExpiresAt,input,Algorithm,
            ["Heuristic only; no finite-capacity optimum or execution permission","UTC instants; no implicit 08:00 start","Legacy LeadTime: truncate(max(1,days*8)) hours; otherwise explicit default","Each Root atomic; across Roots partial completion is possible"],ordered,ordered.Length,manifest,groups.ToArray(),capture.Coverage);
    }
}
