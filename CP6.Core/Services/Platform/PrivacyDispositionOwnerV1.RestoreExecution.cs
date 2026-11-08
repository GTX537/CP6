using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace CP6.Core.Services.Platform;
public sealed partial class PrivacyDispositionOwnerV1
{
    private sealed record RestoreIntent(Guid PlanId,byte[] Input);
    private Task<RestoreIntent?> Intent(Authority a,Guid operation,CancellationToken ct)=>db.Database.SqlQuery<RestoreIntent>($"""
        SELECT plan_id AS "PlanId",input_bytes AS "Input" FROM cp6_privacy_disposition_v1.restore_intents
        WHERE environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId} AND actor_id={a.Actor.Id} AND operation_id={operation}
        """).SingleOrDefaultAsync(ct);
    private async Task<PrivacyRestoreResultV1?> RestoreOutcome(Authority a,Guid operation,CancellationToken ct)
    {
        var json=await db.Database.SqlQuery<string>($"""
            SELECT result_json AS "Value" FROM cp6_privacy_disposition_v1.restore_outcomes
            WHERE environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId} AND actor_id={a.Actor.Id} AND operation_id={operation}
            """).SingleOrDefaultAsync(ct);
        return json==null?null:JsonSerializer.Deserialize<PrivacyRestoreResultV1>(json)!;
    }
    private async Task<PrivacyRestoreResultV1?> RestorePlanOutcome(Authority a,Guid plan,CancellationToken ct)
    {
        var json=await db.Database.SqlQuery<string>($"""
            SELECT result_json AS "Value" FROM cp6_privacy_disposition_v1.restore_outcomes
            WHERE environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId} AND plan_id={plan} AND state='APPLIED'
            """).SingleOrDefaultAsync(ct);
        return json==null?null:JsonSerializer.Deserialize<PrivacyRestoreResultV1>(json)!;
    }
    private Task SaveRestoreOutcome(Authority a,PrivacyRestoreResultV1 result,PrivacyRestoreScopeV1? scope,CancellationToken ct)
        =>db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_privacy_disposition_v1.restore_outcomes(environment_id,actor_tenant,actor_id,operation_id,plan_id,source_version,candidate_id,target_identity,state,result_json)
            VALUES({a.Environment},{a.Actor.TenantId},{a.Actor.Id},{result.OperationId},{result.PlanId},{result.SourceVersion},{result.CandidateId},
              {(scope==null?null:PrivacyRestoreNativeV1.IdentityJson(scope.Target))},{result.State},{JsonSerializer.Serialize(result)})
            """,ct);
    private static PrivacyRestoreResultV1 RestorePending(Guid operation,PlanRow row,PrivacyRestoreScopeV1 scope,string state="PENDING")
        =>new(operation,state,row.Id,scope.CandidateId,scope.SourceVersion,row.Digest,null,scope.UncoveredOwners,null);
    private async Task RequireRestoreApproval(Authority a,PlanRow row,PrivacyRestoreExecuteV1 input,CancellationToken ct)
    {
        var current=await RestoreView(a,row,ct);
        if(current.Version!=input.ExpectedVersion||current.ScopeDigest!=input.ExpectedScopeDigest)throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_APPROVAL_CHANGED",412);
        if(!current.ApprovalCurrent)throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_EXECUTION_BLOCKED");
    }
    private async Task RequireNoOtherRestoreIntent(Authority a,PrivacyRestoreScopeV1 scope,Guid operation,CancellationToken ct)
    {
        if(await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_privacy_disposition_v1.restore_intents i
              JOIN cp6_privacy_disposition_v1.restore_plans p ON p.id=i.plan_id
              WHERE i.environment_id={a.Environment} AND p.scope_json::jsonb->'Target'->>'Name'={scope.Target.Name}
                AND (p.scope_json::jsonb->'Target'->>'Oid')::bigint={scope.Target.Oid}
                AND NOT(i.actor_tenant={a.Actor.TenantId} AND i.actor_id={a.Actor.Id} AND i.operation_id={operation})
                AND NOT EXISTS(SELECT FROM cp6_privacy_disposition_v1.restore_outcomes o WHERE o.environment_id=i.environment_id
                  AND o.actor_tenant=i.actor_tenant AND o.actor_id=i.actor_id AND o.operation_id=i.operation_id)) AS "Value"
            """).SingleAsync(ct))throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_PENDING_RESPONSIBILITY_MUST_BE_RESOLVED");
    }
    private static void RestoreInput(RestoreIntent? intent,byte[] input)
    {if(intent!=null&&!intent.Input.AsSpan().SequenceEqual(input))throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_OPERATION_CONFLICT");}
    public async Task<PrivacyRestoreResultV1> ExecuteRestoreAsync(Guid id,PrivacyRestoreExecuteV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(id);Id(input.OperationId);Id(input.ExpectedVersion);Id(input.ConfirmationRef);
        if(string.IsNullOrEmpty(input.ExpectedScopeDigest))throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_PREVIEW_REQUIRED",400);
        var bytes=MainOwnerOperationJournalV1.Encode(new{id,input});
        // This committed intent survives either database's later commit ambiguity.
        var prepared=await Transaction(true,session,async(a,t)=>
        {
            Can(a,"read");var old=await RestoreOutcome(a,input.OperationId,t);if(old?.State=="CLOSED")return old;
            var intent=await Intent(a,input.OperationId,t);RestoreInput(intent,bytes);if(old!=null)return old;Can(a,"restore-execute");
            var row=await RestorePlan(a,id,t)??throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_PLAN_NOT_FOUND",404);
            await RequireRestoreApproval(a,row,input,t);var scope=JsonSerializer.Deserialize<PrivacyRestoreScopeV1>(row.Scope)!;
            await RequireNoOtherRestoreIntent(a,scope,input.OperationId,t);
            if(intent==null)await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_disposition_v1.restore_intents(environment_id,actor_tenant,actor_id,operation_id,plan_id,input_bytes)
                VALUES({a.Environment},{a.Actor.TenantId},{a.Actor.Id},{input.OperationId},{id},{bytes})
                """,t);
            return RestorePending(input.OperationId,row,scope);
        },ct);
        if(prepared.State!="PENDING")return prepared;
        CP6Context? candidate=null;IDbContextTransaction? candidateTransaction=null;
        try
        {
            return await Transaction(true,session,async(a,t)=>
            {
                Can(a,"read");var old=await RestoreOutcome(a,input.OperationId,t);if(old!=null)return old;
                var intent=(await Intent(a,input.OperationId,t))!;RestoreInput(intent,bytes);
                var row=(await RestorePlan(a,intent.PlanId,t))!;var scope=JsonSerializer.Deserialize<PrivacyRestoreScopeV1>(row.Scope)!;
                candidate=PrivacyRestoreNativeV1.Context(RestoreCandidate(scope.CandidateId).ConnectionString,a.Actor.TenantId,identity);
                candidateTransaction=await PrivacyRestoreNativeV1.CandidateGate(candidate,scope,t);
                var known=await PrivacyRestoreNativeV1.Receipt(candidate,a.Environment,a.Actor.TenantId,a.Actor.Id,input.OperationId,t);
                if(known!=null)
                {
                    RequireRestoreReceipt(known,row,scope,input.OperationId);
                    await candidateTransaction.DisposeAsync();candidateTransaction=null;
                    await SaveRestoreOutcome(a,known,scope,t);return known;
                }
                Can(a,"restore-execute");await RequireRestoreApproval(a,row,input,t);await RequireNoOtherRestoreIntent(a,scope,input.OperationId,t);
                var result=await PrivacyRestoreNativeV1.Apply(candidate,hasher,scope,a.Actor.TenantId,a.Actor.Id,input.OperationId,id,row.Digest,await RestoreTombstones(a,t),await RestorePayloadTombstones(a,t),t);
                await SaveRestoreOutcome(a,result,scope,t);return result;
            },ct,async t=>
            {
                // The source Transaction has just completed the actual host session fence.
                if(candidateTransaction!=null)
                {
                    try{await candidateTransaction.CommitAsync(t);}
                    catch(Exception e)when(PayloadConstraintCode(e)!=null){throw new PrivacyDispositionExceptionV1(PayloadConstraintCode(e)!,409,e);}
                    catch(Exception e){throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_CANDIDATE_OUTCOME_UNKNOWN",503,e);}
                }
            });
        }
        finally{if(candidateTransaction!=null)await candidateTransaction.DisposeAsync();if(candidate!=null)await candidate.DisposeAsync();}
    }
    private static void RequireRestoreReceipt(PrivacyRestoreResultV1 result,PlanRow row,PrivacyRestoreScopeV1 scope,Guid operation)
    {
        // This is the single external candidate-result boundary, bound to the approved physical target and intent.
        if(result.OperationId!=operation||result.State!="APPLIED"||result.PlanId!=row.Id||result.CandidateId!=scope.CandidateId||result.SourceVersion!=scope.SourceVersion||result.ScopeDigest!=row.Digest)
            throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_CANDIDATE_RECEIPT_CONFLICT",412);
    }
    public Task<PrivacyRestoreResultV1> RestoreOperationAsync(Guid operation,string action,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(operation);if(action is not("READ" or "RECOVER" or "CLOSE"))throw new ArgumentOutOfRangeException(nameof(action));
        return Transaction(action!="READ",session,async(a,t)=>
        {
            Can(a,"read");var old=await RestoreOutcome(a,operation,t);if(old!=null)return old;
            var intent=await Intent(a,operation,t);
            if(intent==null)
            {
                var missing=new PrivacyRestoreResultV1(operation,action=="CLOSE"?"CLOSED":"NOT_FOUND",null,null,null,null,null,null,null);
                if(action=="CLOSE")await SaveRestoreOutcome(a,missing,null,t);return missing;
            }
            var row=(await RestorePlan(a,intent.PlanId,t))!;var scope=JsonSerializer.Deserialize<PrivacyRestoreScopeV1>(row.Scope)!;
            await using var candidate=PrivacyRestoreNativeV1.Context(RestoreCandidate(scope.CandidateId).ConnectionString,a.Actor.TenantId,identity);
            await using var gate=await PrivacyRestoreNativeV1.CandidateGate(candidate,scope,t);
            var known=await PrivacyRestoreNativeV1.Receipt(candidate,a.Environment,a.Actor.TenantId,a.Actor.Id,operation,t);
            if(known!=null)
            {
                RequireRestoreReceipt(known,row,scope,operation);
                if(action=="READ")return known with{State="AWAITING_SOURCE_RESULT"};
                await SaveRestoreOutcome(a,known,scope,t);return known;
            }
            var result=RestorePending(operation,row,scope,action=="CLOSE"?"CLOSED":"PENDING");
            if(action=="CLOSE")await SaveRestoreOutcome(a,result,scope,t);return result;
        },ct);
    }
}
