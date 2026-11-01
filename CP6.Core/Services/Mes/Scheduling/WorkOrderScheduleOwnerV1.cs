using CP6.Core.EFDbContext;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;

/// <summary>WO-SCHED-OPERATION-TIME-1 participant. Its caller owns the Main transaction and current WO authorization.</summary>
internal sealed class WorkOrderScheduleOwnerV1(CP6Context db,SchedulingStoreV1 store)
{
    internal static SchedGateV1[] Gates(SchedRootV1 root,SchedObservationV1 observation,SchedOperationChangeV1[] changes)
    {
        var gates=new List<SchedGateV1>();var proposed=root.Operations.Select(o=>changes.FirstOrDefault(c=>c.OperationId==o.OperationId) is {} c?o with{Interval=c.NewInterval}:o).ToArray();
        foreach(var c in changes)
        {
            var g=observation.Guards.Single(x=>x.OperationId==c.OperationId);var op=proposed.Single(x=>x.OperationId==c.OperationId);
            void Deny(string code){gates.Add(new(code,c.OperationId,"NOT_SATISFIED"));}
            if(op.ResourceRef==null)Deny("RESOURCE_UNASSIGNED");
            if(g.ExecutionState!="NOT_STARTED")Deny(g.ExecutionState=="UNKNOWN"?"EXECUTION_UNKNOWN":"OPERATION_STARTED");
            if(!g.ClaimValid)Deny("RESOURCE_CLAIM_INVALID");if(!g.PurposeAllowed)Deny("SOURCE_PURPOSE_DENIED");
            if(!Contains(g.SourceWindow,c.NewInterval))Deny("SOURCE_WINDOW_VIOLATED");
            if(!Contains(g.UseInterval,c.NewInterval))Deny("RESOURCE_USE_INTERVAL_VIOLATED");
            if(g.UnavailableIntervals.Any(b=>Overlap(b,c.NewInterval)))Deny("RESOURCE_UNAVAILABLE");
            if(proposed.Any(p=>p.OperationId!=op.OperationId&&SameResource(p.ResourceRef,op.ResourceRef)&&p.Interval!=null&&Overlap(p.Interval,c.NewInterval)))Deny("RESOURCE_INTERVAL_CONFLICT");
        }
        // A changed predecessor must also preserve the unchanged successor's technical boundary.
        foreach(var op in proposed)
        foreach(var predecessor in observation.Guards.Single(x=>x.OperationId==op.OperationId).PredecessorOccurrences)
        {
            var before=proposed.Single(x=>x.OccurrenceRef==predecessor);
            if(before.Interval==null||op.Interval==null||before.Interval.End>op.Interval.Start)
                gates.Add(new("ROUTE_PREDECESSOR_VIOLATED",op.OperationId,"NOT_SATISFIED"));
        }
        return gates.ToArray();
    }
    internal static RootApplyGroupV1 Prepare(SchedCaptureV1 capture,SchedOperationChangeV1[] changes,string? priority,string reason)
    {
        var root=capture.Root;
        var intervals=root.Operations.Select(o=>changes.FirstOrDefault(c=>c.OperationId==o.OperationId)?.NewInterval??o.Interval).ToArray();
        if(intervals.Any(x=>x==null))throw Error("ROOT_ENVELOPE_UNRESOLVED",422);
        var from=intervals.Min(x=>x!.Start);var to=intervals.Max(x=>x!.End);
        if(root.Schedule.Interval is {} old){if(old.Start<from)from=old.Start;if(old.End>to)to=old.End;}
        var proposed=new RootScheduleV1(new(from,to),priority??root.Schedule.Priority,reason);
        var ordered=changes.OrderBy(x=>x.OperationId).ToArray();var unchanged=root.Operations.Where(o=>!ordered.Any(c=>c.OperationId==o.OperationId)).Select(o=>Ref("SCHED",o.OperationId,o.ScheduleRevision,o)).ToArray();
        var id=Guid.NewGuid();var key=Guid.NewGuid();var manifest=Ref("SCHED-MANIFEST",id,"1",new{root.Operations,changes=ordered});
        var content=new{root.RootId,root.Expected,old=root.Schedule,proposed,Policy,capture.OperationSetRef,manifest,changed=ordered,unchanged,root.QueueHeads,applyKey=key};
        return new(id,root.RootId,root.Expected,root.Schedule,proposed,Policy,capture.OperationSetRef,manifest,ordered,unchanged,root.QueueHeads,Digest(content),key);
    }
    internal Task<RootScheduleGroupResultV1> ApplyRootScheduleGroup(SchedPlanV1 plan,CancellationToken ct)
        =>ApplyRootScheduleGroup(plan.RootApplyGroup,plan.Capture,plan.PlanId,ct);
    internal async Task<RootScheduleGroupResultV1> ApplyRootScheduleGroup(RootApplyGroupV1 group,SchedCaptureV1 capture,Guid planId,CancellationToken ct,SchedObservationV1? adoptedObservation=null)
    {
        if(db.Database.CurrentTransaction==null)throw Error("SCHED_SHARED_TRANSACTION_REQUIRED",503);
        var root=await store.Root(group.RootWoId,ct);
        if(root.Expected!=group.OriginalWoExpected)throw Error("WO_CUT_CHANGED",409);
        if(OperationSet(root)!=group.OldOperationSetRef||root.Schedule!=group.OldRootSchedule)throw Error("SCHED_ROOT_MEMBERS_CHANGED",409);
        if(!root.QueueHeads.SequenceEqual(group.ExpectedQueueHeads))throw Error("QUEUE_CHANGED",409);
        var current=adoptedObservation??await store.Observation(root.RootId,ct);var gates=Gates(root,current,group.ChangedOperations);
        if(gates.Length!=0)throw Error(gates[0].Code,422);
        if(current.Reference!=capture.Observation.Reference)throw Error("SCHED_SOURCE_CUT_CHANGED",409);
        // Physical resource identity is stable across resource-reference versions. Other roots are read under the shared scheduling fence.
        foreach(var other in await store.Roots(root.SiteRef.Id,ct))
        if(other.RootId!=root.RootId)
        {
            var observed=await store.Observation(other.RootId,ct);
            foreach(var change in group.ChangedOperations)
            {
                var operation=root.Operations.Single(o=>o.OperationId==change.OperationId);
                foreach(var occupying in other.Operations.Where(o=>SameResource(o.ResourceRef,operation.ResourceRef)))
                {
                    var guard=observed.Guards.Single(g=>g.OperationId==occupying.OperationId);
                    var interval=guard.ExecutionState is "STARTED" or "COMPLETED"?guard.ActualInterval:occupying.Interval;
                    if(guard.ExecutionState=="UNKNOWN"||guard.ExecutionState is "STARTED" or "COMPLETED"&&interval==null)throw Error("RESOURCE_STATE_UNKNOWN",422);
                    if(interval!=null&&Overlap(interval,change.NewInterval))throw Error("RESOURCE_INTERVAL_CONFLICT",422);
                }
            }
        }
        var operations=root.Operations.ToDictionary(o=>o.OperationId);var results=new List<SchedOperationResultV1>();
        foreach(var change in group.ChangedOperations)
        {
            var old=operations[change.OperationId];
            if(old.ScheduleRevision!=change.ExpectedScheduleRevision)throw Error("STALE_SCHEDULE",409);
            var successor=old with{ScheduleRevision=Next(old.ScheduleRevision),Interval=change.NewInterval};operations[old.OperationId]=successor;
            results.Add(new(old.OperationId,old.ScheduleRevision,successor.ScheduleRevision,Ref("SCHED",old.OperationId,successor.ScheduleRevision,successor),change.NewInterval));
        }
        var expected=root.Expected with{Revision=Next(root.Expected.Revision)};
        var next=root with{Expected=expected,Schedule=group.NewRootSchedule,Operations=operations.Values.OrderBy(x=>x.OperationId).ToArray()};
        await store.SetRoot(next,ct);
        var transition=Ref("WO-SCHED-TRANSITION",Guid.NewGuid(),"1",new{group.ApplyKey,group.GroupDigest,before=root.Expected,after=expected});
        var audit=Ref("SCHED-AUDIT",Guid.NewGuid(),"1",new{group.ApplyKey,planId,group.GroupDigest});
        return new(Ref("WO-SCHED-APPLICATION",Guid.NewGuid(),"1",new{group.ApplyKey,group.GroupDigest,transition}),group.ApplyKey,group.GroupDigest,
            Ref("MES-WO",root.RootId,expected.Revision,next),root.Expected,expected,root.Schedule,next.Schedule,results.ToArray(),
            Ref("SCHED-UNCHANGED",root.RootId,root.Expected.Revision,group.UnchangedOperationRefs),[],transition,audit);
    }
    internal async Task<RootScheduleGroupResultV1> ApplyRebind(SchedPlanV1 p,RebindPhaseRequestV1 request,SchedRefV1 assignment,SchedRefV1 claim,CancellationToken ct)
    {
        var root=await store.Root(p.Capture.Root.RootId,ct);var r=p.Rebind!;var op=root.Operations.Single(o=>o.OperationId==p.Input.OperationId);
        if(root.Expected!=request.Expected.Accepted)throw Error("WO_CUT_CHANGED",409);
        // Five-domain acceptance derives exclusively from this work's Owner receipts, never from a fresh expected value.
        if(root.Expected.SnapshotId!=p.Capture.Root.Expected.SnapshotId||root.Expected.EvidenceVersion!=p.Capture.Root.Expected.EvidenceVersion
            ||root.Schedule!=p.Capture.Root.Schedule||Digest(root.Operations)!=Digest(p.Capture.Root.Operations))throw Error("REBIND_ROOT_SCOPE_CHANGED",409);
        var current=await store.Observation(root.RootId,ct);
        if(current.Reference!=p.Capture.Observation.Reference)throw Error("SCHED_SOURCE_CUT_CHANGED",409);
        var effective=root with{Operations=root.Operations.Select(o=>o.OperationId==op.OperationId?o with{AssignmentRef=assignment,ResourceRef=r.Choice.ResourceRef}:o).ToArray()};
        var observed=current with{Guards=current.Guards.Select(g=>g.OperationId==op.OperationId?g with{ClaimRef=claim,UseInterval=p.RootApplyGroup.ChangedOperations.Single().NewInterval,ClaimValid=true}:g).ToArray()};
        await store.SetRoot(effective,ct);
        // Same source identity inside this transaction; guard adoption is written as a new observation only after the shared kernel succeeds.
        var changes=p.RootApplyGroup.ChangedOperations.Select(c=>c.OperationId==op.OperationId?c with{AssignmentRef=assignment}:c).ToArray();
        var group=p.RootApplyGroup with{OriginalWoExpected=request.Expected.Accepted,OldOperationSetRef=OperationSet(effective),ChangedOperations=changes,ExpectedQueueHeads=root.QueueHeads};
        var result=await ApplyRootScheduleGroup(group,p.Capture with{Root=effective,Observation=observed},p.PlanId,ct,observed);
        var next=await store.Root(root.RootId,ct);var accepted=next.Expected with{BindingRegistryRevision=Next(next.Expected.BindingRegistryRevision)};
        await store.SetRoot(next with{Expected=accepted},ct);
        var source=observed with{Reference=Ref(current.Reference.Owner,current.Reference.Id,Next(current.Reference.Version),new{request.Key,claim,assignment})};await store.Publish(source,ct);
        var transition=Ref("WO-REBIND-TRANSITION",request.Key,"1",new{request.PayloadDigest,before=root.Expected,after=accepted,assignment,claim});
        return result with{NewWoExpected=accepted,RootWoRef=Ref("MES-WO",root.RootId,accepted.Revision,next with{Expected=accepted}),OwnTransitionRef=transition};
    }

}
