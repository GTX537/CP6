using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;

namespace CP6.Core.Services.Wms;

public partial class StockTakeService : IStockTakeCreationOperationsV1
{
    private const string CreationOwner="Main.Wms.StockTakeCreation",CreationContract="Main.Wms.StockTakeCreation.v1";
    public Task<StockTakeCreationContextV1> CreationContextAsync(CancellationToken ct=default)
         =>Owner.ExecuteAsync(OperationsOwnerV1.StockTake,"add",false,async(a,t)=>
        {
            a.RequirePriceWrite();a.RequireAmountWrite();
            (await a.SourceAsync(OperationsOwnerV1.Stock,t)).RequirePriceRead();
            return await a.StockTakeCreationContext(t);
        },ct,readOnlyAuthorityFence:true);
    public Task<StockTakeCreationScopeV1> CreationScopeAsync(CancellationToken ct=default)
        =>Owner.ExecuteAsync(OperationsOwnerV1.StockTake,"query",false,
            (a,_)=>Task.FromResult(new StockTakeCreationScopeV1(_db.CurrentTenantId,a.ActorId)),ct,readOnlyAuthorityFence:true);
    public Task<StockTakeCreationResultV1> CreateCommandAsync(StockTakeCreationCommandV1 request,CancellationToken ct=default)
    {
        if(request==null || request.OperationId==Guid.Empty || request.Plan==null)
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED",400);
        var frozen=OperationsOwnerV1.Freeze(request);var input=MainOwnerOperationJournalV1.Encode(frozen);
        var journal=new MainOwnerOperationJournalV1(_db);MainOwnerOperationJournalV1.Original? original=null;
        return Owner.ExecuteAsync(OperationsOwnerV1.StockTake,"add",true,async(a,t)=>
        {
            if(original!=null)return await DiscloseStockTakeCreation(a,original,frozen.OperationId,input,t);
            var ticket=await a.BeginStockTakeCreation(frozen.ExpectedAuthorityVersion,frozen.DepartmentId,t);
            var code=await CreatePlanOwnedAsync(frozen.Plan,a,ticket.Id,t);
            await a.CompleteStockTakeCreation(ticket,t);
            var result=new StockTakeCreationResultV1(frozen.OperationId,ticket.Id,code,ticket.Department);
            await journal.Store(CreationOwner,CreationContract,frozen.OperationId,a.ActorId,input,result,[ticket.Id],
                new {action="create-stocktake",actor=a.ActorId,department=ticket.Department,stocktake=result.StockTakeId,input=frozen.Plan},t);
            return result;
        },ct,actionAtFence:async t=>
        {original=await journal.Read(CreationOwner,frozen.OperationId,t);return original==null?"add":"query";});
    }
    private static void RequireCreationActor(OperationsAccessV1 a,MainOwnerOperationJournalV1.Original original)
    {
        if(original.Actor!=a.ActorId || original.Contract!=CreationContract)
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_FORBIDDEN",403);
    }
    private static async Task<StockTakeCreationResultV1> DiscloseStockTakeCreation(OperationsAccessV1 access,
        MainOwnerOperationJournalV1.Original original,Guid operation,byte[]? input,CancellationToken ct)
    {
        RequireCreationActor(access,original);
        if(original.State=="CLOSED")throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null && !MainOwnerOperationJournalV1.Matches(original,input))
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<StockTakeCreationResultV1>(original.Result);
        if(result==null || result.OperationId!=operation || result.StockTakeId==Guid.Empty
            || !original.Objects.SequenceEqual(new[]{result.StockTakeId}))
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_UNAVAILABLE",503);
        await access.RequireStockTakeIdentity(result.StockTakeId,ct);
        access.AtCommit(t=>access.RequireStockTakeIdentity(result.StockTakeId,t));
        return result;
    }
    public Task<StockTakeCreationOperationV1> ReadCreationAsync(Guid operation,CancellationToken ct=default)=>RecoverCreation(operation,false,ct);
    public Task<StockTakeCreationOperationV1> CloseCreationAsync(Guid operation,CancellationToken ct=default)=>RecoverCreation(operation,true,ct);
    private Task<StockTakeCreationOperationV1> RecoverCreation(Guid operation,bool close,CancellationToken ct)
    {
        if(operation==Guid.Empty)throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED",400);
        return Owner.ExecuteAsync(OperationsOwnerV1.StockTake,"query",close,async(a,t)=>
        {
            var journal=new MainOwnerOperationJournalV1(_db);var original=await journal.Read(CreationOwner,operation,t);
            if(original!=null)
            {
                RequireCreationActor(a,original);
                return original.State=="CLOSED"?new(operation,"CLOSED"):
                    new StockTakeCreationOperationV1(operation,"COMMITTED",await DiscloseStockTakeCreation(a,original,operation,null,t));
            }
            if(!close)return new StockTakeCreationOperationV1(operation,"UNKNOWN");
            var result=new StockTakeCreationOperationV1(operation,"CLOSED");
            await journal.Store(CreationOwner,CreationContract,operation,a.ActorId,null,result,[],new{action="close-uncommitted-creation",actor=a.ActorId},t);
            return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
