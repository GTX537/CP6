using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Oa;

public sealed class NativeTimeoutOwnerV2(CP6Context db, ScopeAuthorizationFenceV1 fence, NativeTimeoutOptionsV2 options,
    PersonalFormOwnerV2 forms, ApprovalTaskDecisionOwnerV2 approvals)
{
    private static ScopeAuthorizationExceptionV1 Error(string code,int status=409)=>new(code,status);
    private static string Json(object value)=>JsonSerializer.Serialize(value,PersonalFormOwnerV2.Json);
    private static T Parse<T>(string value)=>JsonSerializer.Deserialize<T>(value,PersonalFormOwnerV2.Json)!;
    internal Func<CancellationToken,Task<DateTimeOffset>> Clock { get; set; } = ct => db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
    private sealed record Registration(Guid Revision);
    private async Task<Registration> Service(CancellationToken ct)
    {
        if(!options.Enabled || options.ServiceId==Guid.Empty || string.IsNullOrWhiteSpace(options.HostKey))throw Error("TIMEOUT_HOST_NOT_REGISTERED",503);
        if(!await db.Sys_Tenants.AsNoTracking().AnyAsync(t=>t.Id==db.CurrentTenantId && t.Enable && (t.ExpireDate==null||t.ExpireDate>DateTime.Now),ct))throw Error("TIMEOUT_TENANT_DISABLED",403);
        return await db.Database.SqlQuery<Registration>($"""
            SELECT revision AS "Revision" FROM cp6_native_timeout_v2.registrations
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND service_id={options.ServiceId} AND host_key={options.HostKey} AND enabled
            """).SingleOrDefaultAsync(ct)??throw Error("TIMEOUT_SERVICE_NOT_AUTHORIZED",403);
    }
    private void Entry()
    {if(!db.Database.IsNpgsql()||db.Database.CurrentTransaction!=null||db.ChangeTracker.HasChanges()||System.Transactions.Transaction.Current!=null)throw Error("TIMEOUT_TRANSACTION_REQUIRED",503);}
    private async Task Ready(CancellationToken ct)
    {if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_native_timeout_v2.ready() AS \"Value\"").SingleAsync(ct))throw Error("TIMEOUT_WRITER_UNAVAILABLE",503);}
    private sealed record Candidate(Guid Task,long Revision,long Occurrence);
    private IQueryable<NativeTimeoutClaimV2> Claims()=>db.Database.SqlQuery<NativeTimeoutClaimV2>($"""
        SELECT c.execution_id AS "ExecutionId",c.task_id AS "TaskId",c.revision AS "Revision",c.occurrence AS "Occurrence",
          c.epoch AS "Epoch",c.claim_id AS "ClaimId",c.service_id AS "ServiceId",c.registration_revision AS "RegistrationRevision",c.lease_until AS "LeaseUntil"
        FROM cp6_native_timeout_v2.claims c JOIN cp6_native_timeout_v2.deadlines d ON d.task_id=c.task_id AND d.revision=c.revision
        WHERE d.tenant_id={db.CurrentTenantId} AND d.environment_id={fence.EnvironmentId}
        """);
    public async Task<NativeTimeoutClaimV2[]> ClaimAsync(CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await fence.AcquireAsync(ct);await Ready(ct);var service=await Service(ct);var now=await Clock(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_native_timeout_v2.observations(tenant_id,environment_id,service_id,last_scan)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{options.ServiceId},{now})
                ON CONFLICT(tenant_id,environment_id,service_id) DO UPDATE SET last_scan=EXCLUDED.last_scan
                """,ct);
            var candidates=await db.Database.SqlQuery<Candidate>($"""
                SELECT p.task_id AS "Task",p.revision AS "Revision",p.occurrence AS "Occurrence"
                FROM cp6_native_timeout_v2.pending p JOIN cp6_native_timeout_v2.deadlines d ON d.task_id=p.task_id AND d.revision=p.revision
                JOIN public."Wf_FlowTask" t ON t."Id"=p.task_id JOIN public."Wf_FlowInstance" i ON i."Id"=t."InstanceId"
                LEFT JOIN cp6_native_timeout_v2.claims c ON c.task_id=p.task_id AND c.revision=p.revision AND c.occurrence=p.occurrence
                WHERE d.tenant_id={db.CurrentTenantId} AND d.environment_id={fence.EnvironmentId} AND p.state IN('READY','CLAIMED') AND p.next_due<={now}
                  AND (c.execution_id IS NULL OR c.lease_until<={now}) AND (t."Status" IN(1,2,3) OR t."Status"=0 AND i."Status"=0)
                ORDER BY p.next_due,p.task_id LIMIT 100
                """).ToArrayAsync(ct);
            var result=new List<NativeTimeoutClaimV2>();
            foreach(var c in candidates)
            {
                var existing=await Claims().SingleOrDefaultAsync(x=>x.TaskId==c.Task&&x.Revision==c.Revision&&x.Occurrence==c.Occurrence,ct);
                var claim=new NativeTimeoutClaimV2(existing?.ExecutionId??Guid.NewGuid(),c.Task,c.Revision,c.Occurrence,(existing?.Epoch??0)+1,
                    Guid.NewGuid(),options.ServiceId,service.Revision,now.AddSeconds(60));
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_native_timeout_v2.claims(execution_id,task_id,revision,occurrence,epoch,claim_id,service_id,registration_revision,lease_until)
                    VALUES({claim.ExecutionId},{claim.TaskId},{claim.Revision},{claim.Occurrence},{claim.Epoch},{claim.ClaimId},{claim.ServiceId},{claim.RegistrationRevision},{claim.LeaseUntil})
                    ON CONFLICT(task_id,revision,occurrence) DO UPDATE SET epoch=EXCLUDED.epoch,claim_id=EXCLUDED.claim_id,service_id=EXCLUDED.service_id,
                      registration_revision=EXCLUDED.registration_revision,lease_until=EXCLUDED.lease_until
                    """,ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_native_timeout_v2.pending SET state='CLAIMED' WHERE task_id={c.Task}",ct);
                result.Add(claim);
            }
            await fence.ValidateAsync(lease,ct);committing=true;await tx.CommitAsync(ct);return result.ToArray();
        }
        catch(Exception e){await Rollback(tx,committing,e);throw;}
    }
    private async Task CheckClaim(NativeTimeoutClaimV2 claim,Registration registration,DateTimeOffset now,CancellationToken ct)
    {
        var current=await Claims().SingleOrDefaultAsync(x=>x.ExecutionId==claim.ExecutionId,ct);
        if(current==null||current.ClaimId!=claim.ClaimId||current.Epoch!=claim.Epoch||current.ServiceId!=options.ServiceId
            ||current.TaskId!=claim.TaskId||current.Revision!=claim.Revision||current.Occurrence!=claim.Occurrence
            ||current.RegistrationRevision!=registration.Revision||current.LeaseUntil<=now)throw Error("TIMEOUT_LEASE_LOST");
    }
    public async Task<NativeTimeoutClaimV2> RenewAsync(NativeTimeoutClaimV2 claim,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await fence.AcquireAsync(ct);
        await Ready(ct);var service=await Service(ct);var now=await Clock(ct);await CheckClaim(claim,service,now,ct);
        var renewed=claim with{LeaseUntil=now.AddSeconds(60)};
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_native_timeout_v2.claims SET lease_until={renewed.LeaseUntil} WHERE execution_id={claim.ExecutionId} AND epoch={claim.Epoch} AND claim_id={claim.ClaimId}",ct);
        await fence.ValidateAsync(lease,ct);await tx.CommitAsync(ct);return renewed;
    }
    private sealed record Deadline(string ContextKind,string Policy);
    public async Task<NativeTimeoutResultV2> ExecuteAsync(NativeTimeoutClaimV2 claim,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await fence.AcquireAsync(ct);await Ready(ct);var service=await Service(ct);
            var old=await db.Database.SqlQuery<string>($"""
                SELECT r.body AS "Value" FROM cp6_native_timeout_v2.results r JOIN cp6_native_timeout_v2.claims c ON c.execution_id=r.execution_id
                JOIN cp6_native_timeout_v2.deadlines d ON d.task_id=c.task_id AND d.revision=c.revision
                WHERE r.execution_id={claim.ExecutionId} AND d.tenant_id={db.CurrentTenantId} AND d.environment_id={fence.EnvironmentId} AND c.service_id={options.ServiceId}
                """).SingleOrDefaultAsync(ct);
            if(old!=null)return Parse<NativeTimeoutResultV2>(old);
            var now=await Clock(ct);await CheckClaim(claim,service,now,ct);
            var row=await db.Database.SqlQuery<Deadline>($"""
                SELECT context_kind AS "ContextKind",policy_json AS "Policy" FROM cp6_native_timeout_v2.deadlines
                WHERE task_id={claim.TaskId} AND revision={claim.Revision} AND tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
                """).SingleAsync(ct);
            var policy=Parse<NativeDeadlinePolicyV2>(row.Policy);
            var task=await db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==claim.TaskId,ct);
            var instance=await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==task.InstanceId,ct);
            var instanceStatus=instance.Status;
            var taskVersion=Convert.ToBase64String(await db.Database.SqlQuery<byte[]>($"SELECT \"ApprovalRowVersion\" AS \"Value\" FROM public.\"Wf_FlowTask\" WHERE \"Id\"={task.Id}").SingleAsync(ct));
            var instanceVersion=Convert.ToBase64String(instance.RowVersion!);
            if(task.Status==4 || task.Status==0&&instanceStatus==4)
            {
                // Suspension is reversible. Keep the original deadline/occurrence and
                // execution identity available after resume; it is not a handled task.
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_native_timeout_v2.claims SET lease_until={now} WHERE execution_id={claim.ExecutionId}",ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_native_timeout_v2.pending SET state='READY' WHERE task_id={claim.TaskId}",ct);
                await fence.ValidateAsync(lease,ct);committing=true;await tx.CommitAsync(ct);
                return new(claim.ExecutionId,claim.TaskId,claim.Revision,claim.Occurrence,"DEFERRED_STATE","SUSPENDED",options.ServiceId,now,
                    ClaimEpoch:claim.Epoch,OriginalTaskVersion:taskVersion,OriginalInstanceVersion:instanceVersion);
            }
            var running=task.Status==0&&instanceStatus==0;
            var result=new NativeTimeoutResultV2(claim.ExecutionId,claim.TaskId,claim.Revision,claim.Occurrence,
                "SKIPPED_ALREADY_HANDLED",null,options.ServiceId,now,ClaimEpoch:claim.Epoch,
                OriginalTaskVersion:taskVersion,OriginalInstanceVersion:instanceVersion);
            if(running)result=row.ContextKind=="SFS"?await forms.ApplyTimeoutV2(result,policy,ct):await approvals.ApplyTimeoutV2(result,policy,ct);
            result=result with{ClaimEpoch=claim.Epoch};
            // Current task/authority and epoch share the final Owner transaction. A
            // claimant which expired while staging cannot commit its effects.
            await CheckClaim(claim,service,await Clock(ct),ct);await fence.ValidateAsync(lease,ct);
            if(await Service(ct)!=service)throw Error("TIMEOUT_AUTHORITY_CHANGED",403);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_native_timeout_v2.results(execution_id,body,command_id) VALUES({claim.ExecutionId},{Json(result)},{result.CommandId})",ct);
            var escalated=result.State=="COMPLETED"&&policy.Action=="escalate";
            if(escalated)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_native_timeout_v2.deadlines(task_id,revision,tenant_id,environment_id,instance_id,root_id,context_kind,created_at,due_at,policy_json)
                    SELECT task_id,revision+1,tenant_id,environment_id,instance_id,root_id,context_kind,{now},NULL,policy_json
                    FROM cp6_native_timeout_v2.deadlines WHERE task_id={claim.TaskId} AND revision={claim.Revision}
                    """,ct);
            var repeat=result.State=="COMPLETED"&&policy.Action=="remind";
            DateTimeOffset? next=repeat?(await Clock(ct)).AddHours(policy.Hours!.Value):null;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_native_timeout_v2.pending SET state={(repeat?"READY":result.State)},revision={claim.Revision+(escalated?1:0)},occurrence={claim.Occurrence+(repeat?1:0)},next_due={next}
                WHERE task_id={claim.TaskId} AND revision={claim.Revision} AND occurrence={claim.Occurrence}
                """,ct);
            committing=true;await tx.CommitAsync(ct);db.ChangeTracker.Clear();return result;
        }
        catch(Exception e){await Rollback(tx,committing,e);throw;}
    }
    private async Task Rollback(IDbContextTransaction tx,bool committing,Exception error)
    {
        if(committing && error is Npgsql.PostgresException rejected && rejected.SqlState!="40003"
            && rejected.SqlState[..2] is "22" or "23" or "40" or "42") { db.ChangeTracker.Clear();return; }
        try{await tx.RollbackAsync(CancellationToken.None);}
        catch(Exception rollback){throw new ScopeAuthorizationExceptionV1("TIMEOUT_OUTCOME_UNKNOWN",503,new AggregateException(error,rollback));}
        finally{db.ChangeTracker.Clear();}
        if(committing&&(error is not Npgsql.PostgresException pg||pg.SqlState=="40003"))throw new ScopeAuthorizationExceptionV1("TIMEOUT_OUTCOME_UNKNOWN",503,error);
    }
}
