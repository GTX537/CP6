using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;
public sealed partial class SchedulingOwnerV1
{
    private async Task<(ArrangementBatchV1 Batch,ArrangementPreviewV1 Preview)> BatchContext(OperationsAccessV1 a,Guid id,CancellationToken ct)
    {
        var batch=await Store.BatchById(id,ct);if(batch.ActorId!=a.ActorId)throw Error("SCHED_NOT_VISIBLE",404);
        var preview=Decode<ArrangementPreviewV1>((await Store.Arrangement(batch.Input.PreviewId,"preview",ct)).Body);return(batch,preview);
    }
    private async Task<ArrangementBatchResultV1> BatchView(OperationsAccessV1 a,ArrangementBatchV1 batch,ArrangementPreviewV1 preview,bool replayed,CancellationToken ct)
    {
        var groups=new List<ArrangementGroupResultV1>();
        foreach(var id in batch.Input.ExplicitAtomicGroupIds)
        {
            var group=preview.AtomicGroups.Single(g=>g.Group.GroupId==id);await Visible(a,group.Group.RootWoId,ct);
            var row=(await Store.GroupWork(id,ct))!;
            groups.Add(new(id,row.RootId,row.State=="SEND_INTENT"?"UNKNOWN":row.State,row.ErrorCode,row.Body==null?null:Decode<RootScheduleGroupResultV1>(row.Body)));
        }
        var committed=groups.Count(g=>g.State=="COMMITTED");var rejected=groups.Count(g=>g.State=="REJECTED");
        var unknown=groups.Count(g=>g.State=="UNKNOWN");var waiting=groups.Count(g=>g.State=="WAITING");var cancelled=groups.Count(g=>g.State=="CANCELLED");
        return new(batch.BatchId,batch.Input.CommandId,unknown+waiting>0?"PROCESSING":committed==groups.Count?"COMMITTED":"REJECTED",replayed,groups.Count,committed,rejected,unknown,waiting,cancelled,groups.ToArray());
    }
    public Task<ArrangementBatchResultV1> ArrangementBatchAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.Planning,"work.read",false,async(a,t)=>
    {await Lease(a,false,t);var (batch,preview)=await BatchContext(a,id,t);return await BatchView(a,batch,preview,false,t);},ct);
    public async Task<ArrangementBatchResultV1> ApplyArrangementAsync(ArrangementApplyV1 input,CancellationToken ct=default)
    {
        if(input==null||input.CommandId==Guid.Empty||input.PreviewId==Guid.Empty||input.ExplicitAtomicGroupIds is not{Length:>0 and <=500}
            ||input.ExplicitAtomicGroupIds.Distinct().Count()!=input.ExplicitAtomicGroupIds.Length||string.IsNullOrWhiteSpace(input.ExpectedPreviewDigest)||string.IsNullOrWhiteSpace(input.ExpectedManifestDigest))throw Error("SCHED_BATCH_INPUT_INVALID");
        Reason(input.Reason);Version(input.ExpectedPreviewRevision);
        var registered=await Authority.ExecuteAsync(OperationsOwnerV1.Planning,"arrange.apply",true,async(a,t)=>
        {
            await Lease(a,true,t);var old=await Store.Batch(a.ActorId,input.CommandId,t);
            if(await Store.Command(a.ActorId,input.CommandId,t)!=null)throw Error("SCHED_COMMAND_CONFLICT",409);
            if(old!=null){if(old.InputDigest!=Digest(input))throw Error("SCHED_COMMAND_CONFLICT",409);return(old,true);}
            var d=await Store.Arrangement(input.PreviewId,"preview",t);if(d.ActorId!=a.ActorId)throw Error("SCHED_PREVIEW_ACTOR_MISMATCH",403);
            var preview=Decode<ArrangementPreviewV1>(d.Body);
            if(preview.Revision!=input.ExpectedPreviewRevision||preview.Digest!=input.ExpectedPreviewDigest||preview.ManifestDigest!=input.ExpectedManifestDigest||preview.Input.Reason!=input.Reason)
                throw Error("SCHED_PREVIEW_MISMATCH",409);
            if(preview.ExpiresAt<=DateTimeOffset.UtcNow)throw Error("SCHED_PREVIEW_EXPIRED",409);
            foreach(var id in input.ExplicitAtomicGroupIds)
            {
                var group=preview.AtomicGroups.SingleOrDefault(g=>g.Group.GroupId==id)??throw Error("SCHED_GROUP_NOT_IN_PREVIEW");
                await Visible(a,group.Group.RootWoId,t);if(!group.Eligible)throw Error("SCHED_GROUP_INELIGIBLE",422);
            }
            var batch=new ArrangementBatchV1(Guid.NewGuid(),a.ActorId,input,Digest(input),Now());await Store.SaveBatch(batch,t);
            foreach(var group in preview.AtomicGroups.Where(g=>input.ExplicitAtomicGroupIds.Contains(g.Group.GroupId)))await Store.RegisterGroup(preview.PreviewId,group,t);
            return(batch,false);
        },ct,actionAtFence:async t=>await Store.Batch(await owner.CurrentSubjectAsync(t),input.CommandId,t)!=null?"command.read":"arrange.apply");
        if(!registered.Item2)await DriveArrangement(registered.Item1.BatchId,ct);
        return await Authority.ExecuteAsync(OperationsOwnerV1.Planning,registered.Item2?"command.read":"arrange.apply",false,async(a,t)=>
        {await Lease(a,false,t);var (batch,preview)=await BatchContext(a,registered.Item1.BatchId,t);return await BatchView(a,batch,preview,registered.Item2,t);},ct);
    }
    public async Task<ArrangementBatchResultV1> ContinueArrangementAsync(Guid batchId,CancellationToken ct=default)
    {
        await DriveArrangement(batchId,ct);
        return await Authority.ExecuteAsync(OperationsOwnerV1.Planning,"arrange.apply",false,async(a,t)=>
        {await Lease(a,false,t);var (batch,preview)=await BatchContext(a,batchId,t);return await BatchView(a,batch,preview,true,t);},ct);
    }
    private async Task DriveArrangement(Guid batchId,CancellationToken ct)
    {
        var order=await Authority.ExecuteAsync(OperationsOwnerV1.Planning,"arrange.apply",false,async(a,t)=>
        {await Lease(a,false,t);var (batch,preview)=await BatchContext(a,batchId,t);return preview.AtomicGroups.Where(g=>batch.Input.ExplicitAtomicGroupIds.Contains(g.Group.GroupId)).Select(g=>g.Group.GroupId).ToArray();},ct);
        var attempted=new HashSet<Guid>();bool progress;
        do
        {
            progress=false;
            foreach(var id in order.Where(id=>!attempted.Contains(id)))
            {
                var send=await Authority.ExecuteAsync(OperationsOwnerV1.Planning,"arrange.apply",true,async(a,t)=>
                {
                    await Lease(a,true,t);var (_,preview)=await BatchContext(a,batchId,t);var group=preview.AtomicGroups.Single(g=>g.Group.GroupId==id);
                    var row=(await Store.GroupWork(id,t))!;if(row.State is "COMMITTED" or "REJECTED" or "CANCELLED")return false;
                    foreach(var predecessor in group.PredecessorGroupIds)if((await Store.GroupWork(predecessor,t))?.State!="COMMITTED")return false;
                    if(row.State=="WAITING")await Store.GroupState(id,"SEND_INTENT",null,null,t);
                    return true;
                },ct);
                if(!send)continue;attempted.Add(id);progress=true;
                try
                {
                    await Authority.ExecuteAsync(OperationsOwnerV1.Planning,"arrange.apply",true,async(a,t)=>
                    {
                        await Lease(a,true,t);var (_,preview)=await BatchContext(a,batchId,t);var group=preview.AtomicGroups.Single(g=>g.Group.GroupId==id);
                        var row=(await Store.GroupWork(id,t))!;if(row.State is "COMMITTED" or "REJECTED" or "CANCELLED")return true;
                        await Visible(a,group.Group.RootWoId,t);
                        var previous=await Store.Applied(id,t);
                        if(previous!=null){await Store.GroupState(id,"COMMITTED",null,Decode<RootScheduleGroupResultV1>(previous.Body),t);return true;}
                        if(preview.ExpiresAt<=DateTimeOffset.UtcNow)throw Error("SCHED_PREVIEW_EXPIRED",409);
                        if((await Store.Planning(group.Group.RootWoId,t))?.Reference!=group.PlanningInputRef)throw Error("SCHED_PLANNING_CUT_CHANGED",409);
                        var wo=await a.ActionAsync(OperationsOwnerV1.Mes,"change.schedule",t);await wo.RequireWorkOrder(group.Capture.Root.WorkOrderNo,false,t);
                        var result=await new WorkOrderScheduleOwnerV1(db,Store).ApplyRootScheduleGroup(group.Group,group.Capture,id,t);
                        await Store.SaveApplication(id,group.Group.RootWoId,a.ActorId,result,preview.Input.Reason,t);
                        await Store.GroupState(id,"COMMITTED",null,result,t);return true;
                    },ct);
                }
                catch(OrderPriceCorrectionException e)when(e.Status is 400 or 403 or 404 or 409 or 412 or 422)
                {
                    // These errors certify that this attempt rolled back. Keep any earlier committed original result.
                    await Authority.ExecuteAsync(OperationsOwnerV1.Planning,"arrange.apply",true,async(a,t)=>
                    {
                        await Lease(a,true,t);await BatchContext(a,batchId,t);var row=(await Store.GroupWork(id,t))!;
                        if(row.State is "COMMITTED" or "REJECTED" or "CANCELLED")return true;
                        var application=await Store.Applied(id,t);
                        await Store.GroupState(id,application==null?"REJECTED":"COMMITTED",application==null?e.Message:null,application==null?null:Decode<RootScheduleGroupResultV1>(application.Body),t);return true;
                    },ct);
                }
                catch(OrderPriceCorrectionException e)when(e.Status==503)
                {
                    // SEND_INTENT stays durable/UNKNOWN. Independent roots can continue; this request never blindly retries this group.
                }
            }
        }while(progress&&attempted.Count<order.Length);
    }
    public Task<ArrangementBatchResultV1> StopArrangementAsync(Guid batchId,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.Planning,"arrange.apply",true,async(a,t)=>
    {
        await Lease(a,true,t);var (batch,preview)=await BatchContext(a,batchId,t);
        foreach(var id in batch.Input.ExplicitAtomicGroupIds)
        {
            var row=(await Store.GroupWork(id,t))!;
            if(row.State=="WAITING")await Store.GroupState(id,"CANCELLED","UNSENT_CANCELLED",null,t);
        }
        return await BatchView(a,batch,preview,false,t);
    },ct);
}
