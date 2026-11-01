using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;
public sealed partial class SchedulingOwnerV1
{
    public Task<object> QueueContextAsync(CancellationToken ct=default)=>Authority.ExecuteAsync<object>(OperationsOwnerV1.Planning,"queue.read",false,async(a,t)=>
    {
        await Lease(a,false,t);var visible=await (await a.PlanningOrders(t)).Select(w=>w.Id).ToArrayAsync(t);var bodies=await db.Database.SqlQuery<byte[]>($"""
          SELECT body AS "Value" FROM cp6_scheduling_v1.queue_heads WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId}
          """).ToArrayAsync(t);var resources=new List<object>();
        foreach(var head in bodies.Select(Decode<QueueHeadV1>))
        {
            var roots=await Store.Roots(head.SiteId,t);
            if(roots.Any(r=>visible.Contains(r.RootId)&&r.QueueHeads.Any(q=>q.Id==head.QueueId)))resources.Add(new{head.QueueId,head.SiteId,head.ResourceRef});
        }
        return new{tenantId=db.CurrentTenantId,environmentId=scope.EnvironmentId,actorId=a.ActorId,resources};
    },ct);
    public Task<QueueHeadV1> InitializeQueueSimulationAsync(Guid site,SchedRefV1 resource,CancellationToken ct=default)
    {
        if(!simulation.Value.Enabled)throw Error("SCHED_SIMULATION_DISABLED",404);Reference(resource);if(site==Guid.Empty)throw Error("SCHED_SITE_REQUIRED");
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"simulate-source",true,async(a,t)=>
        {
            await Lease(a,true,t);a.RequireSchedulingSimulation();var roots=await Store.Roots(site,t);
            var members=new List<QueueMemberV1>();
            foreach(var root in roots.OrderBy(r=>r.RootId))
            foreach(var op in root.Operations.Where(o=>SameResource(o.ResourceRef,resource)).OrderBy(o=>o.RouteSortOrder).ThenBy(o=>o.OperationId))
            {
                await Visible(a,root.RootId,t);var observed=await Store.Observation(root.RootId,t);
                if(observed.Guards.Single(g=>g.OperationId==op.OperationId).ExecutionState=="COMPLETED")continue;
                members.Add(new(op.OperationId,root.RootId,op.OperationId,op.OccurrenceRef,op.AssignmentRef!,op.RouteSortOrder));
            }
            var old=await Store.Queue(site,resource.Id,t);
            if(old!=null)
            {
                if(!SameResource(old.ResourceRef,resource)||old.Members.Any(m=>!members.Contains(m)))throw Error("QUEUE_ASSIGNMENT_MIGRATION_REQUIRED",409);
                var added=members.Where(m=>!old.Members.Contains(m)).ToArray();if(added.Length==0)return old;
                var next=old with{Revision=Next(old.Revision),Members=old.Members.Concat(added).ToArray()};
                await Store.SaveQueue(next,new{kind="SIMULATED_CURRENT_ASSIGNMENT_APPEND",a.ActorId,at=Now()},t);return next;
            }
            if(members.Count==0)throw Error("QUEUE_RESOURCE_NOT_ASSIGNED",404);
            var head=new QueueHeadV1(Guid.NewGuid(),site,resource,"1",members.ToArray());
            await Store.SaveQueue(head,new{kind="SIMULATED_CURRENT_ASSIGNMENT_OPENING",a.ActorId,at=Now()},t);return head;
        },ct);
    }
    private async Task<QueueHeadV1> QueueRequired(Guid site,Guid resource,CancellationToken ct)=>await Store.Queue(site,resource,ct)??throw Error("QUEUE_NOT_VISIBLE",404);
    private async Task<QueueItemV1[]> QueueProjection(QueueHeadV1 head,CancellationToken ct)
    {
        var rows=new List<QueueItemV1>();var roots=await Store.Roots(head.SiteId,ct);var migrations=await Store.RebindMigrations(ct);
        foreach(var m in head.Members)
        {
            var root=roots.Single(r=>r.RootId==m.RootId);var op=root.Operations.Single(o=>o.OperationId==m.OperationId);
            var superseded=op.AssignmentRef!=m.AssignmentRef||!SameResource(op.ResourceRef,head.ResourceRef);
            if(superseded&&!migrations.Any(x=>x.State=="PENDING"&&x.OldMember==m&&x.NewAssignment==op.AssignmentRef))throw Error("QUEUE_ASSIGNMENT_MIGRATION_REQUIRED",409);
            var observation=await Store.Observation(root.RootId,ct);var guard=observation.Guards.Single(g=>g.OperationId==op.OperationId);
            var state=superseded?"ASSIGNMENT_SUPERSEDED":guard.ExecutionState switch{"NOT_STARTED"=>"WAITING","STARTED"=>"LOCKED_IN_EXECUTION","COMPLETED"=>"COMPLETED",_=>"UNKNOWN"};
            rows.Add(new(m,rows.Count+1,state,Ref("SCHED",op.OperationId,op.ScheduleRevision,op),op.Interval,guard.PredecessorOccurrences,
                "NO_EXECUTION_PERMISSION",state=="LOCKED_IN_EXECUTION"&&rows.Any(x=>x.State=="WAITING"),observation.Reference));
        }
        // New assignments cannot silently disappear from a purportedly complete queue.
        foreach(var r in roots)
        foreach(var op in r.Operations.Where(o=>SameResource(o.ResourceRef,head.ResourceRef)&&!head.Members.Any(m=>m.OperationId==o.OperationId)))
        {
            var observed=await Store.Observation(r.RootId,ct);var guard=observed.Guards.Single(g=>g.OperationId==op.OperationId);if(guard.ExecutionState=="COMPLETED")continue;
            var pending=migrations.SingleOrDefault(x=>x.State=="PENDING"&&x.OperationId==op.OperationId&&x.NewAssignment==op.AssignmentRef);
            if(pending==null)throw Error("QUEUE_MEMBERSHIP_CHANGED",409);
            rows.Add(new(pending.OldMember with{ItemId=pending.NewItemId,AssignmentRef=pending.NewAssignment},rows.Count+1,"PENDING_QUEUE_MIGRATION",
                Ref("SCHED",op.OperationId,op.ScheduleRevision,op),op.Interval,guard.PredecessorOccurrences,"NO_EXECUTION_PERMISSION",false,observed.Reference));
        }
        return rows.ToArray();
    }
    private static bool InQueueWindow(QueueItemV1 i,SchedIntervalV1? window)=>i.State!="COMPLETED"&&(window==null||i.Interval==null||Overlap(i.Interval,window));
    private async Task QueueVisible(OperationsAccessV1 a,QueueCaptureV1 c,CancellationToken ct)
    {foreach(var id in c.VisibleRoots)await Visible(a,id,ct);}
    public Task<QueueViewV1> QueueAsync(Guid site,Guid resource,Guid? captureId=null,int cursor=0,int pageSize=20,SchedIntervalV1? window=null,CancellationToken ct=default)
    {
        if(site==Guid.Empty||resource==Guid.Empty||cursor<0||pageSize is not(20 or 50 or 100)||captureId==null&&cursor!=0)throw Error("QUEUE_QUERY_INVALID");
        if(window!=null)Interval(window);
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"queue.read",true,async(a,t)=>
        {
            await Lease(a,true,t);QueueCaptureV1 capture;
            if(captureId==null)
            {
                var head=await QueueRequired(site,resource,t);var items=await QueueProjection(head,t);
                var visible=await (await a.PlanningOrders(t)).Select(w=>w.Id).ToArrayAsync(t);
                if(!items.Any(i=>visible.Contains(i.Member.RootId))&&head.Members.Length>0)throw Error("QUEUE_NOT_VISIBLE",404);
                var allowed=items.Where(i=>visible.Contains(i.Member.RootId)).Select(i=>i.Member.RootId).Distinct().ToArray();
                // Empty queues still require visibility of their historical root scopes.
                if(head.Members.Length==0)
                {var all=await Store.Roots(site,t);allowed=all.Where(r=>r.QueueHeads.Any(q=>q.Id==head.QueueId)).Select(r=>r.RootId).ToArray();foreach(var id in allowed)await Visible(a,id,t);}
                var complete=items.All(i=>visible.Contains(i.Member.RootId)&&InQueueWindow(i,window)&&i.State!="PENDING_QUEUE_MIGRATION");var now=Now();
                capture=new(Guid.NewGuid(),a.ActorId,head,items,allowed,now,now.AddMinutes(30),Digest(items),complete,window);
                await Store.SaveArrangement(capture.CaptureId,site,"queue-capture",a.ActorId,capture,t);
            }
            else
            {
                var d=await Store.Arrangement(captureId.Value,"queue-capture",t);capture=Decode<QueueCaptureV1>(d.Body);
                if(capture.ActorId!=a.ActorId)throw Error("QUEUE_CAPTURE_ACTOR_MISMATCH",403);
                if(capture.Head.SiteId!=site||capture.Head.ResourceRef.Id!=resource||capture.Window!=window)throw Error("QUEUE_CAPTURE_SCOPE_MISMATCH",409);
                if(capture.ExpiresAt<=DateTimeOffset.UtcNow)throw Error("QUEUE_CAPTURE_EXPIRED",409);await QueueVisible(a,capture,t);
            }
            var selected=capture.Items.Where(i=>capture.VisibleRoots.Contains(i.Member.RootId)&&InQueueWindow(i,window)).Select((i,n)=>i with{QueueOrdinal=n+1}).ToArray();
            return new QueueViewV1(capture.CaptureId,capture.Head.QueueId,site,capture.Head.ResourceRef,capture.Head.Revision,selected.Skip(cursor).Take(pageSize).ToArray(),
                capture.CompleteForWrite?capture.ManifestDigest:Digest(selected),capture.CompleteForWrite?selected.Length:null,capture.CompleteForWrite,capture.CompleteForWrite?"COMPLETE":"PARTIAL",capture.AsOf,capture.ExpiresAt,cursor+pageSize<selected.Length?cursor+pageSize:null);
        },ct);
    }
    private static Guid[] MoveQueue(QueueItemV1[] items,QueuePlanRequestV1 input)
    {
        var moving=items.SingleOrDefault(i=>i.Member.ItemId==input.MoveItemId)??throw Error("QUEUE_ITEM_NOT_IN_CAPTURE");
        if(moving.State!="WAITING")throw Error(moving.State=="UNKNOWN"?"QUEUE_EXECUTION_UNKNOWN":"ITEM_LOCKED",409);
        var order=items.Select(i=>i.Member.ItemId).Where(id=>id!=input.MoveItemId).ToList();var anchor=input.BeforeItemId??input.AfterItemId;
        var at=anchor==null?order.Count:order.IndexOf(anchor.Value);if(at<0)throw Error("QUEUE_ANCHOR_NOT_IN_CAPTURE");if(input.Position=="AFTER")at++;
        order.Insert(at,input.MoveItemId);
        if(order.SequenceEqual(items.Select(i=>i.Member.ItemId)))throw Error("QUEUE_ORDER_UNCHANGED",422);
        foreach(var row in items)
        {
            if(row.State!="WAITING"&&order.IndexOf(row.Member.ItemId)!=Array.IndexOf(items,row))throw Error("QUEUE_LOCKED_ANCHOR",409);
            foreach(var dependency in row.RoutePredecessorRefs)
            {var prior=items.SingleOrDefault(i=>i.Member.OccurrenceRef==dependency);if(prior!=null&&order.IndexOf(prior.Member.ItemId)>=order.IndexOf(row.Member.ItemId))throw Error("QUEUE_ROUTE_PREDECESSOR_VIOLATED",422);}
        }
        return order.ToArray();
    }
    public Task<QueuePlanV1> PrepareQueueAsync(Guid resource,QueuePlanRequestV1 input,CancellationToken ct=default)
    {
        if(input==null||input.CaptureId==Guid.Empty||input.MoveItemId==Guid.Empty||input.ResourceRef?.Id!=resource
            ||!(input.Position=="BEFORE"&&input.BeforeItemId!=null&&input.AfterItemId==null||input.Position=="AFTER"&&input.AfterItemId!=null&&input.BeforeItemId==null||input.Position=="END"&&input.BeforeItemId==null&&input.AfterItemId==null))throw Error("QUEUE_POSITION_INVALID");
        Reference(input.ResourceRef);Version(input.ExpectedQueueRevision);Reason(input.Reason);
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"queue.prepare",true,async(a,t)=>
        {
            await Lease(a,true,t);var capture=Decode<QueueCaptureV1>((await Store.Arrangement(input.CaptureId,"queue-capture",t)).Body);
            if(capture.ActorId!=a.ActorId)throw Error("QUEUE_CAPTURE_ACTOR_MISMATCH",403);await QueueVisible(a,capture,t);
            if(!capture.CompleteForWrite)throw Error("QUEUE_COMPLETE_SCOPE_REQUIRED",403);
            if(capture.ExpiresAt<=DateTimeOffset.UtcNow)throw Error("QUEUE_CAPTURE_EXPIRED",409);
            if(capture.Head.ResourceRef!=input.ResourceRef||capture.Head.Revision!=input.ExpectedQueueRevision||capture.ManifestDigest!=input.ExpectedManifestDigest)throw Error("QUEUE_CAPTURE_MISMATCH",409);
            var current=await QueueRequired(capture.Head.SiteId,resource,t);var rows=await QueueProjection(current,t);
            if(current.Revision!=capture.Head.Revision||Digest(rows)!=capture.ManifestDigest)throw Error("QUEUE_CAPTURE_STALE",409);
            var order=MoveQueue(rows,input);var old=rows.Select(i=>i.Member.ItemId).ToArray();var id=Guid.NewGuid();
            var plan=new QueuePlanV1(id,"1",Digest(new{input,capture,order}),capture.ExpiresAt,input,capture,old,order,order.Where((x,n)=>x!=old[n]).ToArray());
            await Store.SaveArrangement(id,capture.Head.SiteId,"queue-plan",a.ActorId,plan,t);return plan;
        },ct);
    }
    private async Task<QueuePlanV1> QueuePlan(OperationsAccessV1 a,Guid id,CancellationToken ct)
    {var d=await Store.Arrangement(id,"queue-plan",ct);if(d.ActorId!=a.ActorId)throw Error("QUEUE_PLAN_ACTOR_MISMATCH",403);var p=Decode<QueuePlanV1>(d.Body);await QueueVisible(a,p.Capture,ct);return p;}
    public Task<QueuePlanV1> QueuePlanAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.Planning,"queue.read",false,async(a,t)=>
    {await Lease(a,false,t);return await QueuePlan(a,id,t);},ct);
    public Task<QueueReplyV1> ApplyQueueAsync(SchedApplyV1 input,CancellationToken ct=default)
    {
        if(input==null||input.CommandId==Guid.Empty||input.PlanId==Guid.Empty)throw Error("QUEUE_APPLY_INVALID");Reason(input.Reason);Version(input.ExpectedPlanRevision);
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"queue.apply",true,async(a,t)=>
        {
            await Lease(a,true,t);var old=await Store.QueueCommand(a.ActorId,input.CommandId,t);
            if(await Store.RebindCommand(a.ActorId,input.CommandId,t)!=null||await Store.Command(a.ActorId,input.CommandId,t)!=null||await Store.Batch(a.ActorId,input.CommandId,t)!=null)throw Error("SCHED_COMMAND_CONFLICT",409);
            var p=await QueuePlan(a,input.PlanId,t);
            if(old!=null){if(old.InputDigest!=Digest(input))throw Error("SCHED_COMMAND_CONFLICT",409);return Decode<QueueReplyV1>(old.Body) with{Replayed=true};}
            if(p.Revision!=input.ExpectedPlanRevision||p.Digest!=input.ExpectedPlanDigest||p.Input.Reason!=input.Reason)throw Error("QUEUE_PLAN_MISMATCH",409);
            var previous=await Store.QueueApplied(p.PlanId,t);
            if(previous!=null){var replay=previous with{CommandId=input.CommandId,Replayed=true};await Store.SaveQueueCommand(a.ActorId,input,replay,t);return replay;}
            if(p.ExpiresAt<=DateTimeOffset.UtcNow)throw Error("QUEUE_PLAN_EXPIRED",409);
            var head=await QueueRequired(p.Capture.Head.SiteId,p.Input.ResourceRef.Id,t);var rows=await QueueProjection(head,t);
            if(head.Revision!=p.Input.ExpectedQueueRevision)throw Error("QUEUE_CHANGED",409);
            MoveQueue(rows,p.Input); // Current execution locks and technical edges own the final decision.
            if(Digest(rows)!=p.Capture.ManifestDigest)throw Error("QUEUE_CURRENT_STATE_CHANGED",409);
            var next=head with{Revision=Next(head.Revision),Members=p.NewOrder.Select(id=>head.Members.Single(m=>m.ItemId==id)).ToArray()};
            var audit=Ref("SCHED-QUEUE-AUDIT",Guid.NewGuid(),"1",new{p.PlanId,a.ActorId,input.CommandId,input.Reason});
            await Store.SaveQueue(next,new{audit,a.ActorId,input.CommandId,p.PlanId,input.Reason,old=head,at=Now()},t);
            var result=new QueueReplyV1(input.CommandId,"COMMITTED",p.PlanId,Ref("SCHED-QUEUE",next.QueueId,next.Revision,next),audit,false);
            await Store.SaveQueueApplied(p.PlanId,result,t);await Store.SaveQueueCommand(a.ActorId,input,result,t);return result;
        },ct,actionAtFence:async t=>await Store.QueueCommand(await owner.CurrentSubjectAsync(t),input.CommandId,t)!=null?"command.read":"queue.apply");
    }
    public Task<object[]> QueueHistoryAsync(Guid site,Guid resource,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.Planning,"history.read",false,async(a,t)=>
    {
        await Lease(a,false,t);var head=await QueueRequired(site,resource,t);var bodies=await db.Database.SqlQuery<byte[]>($"""
            SELECT body AS "Value" FROM cp6_scheduling_v1.queue_revisions WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND queue_id={head.QueueId} ORDER BY length(revision),revision
            """).ToArrayAsync(t);var history=bodies.Select(Decode<QueueHeadV1>).ToArray();
        foreach(var root in history.SelectMany(h=>h.Members).Select(m=>m.RootId).Distinct())await Visible(a,root,t);
        return history.Cast<object>().ToArray();
    },ct);
}
