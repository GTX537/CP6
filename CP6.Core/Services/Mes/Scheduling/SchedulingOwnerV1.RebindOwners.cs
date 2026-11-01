using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;
public sealed partial class SchedulingOwnerV1
{
    private Task<RebindPhaseReceiptV1> DeliverRebindOwner(Guid id,bool simulatedService,CancellationToken ct)=>Authority.ExecuteAsync(
        OperationsOwnerV1.Planning,simulatedService?"simulate-source":"change.apply",true,async(a,t)=>
    {
        await Lease(a,true,t);if(simulatedService)a.RequireSchedulingSimulation();var w=await Store.RebindWork(id,t);
        var p=await RebindPlan(a,w.PlanId,t);if(w.PendingPhase==null)throw Error("REBIND_NO_REGISTERED_PHASE",409);
        var request=await Store.RebindRequest(id,w.PendingPhase,t);var prior=await Store.RebindReceipt(id,w.PendingPhase,t);if(prior!=null)return prior;
        var phase=request.Phase;var stopped=await Store.RebindStopped(id,t);
        // Conservation service can finish registered cleanup after authority revocation. It cannot begin/reserve/commit anew.
        if(phase is "BEGIN" or "RESERVE_NEW" or "COMMIT_BINDING"&&!stopped)await RebindWriteAuthority(a,p,t);
        var root=await Store.Root(w.RootId,t);var op=root.Operations.Single(o=>o.OperationId==w.OperationId);var r=p.Rebind!;
        var accepted=request.Expected.Accepted;SchedRefV1? transition=null;SchedRefV1? claimRef=null;RootScheduleGroupResultV1? binding=null;string outcome;string? failure=null;
        var tx=db.Database.CurrentTransaction!;await tx.CreateSavepointAsync("rebind_owner_effect",t);
        try
        {
        if(stopped&&phase is "BEGIN" or "COMMIT_BINDING")outcome="STOPPED_CLEANUP_PENDING";
        else if(!stopped&&phase is "BEGIN" or "CLOSE_OLD" or "RESERVE_NEW" or "COMMIT_BINDING"&&root.Expected!=accepted)outcome="PAUSED_STALE";
        else switch(phase)
        {
            case "BEGIN":
                if(op.AssignmentRef!=r.Context.OldBindingRef||OperationSet(root)!=p.Capture.OperationSetRef
                    ||!root.QueueHeads.SequenceEqual(p.RootApplyGroup.ExpectedQueueHeads))throw Error("REBIND_OLD_BINDING_OR_SCOPE_CHANGED",409);
                foreach(var c in r.Context.OldClaims)
                {var current=await Store.RebindClaim(c.Id,t);if(current?.Reference!=c||current.AssignmentId!=op.AssignmentRef.Id||current.State!="ADOPTED")throw Error("REBIND_OLD_CLAIM_CHANGED",409);}
                await Store.SetRebindFence(w,false,t);
                accepted=root.Expected with{ControlEpoch=Next(root.Expected.ControlEpoch)};
                transition=Ref("WO-REBIND-TRANSITION",request.Key,"1",new{before=root.Expected,after=accepted,request.PayloadDigest});
                await Store.SetRoot(root with{Expected=accepted},t);outcome="OLD_FENCED";break;
            case "CLOSE_OLD":
                var observed=(await Store.Observation(root.RootId,t)).Guards.Single(g=>g.OperationId==op.OperationId);
                if(observed.ExecutionState!="NOT_STARTED"||observed.ActualInterval!=null)throw Error("REBIND_OLD_EXECUTION_UNRESOLVED",409);
                foreach(var old in r.Context.OldExecutions)
                {
                    var current=await Store.RebindExecution(old.WorkRef.Id,t);
                    if(current.State is not("REGISTERED" or "CLOSED_NO_EXECUTION"))throw Error("REBIND_OLD_EXECUTION_UNRESOLVED",409);
                    await Store.SaveRebindExecution(r.Context.OldBindingRef.Id,current with{State="CLOSED_NO_EXECUTION"},t);
                }
                outcome="OLD_CLOSED";break;
            case "RESERVE_NEW":
                // Only a previously registered original request may arrive after Stop; its claim stays non-executable.
                if((await Store.RebindReceipt(id,"CLOSE_OLD",t))?.Outcome!="OLD_CLOSED")throw Error("REBIND_CLOSURE_PROOF_REQUIRED",409);
                foreach(var e in r.Context.OldExecutions)if((await Store.RebindExecution(e.WorkRef.Id,t)).State!="CLOSED_NO_EXECUTION")throw Error("REBIND_OLD_EXECUTION_UNRESOLVED",409);
                if(!r.Choice.Qualified||r.Choice.ExpiresAt<=Now()||!Contains(r.Choice.UseInterval,p.RootApplyGroup.ChangedOperations.Single().NewInterval))throw Error("REBIND_RESOURCE_QUALIFICATION_REQUIRED",422);
                var use=p.RootApplyGroup.ChangedOperations.Single().NewInterval;
                var claims=(await db.Database.SqlQuery<byte[]>($"""SELECT body AS "Value" FROM cp6_scheduling_v1.rebind_claims WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId}""").ToArrayAsync(t)).Select(Decode<RebindClaimV1>);
                if(claims.Any(c=>c.WorkId!=id&&c.State is "PREPARED_NO_EXECUTION" or "ADOPTED"&&SameResource(c.ResourceRef,r.Choice.ResourceRef)&&Overlap(c.UseInterval,use)))throw Error("REBIND_RESOURCE_ALREADY_RESERVED",409);
                claimRef=Ref("RES-CLAIM",r.NewClaimId,"1",new{request.Key,r.Choice,w.RootId,w.OperationId,r.NewBindingId});
                await Store.SaveRebindClaim(new(claimRef,id,r.Choice.ResourceRef,use,"PREPARED_NO_EXECUTION",null,"SIMULATED_RES_OWNER"),t);
                outcome=stopped?"STOPPED_CLEANUP_PENDING":"READY_TO_COMMIT";break;
            case "COMMIT_BINDING":
                var prepared=await Store.RebindClaim(r.NewClaimId,t)??throw Error("REBIND_CLAIM_REQUIRED",409);
                if(prepared.State!="PREPARED_NO_EXECUTION"||prepared.WorkId!=id||r.Choice.ExpiresAt<=Now())throw Error("REBIND_CLAIM_NOT_ADOPTABLE",409);
                if(op.AssignmentRef!=r.Context.OldBindingRef)throw Error("REBIND_OLD_BINDING_CHANGED",409);
                var assignment=Ref("WO-BINDING",r.NewBindingId,"1",new{request.Key,r.Choice.ResourceRef,claim=prepared.Reference,op.OccurrenceRef});
                binding=await new WorkOrderScheduleOwnerV1(db,Store).ApplyRebind(p,request,assignment,prepared.Reference,t);
                accepted=binding.NewWoExpected;transition=binding.OwnTransitionRef;claimRef=prepared.Reference;
                await Store.SaveRebindClaim(prepared with{State="ADOPTED",AssignmentId=assignment.Id},t);
                await Store.SaveApplication(p,a.ActorId,binding,t);
                await Store.SaveRebindMigration(new(id,request.Key,w.RootId,w.OperationId,root.SiteRef.Id,r.Context.OldQueue.ResourceRef,r.Context.OldMember,
                    r.Choice.ResourceRef,assignment,r.NewQueueItemId,"PENDING",[]),t);
                outcome="ASSIGNMENT_COMMITTED_CLEANUP_PENDING";break;
            case "RELEASE_OLD":
                if((await Store.RebindReceipt(id,"COMMIT_BINDING",t))?.BindingResult==null)throw Error("REBIND_BINDING_RECEIPT_REQUIRED",409);
                foreach(var c in r.Context.OldClaims)
                {var old=(await Store.RebindClaim(c.Id,t))!;await Store.SaveRebindClaim(old with{State="RELEASED",Responsibility="RES_OWNER: ORIGINAL_ASSIGNMENT_CLOSED"},t);}
                outcome="ASSIGNMENT_COMMITTED_QUEUE_PENDING";break;
            case "CLOSE_UNUSED":
                if(!stopped||(await Store.RebindReceipt(id,"COMMIT_BINDING",t))?.BindingResult!=null)throw Error("REBIND_PERMANENT_NON_ADOPTION_PROOF_REQUIRED",409);
                var unused=await Store.RebindClaim(r.NewClaimId,t);
                if(unused!=null)
                {
                    if(unused.State=="ADOPTED"||unused.AssignmentId!=null)throw Error("REBIND_ADOPTED_CLAIM_CANNOT_CLOSE",409);
                    await Store.SaveRebindClaim(unused with{State="CLOSED_UNUSED",Responsibility="RES_OWNER: PERMANENT_WO_STOP_AND_NO_ADOPTION"},t);claimRef=unused.Reference;
                }
                // No outstanding RESERVE inbox can be interpreted as no effect.
                var reserveRegistered=await db.Database.SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM cp6_scheduling_v1.rebind_requests WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND work_id={id} AND phase='RESERVE_NEW'""").SingleAsync(t);
                if(reserveRegistered!=0&&await Store.RebindReceipt(id,"RESERVE_NEW",t)==null)throw Error("REBIND_RESERVE_OUTCOME_UNKNOWN",409);
                outcome="STOPPED_NO_BINDING_CHANGE";break;
            default:throw Error("REBIND_PHASE_UNSUPPORTED",409);
        }
        }
        catch(CP6.Core.Services.Erp.OrderPriceCorrectionException e) when(e.Status is 409 or 422 && e.Message is not("REBIND_OLD_EXECUTION_UNRESOLVED" or "REBIND_RESERVE_OUTCOME_UNKNOWN"))
        {
            // A definite Owner rejection is an original-key result; unknown transport/database failures remain unknown.
            await tx.RollbackToSavepointAsync("rebind_owner_effect",t);accepted=request.Expected.Accepted;transition=null;claimRef=null;binding=null;
            outcome="PAUSED_REJECTED";failure=e.Message;
        }
        var receipt=new RebindPhaseReceiptV1(phase,request.Key,Ref("REBIND-OWNER-RECEIPT",request.Key,"1",new{request.PayloadDigest,accepted,outcome,claimRef,binding,failure}),accepted,transition,outcome,claimRef,binding,failure);
        await Store.SaveRebindReceipt(id,receipt,t);return receipt;
    },ct);
    public Task<RebindExecutionV1> ResolveRebindExecutionSimulationAsync(Guid id,RebindExecutionV1 input,CancellationToken ct=default)
    {
        if(!simulation.Value.Enabled)throw Error("SCHED_SIMULATION_DISABLED",404);Reference(input.WorkRef);
        if(input.State is not("CLOSED_NO_EXECUTION" or "MAY_EXECUTE" or "ACTUAL_CONFLICT"))throw Error("REBIND_EXECUTION_STATE_INVALID");
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"simulate-source",true,async(a,t)=>
        {
            await Lease(a,true,t);a.RequireSchedulingSimulation();var w=await Store.RebindWork(id,t);var p=await RebindPlan(a,w.PlanId,t);
            if(!p.Rebind!.Context.OldExecutions.Any(e=>e.WorkRef==input.WorkRef))throw Error("REBIND_EXECUTION_NOT_REGISTERED",409);
            var guard=(await Store.Observation(w.RootId,t)).Guards.Single(g=>g.OperationId==w.OperationId);
            if(input.State=="CLOSED_NO_EXECUTION"&&(guard.ExecutionState!="NOT_STARTED"||guard.ActualInterval!=null))throw Error("REBIND_EXECUTION_ACTUAL_CONFLICT",409);
            var old=await Store.RebindExecution(input.WorkRef.Id,t);
            if(old.State=="CLOSED_NO_EXECUTION"&&input.State!="CLOSED_NO_EXECUTION")throw Error("REBIND_EXECUTION_CLOSURE_FINAL",409);
            await Store.SaveRebindExecution(p.Rebind.Context.OldBindingRef.Id,input,t);return input;
        },ct);
    }
    public Task<RebindWorkV1> ConserveRebindSimulationAsync(Guid id,CancellationToken ct=default)
    {
        if(!simulation.Value.Enabled)throw Error("SCHED_SIMULATION_DISABLED",404);
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"simulate-source",true,async(a,t)=>
        {
            await Lease(a,true,t);a.RequireSchedulingSimulation();var w=await Store.RebindWork(id,t);var p=await RebindPlan(a,w.PlanId,t);
            w=await StopRebindCore(w,t);
            if(w.PendingPhase==null)
            {
                var phase=NextRebindPhase(w);
                if(phase is "CLOSE_UNUSED" or "RELEASE_OLD")
                {
                    var expected=new RebindStageExpectedV1(p.RootApplyGroup.OriginalWoExpected,w.Accepted,w.Receipts.LastOrDefault()?.ReceiptRef,w.OwnTransitions,w.Revision);
                    var key=p.Rebind!.PhaseKeys.Single(k=>k.Phase==phase).Key;
                    await Store.SaveRebindRequest(new(w.WorkId,phase,key,Digest(new{w.WorkId,phase,key,expected,plan=p}),expected,p),t);
                    w=w with{Revision=Next(w.Revision),PendingPhase=phase};await Store.SaveRebindWork(w,t);
                }
            }
            return w; // Registered scope only. Never new BEGIN, RESERVE or COMMIT.
        },ct);
    }
    public Task<RebindMigrationV1> MigrateRebindQueueAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.Planning,"work.reconcile",true,async(a,t)=>
    {
        await Lease(a,true,t);var w=await Store.RebindWork(id,t);var p=await RebindPlan(a,w.PlanId,t);
        var migration=await Store.RebindMigration(id,t)??throw Error("REBIND_MIGRATION_NOT_REGISTERED",409);if(migration.State=="COMPLETED")return migration;
        // Both current heads are read under the same scheduling lock. Only exact membership is edited; concurrent unrelated order survives.
        var old=await QueueRequired(migration.SiteId,migration.OldResource.Id,t);
        if(!old.Members.Contains(migration.OldMember))throw Error("REBIND_OLD_MEMBERSHIP_NOT_PROVEN",409);
        var target=await Store.Queue(migration.SiteId,migration.NewResource.Id,t)??new QueueHeadV1(Guid.NewGuid(),migration.SiteId,migration.NewResource,"1",[]);
        if(target.Members.Any(x=>x.OperationId==migration.OperationId))throw Error("REBIND_TARGET_MEMBERSHIP_CONFLICT",409);
        var newMember=migration.OldMember with{ItemId=migration.NewItemId,AssignmentRef=migration.NewAssignment};
        var nextOld=old with{Revision=Next(old.Revision),Members=old.Members.Where(m=>m!=migration.OldMember).ToArray()};
        var targetExists=await Store.Queue(migration.SiteId,migration.NewResource.Id,t)!=null;
        var nextTarget=target with{Revision=targetExists?Next(target.Revision):"1",Members=target.Members.Append(newMember).ToArray()};
        foreach(var head in new[]{nextOld,nextTarget}.OrderBy(h=>h.QueueId))await Store.SaveQueue(head,new{kind="REBIND_EXACT_ASSIGNMENT_MIGRATION",migration.RebindKey,migration.NewAssignment,at=Now()},t);
        migration=migration with{State="COMPLETED",QueueReceipts=new[]{Ref("SCHED-QUEUE",nextOld.QueueId,nextOld.Revision,nextOld),Ref("SCHED-QUEUE",nextTarget.QueueId,nextTarget.Revision,nextTarget)}};
        await Store.SaveRebindMigration(migration,t);w=await ObserveRebind(w,t);
        await Store.SaveRebindWork(w with{Revision=Next(w.Revision),QueueMigrationState="COMPLETED",State=w.Receipts.Any(r=>r.Phase=="RELEASE_OLD")?"COMPLETED":"ASSIGNMENT_COMMITTED_CLEANUP_PENDING"},t);return migration;
    },ct);
}
