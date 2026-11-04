using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PersonalFormSnapshotRecoveryV2(Guid HistoryId,string Reason);
public sealed record PersonalFormRecoverySourceV2(Guid HistoryId,string RebaseCommandId,Guid SourceVersionId,int SourceVersion,
    Guid TargetVersionId,int TargetVersion,DateTimeOffset RebasedAtUtc,bool CanRecover,string? BlockedReason);
public sealed record PersonalFormRecoveryPageV2(string ExpectedVersion,PersonalFormRecoverySourceV2[] Items,int Total,int Page,int PageSize);
public sealed record PersonalFormRecoveryPreviewV2(string ExpectedVersion,PersonalFormRecoverySourceV2 Source,PersonalFormDraftV2 Snapshot);

public sealed partial class PersonalFormOwnerV2
{
    internal const string RecoverSnapshot="FORM_DRAFT_RECOVER_SNAPSHOT";
    private sealed record RecoveryRow(Guid Id,string Command,Guid Version,Guid Target,Guid Token,string Data,string? Title,DateTimeOffset BeforeUpdated,DateTimeOffset Recorded);
    private IQueryable<RecoveryRow> RecoverySources(Guid draft)=>db.Database.SqlQuery<RecoveryRow>($"""
        SELECT h.id AS "Id",c.command_id AS "Command",(h.before_json::jsonb->>'form_version')::uuid AS "Version",
          (h.after_json::jsonb->>'form_version')::uuid AS "Target",(h.before_json::jsonb->>'row_version')::uuid AS "Token",
          h.before_json::jsonb->>'data_json' AS "Data",h.before_json::jsonb->>'title' AS "Title",
          (h.before_json::jsonb->>'updated_at')::timestamptz AS "BeforeUpdated",c.recorded_at AS "Recorded"
        FROM cp6_personal_form_v2.history h JOIN cp6_personal_form_v2.commands c ON c.id=h.command_id
        WHERE h.draft_id={draft} AND c.tenant_id={db.CurrentTenantId} AND c.environment_id={fence.EnvironmentId}
          AND c.operation='FORM_DRAFT_REBASE' AND c.completed AND NOT c.closed AND h.before_json IS NOT NULL
        """);
    private string? RecoveryBlocked(DraftRow row,RecoveryRow source,PersonalFormPolicyV2 policy,UserPermissionContext actor)
    {
        if(!actor.ActionKeys.Contains("oa-form-catalog:add")||!actor.ActionKeys.Contains("oa-form-catalog:edit"))return "FORM_RECOVERY_FORBIDDEN";
        if(!policy.Enabled||!policy.AllowRestore)return "FORM_RECOVERY_DISABLED";
        if(source.Recorded.AddDays(policy.RetentionDays)<=Clock() || row.Status==2&&(row.RetainUntil<=Clock()||row.RetentionRef!=policy.RetentionPolicyRef))return "FORM_RECOVERY_RETENTION_EXPIRED";
        return null;
    }
    private async Task<PersonalFormRecoverySourceV2> RecoverySourceView(DraftRow row,RecoveryRow source,PersonalFormPolicyV2 policy,UserPermissionContext actor,CancellationToken ct)
    {
        var before=await Publication(row.Head,source.Version,ct);var after=await Publication(row.Head,source.Target,ct);
        var blocked=RecoveryBlocked(row,source,policy,actor);
        return new(source.Id,source.Command,source.Version,before.Version,source.Target,after.Version,source.Recorded,blocked==null,blocked);
    }
    public async Task<PersonalFormRecoveryPageV2> RebaseHistoryAsync(Guid draft,int page=1,int pageSize=20,CancellationToken ct=default)
    {
        if(page is <1 or >100000||pageSize is <1 or >100)throw Error("PAGE_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);
        var actor=await Actor(ct);await Action(actor,"query",ct);var row=await Owned(draft,actor.UserId,ct);
        await Mask(actor,row.Head,await Publication(row.Head,row.Version,ct),ct);var policy=(await Policy(row.Head,ct))!;
        var query=RecoverySources(draft);var total=await query.CountAsync(ct);
        var sources=await query.OrderByDescending(x=>x.Recorded).ThenByDescending(x=>x.Id).Skip((page-1)*pageSize).Take(pageSize).ToArrayAsync(ct);
        var items=new List<PersonalFormRecoverySourceV2>();foreach(var source in sources)items.Add(await RecoverySourceView(row,source,policy,actor,ct));
        await Final(actor,lease,ct,false);return new(row.Token.ToString("D"),items.ToArray(),total,page,pageSize);
    }
    public async Task<PersonalFormRecoveryPreviewV2> RecoveryPreviewAsync(Guid draft,Guid history,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);
        var actor=await Actor(ct);await Action(actor,"query",ct);var row=await Owned(draft,actor.UserId,ct);
        var source=await RecoverySources(draft).SingleOrDefaultAsync(x=>x.Id==history,ct)??throw Error("FORM_RECOVERY_SOURCE_NOT_FOUND",404);
        var snapshot=await DraftView(row with{Version=source.Version,Token=source.Token,Data=source.Data,Title=source.Title,Updated=source.BeforeUpdated,Status=0,FormData=null},actor,ct);
        var policy=(await Policy(row.Head,ct))!;var view=await RecoverySourceView(row,source,policy,actor,ct);
        await Final(actor,lease,ct,false);return new(row.Token.ToString("D"),view,snapshot with{Capabilities=[]});
    }
    public async Task<PersonalFormResultV2> RecoverSnapshotAsync(Guid draft,byte[] raw,CancellationToken ct=default)
    {
        var command=CommandInput<PersonalFormSnapshotRecoveryV2>(raw,true);var body=command.Body;var resource=draft.ToString("D");
        if(body.HistoryId==Guid.Empty||string.IsNullOrWhiteSpace(body.Reason)||body.Reason.Length>500)throw Error("FORM_RECOVERY_INPUT_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await Action(actor,"query",ct);
            var old=await Original(actor.UserId,RecoverSnapshot,resource,command.Id,ct);
            if(old!=null){Match(old,command.Digest);var saved=await Disclose(old,actor,ct);await Final(actor,lease,ct,false);return saved;}
            await WriteReady(lease,ct);await Action(actor,"add",ct);await Action(actor,"edit",ct);
            if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_personal_form_v2.recovery_ready() AS \"Value\"").SingleAsync(ct))throw Error("FORM_RECOVERY_WRITER_COVERAGE_UNAVAILABLE");
            var row=await Owned(draft,actor.UserId,ct);Expected(row,command.Expected);
            var source=await RecoverySources(draft).SingleOrDefaultAsync(x=>x.Id==body.HistoryId,ct)??throw Error("FORM_RECOVERY_SOURCE_NOT_FOUND",404);
            var publication=await Publication(row.Head,source.Version,ct);var mask=await Mask(actor,row.Head,publication,ct);var policy=(await Policy(row.Head,ct))!;
            if(RecoveryBlocked(row,source,policy,actor) is {} blocked)throw Error(blocked,409);
            var schema=PersonalFormValuesV2.Schema(publication.Fields["SchemaJson"]!);
            // These are immutable, previously accepted values. Recovery does not recalculate,
            // remap, or let a caller edit hidden fields. Current FILE availability remains authoritative.
            await CheckFiles(row.Head,source.Version,"DRAFT",draft,actor.UserId,schema,source.Data,source.Data,true,mask,ct);
            var id=Guid.NewGuid();var write=Guid.NewGuid();await BeginCommand(write,actor.UserId,RecoverSnapshot,resource,command,id,null,null,null,ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_personal_form_v2.drafts(id,tenant_id,environment_id,owner_id,head_id,form_version,data_json,title)
                VALUES({id},{db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{row.Head},{source.Version},{source.Data},{source.Title});
                INSERT INTO cp6_personal_form_v2.draft_recoveries(draft_id,source_draft_id,source_history_id,command_id,tenant_id,environment_id,owner_id,policy_version,policy_ref,recover_until)
                VALUES({id},{draft},{source.Id},{write},{db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{policy.Version},{policy.RetentionPolicyRef},{source.Recorded.AddDays(policy.RetentionDays)})
                """,ct);
            await History(write,id,null,ct);var view=await DraftView(await Owned(id,actor.UserId,ct),actor,ct);
            await BindFiles(write,source.Version,"DRAFT",id,view.RowVersion,schema,source.Data,ct);
            var result=await Complete(write,command,id,view.RowVersion,view,new{Scope=lease.Snapshot,actor.UserId,SourceDraft=draft,SourceHistory=source.Id,
                SourceRebaseCommand=source.Command,SourceVersion=source.Version,SourceDraftVersion=source.Token,Policy=policy.Version,policy.RetentionPolicyRef,body.Reason},ct);
            await Final(actor,lease,ct);
            if(RecoveryBlocked(row,source,policy,actor) is {} expired)throw Error(expired,409);
            committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
}
