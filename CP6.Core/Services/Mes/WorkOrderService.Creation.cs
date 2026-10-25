using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;

namespace CP6.Core.Services.Mes;

public partial class WorkOrderService : IWorkOrderCreationOperationsV1
{
    private const string CreationOwner="Main.Mes.WorkOrderCreation",CreationContract="Main.Mes.WorkOrderCreation.v1";
    public Task<WorkOrderCreationContextV1> CreationContextAsync(CancellationToken ct=default)
        =>Owner.ExecuteAsync(OperationsOwnerV1.Mes,"add",false,(a,t)=>a.WorkOrderCreationContext(t),ct,readOnlyAuthorityFence:true);
    public Task<WorkOrderCreationScopeV1> CreationScopeAsync(CancellationToken ct=default)
        =>Owner.ExecuteAsync(OperationsOwnerV1.Mes,"query",false,
            (a,_)=>Task.FromResult(new WorkOrderCreationScopeV1(_db.CurrentTenantId,a.ActorId)),ct,readOnlyAuthorityFence:true);
    public Task<WorkOrderCreationResultV1> CreateCommandAsync(WorkOrderCreationCommandV1 request,CancellationToken ct=default)
    {
        if(request==null || request.OperationId==Guid.Empty || request.WorkOrder==null)
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED",400);
        var frozen=OperationsOwnerV1.Freeze(request);var input=MainOwnerOperationJournalV1.Encode(frozen);
        var journal=new MainOwnerOperationJournalV1(_db);MainOwnerOperationJournalV1.Original? original=null;
        return Owner.ExecuteAsync(OperationsOwnerV1.Mes,"add",true,async(a,t)=>
        {
            if(original!=null)return await DiscloseWorkOrderCreation(a,original,frozen.OperationId,input,t);
            var ticket=await a.BeginWorkOrderCreation(frozen.ExpectedAuthorityVersion,frozen.DepartmentId,frozen.WorkOrder.WebOrderNo,t);
            ValidateNew(frozen.WorkOrder);
            await a.RequireActiveAssociations(frozen.WorkOrder.WebOrderNo,null,t);
            var code=await CreateCoreAsync(frozen.WorkOrder,a.Actor,true,ticket.Id);
            await a.CompleteWorkOrderCreation(ticket,t);
            var result=new WorkOrderCreationResultV1(frozen.OperationId,ticket.Id,code,ticket.Department);
            await journal.Store(CreationOwner,CreationContract,frozen.OperationId,a.ActorId,input,result,[ticket.Id],
                new {action="create-work-order",actor=a.ActorId,department=ticket.Department,workOrder=result.WorkOrderId,input=frozen.WorkOrder},t);
            return result;
        },ct,actionAtFence:async t=>
        {original=await journal.Read(CreationOwner,frozen.OperationId,t);return original==null?"add":"query";});
    }
    private static void RequireCreationActor(OperationsAccessV1 a,MainOwnerOperationJournalV1.Original original)
    {
        if(original.Actor!=a.ActorId || original.Contract!=CreationContract)
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_FORBIDDEN",403);
    }
    private static async Task<WorkOrderCreationResultV1> DiscloseWorkOrderCreation(OperationsAccessV1 access,
        MainOwnerOperationJournalV1.Original original,Guid operation,byte[]? input,CancellationToken ct)
    {
        RequireCreationActor(access,original);
        if(original.State=="CLOSED")throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null && !MainOwnerOperationJournalV1.Matches(original,input))
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<WorkOrderCreationResultV1>(original.Result);
        if(result==null || result.OperationId!=operation || result.WorkOrderId==Guid.Empty
            || !original.Objects.SequenceEqual(new[]{result.WorkOrderId}))
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_UNAVAILABLE",503);
        await access.RequireWorkOrderIdentity(result.WorkOrderId,ct);
        access.AtCommit(t=>access.RequireWorkOrderIdentity(result.WorkOrderId,t));
        return result;
    }
    public Task<WorkOrderCreationOperationV1> ReadCreationAsync(Guid operation,CancellationToken ct=default)=>RecoverCreation(operation,false,ct);
    public Task<WorkOrderCreationOperationV1> CloseCreationAsync(Guid operation,CancellationToken ct=default)=>RecoverCreation(operation,true,ct);
    private Task<WorkOrderCreationOperationV1> RecoverCreation(Guid operation,bool close,CancellationToken ct)
    {
        if(operation==Guid.Empty)throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED",400);
        return Owner.ExecuteAsync(OperationsOwnerV1.Mes,"query",close,async(a,t)=>
        {
            var journal=new MainOwnerOperationJournalV1(_db);var original=await journal.Read(CreationOwner,operation,t);
            if(original!=null)
            {
                RequireCreationActor(a,original);
                return original.State=="CLOSED"?new(operation,"CLOSED"):
                    new WorkOrderCreationOperationV1(operation,"COMMITTED",await DiscloseWorkOrderCreation(a,original,operation,null,t));
            }
            if(!close)return new WorkOrderCreationOperationV1(operation,"UNKNOWN");
            var result=new WorkOrderCreationOperationV1(operation,"CLOSED");
            await journal.Store(CreationOwner,CreationContract,operation,a.ActorId,null,result,[],new{action="close-uncommitted-creation",actor=a.ActorId},t);
            return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
