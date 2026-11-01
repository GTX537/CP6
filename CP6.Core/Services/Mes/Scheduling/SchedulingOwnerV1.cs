using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;

public sealed class SchedulingOwnerV1(CP6Context db,OperationsOwnerV1 owner,ScopeAuthorizationFenceV1 scope,IOptions<SchedulingSimulationOptionsV1> simulation)
{
    private OperationsOwnerV1 Authority=>owner.Owns(db)&&scope.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private SchedulingStoreV1 Store=>new(db,scope.EnvironmentId);
    private async Task Lease(OperationsAccessV1 a,bool write,CancellationToken ct)
    {var lease=await scope.AcquireAsync(ct);a.AtCommit(t=>scope.ValidateAsync(lease,t));await Store.Fence(write,ct);}
    private static async Task Visible(OperationsAccessV1 a,Guid id,CancellationToken ct)
    {if(!await (await a.PlanningOrders(ct)).AnyAsync(w=>w.Id==id,ct))throw Error("SCHED_NOT_VISIBLE",404);}
    public Task<SchedRootV1> OpenSimulationAsync(SchedOpeningV1 input,CancellationToken ct=default)
    {
        if(!simulation.Value.Enabled)throw Error("SCHED_SIMULATION_DISABLED",404);Opening(input);var frozen=input;
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"simulate-source",true,async(a,t)=>
        {
            await Lease(a,true,t);a.RequireSchedulingSimulation();await Visible(a,frozen.RootId,t);
            var root=await db.WorkOrders.AsNoTracking().SingleAsync(w=>w.Id==frozen.RootId,t);
            var rows=await db.WorkOrderProcesses.AsNoTracking().Where(x=>x.WorkOrderNo==root.WorkOrderNo&&!x.IsDeleted).ToArrayAsync(t);
            if(root.ActualStartDate!=null||root.CompletedQty!=0||root.DefectQty!=0||root.Status is not(0 or 1)
                ||rows.Any(x=>x.ProcessStatus!=0||x.ActualStartTime!=null||x.ActualEndTime!=null||x.GoodQty!=0||x.DefectQty!=0)
                ||!rows.Select(x=>x.Id).Order().SequenceEqual(frozen.Operations.Select(x=>x.OperationId).Order()))throw Error("SCHED_OPENING_REQUIRES_UNUSED_EXACT_ROOT",409);
            foreach(var op in frozen.Operations)if(rows.Single(x=>x.Id==op.OperationId).SortOrder!=op.RouteSortOrder)throw Error("SCHED_ROUTE_ORDER_MISMATCH");
            return await Store.Open(frozen,root.WorkOrderNo,t);
        },ct);
    }
    public Task<SchedRefV1> PublishSimulationAsync(SchedObservationV1 input,CancellationToken ct=default)
    {
        if(!simulation.Value.Enabled)throw Error("SCHED_SIMULATION_DISABLED",404);var frozen=input;
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"simulate-source",true,async(a,t)=>
        {await Lease(a,true,t);a.RequireSchedulingSimulation();await Visible(a,frozen.RootId,t);var root=await Store.Root(frozen.RootId,t);
         Observation(frozen,root.RootId,root.Operations);var current=await Store.Observation(root.RootId,t);
         if(frozen.Reference!=current.Reference&&(frozen.Reference.Id!=current.Reference.Id||frozen.Reference.Owner!=current.Reference.Owner
             ||frozen.Reference.Version!=Next(current.Reference.Version)))throw Error("SCHED_SOURCE_PREDECESSOR_CHANGED",409);
         await Store.Publish(frozen,t);return frozen.Reference;},ct);
    }
    public Task<object> ContextAsync(Guid? siteId=null,CancellationToken ct=default)=>Authority.ExecuteAsync<object>(OperationsOwnerV1.Planning,"query",false,async(a,t)=>
    {
        await Lease(a,false,t);var allowed=await (await a.PlanningOrders(t)).Select(w=>w.Id).ToArrayAsync(t);
        var ids=await Store.RootIds(siteId).Where(id=>allowed.Contains(id)).ToArrayAsync(t);var roots=new List<object>();
        foreach(var id in ids){var r=await Store.Root(id,t);roots.Add(new{r.RootId,r.WorkOrderNo,r.SiteRef,r.ZoneId,operations=r.Operations.Select(o=>new{o.OperationId,o.ProcessLabel,o.OccurrenceRef})});}
        return new{tenantId=db.CurrentTenantId,actorId=a.ActorId,environmentId=scope.EnvironmentId,roots,mode="TIME_ONLY",dataKind="SIMULATED_UPSTREAM"};
    },ct);
    public Task<SchedCaptureV1> CaptureAsync(Guid rootId,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.Planning,"query",true,async(a,t)=>
    {
        await Lease(a,true,t);await Visible(a,rootId,t);var root=await Store.Root(rootId,t);var source=await Store.Observation(rootId,t);var now=Now();
        var capture=new SchedCaptureV1(Guid.NewGuid(),now,now.AddMinutes(30),source.DataKind,root,source,OperationSet(root),Policy,true);
        await Store.Save(capture.CaptureId,rootId,"capture",a.ActorId,capture,t);return capture;
    },ct);
    public Task<SchedPlanV1> PrepareAsync(SchedChangeRequestV1 input,CancellationToken ct=default)
    {
        if(input==null||input.CaptureId==Guid.Empty||input.OperationId==Guid.Empty)throw Error("SCHED_INPUT_INVALID");
        Reason(input.Reason);Expected(input.WoExpected);Version(input.ExpectedScheduleRevision);Interval(new(input.ProposedStart,input.ProposedEnd));
        if(input.Kind!="TIME_ONLY"||input.ResourceChoiceRef!=null)throw Error("SCHED_TIME_ONLY_REQUIRED");
        if(input.ProposedPriority!=null&&input.ProposedPriority is not("NORMAL" or "URGENT" or "EXPEDITE"))throw Error("SCHED_PRIORITY_INVALID");
        var frozen=input;
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"plan.prepare",true,async(a,t)=>
        {
            await Lease(a,true,t);var d=await Store.Read(frozen.CaptureId,"capture",t);await Visible(a,d.RootId,t);
            if(d.ActorId!=a.ActorId)throw Error("SCHED_CAPTURE_ACTOR_MISMATCH",403);
            var capture=Decode<SchedCaptureV1>(d.Body);if(capture.ExpiresAt<=DateTimeOffset.UtcNow)throw Error("SCHED_CAPTURE_EXPIRED",409);
            var current=await Store.Root(d.RootId,t);
            if(current.Expected!=capture.Root.Expected||OperationSet(current)!=capture.OperationSetRef
                ||(await Store.Observation(d.RootId,t)).Reference!=capture.Observation.Reference)throw Error("SCHED_CAPTURE_STALE",409);
            var op=capture.Root.Operations.SingleOrDefault(x=>x.OperationId==frozen.OperationId)??throw Error("SCHED_OPERATION_NOT_IN_CAPTURE");
            if(capture.Root.Expected!=frozen.WoExpected||op.ScheduleRevision!=frozen.ExpectedScheduleRevision)throw Error("SCHED_CAPTURE_PREDECESSOR_MISMATCH",409);
            var guard=capture.Observation.Guards.Single(x=>x.OperationId==op.OperationId);
            var changes=new[]{new SchedOperationChangeV1(op.OperationId,op.OccurrenceRef,op.ScheduleRevision,op.Interval,new(frozen.ProposedStart,frozen.ProposedEnd),op.AssignmentRef,guard.SourceWindowRef)};
            var group=WorkOrderScheduleOwnerV1.Prepare(capture,changes,frozen.ProposedPriority,frozen.Reason);
            var gates=WorkOrderScheduleOwnerV1.Gates(capture.Root,capture.Observation,changes);var id=Guid.NewGuid();
            var plan=new SchedPlanV1(id,"1",Digest(new{frozen,group,actor=a.ActorId,capture.Observation.Reference}),capture.ExpiresAt,frozen,capture,group,gates,gates.Length==0);
            await Store.Save(id,d.RootId,"plan",a.ActorId,plan,t);return plan;
        },ct);
    }
    public Task<SchedPlanV1> PlanAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.Planning,"query",false,async(a,t)=>
    {await Lease(a,false,t);var d=await Store.Read(id,"plan",t);await Visible(a,d.RootId,t);return Decode<SchedPlanV1>(d.Body);},ct);
    public Task<SchedReplyV1> ApplyAsync(SchedApplyV1 input,CancellationToken ct=default)
    {
        if(input==null||input.CommandId==Guid.Empty||input.PlanId==Guid.Empty||string.IsNullOrWhiteSpace(input.ExpectedPlanDigest))throw Error("SCHED_APPLY_INPUT_INVALID");
        Reason(input.Reason);Version(input.ExpectedPlanRevision);var frozen=input with{};
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"change.apply",true,async(a,t)=>
        {
            await Lease(a,true,t);var store=Store;var old=await store.Command(a.ActorId,frozen.CommandId,t);
            var d=await store.Read(frozen.PlanId,"plan",t);await Visible(a,d.RootId,t);
            if(d.ActorId!=a.ActorId)throw Error("SCHED_PLAN_ACTOR_MISMATCH",403);
            if(old!=null){if(old.InputDigest!=Digest(frozen))throw Error("SCHED_COMMAND_CONFLICT",409);return Decode<SchedReplyV1>(old.Body) with{Replayed=true};}
            var plan=Decode<SchedPlanV1>(d.Body);
            if(plan.Revision!=frozen.ExpectedPlanRevision||plan.Digest!=frozen.ExpectedPlanDigest||plan.Input.Reason!=frozen.Reason)throw Error("SCHED_PLAN_MISMATCH",409);
            var applied=await store.Applied(plan.PlanId,t);
            if(applied!=null)
            {
                var same=new SchedReplyV1(frozen.CommandId,"COMMITTED",plan.RootApplyGroup.GroupId,Decode<RootScheduleGroupResultV1>(applied.Body),true);
                await store.SaveCommand(a.ActorId,frozen,same,t);return same;
            }
            if(plan.ExpiresAt<=DateTimeOffset.UtcNow)throw Error("SCHED_PLAN_EXPIRED",409);
            if(!plan.Eligible)throw Error("SCHED_PLAN_INELIGIBLE",422);
            var work=await a.ActionAsync(OperationsOwnerV1.Mes,"change.schedule",t);
            await work.RequireWorkOrder(plan.Capture.Root.WorkOrderNo,false,t);
            var result=await new WorkOrderScheduleOwnerV1(db,store).ApplyRootScheduleGroup(plan,t);
            await store.SaveApplication(plan,a.ActorId,result,t);
            var reply=new SchedReplyV1(frozen.CommandId,"COMMITTED",plan.RootApplyGroup.GroupId,result,false);await store.SaveCommand(a.ActorId,frozen,reply,t);return reply;
        },ct,actionAtFence:async t=>
        {var actor=await owner.CurrentSubjectAsync(t);return await Store.Command(actor,frozen.CommandId,t)!=null?"command.read":"change.apply";});
    }
    public Task<object> CommandAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync<object>(OperationsOwnerV1.Planning,"command.read",false,async(a,t)=>
    {await Lease(a,false,t);var row=await Store.Command(a.ActorId,id,t);if(row==null)return new{status="NOT_OBSERVED",canInferNoEffect=false,asOf=DateTimeOffset.UtcNow};
     var d=await Store.Read(row.PlanId,"plan",t);await Visible(a,d.RootId,t);return new{status="FOUND",reply=Decode<SchedReplyV1>(row.Body),canInferNoEffect=false};},ct);
    public Task<object[]> HistoryAsync(Guid rootId,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.Planning,"history.read",false,async(a,t)=>
    {
        await Lease(a,false,t);await Visible(a,rootId,t);var bodies=await db.Database.SqlQuery<byte[]>($"""
            SELECT body AS "Value" FROM cp6_scheduling_v1.applications WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND root_id={rootId}
            """).ToArrayAsync(t);return bodies.Select(x=>(object)Decode<RootScheduleGroupResultV1>(x)).ToArray();
    },ct);
}
