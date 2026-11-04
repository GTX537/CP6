using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class PersonalFormOwnerV2
{
    private sealed record PreviewRow(Guid Actor,Guid Draft,Guid Token,Guid Source,Guid Target,string Digest,string Result,DateTimeOffset Expires);
    private sealed record RebasePlanResult(FormPublicationV2 Target,PersonalFormRebasePlanV2? Plan,string? Digest,
        Dictionary<string,string> Mask,FormDispositionRebaseV1? Authority,Guid? RequiredRequest=null);
    private FormFieldDispositionOwnerV1 DispositionOwner()=>new(db,this,fence);
    private async Task<RebasePlanResult> RebasePlan(DraftRow row,Guid targetId,FormDispositionRefV1? reference,
        UserPermissionContext actor,ScopeAuthorizationLeaseV1 lease,bool createRequest,CancellationToken ct)
    {
        var latest=await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_form_authoring_v2.versions WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={row.Head}
            ORDER BY version_number DESC LIMIT 1
            """).SingleAsync(ct);
        if(targetId!=latest || targetId==row.Version)throw Error("REBASE_TARGET_CHANGED",409);
        var source=await Publication(row.Head,row.Version,ct);var target=await Publication(row.Head,targetId,ct);
        var policy=await Policy(row.Head,ct)??throw Error("FORM_USE_POLICY_REQUIRED",403);
        var sourceMask=Mask(actor,source,policy);var targetMask=Mask(actor,target,policy);
        var authority=reference==null?null:await DispositionOwner().ForRebase(reference,row.Id,row.Token,targetId,actor,policy,ct);
        PersonalFormRebasePlanV2 plan;
        try { plan=PersonalFormRebaseV2.Build(source.Fields["SchemaJson"]!,target.Fields["SchemaJson"]!,row.Data,sourceMask,targetMask,disposition:authority); }
        catch(ScopeAuthorizationExceptionV1 e) when(createRequest&&e.Message=="REBASE_HIDDEN_LOSS_BLOCKED")
        {
            if(!policy.Enabled)throw Error("FORM_USE_POLICY_DISABLED",403);
            var scope=await DispositionScope(row,source,target,policy,ct);
            var request=await DispositionOwner().RequiredForRebase(scope,actor,lease,ct);
            return new(target,null,null,targetMask,null,request);
        }
        try{await CheckOptions(PersonalFormValuesV2.Schema(target.Fields["SchemaJson"]!),plan.Data,null,true,ct);}
        catch(ScopeAuthorizationExceptionV1 e) when(e.Status==422){plan=plan with{Errors=[..plan.Errors,e.Message]};}
        var digest=MainOwnerOperationJournalV1.Digest(MainOwnerOperationJournalV1.Encode(new{row.Id,row.Token,Source=row.Version,Target=targetId,source.SchemaDigest,TargetDigest=target.SchemaDigest,Plan=plan,SourceMask=sourceMask,TargetMask=targetMask,
            Disposition=authority?.Reference,Processor=authority?.Processor,RetainUntil=authority?.RetainUntil}));
        return new(target,plan,digest,targetMask,authority);
    }
    public async Task<PersonalFormRebasePreviewV2> RebasePreviewAsync(Guid draft,PersonalFormRebasePreviewInputV2 input,CancellationToken ct=default)
    {
        if(input.TargetVersionId==Guid.Empty || !Guid.TryParseExact(input.ExpectedVersion,"D",out _))throw Error("REBASE_INPUT_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        Guid? requiredRequest=null;PersonalFormRebasePreviewV2? result=null;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await WriteReady(lease,ct);await Action(actor,"edit",ct);var row=await Owned(draft,actor.UserId,ct);Active(row);Expected(row,input.ExpectedVersion);
            var next=await RebasePlan(row,input.TargetVersionId,input.DispositionRef,actor,lease,true,ct);
            requiredRequest=next.RequiredRequest;
            if(requiredRequest==null)
            {
                var id=Guid.NewGuid();var expires=Clock().AddMinutes(10);var plan=next.Plan!;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_personal_form_v2.previews(id,tenant_id,environment_id,actor_id,draft_id,draft_version,source_version,target_version,digest,result_json,expires_at)
                VALUES({id},{db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{draft},{row.Token},{row.Version},{input.TargetVersionId},{next.Digest},{Encode(plan)},{expires})
                """,ct);
                result=new(id,next.Digest!,row.Version,input.TargetVersionId,row.Token.ToString("D"),expires,plan.Changes,plan.Errors,plan.Loss,
                    row.Token.ToString("D"),next.Target.SchemaDigest!,plan.RetainedPaths,plan.RemovedPaths,plan.AddedPaths,
                    plan.TypeChangedPaths,plan.DefaultedPaths,plan.RequiredAfterRebase,plan.Diagnostics,next.Authority?.Reference);
            }
            await Final(actor,lease,ct);committing=true;await tx.CommitAsync(ct);
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
        // The request is committed, not a failed or unknown write. No hidden path/value leaves this boundary.
        if(requiredRequest is {} requestId)throw new PersonalFormDispositionRequiredV2(requestId);
        return result!;
    }
    public Task<PersonalFormResultV2> RebaseDraftAsync(Guid draft,byte[] raw,CancellationToken ct=default)
        =>RebaseDraft(draft,CommandInput<PersonalFormRebaseInputV2>(raw,true),ct);
    private async Task<PersonalFormResultV2> RebaseDraft(Guid draft,Command<PersonalFormRebaseInputV2> command,CancellationToken ct)
    {
        var body=command.Body;var resource=draft.ToString("D");
        if(body.PreviewId==Guid.Empty || body.TargetVersionId==Guid.Empty || body.PreviewDigest?.Length!=64)throw Error("REBASE_INPUT_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await Action(actor,"query",ct);
            var old=await Original(actor.UserId,Rebase,resource,command.Id,ct);
            if(old!=null){await MatchLegacy(old,command,ct);var saved=await Disclose(old,actor,ct);await Final(actor,lease,ct,false);return saved;}
            await WriteReady(lease,ct);await Action(actor,"edit",ct);var row=await Owned(draft,actor.UserId,ct);Active(row);Expected(row,command.Expected);
            var preview=await db.Database.SqlQuery<PreviewRow>($"""
                SELECT actor_id AS "Actor",draft_id AS "Draft",draft_version AS "Token",source_version AS "Source",target_version AS "Target",digest AS "Digest",result_json AS "Result",expires_at AS "Expires"
                FROM cp6_personal_form_v2.previews WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND id={body.PreviewId}
                """).SingleOrDefaultAsync(ct)??throw Error("REBASE_PREVIEW_NOT_FOUND",404);
            if(preview.Actor!=actor.UserId || preview.Draft!=draft)throw Error("REBASE_PREVIEW_NOT_FOUND",404);
            if(preview.Expires<=Clock() || preview.Source!=row.Version || preview.Target!=body.TargetVersionId || preview.Token!=row.Token || preview.Digest!=body.PreviewDigest)throw Error("REBASE_PREVIEW_CHANGED",409);
            var next=await RebasePlan(row,body.TargetVersionId,body.DispositionRef,actor,lease,false,ct);
            if(command.Legacy?.TargetVersion is int targetVersion && targetVersion!=next.Target.Version)throw Error("REBASE_TARGET_CHANGED",409);
            if(next.Digest!=preview.Digest)throw Error("REBASE_PREVIEW_CHANGED",409);
            var plan=next.Plan!;
            if(plan.Loss && !body.ConfirmRemovedValues)throw Error("REBASE_CONFIRMATION_REQUIRED",422);
            await CheckFiles(row.Head,body.TargetVersionId,"DRAFT",draft,actor.UserId,PersonalFormValuesV2.Schema(next.Target.Fields["SchemaJson"]!),plan.Data,row.Data,false,next.Mask,ct);
            var write=Guid.NewGuid();await BeginCommand(write,actor.UserId,Rebase,resource,command,draft,null,null,null,ct);
            // Preserve every source row even if target MaxRows shrank. Diagnostics block submission;
            // only a later authorized user edit may remove chosen rows.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_personal_form_v2.drafts SET form_version={body.TargetVersionId},rebased_from={row.Version},data_json={plan.Data} WHERE id={draft}
                """,ct);
            await History(write,draft,row.Snapshot,ct);var view=await DraftView(await Owned(draft,actor.UserId,ct),actor,ct);
            await BindFiles(write,body.TargetVersionId,"DRAFT",draft,view.RowVersion,PersonalFormValuesV2.Schema(next.Target.Fields["SchemaJson"]!),plan.Data,ct);
            if(next.Authority is {} authority)
                await DispositionOwner().Consume(authority,write,command.Id,actor.UserId,Guid.Parse(view.RowVersion),ct);
            var result=await Complete(write,command,draft,view.RowVersion,view,new{Scope=lease.Snapshot,actor.UserId,Preview=body.PreviewId,body.ConfirmRemovedValues,
                Disposition=next.Authority?.Reference,Processor=next.Authority?.Processor,plan.Changes,plan.Errors,plan.Diagnostics},ct);
            await Final(actor,lease,ct);
            if(preview.Expires<=Clock())throw Error("REBASE_PREVIEW_CHANGED",409);
            committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
}
