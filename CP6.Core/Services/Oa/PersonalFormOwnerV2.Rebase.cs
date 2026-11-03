using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class PersonalFormOwnerV2
{
    private sealed record PreviewRow(Guid Actor,Guid Draft,Guid Token,Guid Source,Guid Target,string Digest,string Result,DateTimeOffset Expires);
    private async Task<(FormPublicationV2 Target,PersonalFormRebasePlanV2 Plan,string Digest,Dictionary<string,string> Mask)> RebasePlan(DraftRow row,Guid targetId,UserPermissionContext actor,CancellationToken ct)
    {
        var latest=await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_form_authoring_v2.versions WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={row.Head}
            ORDER BY version_number DESC LIMIT 1
            """).SingleAsync(ct);
        if(targetId!=latest || targetId==row.Version)throw Error("REBASE_TARGET_CHANGED",409);
        var source=await Publication(row.Head,row.Version,ct);var target=await Publication(row.Head,targetId,ct);
        var sourceMask=await Mask(actor,row.Head,source,ct);var targetMask=await Mask(actor,row.Head,target,ct);
        var plan=PersonalFormRebaseV2.Build(source.Fields["SchemaJson"]!,target.Fields["SchemaJson"]!,row.Data,sourceMask,targetMask);
        try{await CheckOptions(PersonalFormValuesV2.Schema(target.Fields["SchemaJson"]!),plan.Data,null,true,ct);}
        catch(ScopeAuthorizationExceptionV1 e) when(e.Status==422){plan=plan with{Errors=[..plan.Errors,e.Message]};}
        var digest=MainOwnerOperationJournalV1.Digest(MainOwnerOperationJournalV1.Encode(new{row.Id,row.Token,Source=row.Version,Target=targetId,source.SchemaDigest,TargetDigest=target.SchemaDigest,Plan=plan,SourceMask=sourceMask,TargetMask=targetMask}));
        return(target,plan,digest,targetMask);
    }
    public async Task<PersonalFormRebasePreviewV2> RebasePreviewAsync(Guid draft,PersonalFormRebasePreviewInputV2 input,CancellationToken ct=default)
    {
        if(input.TargetVersionId==Guid.Empty || !Guid.TryParseExact(input.ExpectedVersion,"D",out _))throw Error("REBASE_INPUT_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await WriteReady(lease,ct);await Action(actor,"edit",ct);var row=await Owned(draft,actor.UserId,ct);Active(row);Expected(row,input.ExpectedVersion);
            var next=await RebasePlan(row,input.TargetVersionId,actor,ct);var id=Guid.NewGuid();var expires=Clock().AddMinutes(10);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_personal_form_v2.previews(id,tenant_id,environment_id,actor_id,draft_id,draft_version,source_version,target_version,digest,result_json,expires_at)
                VALUES({id},{db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{draft},{row.Token},{row.Version},{input.TargetVersionId},{next.Digest},{Encode(next.Plan)},{expires})
                """,ct);
            await Final(actor,lease,ct);committing=true;await tx.CommitAsync(ct);
            return new(id,next.Digest,row.Version,input.TargetVersionId,row.Token.ToString("D"),expires,next.Plan.Changes,next.Plan.Errors,next.Plan.Loss);
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
    public async Task<PersonalFormResultV2> RebaseDraftAsync(Guid draft,byte[] raw,CancellationToken ct=default)
    {
        var command=CommandInput<PersonalFormRebaseInputV2>(raw,true);var body=command.Body;var resource=draft.ToString("D");
        if(body.PreviewId==Guid.Empty || body.TargetVersionId==Guid.Empty || body.PreviewDigest?.Length!=64)throw Error("REBASE_INPUT_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await Action(actor,"query",ct);
            var old=await Original(actor.UserId,Rebase,resource,command.Id,ct);
            if(old!=null){Match(old,command.Digest);var saved=await Disclose(old,actor,ct);await Final(actor,lease,ct,false);return saved;}
            await WriteReady(lease,ct);await Action(actor,"edit",ct);var row=await Owned(draft,actor.UserId,ct);Active(row);Expected(row,command.Expected);
            var preview=await db.Database.SqlQuery<PreviewRow>($"""
                SELECT actor_id AS "Actor",draft_id AS "Draft",draft_version AS "Token",source_version AS "Source",target_version AS "Target",digest AS "Digest",result_json AS "Result",expires_at AS "Expires"
                FROM cp6_personal_form_v2.previews WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND id={body.PreviewId}
                """).SingleOrDefaultAsync(ct)??throw Error("REBASE_PREVIEW_NOT_FOUND",404);
            if(preview.Actor!=actor.UserId || preview.Draft!=draft)throw Error("REBASE_PREVIEW_NOT_FOUND",404);
            if(preview.Expires<=Clock() || preview.Source!=row.Version || preview.Target!=body.TargetVersionId || preview.Token!=row.Token || preview.Digest!=body.PreviewDigest)throw Error("REBASE_PREVIEW_CHANGED",409);
            var next=await RebasePlan(row,body.TargetVersionId,actor,ct);
            if(next.Digest!=preview.Digest)throw Error("REBASE_PREVIEW_CHANGED",409);
            if(next.Plan.Loss && !body.ConfirmRemovedValues)throw Error("REBASE_CONFIRMATION_REQUIRED",422);
            await CheckFiles(row.Head,body.TargetVersionId,"DRAFT",draft,actor.UserId,PersonalFormValuesV2.Schema(next.Target.Fields["SchemaJson"]!),next.Plan.Data,row.Data,false,next.Mask,ct);
            var write=Guid.NewGuid();await BeginCommand(write,actor.UserId,Rebase,resource,command,draft,null,null,null,ct);
            // Preserve every source row even if target MaxRows shrank. Diagnostics block submission;
            // only a later authorized user edit may remove chosen rows.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_personal_form_v2.drafts SET form_version={body.TargetVersionId},rebased_from={row.Version},data_json={next.Plan.Data} WHERE id={draft}
                """,ct);
            await History(write,draft,row.Snapshot,ct);var view=await DraftView(await Owned(draft,actor.UserId,ct),actor,ct);
            await BindFiles(write,body.TargetVersionId,"DRAFT",draft,view.RowVersion,PersonalFormValuesV2.Schema(next.Target.Fields["SchemaJson"]!),next.Plan.Data,ct);
            var result=await Complete(write,command,draft,view.RowVersion,view,new{Scope=lease.Snapshot,actor.UserId,Preview=body.PreviewId,body.ConfirmRemovedValues,Plan=next.Plan},ct);
            await Final(actor,lease,ct);
            if(preview.Expires<=Clock())throw Error("REBASE_PREVIEW_CHANGED",409);
            committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
}
