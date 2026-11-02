using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class NativeInboxOwnerV2
{
    private sealed record ReadCommand(Guid Id,string? Digest,string Result,bool Closed);
    private sealed record ReadTarget(Guid InstanceId,Guid PrincipalId);
    private static void Key(string kind,Guid target,string command)
    {
        if(kind is not("TASK" or "CC")||target==Guid.Empty||!Regex.IsMatch(command,"\\A[A-Za-z0-9_.-]{1,100}\\z"))throw Error("COMMAND_SCOPE_INVALID",400);
    }
    private static string Query(string kind,Guid target,string command)=>$"/api/oa/v2/inbox/read-commands/{kind}/{target}/{command}";
    private Task<ReadCommand?> Original(string kind,Guid target,Guid actor,string command,CancellationToken ct)=>db.Database.SqlQuery<ReadCommand>($"""
        SELECT id AS "Id",digest AS "Digest",result_json AS "Result",closed AS "Closed" FROM cp6_workflow_read_v2.commands
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND actor_id={actor}
          AND kind={kind} AND target_id={target} AND command_key={command}
        """).SingleOrDefaultAsync(ct);
    private async Task<ReadTarget> Target(string kind,Guid target,ReadIdentity actor,CancellationToken ct)
    {
        var row=kind=="TASK"
            ?await db.Wf_FlowTasks.Where(t=>t.Id==target&&t.AssigneeId==actor.EffectiveId).Select(t=>new ReadTarget(t.InstanceId,t.AssigneeId)).SingleOrDefaultAsync(ct)
            :await db.Wf_FlowCcs.Where(c=>c.Id==target&&c.RecipientId==actor.EffectiveId).Select(c=>new ReadTarget(c.InstanceId,c.RecipientId)).SingleOrDefaultAsync(ct);
        if(row==null)throw Error("RESOURCE_NOT_FOUND",404);
        await Visible(row.InstanceId,actor,ct);return row;
    }
    private async Task ReadWriter(ScopeAuthorizationLeaseV1 lease,CancellationToken ct)
    {
        if(!lease.Snapshot.Enabled||lease.Snapshot.WriterProfile!=ScopeAuthorizationFenceV1.NativeWriterProfile)throw Error("SCOPE_MAPPING_DISABLED");
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_workflow_read_v2.ready() AS \"Value\"").SingleAsync(ct))throw Error("INBOX_READ_WRITER_UNAVAILABLE");
    }
    public async Task<NativeReadCommandResultV2> MarkReadAsync(string kind,Guid target,byte[] raw,Guid? behalf=null,CancellationToken ct=default)
    {
        var command=PersonalFormOwnerV2.CommandInput<JsonElement>(raw,false);Key(kind,target,command.Id);
        if(command.Body.ValueKind!=JsonValueKind.Object||command.Body.EnumerateObject().Any())throw Error("COMMAND_SHAPE_INVALID",400);
        forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var(actor,lease)=await Begin(behalf,ct);var row=await Target(kind,target,actor,ct);var old=await Original(kind,target,actor.UserId,command.Id,ct);
            if(old!=null)
            {
                if(old.Closed)throw Error("COMMAND_CLOSED",409);
                if(old.Digest!=command.Digest)throw Error("COMMAND_BODY_CONFLICT",409);
                await FinalRead(actor,lease,ct);return Parse<NativeReadCommandResultV2>(old.Result);
            }
            await forms.Action(actor.Actual,"read",ct,"oa-inbox");if(actor.Delegated)await forms.Action(actor.Effective,"read",ct,"oa-inbox");await ReadWriter(lease,ct);
            var fact=(await Reads([row.InstanceId],ct)).SingleOrDefault(r=>r.Kind==kind&&r.TargetId==target);
            var first=fact==null;var id=Guid.NewGuid();
            var readGrant=actor.Delegated?actor.Selected.Values.Single():null;
            fact??=new(kind,target,row.PrincipalId,actor.UserId,
                await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct),command.Id,readGrant?.GrantId,readGrant?.Revision);
            var result=new NativeReadCommandResultV2(command.Id,"READ",fact,Query(kind,target,command.Id));
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_workflow_read_v2.commands(id,tenant_id,environment_id,actor_id,kind,target_id,command_key,digest,raw_body,result_json,closed)
                VALUES({id},{db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{kind},{target},{command.Id},{command.Digest},{raw},{JsonSerializer.Serialize(result,Json)},false)
                """,ct);
            if(first)await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_workflow_read_v2.facts(tenant_id,environment_id,instance_id,kind,target_id,principal_id,actual_id,first_read_at,first_command_key,command_id,grant_id,grant_revision)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{row.InstanceId},{kind},{target},{row.PrincipalId},{actor.UserId},{fact.FirstReadAtUtc},{command.Id},{id},{fact.GrantId},{(fact.GrantRevision==null?(long?)null:TaskDelegationReaderV2.Revision(fact.GrantRevision))})
                """,ct);
            await FinalRead(actor,lease,ct);committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception e){await forms.Rollback(tx,committing,e);throw;}
    }
    public async Task<NativeReadCommandResultV2> ReadOriginalAsync(string kind,Guid target,string command,Guid? behalf=null,CancellationToken ct=default)
    {
        Key(kind,target,command);forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var(actor,lease)=await Begin(behalf,ct);
        await Target(kind,target,actor,ct);var old=await Original(kind,target,actor.UserId,command,ct);
        var result=old==null?new(command,"NOT_OBSERVED",null,Query(kind,target,command)):Parse<NativeReadCommandResultV2>(old.Result);
        await FinalRead(actor,lease,ct);return result;
    }
    public async Task<NativeReadCommandResultV2> CloseReadAsync(string kind,Guid target,string command,Guid? behalf=null,CancellationToken ct=default)
    {
        Key(kind,target,command);forms.Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var(actor,lease)=await Begin(behalf,ct);await Target(kind,target,actor,ct);var old=await Original(kind,target,actor.UserId,command,ct);
            if(old!=null){await FinalRead(actor,lease,ct);return Parse<NativeReadCommandResultV2>(old.Result);}
            await ReadWriter(lease,ct);var result=new NativeReadCommandResultV2(command,"CLOSED",null,Query(kind,target,command));
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_workflow_read_v2.commands(id,tenant_id,environment_id,actor_id,kind,target_id,command_key,digest,raw_body,result_json,closed)
                VALUES({Guid.NewGuid()},{db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{kind},{target},{command},NULL,NULL,{JsonSerializer.Serialize(result,Json)},true)
                """,ct);
            await FinalRead(actor,lease,ct);committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception e){await forms.Rollback(tx,committing,e);throw;}
    }
}
