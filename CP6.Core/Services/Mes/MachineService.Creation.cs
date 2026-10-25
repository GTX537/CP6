using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;

namespace CP6.Core.Services.Mes;

public partial class MachineService : IMachineCreationOperationsV1
{
    private const string CreationOwner="Main.Mes.MachineCreation",CreationContract="Main.Mes.MachineCreation.v1";
    public Task<MachineCreationContextV1> CreationContextAsync(CancellationToken ct=default)
        =>Owner.ExecuteAsync(OperationsOwnerV1.Machine,"add",false,(a,t)=>a.MachineCreationContext(t),ct,readOnlyAuthorityFence:true);
    public Task<MachineCreationScopeV1> CreationScopeAsync(CancellationToken ct=default)
        =>Owner.ExecuteAsync(OperationsOwnerV1.Machine,"query",false,
            (a,_)=>Task.FromResult(new MachineCreationScopeV1(_db.CurrentTenantId,a.ActorId)),ct,readOnlyAuthorityFence:true);
    public Task<MachineCreationResultV1> CreateCommandAsync(MachineCreationCommandV1 request,CancellationToken ct=default)
    {
        if(request==null || request.OperationId==Guid.Empty || request.Machine==null)
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED",400);
        var frozen=OperationsOwnerV1.Freeze(request);var input=MainOwnerOperationJournalV1.Encode(frozen);
        var journal=new MainOwnerOperationJournalV1(_db);MainOwnerOperationJournalV1.Original? original=null;
        return Owner.ExecuteAsync(OperationsOwnerV1.Machine,"add",true,async(a,t)=>
        {
            if(original!=null)return await DiscloseMachineCreation(a,original,frozen.OperationId,input,t);
            var ticket=await a.BeginMachineCreation(frozen.ExpectedAuthorityVersion,frozen.DepartmentId,t);
            var code=await CreateCoreAsync(frozen.Machine,a.Actor,ticket.Id);
            await a.CompleteMachineCreation(ticket,t);
            var result=new MachineCreationResultV1(frozen.OperationId,ticket.Id,code,ticket.Department);
            await journal.Store(CreationOwner,CreationContract,frozen.OperationId,a.ActorId,input,result,[ticket.Id],
                new {action="create-machine",actor=a.ActorId,department=ticket.Department,machine=result.MachineId,input=frozen.Machine},t);
            return result;
        },ct,actionAtFence:async t=>
        {original=await journal.Read(CreationOwner,frozen.OperationId,t);return original==null?"add":"query";});
    }
    private static void RequireCreationActor(OperationsAccessV1 a,MainOwnerOperationJournalV1.Original original)
    {
        if(original.Actor!=a.ActorId || original.Contract!=CreationContract)
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_FORBIDDEN",403);
    }
    private static async Task<MachineCreationResultV1> DiscloseMachineCreation(OperationsAccessV1 access,
        MainOwnerOperationJournalV1.Original original,Guid operation,byte[]? input,CancellationToken ct)
    {
        RequireCreationActor(access,original);
        if(original.State=="CLOSED")throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null && !MainOwnerOperationJournalV1.Matches(original,input))
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<MachineCreationResultV1>(original.Result);
        if(result==null || result.OperationId!=operation || result.MachineId==Guid.Empty
            || !original.Objects.SequenceEqual(new[]{result.MachineId}))
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_UNAVAILABLE",503);
        await access.RequireMachineIdentity(result.MachineId,ct);
        access.AtCommit(t=>access.RequireMachineIdentity(result.MachineId,t));
        return result;
    }
    public Task<MachineCreationOperationV1> ReadCreationAsync(Guid operation,CancellationToken ct=default)=>RecoverCreation(operation,false,ct);
    public Task<MachineCreationOperationV1> CloseCreationAsync(Guid operation,CancellationToken ct=default)=>RecoverCreation(operation,true,ct);
    private Task<MachineCreationOperationV1> RecoverCreation(Guid operation,bool close,CancellationToken ct)
    {
        if(operation==Guid.Empty)throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED",400);
        return Owner.ExecuteAsync(OperationsOwnerV1.Machine,"query",close,async(a,t)=>
        {
            var journal=new MainOwnerOperationJournalV1(_db);var original=await journal.Read(CreationOwner,operation,t);
            if(original!=null)
            {
                RequireCreationActor(a,original);
                return original.State=="CLOSED"?new(operation,"CLOSED"):
                    new MachineCreationOperationV1(operation,"COMMITTED",await DiscloseMachineCreation(a,original,operation,null,t));
            }
            if(!close)return new MachineCreationOperationV1(operation,"UNKNOWN");
            var result=new MachineCreationOperationV1(operation,"CLOSED");
            await journal.Store(CreationOwner,CreationContract,operation,a.ActorId,null,result,[],new{action="close-uncommitted-creation",actor=a.ActorId},t);
            return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
