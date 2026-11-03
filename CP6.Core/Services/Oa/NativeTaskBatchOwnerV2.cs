using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

/// <summary>Freezes the management selection. Each item is still committed by its
/// native Owner with the same authority fence, versions and original-command slot.</summary>
public sealed class NativeTaskBatchOwnerV2(CP6Context db,ScopeAuthorizationFenceV1 fence,
    PersonalFormOwnerV2 forms,ApprovalTaskDecisionOwnerV2 app)
{
    private static ScopeAuthorizationExceptionV1 Error(string code,int status=400)=>new(code,status);
    private static string Json(object value)=>JsonSerializer.Serialize(value,PersonalFormOwnerV2.Json);
    private sealed record Stored(Guid Id,Guid Actor,string CommandKey,string Digest,string Snapshot);
    private Task<Stored?> StoredBatch(Guid actor,Guid? id,string? key,CancellationToken ct)=>db.Database.SqlQuery<Stored>($"""
        SELECT id AS "Id",actor_id AS "Actor",command_key AS "CommandKey",digest AS "Digest",snapshot_json AS "Snapshot"
        FROM cp6_native_tasks_v2.transfer_batches WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
          AND actor_id={actor} AND ({id}::uuid IS NULL OR id={id}) AND ({key}::text IS NULL OR command_key={key})
        """).SingleOrDefaultAsync(ct);
    private async Task<UserPermissionContext> Manager(CancellationToken ct)
    {var actor=await forms.Actor(ct);await forms.Action(actor,"query",ct,"oa-inbox");await forms.Action(actor,"batch-transfer",ct,"oa-inbox");return actor;}
    private async Task Ready(CancellationToken ct)
    {if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_native_tasks_v2.batch_ready() AS \"Value\"").SingleAsync(ct))throw Error("BATCH_STORE_UNAVAILABLE",503);}
    public async Task<NativeTransferBatchPreviewV2> PreviewAsync(byte[] raw,CancellationToken ct=default)
    {
        var command=PersonalFormOwnerV2.CommandInput<NativeTransferBatchFilterV2>(raw,false);var filter=command.Body;
        if(filter.FromUserId==Guid.Empty||filter.ToUserId==Guid.Empty||filter.FromUserId==filter.ToUserId
            ||string.IsNullOrWhiteSpace(filter.Reason)||filter.Reason.EnumerateRunes().Count()>1000
            ||filter.FlowKey!=null&&(string.IsNullOrWhiteSpace(filter.FlowKey)||filter.FlowKey.Length>100)
            ||filter.BeforeUtc is {} before&&before.Offset!=TimeSpan.Zero
            ||filter.ExplicitTaskIds is {} ids&&(ids.Length is <1 or >500||ids.Contains(Guid.Empty)||ids.Distinct().Count()!=ids.Length))throw Error("BATCH_FILTER_INVALID",422);
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await fence.AcquireAsync(ct);var actor=await Manager(ct);await Ready(ct);
            var old=await StoredBatch(actor.UserId,null,command.Id,ct);
            if(old!=null)
            {if(old.Digest!=command.Digest)throw Error("COMMAND_BODY_CONFLICT",409);return await Disclose(old,actor,lease,ct);}
            var q=from t in db.Wf_FlowTasks.AsNoTracking() join i in db.Wf_FlowInstances.AsNoTracking() on t.InstanceId equals i.Id
                where t.AssigneeId==filter.FromUserId&&t.Status==0&&i.Status==0&&(i.Creator==FlowEngine.PersonalFormRuntimeV2||i.Creator==FlowEngine.ApprovalRuntimeV2)
                select new{Task=t.Id,Instance=i.Id,i.Creator,i.FlowKey};
            if(filter.FlowKey!=null)q=q.Where(x=>EF.Functions.Collate(x.FlowKey,"C")==filter.FlowKey);
            if(filter.ExplicitTaskIds!=null)q=q.Where(x=>filter.ExplicitTaskIds.Contains(x.Task));
            // UTC comes from immutable native assignment facts, never a local wall-clock CreateDate.
            if(filter.BeforeUtc is {} cutoff)
            {
                var older=db.Database.SqlQuery<Guid>($"""
                    SELECT task_id AS "Value" FROM cp6_personal_form_v2.assignments WHERE (body::jsonb->>'assignedAt')::timestamptz<={cutoff}
                    UNION ALL SELECT task_id AS "Value" FROM cp6_approval_tasks_v2.assignments WHERE (body::jsonb->>'assignedAt')::timestamptz<={cutoff}
                    """);
                q=q.Where(x=>older.Contains(x.Task));
            }
            var batch=Guid.NewGuid();var items=new List<NativeTransferBatchItemV2>();
            // Count only currently authorized resources. Page the identities before
            // calling their Owners so a live reader never overlaps another DB command.
            for(var offset=0;;offset+=501)
            {
              var candidates=await q.OrderBy(x=>x.Task).Skip(offset).Take(501).ToArrayAsync(ct);
              foreach(var candidate in candidates)
              {
                ApprovalTaskDecisionViewV2 view;
                try{view=candidate.Creator==FlowEngine.PersonalFormRuntimeV2?await forms.AdministrativeTransferContext(candidate.Task,filter.FromUserId,ct):await app.AdministrativeTransferContext(candidate.Task,filter.FromUserId,ct);}
                catch(ScopeAuthorizationExceptionV1 e)when(e.Status is 403 or 404){continue;} // Current resource visibility, not an admin bypass.
                items.Add(new(candidate.Task,candidate.Instance,candidate.Creator==FlowEngine.PersonalFormRuntimeV2?"SFS":"APP",candidate.FlowKey,
                    view.TaskRowVersion,view.InstanceRowVersion,$"bt.{batch:N}.{candidate.Task:N}"));
                if(items.Count>500)throw Error("BATCH_LIMIT_EXCEEDED",422);
              }
              if(candidates.Length<501)break;
            }
            var at=DateTimeOffset.UtcNow;
            var digest=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{BatchId=batch,PreviewedAt=at,Filter=filter,Items=items}));
            var result=new NativeTransferBatchPreviewV2(batch,at,digest,filter,items.ToArray());
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_native_tasks_v2.transfer_batches(id,tenant_id,environment_id,actor_id,command_key,digest,snapshot_json)
                VALUES({batch},{db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{command.Id},{command.Digest},{Json(result)})
                """,ct);
            await forms.Final(actor,lease,ct,false);committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception e){await forms.Rollback(tx,committing,e);throw;}
    }
    private async Task<NativeTransferBatchPreviewV2> Disclose(Stored row,UserPermissionContext actor,ScopeAuthorizationLeaseV1 lease,CancellationToken ct)
    {
        var value=JsonSerializer.Deserialize<NativeTransferBatchPreviewV2>(row.Snapshot,PersonalFormOwnerV2.Json)!;
        foreach(var item in value.Items)
            if(item.ContextKind=="SFS")await forms.AdministrativeTransferRead(item.TaskId,ct);else await app.AdministrativeTransferRead(item.TaskId,ct);
        await forms.Final(actor,lease,ct,false);return value;
    }
    public async Task<NativeTransferBatchPreviewV2?> OriginalPreviewAsync(string command,CancellationToken ct=default)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(command,"\\A[A-Za-z0-9_.-]{1,100}\\z"))throw Error("COMMAND_SHAPE_INVALID");
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await forms.ReadLease(ct);var actor=await Manager(ct);
        var old=await StoredBatch(actor.UserId,null,command,ct);return old==null?null:await Disclose(old,actor,lease,ct);
    }
    private async Task<NativeTransferBatchPreviewV2> ReadBatch(Guid batch,CancellationToken ct)
    {
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await forms.ReadLease(ct);var actor=await Manager(ct);
        var old=await StoredBatch(actor.UserId,batch,null,ct)??throw Error("RESOURCE_NOT_FOUND",404);
        // Each item's current resource authority is checked by its Owner below. Losing
        // one resource must not hide the independently committed outcomes of other items.
        await forms.Final(actor,lease,ct,false);return JsonSerializer.Deserialize<NativeTransferBatchPreviewV2>(old.Snapshot,PersonalFormOwnerV2.Json)!;
    }
    private async Task<NativeTransferBatchItemResultV2> OriginalItem(NativeTransferBatchItemV2 item,CancellationToken ct)
    {
        var result=item.ContextKind=="SFS"?await forms.OriginalAsync("OA_TASK_TRANSFER",item.TaskId.ToString("D"),item.ItemCommandId,ct)
            :await app.OriginalOperationAsync("OA_TASK_TRANSFER",item.TaskId,item.ItemCommandId,ct);
        return new(item.TaskId,item.ItemCommandId,result?.Outcome=="SUCCEEDED"?"SUCCEEDED":result?.Outcome=="CLOSED"?"REJECTED":"UNKNOWN",
            result?.Outcome=="CLOSED"?"COMMAND_CLOSED":null,result?.Outcome=="SUCCEEDED"?result:null);
    }
    public async Task<NativeTransferBatchReportV2> ReportAsync(Guid batch,CancellationToken ct=default)
    {
        var snapshot=await ReadBatch(batch,ct);var results=new List<NativeTransferBatchItemResultV2>();
        foreach(var item in snapshot.Items)
        {
            try
            {
                var result=await OriginalItem(item,ct);
                if(result.State=="UNKNOWN")
                {
                    var observed=await db.Database.SqlQuery<string>($"SELECT error_code AS \"Value\" FROM cp6_native_tasks_v2.transfer_rejections WHERE batch_id={batch} AND task_id={item.TaskId}").SingleOrDefaultAsync(ct);
                    if(observed!=null)result=result with{State="REJECTED",Error=observed};
                }
                results.Add(result);
            }
            catch(ScopeAuthorizationExceptionV1 e){results.Add(new(item.TaskId,item.ItemCommandId,e.Status>=500?"UNKNOWN":"REJECTED",e.Message,null));}
        }
        return new(batch,results.ToArray());
    }
    public async Task<NativeTransferBatchReportV2> ExecuteAsync(NativeTransferBatchConfirmV2 confirmation,CancellationToken ct=default)
    {
        var snapshot=await ReadBatch(confirmation.BatchId,ct);
        if(snapshot.PreviewDigest!=confirmation.PreviewDigest)throw Error("BATCH_PREVIEW_CHANGED",409);
        var results=new List<NativeTransferBatchItemResultV2>();
        foreach(var item in snapshot.Items)
        {
            ct.ThrowIfCancellationRequested();
            var raw=JsonSerializer.SerializeToUtf8Bytes(new{commandId=item.ItemCommandId,expectedVersion=item.InstanceVersion,
                body=new NativeTransferInputV2(item.TaskId,snapshot.Filter.ToUserId,snapshot.Filter.Reason,item.TaskVersion)},PersonalFormOwnerV2.Json);
            try
            {
                var result=item.ContextKind=="SFS"?await forms.AdministrativeTransferTaskAsync(item.TaskId,snapshot.Filter.FromUserId,snapshot.BatchId,raw,ct)
                    :await app.AdministrativeTransferTaskAsync(item.TaskId,snapshot.Filter.FromUserId,snapshot.BatchId,raw,ct);
                results.Add(new(item.TaskId,item.ItemCommandId,"SUCCEEDED",null,result));
            }
            catch(ScopeAuthorizationExceptionV1 e)
            {
                var outcome=e.Status>=500?"UNKNOWN":"REJECTED";
                if(outcome=="REJECTED")await ObserveRejection(snapshot.BatchId,item.TaskId,e.Message,ct);
                results.Add(new(item.TaskId,item.ItemCommandId,outcome,e.Message,null));
            }
            catch(Exception e)when(e is Npgsql.NpgsqlException or DbUpdateException or CurrentPermissionUnavailableException)
            {db.ChangeTracker.Clear();results.Add(new(item.TaskId,item.ItemCommandId,"UNKNOWN","TASK_STORAGE_UNAVAILABLE",null));}
        }
        return new(snapshot.BatchId,results.ToArray());
    }
    private async Task ObserveRejection(Guid batch,Guid task,string code,CancellationToken ct)
    {
        // An observation is not a success receipt. Original Owner commands always win
        // when another request committed before this independently recorded rejection.
        await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await fence.AcquireAsync(ct);var actor=await Manager(ct);
        if(await StoredBatch(actor.UserId,batch,null,ct)==null)throw Error("RESOURCE_NOT_FOUND",404);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_native_tasks_v2.transfer_rejections(batch_id,task_id,error_code) VALUES({batch},{task},{code})
            ON CONFLICT(batch_id,task_id) DO UPDATE SET error_code=EXCLUDED.error_code
            """,ct);
        await forms.Final(actor,lease,ct,false);await tx.CommitAsync(ct);
    }
}
