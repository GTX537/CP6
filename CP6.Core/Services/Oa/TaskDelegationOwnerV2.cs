using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed class TaskDelegationOwnerV2(CP6Context db,ScopeAuthorizationFenceV1 fence,PersonalFormOwnerV2 forms)
{
    private static readonly JsonSerializerOptions Json=PersonalFormOwnerV2.Json;
    private TaskDelegationReaderV2 Reader=>new(db,fence.EnvironmentId);
    private static ScopeAuthorizationExceptionV1 Error(string code,int status=403)=>new(code,status);
    private static string Encode(object value)=>JsonSerializer.Serialize(value,Json);
    private static string Query(string operation,string resource,string key)=>$"/api/oa/v2/delegations/commands/{operation}/{resource}/{key}";
    private sealed record Saved(string? Digest,string Result,bool Closed);
    private Task<Saved?> Original(Guid actor,string operation,string resource,string key,CancellationToken ct)=>db.Database.SqlQuery<Saved>($"""
        SELECT digest AS "Digest",result_json AS "Result",closed AS "Closed" FROM cp6_task_delegation_v2.commands
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND actor_id={actor}
          AND operation={operation} AND resource={resource} AND command_key={key}
        """).SingleOrDefaultAsync(ct);
    private async Task<UserPermissionContext> Actor(Guid? behalf,CancellationToken ct)
    {
        var actor=await forms.Actor(ct);
        if(behalf.HasValue&&behalf!=actor.UserId)throw Error("GRANT_REDELEGATION_FORBIDDEN");
        await forms.Action(actor,"query",ct,"oa-settings");return actor;
    }
    private async Task WriteReady(ScopeAuthorizationLeaseV1 lease,CancellationToken ct)
    {
        if(!lease.Snapshot.Enabled||lease.Snapshot.WriterProfile!=ScopeAuthorizationFenceV1.NativeWriterProfile)throw Error("SCOPE_MAPPING_DISABLED",503);
        await Reader.Ready(ct);
    }
    public async Task<TaskDelegationPrincipalV2[]> ReceivedAsync(CancellationToken ct=default)
    {
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await forms.ReadLease(ct);var actor=await forms.Actor(ct);
        await forms.Action(actor,"query",ct,"oa-inbox");await Reader.Ready(ct);var now=await Reader.Now(ct);
        var grants=(await Reader.Current(ct)).Where(g=>g.DelegateId==actor.UserId&&g.Enabled&&g.FromUtc<=now&&now<g.ToUtc).ToArray();
        var ids=grants.Select(g=>g.GrantorId).Distinct().ToArray();var users=await db.Sys_Users.AsNoTracking()
            .Where(u=>ids.Contains(u.Id)&&u.Enable&&(u.LockedUntil==null||u.LockedUntil<=DateTime.Now)).ToArrayAsync(ct);
        var result=new List<TaskDelegationPrincipalV2>();
        foreach(var user in users)
        {
            var permission=await PostgreSqlPermissionReader.ReadAsync(db,user.Id,ct);
            if(permission.MenuKeys.Contains("oa-inbox")&&permission.ActionKeys.Contains("oa-inbox:query"))
                result.Add(new(user.Id,string.IsNullOrWhiteSpace(user.NickName)?user.UserName:user.NickName,grants.Where(g=>g.GrantorId==user.Id).ToArray()));
        }
        await forms.Final(actor,lease,ct,false);return result.OrderBy(x=>x.Name,StringComparer.Ordinal).ThenBy(x=>x.UserId).ToArray();
    }
    public async Task<TaskDelegationTargetV2[]> TargetsAsync(string search,Guid? behalf=null,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(search)||search.Length>100)throw Error("GRANT_TARGET_SEARCH_REQUIRED",400);
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await forms.ReadLease(ct);var actor=await Actor(behalf,ct);
        await forms.Action(actor,"delegate",ct,"oa-settings");
        var users=await db.Sys_Users.AsNoTracking().Where(u=>u.Id!=actor.UserId&&u.Enable&&(u.LockedUntil==null||u.LockedUntil<=DateTime.Now)
            &&(u.UserName.Contains(search)||u.NickName!=null&&u.NickName.Contains(search))).OrderBy(u=>u.UserName).ThenBy(u=>u.Id).Take(20)
            .Select(u=>new TaskDelegationTargetV2(u.Id,string.IsNullOrWhiteSpace(u.NickName)?u.UserName:u.NickName!)).ToArrayAsync(ct);
        await forms.Final(actor,lease,ct,false);return users;
    }
    public async Task<TaskDelegationListV2> ListAsync(Guid? behalf=null,CancellationToken ct=default)
    {
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await forms.ReadLease(ct);var actor=await Actor(behalf,ct);
        await Reader.Ready(ct);var grants=await Reader.Current(ct);
        var result=new TaskDelegationListV2(grants.Where(g=>g.GrantorId==actor.UserId).OrderBy(g=>g.GrantId).ToArray(),
            grants.Where(g=>g.DelegateId==actor.UserId).OrderBy(g=>g.GrantId).ToArray(),actor.UserId,await Reader.Now(ct));
        await forms.Final(actor,lease,ct,false);return result;
    }
    public async Task<TaskDelegationRevisionV2[]> HistoryAsync(Guid grant,Guid? behalf=null,CancellationToken ct=default)
    {
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await forms.ReadLease(ct);var actor=await Actor(behalf,ct);
        var current=(await Reader.Current(ct)).SingleOrDefault(g=>g.GrantId==grant);
        if(current==null||current.GrantorId!=actor.UserId&&current.DelegateId!=actor.UserId)throw Error("RESOURCE_NOT_FOUND",404);
        var rows=await db.Database.SqlQuery<string>($"""
            SELECT body AS "Value" FROM cp6_task_delegation_v2.revisions WHERE tenant_id={db.CurrentTenantId}
              AND environment_id={fence.EnvironmentId} AND grant_id={grant} ORDER BY revision
            """).ToArrayAsync(ct);
        await forms.Final(actor,lease,ct,false);return rows.Select(x=>JsonSerializer.Deserialize<TaskDelegationRevisionV2>(x,Json)!)
            .Where(g=>g.GrantorId==actor.UserId||g.DelegateId==actor.UserId).ToArray();
    }
    private async Task Input(TaskDelegationInputV2 input,Guid actor,Guid? exclude,CancellationToken ct)
    {
        TaskDelegationReaderV2.Validate(input.Scope);
        if(input.DelegateId==Guid.Empty||input.DelegateId==actor||input.ValidFromUtc.Offset!=TimeSpan.Zero||input.ValidToUtc.Offset!=TimeSpan.Zero
            ||input.ValidToUtc<=input.ValidFromUtc||input.Remark==null||input.Remark.EnumerateRunes().Count()>1000)throw Error("GRANT_INPUT_INVALID",400);
        if(!await db.Sys_Users.AsNoTracking().AnyAsync(u=>u.Id==input.DelegateId&&u.Enable&&(u.LockedUntil==null||u.LockedUntil<=DateTime.Now),ct))throw Error("GRANT_TARGET_UNAVAILABLE",422);
        if(input.Scope.FlowKeys is {} flows)
        {
            var known=await db.Database.SqlQuery<string>($"""
                SELECT flow_key AS "Value" FROM cp6_workflow_authoring_v2.heads WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
                """).ToArrayAsync(ct);
            if(flows.Except(known,StringComparer.Ordinal).Any())throw Error("GRANT_SCOPE_KEY_UNREGISTERED",422);
        }
        if(input.Scope.BizTypes is {} types)
        {
            var known=await db.Database.SqlQuery<string>($"""
                SELECT DISTINCT biz_type AS "Value" FROM cp6_approval_binding_v2.registrations WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
                """).ToArrayAsync(ct);
            if(types.Except(known,StringComparer.Ordinal).Any())throw Error("GRANT_SCOPE_KEY_UNREGISTERED",422);
        }
        // Explicit current actors grant only their own responsibility; overlapping chains/cycles
        // cannot turn an incoming grant into authority to grant again.
        if((await Reader.Current(ct)).Any(g=>g.GrantId!=exclude&&g.Enabled&&g.FromUtc<input.ValidToUtc&&input.ValidFromUtc<g.ToUtc
            &&(g.DelegateId==actor||g.GrantorId==input.DelegateId)&&TaskDelegationReaderV2.Overlaps(g.Scope,input.Scope)))throw Error("GRANT_REDELEGATION_FORBIDDEN");
    }
    private static void Scope(string operation,string resource,string key)
    {
        if(operation is not("CREATE" or "REVISE" or "REVOKE")||!Regex.IsMatch(key,"\\A[A-Za-z0-9_.-]{1,100}\\z")
            ||operation=="CREATE"&&resource!="new"||operation!="CREATE"&&(!Guid.TryParseExact(resource,"D",out var id)||id==Guid.Empty))throw Error("COMMAND_SCOPE_INVALID",400);
    }
    public async Task<TaskDelegationCommandV2> WriteAsync(string operation,Guid? grant,byte[] raw,Guid? behalf=null,CancellationToken ct=default)
    {
        var command=PersonalFormOwnerV2.CommandInput<JsonElement>(raw,operation!="CREATE");var resource=grant?.ToString("D")??"new";Scope(operation,resource,command.Id);
        TaskDelegationInputV2? input=null;TaskDelegationRevokeV2? revoke=null;
        try
        {
            var required=operation=="REVOKE"?new[]{"reason"}:new[]{"delegateId","validFromUtc","validToUtc","scope","remark"};
            if(command.Body.ValueKind!=JsonValueKind.Object||required.Any(k=>!command.Body.TryGetProperty(k,out _)))throw new JsonException();
            if(operation!="REVOKE"&&new[]{"validFromUtc","validToUtc"}.Any(k=>command.Body.GetProperty(k).ValueKind!=JsonValueKind.String
                ||!(command.Body.GetProperty(k).GetString()!.EndsWith('Z')||command.Body.GetProperty(k).GetString()!.EndsWith("+00:00",StringComparison.Ordinal))))throw new JsonException();
            if(operation=="REVOKE")revoke=command.Body.Deserialize<TaskDelegationRevokeV2>(Json)??throw new JsonException();
            else input=command.Body.Deserialize<TaskDelegationInputV2>(Json)??throw new JsonException();
        }
        catch(JsonException){throw Error("GRANT_INPUT_INVALID",400);}
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await forms.ReadLease(ct);var actor=await Actor(behalf,ct);var old=await Original(actor.UserId,operation,resource,command.Id,ct);
            if(old!=null)
            {
                if(old.Closed)throw Error("COMMAND_CLOSED",409);if(old.Digest!=command.Digest)throw Error("COMMAND_BODY_CONFLICT",409);
                await forms.Final(actor,lease,ct,false);return JsonSerializer.Deserialize<TaskDelegationCommandV2>(old.Result,Json)!;
            }
            await forms.Action(actor,"delegate",ct,"oa-settings");await WriteReady(lease,ct);
            var current=grant.HasValue?(await Reader.Current(ct)).SingleOrDefault(g=>g.GrantId==grant):null;
            if(grant.HasValue&&(current==null||current.GrantorId!=actor.UserId))throw Error("RESOURCE_NOT_FOUND",404);
            if(current!=null&&current.Version.ToString("D")!=command.Expected)throw Error("GRANT_VERSION_CONFLICT",409);
            if(input!=null)await Input(input,actor.UserId,grant,ct);
            else if(string.IsNullOrWhiteSpace(revoke!.Reason)||revoke.Reason.EnumerateRunes().Count()>1000)throw Error("GRANT_REASON_REQUIRED",400);
            var at=await Reader.Now(ct);var id=grant??Guid.NewGuid();var revision=current==null?1:checked(TaskDelegationReaderV2.Revision(current.Revision)+1);
            var value=new TaskDelegationRevisionV2(id,revision.ToString(CultureInfo.InvariantCulture),Guid.NewGuid(),actor.UserId,
                input?.DelegateId??current!.DelegateId,input?.ValidFromUtc??current!.FromUtc,input?.ValidToUtc??current!.ToUtc,input?.Scope??current!.Scope,
                operation!="REVOKE",checked(lease.Snapshot.Epoch+1).ToString(CultureInfo.InvariantCulture),current?.GrantedAtUtc??at,current?.GrantedBy??actor.UserId,
                operation=="REVOKE"?at:null,operation=="REVOKE"?actor.UserId:null,input?.Remark??revoke!.Reason,$"/api/oa/v2/delegations/{id}/history");
            var result=new TaskDelegationCommandV2(command.Id,"SUCCEEDED",value,Query(operation,resource,command.Id));var commandId=Guid.NewGuid();
            await forms.Final(actor,lease,ct,false);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_task_delegation_v2.commands(id,tenant_id,environment_id,actor_id,operation,resource,command_key,digest,raw_body,grant_id,result_json,closed)
                VALUES({commandId},{db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{operation},{resource},{command.Id},{command.Digest},{raw},{id},{Encode(result)},false);
                INSERT INTO cp6_task_delegation_v2.revisions(tenant_id,environment_id,grant_id,revision,version,body,command_id)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{id},{revision},{value.Version},{Encode(value)},{commandId})
                """,ct);
            if(current==null)await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_task_delegation_v2.heads(tenant_id,environment_id,id,grantor_id,revision)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{id},{actor.UserId},{revision})
                """,ct);
            else await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_task_delegation_v2.heads SET revision={revision} WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND id={id}
                """,ct);
            // This Owner's head mutation advances the shared epoch exactly once under the root.
            var final=await fence.ReadLocked(ct)??throw Error("SCOPE_MAPPING_REQUIRED",503);
            if(final.Epoch!=lease.Snapshot.Epoch+1||(final with{Epoch=lease.Snapshot.Epoch,RowVersion=lease.Snapshot.RowVersion})!=lease.Snapshot)throw Error("SCOPE_AUTHORITY_CHANGED",409);
            var finalActor=await Actor(behalf,ct);await forms.Action(finalActor,"delegate",ct,"oa-settings");await Reader.Ready(ct);
            committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception error){await forms.Rollback(tx,committing,error);throw;}
    }
    public async Task<TaskDelegationCommandV2> OriginalAsync(string operation,string resource,string key,bool close=false,Guid? behalf=null,CancellationToken ct=default)
    {
        Scope(operation,resource,key);if(operation!="CREATE")resource=Guid.Parse(resource).ToString("D");
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await forms.ReadLease(ct);var actor=await Actor(behalf,ct);var old=await Original(actor.UserId,operation,resource,key,ct);
            if(old!=null){await forms.Final(actor,lease,ct,false);return JsonSerializer.Deserialize<TaskDelegationCommandV2>(old.Result,Json)!;}
            var result=new TaskDelegationCommandV2(key,close?"CLOSED":"NOT_OBSERVED",null,Query(operation,resource,key));
            if(close)
            {
                await WriteReady(lease,ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_task_delegation_v2.commands(id,tenant_id,environment_id,actor_id,operation,resource,command_key,result_json,closed)
                    VALUES({Guid.NewGuid()},{db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{operation},{resource},{key},{Encode(result)},true)
                    """,ct);
            }
            await forms.Final(actor,lease,ct,false);if(close){committing=true;await tx.CommitAsync(ct);}return result;
        }
        catch(Exception error){await forms.Rollback(tx,committing,error);throw;}
    }
}
