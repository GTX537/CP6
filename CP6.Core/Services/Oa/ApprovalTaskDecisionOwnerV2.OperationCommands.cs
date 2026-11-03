using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class ApprovalTaskDecisionOwnerV2
{
    private async Task<CP6.Core.Services.Sys.ScopeAuthorizationLeaseV1> ReadOperationFence(CancellationToken ct)
    {
        await scope.Root(ct);var snapshot=await scope.ReadLocked(ct)??throw Error("SCOPE_MAPPING_REQUIRED");
        return new(db,db.Database.CurrentTransaction!.TransactionId,snapshot);
    }
    private static void OperationScope(string operation,Guid resource,string key)
    {
        if(operation is not("OA_TASK_TRANSFER" or "OA_TASK_ADDSIGN" or "OA_TASK_SENDBACK" or "OA_APPROVER_RESOLVE")||resource==Guid.Empty
            ||!System.Text.RegularExpressions.Regex.IsMatch(key,"\\A[A-Za-z0-9_.-]{1,100}\\z"))throw Error("COMMAND_SCOPE_INVALID",400);
    }
    public async Task<PersonalFormResultV2?> OriginalOperationAsync(string operation,Guid resource,string command,CancellationToken ct=default)
    {
        OperationScope(operation,resource,command);Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);
        var fence=await ReadOperationFence(ct);var actor=await authority.CurrentActor(false,ct);var row=await OriginalOperation(actor,operation,resource,command,ct);
        if(row==null){await scope.ValidateAsync(fence,ct);return null;}
        await Business.Decision(await OperationOriginal(row.Root,ct),null,false,ct);await scope.ValidateAsync(fence,ct);return Parse<PersonalFormResultV2>(row.Result);
    }
    public async Task<PersonalFormResultV2> CloseOperationAsync(string operation,Guid resource,string command,CancellationToken ct=default)
    {
        OperationScope(operation,resource,command);Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var fence=await ReadOperationFence(ct);var actor=await authority.CurrentActor(false,ct);var row=await OriginalOperation(actor,operation,resource,command,ct);
            Guid root;Guid? task;
            if(row!=null)
            {
                await Business.Decision(await OperationOriginal(row.Root,ct),null,false,ct);await scope.ValidateAsync(fence,ct);return Parse<PersonalFormResultV2>(row.Result);
            }
            await scope.AcquireAsync(ct);await Ready(ct);
            if(operation=="OA_APPROVER_RESOLVE"){root=(await AppResolutionRow(resource,ct)).Root;task=null;}
            else {root=(await ReadSource(resource,ct)).Root;task=resource;}
            await Business.Decision(await OperationOriginal(root,ct),null,false,ct);
            var result=new PersonalFormResultV2(command,"CLOSED",null,null,null,DateTimeOffset.UtcNow,null);
            var audit=Json(new{Actor=actor,Scope=fence.Snapshot,Reason="ACTOR_EXPLICIT_CLOSE_WITHOUT_EFFECTS"});
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_native_tasks_v2.commands(id,tenant_id,environment_id,actor_id,operation,resource_id,command_key,digest,raw_body,root_id,task_id,result_json,audit_json,completed,closed)
                VALUES({Guid.NewGuid()},{db.CurrentTenantId},{scope.EnvironmentId},{actor},{operation},{resource},{command},{"CLOSED"},{Array.Empty<byte>()},{root},{task},{Json(result)},{audit},true,true)
                """,ct);
            await scope.ValidateAsync(fence,ct);await Ready(ct);committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception e){await RollbackOperation(tx,committing,e);throw;}
    }
}
