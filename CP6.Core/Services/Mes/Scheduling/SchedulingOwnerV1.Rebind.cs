using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;
public sealed partial class SchedulingOwnerV1
{
    private static readonly string[] RebindPhases=["BEGIN","CLOSE_OLD","RESERVE_NEW","COMMIT_BINDING","RELEASE_OLD","CLOSE_UNUSED"];
    public Task<SchedRefV1> OpenRebindSimulationAsync(RebindSimulationV1 input,CancellationToken ct=default)
    {
        if(!simulation.Value.Enabled)throw Error("SCHED_SIMULATION_DISABLED",404);
        if(input?.DataKind!="SIMULATED"||input.RootId==Guid.Empty||input.OperationId==Guid.Empty||input.OldClaimRefs is not{Length:>0}
            ||input.OldExecutions==null||input.Choices is not{Length:>0}||input.DeferredPhases==null)throw Error("REBIND_SIMULATED_MANIFEST_REQUIRED");
        foreach(var c in input.OldClaimRefs)Reference(c);
        foreach(var e in input.OldExecutions){Reference(e.WorkRef);if(e.State is not("REGISTERED" or "CLOSED_NO_EXECUTION" or "UNKNOWN" or "MAY_EXECUTE" or "ACTUAL_CONFLICT"))throw Error("REBIND_EXECUTION_STATE_INVALID");}
        foreach(var c in input.Choices){Reference(c.ChoiceRef);Reference(c.ResourceRef);Reference(c.RequirementRef);Reference(c.TechnicalRef);Interval(c.UseInterval);if(c.Protocol!="RES-REBIND-1")throw Error("REBIND_PROTOCOL_UNSUPPORTED");}
        if(input.OldClaimRefs.Select(x=>x.Id).Distinct().Count()!=input.OldClaimRefs.Length||input.OldExecutions.Select(x=>x.WorkRef.Id).Distinct().Count()!=input.OldExecutions.Length
            ||input.Choices.Select(x=>x.ChoiceRef.Id).Distinct().Count()!=input.Choices.Length||input.DeferredPhases.Any(x=>!RebindPhases.Contains(x)))throw Error("REBIND_SOURCE_MANIFEST_INVALID");
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"simulate-source",true,async(a,t)=>
        {
            await Lease(a,true,t);a.RequireSchedulingSimulation();await Visible(a,input.RootId,t);var root=await Store.Root(input.RootId,t);
            var op=root.Operations.SingleOrDefault(o=>o.OperationId==input.OperationId)??throw Error("REBIND_OPERATION_NOT_FOUND",404);
            if(op.AssignmentRef==null)throw Error("REBIND_OLD_BINDING_REQUIRED",409);
            var existing=await Store.RebindSource(op.AssignmentRef.Id,t);
            if(existing!=null){if(Digest(existing)!=Digest(input))throw Error("REBIND_SOURCE_IMMUTABLE",409);return Ref("SIMULATED-REBIND-SOURCE",op.AssignmentRef.Id,"1",input);}
            var guard=(await Store.Observation(root.RootId,t)).Guards.Single(g=>g.OperationId==op.OperationId);
            if(!input.OldClaimRefs.Contains(guard.ClaimRef))throw Error("REBIND_EXACT_OLD_CLAIM_SET_REQUIRED",409);
            foreach(var c in input.OldClaimRefs)
            {
                var old=await Store.RebindClaim(c.Id,t);
                if(old!=null&&(old.Reference!=c||old.AssignmentId!=op.AssignmentRef.Id||old.State!="ADOPTED"))throw Error("REBIND_CLAIM_IDENTITY_CONFLICT",409);
                if(old==null)await Store.SaveRebindClaim(new(c,Guid.Empty,op.ResourceRef!,guard.UseInterval,"ADOPTED",op.AssignmentRef.Id,"SIMULATED_RES_OWNER"),t);
            }
            foreach(var e in input.OldExecutions)
            {
                var registered=await db.Database.SqlQuery<Guid>($"""SELECT assignment_id AS "Value" FROM cp6_scheduling_v1.rebind_executions WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND work_ref_id={e.WorkRef.Id}""").SingleOrDefaultAsync(t);
                if(registered!=Guid.Empty)throw Error("REBIND_EXECUTION_ALREADY_REGISTERED",409);
                await Store.SaveRebindExecution(op.AssignmentRef.Id,e,t);
            }
            await Store.SaveRebindSource(op.AssignmentRef.Id,input,t);return Ref("SIMULATED-REBIND-SOURCE",op.AssignmentRef.Id,"1",input);
        },ct);
    }
    public Task<RebindContextV1> RebindContextAsync(Guid rootId,Guid operationId,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.Planning,"query",true,async(a,t)=>
    {
        await Lease(a,true,t);
        if(rootId==Guid.Empty)
        {
            rootId=await (from wo in await a.PlanningOrders(t) join row in db.WorkOrderProcesses.AsNoTracking() on wo.WorkOrderNo equals row.WorkOrderNo
                where row.Id==operationId select wo.Id).SingleOrDefaultAsync(t);
            if(rootId==Guid.Empty)throw Error("REBIND_OPERATION_NOT_FOUND",404);
        }
        else await Visible(a,rootId,t);
        var root=await Store.Root(rootId,t);var op=root.Operations.SingleOrDefault(o=>o.OperationId==operationId)??throw Error("REBIND_OPERATION_NOT_FOUND",404);
        if(op.AssignmentRef==null)throw Error("REBIND_OLD_BINDING_REQUIRED",409);
        var source=await Store.RebindSource(op.AssignmentRef.Id,t)??throw Error("REBIND_OWNER_SOURCE_REQUIRED",409);
        var observation=await Store.Observation(rootId,t);var head=await QueueRequired(root.SiteRef.Id,op.ResourceRef!.Id,t);
        var member=head.Members.SingleOrDefault(m=>m.OperationId==operationId&&m.AssignmentRef==op.AssignmentRef)??throw Error("REBIND_EXACT_OLD_QUEUE_REQUIRED",409);
        var now=Now();var id=Guid.NewGuid();var capture=new SchedCaptureV1(id,now,now.AddMinutes(30),"SIMULATED_UPSTREAM",root,observation,OperationSet(root),Policy,true);
        var context=new RebindContextV1(id,a.ActorId,capture,operationId,op.AssignmentRef,source.OldClaimRefs,source.OldExecutions,source.Choices,head,member,
            Ref("SIMULATED-REBIND-SOURCE",op.AssignmentRef.Id,"1",source),"SIMULATED_EXEC_RES",true);
        await Store.Save(id,rootId,"rebind-context",a.ActorId,context,t);return context;
    },ct);
    private Task<SchedPlanV1> PrepareRebindAsync(SchedChangeRequestV1 input,CancellationToken ct)=>Authority.ExecuteAsync(OperationsOwnerV1.Planning,"plan.prepare",true,async(a,t)=>
    {
        await Lease(a,true,t);var d=await Store.Read(input.CaptureId,"rebind-context",t);await Visible(a,d.RootId,t);
        var c=Decode<RebindContextV1>(d.Body);if(c.ActorId!=a.ActorId)throw Error("SCHED_CAPTURE_ACTOR_MISMATCH",403);
        if(c.Capture.ExpiresAt<=Now())throw Error("SCHED_CAPTURE_EXPIRED",409);
        var root=await Store.Root(d.RootId,t);var observation=await Store.Observation(d.RootId,t);
        if(root.Expected!=c.Capture.Root.Expected||OperationSet(root)!=c.Capture.OperationSetRef||observation.Reference!=c.Capture.Observation.Reference)throw Error("SCHED_CAPTURE_STALE",409);
        var op=root.Operations.Single(o=>o.OperationId==c.OperationId);
        if(input.OperationId!=op.OperationId||input.WoExpected!=root.Expected||input.ExpectedScheduleRevision!=op.ScheduleRevision)throw Error("SCHED_CAPTURE_PREDECESSOR_MISMATCH",409);
        var choice=c.Choices.SingleOrDefault(x=>x.ChoiceRef==input.ResourceChoiceRef)??throw Error("REBIND_CHOICE_NOT_IN_CONTEXT");
        if(SameResource(choice.ResourceRef,op.ResourceRef))throw Error("REBIND_RESOURCE_UNCHANGED");
        var guard=observation.Guards.Single(g=>g.OperationId==op.OperationId);
        var changes=new[]{new SchedOperationChangeV1(op.OperationId,op.OccurrenceRef,op.ScheduleRevision,op.Interval,new(input.ProposedStart,input.ProposedEnd),op.AssignmentRef,guard.SourceWindowRef)};
        var group=WorkOrderScheduleOwnerV1.Prepare(c.Capture,changes,input.ProposedPriority,input.Reason);
        var proposed=root with{Operations=root.Operations.Select(o=>o.OperationId==op.OperationId?o with{ResourceRef=choice.ResourceRef}:o).ToArray()};
        var candidate=observation with{Guards=observation.Guards.Select(g=>g.OperationId==op.OperationId?g with{UseInterval=choice.UseInterval,ClaimValid=choice.Qualified}:g).ToArray()};
        var gates=WorkOrderScheduleOwnerV1.Gates(proposed,candidate,changes).ToList();
        if(!choice.Qualified||choice.ExpiresAt<=Now())gates.Add(new("REBIND_RESOURCE_QUALIFICATION_REQUIRED",op.OperationId,"NOT_SATISFIED"));
        var rebind=new RebindPlanV1(c,choice,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"REMOVE_OLD_APPEND_NEW_WAITING",RebindPhases.Select(p=>new RebindPhaseKeyV1(p,Guid.NewGuid())).ToArray());
        var plan=new SchedPlanV1(Guid.NewGuid(),"1",Digest(new{input,group,rebind}),c.Capture.ExpiresAt,input,c.Capture,group,gates.ToArray(),gates.Count==0,rebind);
        await Store.Save(plan.PlanId,d.RootId,"plan",a.ActorId,plan,t);return plan;
    },ct);
    private async Task<SchedPlanV1> RebindPlan(OperationsAccessV1 a,Guid id,CancellationToken ct)
    {
        var d=await Store.Read(id,"plan",ct);await Visible(a,d.RootId,ct);if(d.ActorId!=a.ActorId)throw Error("SCHED_PLAN_ACTOR_MISMATCH",403);
        var p=Decode<SchedPlanV1>(d.Body);if(p.Rebind==null)throw Error("REBIND_PLAN_REQUIRED");return p;
    }
    private async Task RebindWriteAuthority(OperationsAccessV1 a,SchedPlanV1 p,CancellationToken ct)
    {await a.ActionAsync(OperationsOwnerV1.Planning,"change.apply",ct);var wo=await a.ActionAsync(OperationsOwnerV1.Mes,"change.schedule",ct);await wo.RequireWorkOrder(p.Capture.Root.WorkOrderNo,false,ct);}
    private async Task RebindCommandVacant(Guid actor,Guid id,CancellationToken ct)
    {if(await Store.Command(actor,id,ct)!=null||await Store.Batch(actor,id,ct)!=null||await Store.QueueCommand(actor,id,ct)!=null)throw Error("SCHED_COMMAND_CONFLICT",409);}
    public async Task<object> ApplyChangeAsync(SchedApplyV1 input,CancellationToken ct=default)
    {
        var d=await Store.Read(input.PlanId,"plan",ct);return Decode<SchedPlanV1>(d.Body).Rebind==null?await ApplyAsync(input,ct):await ApplyRebindAsync(input,ct);
    }
    public Task<RebindReplyV1> ApplyRebindAsync(SchedApplyV1 input,CancellationToken ct=default)
    {
        if(input==null||input.CommandId==Guid.Empty||input.PlanId==Guid.Empty)throw Error("SCHED_APPLY_INPUT_INVALID");Reason(input.Reason);Version(input.ExpectedPlanRevision);
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"change.apply",true,async(a,t)=>
        {
            await Lease(a,true,t);await RebindCommandVacant(a.ActorId,input.CommandId,t);var p=await RebindPlan(a,input.PlanId,t);var prior=await Store.RebindCommand(a.ActorId,input.CommandId,t);
            if(prior!=null){if(prior.InputDigest!=Digest(input))throw Error("SCHED_COMMAND_CONFLICT",409);return Decode<RebindReplyV1>(prior.Body) with{Replayed=true};}
            if(p.Revision!=input.ExpectedPlanRevision||p.Digest!=input.ExpectedPlanDigest||p.Input.Reason!=input.Reason)throw Error("SCHED_PLAN_MISMATCH",409);
            var id=await db.Database.SqlQuery<Guid>($"""SELECT work_id AS "Value" FROM cp6_scheduling_v1.rebind_works WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND plan_id={p.PlanId}""").SingleOrDefaultAsync(t);
            RebindWorkV1 work;
            if(id!=Guid.Empty)work=await Store.RebindWork(id,t);
            else
            {
                if(!simulation.Value.Enabled)throw Error("REBIND_OWNER_TRANSPORT_UNAVAILABLE",503);
                if(p.ExpiresAt<=Now())throw Error("SCHED_PLAN_EXPIRED",409);if(!p.Eligible)throw Error("SCHED_PLAN_INELIGIBLE",422);await RebindWriteAuthority(a,p,t);
                work=new(p.RootApplyGroup.GroupId,a.ActorId,p.PlanId,p.Capture.Root.RootId,p.Input.OperationId,"1","PREPARED",p.Capture.Root.Expected,[],[],null,false,false,null,"NOT_REGISTERED");
                await Store.SaveRebindWork(work,t);
            }
            var reply=new RebindReplyV1(input.CommandId,"WORK_ACCEPTED",work,id!=Guid.Empty);await Store.SaveRebindCommand(a.ActorId,input.CommandId,input,reply,t);return reply;
        },ct,actionAtFence:async t=>await Store.RebindCommand(await owner.CurrentSubjectAsync(t),input.CommandId,t)!=null?"command.read":"change.apply");
    }
    public Task<RebindWorkV1> RebindWorkAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.Planning,"work.read",false,async(a,t)=>
    {await Lease(a,false,t);var w=await Store.RebindWork(id,t);await RebindPlan(a,w.PlanId,t);return w;},ct);
    private static string? NextRebindPhase(RebindWorkV1 w)
    {
        bool Has(string p)=>w.Receipts.Any(r=>r.Phase==p);
        if(w.BindingResult!=null)return Has("RELEASE_OLD")?null:"RELEASE_OLD";
        if(w.Stopped)return Has("CLOSE_UNUSED")?null:"CLOSE_UNUSED";
        return RebindPhases.Take(4).FirstOrDefault(p=>!Has(p));
    }
    public async Task<RebindReplyV1> ContinueRebindAsync(Guid id,RebindActionV1 input,CancellationToken ct=default)
    {
        ValidateRebindAction(input);var dispatch=false;
        var reply=await Authority.ExecuteAsync(OperationsOwnerV1.Planning,"change.apply",true,async(a,t)=>
        {
            await Lease(a,true,t);var w=await Store.RebindWork(id,t);var p=await RebindPlan(a,w.PlanId,t);await RebindCommandVacant(a.ActorId,input.CommandId,t);
            var old=await Store.RebindCommand(a.ActorId,input.CommandId,t);var payload=new{id,action="CONTINUE",input};
            if(old!=null){if(old.InputDigest!=Digest(payload))throw Error("SCHED_COMMAND_CONFLICT",409);return Decode<RebindReplyV1>(old.Body) with{Replayed=true};}
            if(w.Revision!=input.ExpectedWorkRevision)throw Error("REBIND_WORK_CHANGED",409);
            await RebindWriteAuthority(a,p,t);
            if(!simulation.Value.Enabled&&w.PendingPhase==null&&NextRebindPhase(w)!=null)throw Error("REBIND_OWNER_TRANSPORT_UNAVAILABLE",503);
            if(w.PendingPhase!=null)w=await ObserveRebind(w,t);
            else
            {
                var phase=NextRebindPhase(w);
                if(phase!=null&&w.State is not("PAUSED_STALE" or "PAUSED_REJECTED"))
                {
                    var expected=new RebindStageExpectedV1(p.RootApplyGroup.OriginalWoExpected,w.Accepted,w.Receipts.LastOrDefault()?.ReceiptRef,w.OwnTransitions,w.Revision);
                    var key=p.Rebind!.PhaseKeys.Single(x=>x.Phase==phase).Key;
                    var request=new RebindPhaseRequestV1(w.WorkId,phase,key,Digest(new{w.WorkId,phase,key,expected,plan=p}),expected,p);
                    await Store.SaveRebindRequest(request,t);w=w with{Revision=Next(w.Revision),PendingPhase=phase,State=phase+"_UNKNOWN"};await Store.SaveRebindWork(w,t);dispatch=true;
                }
            }
            var accepted=new RebindReplyV1(input.CommandId,w.PendingPhase==null?w.State:"ORIGINAL_PHASE_REGISTERED",w,false);
            await Store.SaveRebindCommand(a.ActorId,input.CommandId,payload,accepted,t);return accepted;
        },ct,actionAtFence:async t=>await Store.RebindCommand(await owner.CurrentSubjectAsync(t),input.CommandId,t)!=null?"command.read":"change.apply");
        if(dispatch)
        {
            // The inbox is committed before calling the explicitly simulated Owner transport.
            var p=Decode<SchedPlanV1>((await Store.Read(reply.Work.PlanId,"plan",ct)).Body);
            var source=await Store.RebindSource(p.Rebind!.Context.OldBindingRef.Id,ct);
            if(!source!.DeferredPhases.Contains(reply.Work.PendingPhase!))
            {await DeliverRebindOwner(id,false,ct);await LookupRebind(id,"change.apply",ct);}
        }
        return reply; // Immutable command receipt; Q17 supplies the independently advancing work.
    }
    private static void ValidateRebindAction(RebindActionV1 i)
    {if(i==null||i.CommandId==Guid.Empty)throw Error("REBIND_ACTION_INVALID");Version(i.ExpectedWorkRevision);Reason(i.Reason);}
    public Task<RebindReplyV1> StopRebindAsync(Guid id,RebindActionV1 input,CancellationToken ct=default)
    {
        ValidateRebindAction(input);
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"change.apply",true,async(a,t)=>
        {
            await Lease(a,true,t);var w=await Store.RebindWork(id,t);var p=await RebindPlan(a,w.PlanId,t);await RebindCommandVacant(a.ActorId,input.CommandId,t);
            var payload=new{id,action="STOP",input};var prior=await Store.RebindCommand(a.ActorId,input.CommandId,t);
            if(prior!=null){if(prior.InputDigest!=Digest(payload))throw Error("SCHED_COMMAND_CONFLICT",409);return Decode<RebindReplyV1>(prior.Body) with{Replayed=true};}
            if(w.Revision!=input.ExpectedWorkRevision)throw Error("REBIND_WORK_CHANGED",409);await RebindWriteAuthority(a,p,t);
            w=await StopRebindCore(w,t);var reply=new RebindReplyV1(input.CommandId,w.State,w,false);await Store.SaveRebindCommand(a.ActorId,input.CommandId,payload,reply,t);return reply;
        },ct,actionAtFence:async t=>await Store.RebindCommand(await owner.CurrentSubjectAsync(t),input.CommandId,t)!=null?"command.read":"change.apply");
    }
    private async Task<RebindWorkV1> StopRebindCore(RebindWorkV1 w,CancellationToken t)
    {
        if(w.PendingPhase!=null)w=await ObserveRebind(w,t);
        if(w.State=="STOPPED_NO_BINDING_CHANGE")return w;
        // Same scheduling/WO deciding lock as COMMIT_BINDING. A committed binding always wins.
        if(w.BindingResult!=null)return w;
        await Store.SetRebindFence(w,true,t);
        w=w with{Stopped=true,NoNewExecution=true,Revision=Next(w.Revision),State="STOPPED_CLEANUP_PENDING"};await Store.SaveRebindWork(w,t);return w;
    }
    private async Task<RebindWorkV1> ObserveRebind(RebindWorkV1 w,CancellationToken ct)
    {
        if(w.PendingPhase==null)return w;var receipt=await Store.RebindReceipt(w.WorkId,w.PendingPhase,ct);if(receipt==null)return w;
        var next=w with{Revision=Next(w.Revision),PendingPhase=null,Accepted=receipt.Accepted,Receipts=w.Receipts.Append(receipt).ToArray(),
            OwnTransitions=receipt.OwnTransition==null?w.OwnTransitions:w.OwnTransitions.Append(receipt.OwnTransition).ToArray(),
            NoNewExecution=w.NoNewExecution||receipt.Phase=="BEGIN",BindingResult=receipt.BindingResult??w.BindingResult,State=receipt.Outcome};
        if(receipt.BindingResult!=null)next=next with{QueueMigrationState="PENDING"};
        if(next.Receipts.Any(r=>r.Phase=="RELEASE_OLD")&&next.QueueMigrationState=="COMPLETED")next=next with{State="COMPLETED"};
        await Store.SaveRebindWork(next,ct);return next;
    }
    public Task<RebindWorkV1> ReconcileRebindAsync(Guid id,CancellationToken ct=default)=>LookupRebind(id,"work.reconcile",ct);
    private Task<RebindWorkV1> LookupRebind(Guid id,string action,CancellationToken ct)=>Authority.ExecuteAsync(OperationsOwnerV1.Planning,action,true,async(a,t)=>
    {await Lease(a,true,t);var w=await Store.RebindWork(id,t);await RebindPlan(a,w.PlanId,t);return await ObserveRebind(w,t);},ct);
    public Task<RebindPhaseReceiptV1> DeliverRebindSimulationAsync(Guid id,CancellationToken ct=default)
    {if(!simulation.Value.Enabled)throw Error("SCHED_SIMULATION_DISABLED",404);return DeliverRebindOwner(id,true,ct);}
}
