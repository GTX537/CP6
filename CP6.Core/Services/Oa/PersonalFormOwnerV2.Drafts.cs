using System.Text.Json;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class PersonalFormOwnerV2
{
    private sealed record DraftRow(Guid Id,Guid Owner,Guid Head,Guid Version,string Data,string? Title,int Status,Guid Token,
        DateTimeOffset Updated,DateTimeOffset? RetainUntil,string? RetentionRef,Guid? FormData,string Snapshot);
    private IQueryable<DraftRow> Drafts() => db.Database.SqlQuery<DraftRow>($"""
        SELECT d.id AS "Id",d.owner_id AS "Owner",d.head_id AS "Head",d.form_version AS "Version",d.data_json AS "Data",d.title AS "Title",
          d.status AS "Status",d.row_version AS "Token",d.updated_at AS "Updated",d.retain_until AS "RetainUntil",d.retention_ref AS "RetentionRef",
          d.form_data_id AS "FormData",to_jsonb(d)::text AS "Snapshot" FROM cp6_personal_form_v2.drafts d
        WHERE d.tenant_id={db.CurrentTenantId} AND d.environment_id={fence.EnvironmentId}
        """);
    private async Task<DraftRow> Owned(Guid id,Guid actor,CancellationToken ct)
    {
        var row=await Drafts().SingleOrDefaultAsync(x=>x.Id==id && x.Owner==actor,ct)??throw Error("DRAFT_NOT_FOUND",404);
        return row;
    }
    private static void Active(DraftRow row)
    {
        if(row.Status==1)throw Error("DRAFT_ALREADY_SUBMITTED",409);
        if(row.Status==2)throw Error("DRAFT_DELETED",409);
    }
    private static void Expected(DraftRow row,string? token)
    {
        if(token!=row.Token.ToString("D"))throw Error("DRAFT_VERSION_CONFLICT",409);
    }
    private async Task<PersonalFormDraftV2> DraftView(DraftRow row,UserPermissionContext actor,CancellationToken ct)
    {
        var publication=await Publication(row.Head,row.Version,ct);var mask=await Mask(actor,row.Head,publication,ct);
        var latestId=await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_form_authoring_v2.versions WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={row.Head}
            ORDER BY version_number DESC LIMIT 1
            """).SingleAsync(ct);
        var latest=(await Publication(row.Head,latestId,ct)).Version;
        var capabilities=new List<string>();var policy=(await Policy(row.Head,ct))!;
        if(row.Status==0)
        {
            if(actor.ActionKeys.Contains("oa-form-catalog:edit")){capabilities.Add("SAVE");capabilities.Add("REBASE");}
            if(actor.ActionKeys.Contains("oa-form-catalog:del"))capabilities.Add("DELETE");
            if(actor.ActionKeys.Contains("oa-form-catalog:submit"))capabilities.Add("SUBMIT_WITH_CURRENT_CONTEXT");
        }
        if(row.Status==2 && policy.AllowRestore && row.RetainUntil>Clock() && row.RetentionRef==policy.RetentionPolicyRef && actor.ActionKeys.Contains("oa-form-catalog:edit"))capabilities.Add("RESTORE");
        return new(row.Id,publication.Key,row.Version,publication.Version,latest,publication.Fields["Name"]!,
            ProjectSchema(publication.Fields["SchemaJson"]!,mask),PersonalFormValuesV2.Project(row.Data,mask),TitleReadable(actor)?row.Title:null,
            row.Token.ToString("D"),row.Status,publication.Version<latest,row.Updated,mask,capabilities.ToArray(),latestId,TitleAccess(actor));
    }
    private Task History(Guid command,Guid draft,string? before,CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_personal_form_v2.history(id,command_id,draft_id,before_json,after_json)
        SELECT {Guid.NewGuid()},{command},id,{before},to_jsonb(d)::text FROM cp6_personal_form_v2.drafts d WHERE id={draft}
        """,ct);
    private async Task<PersonalFormResultV2> Disclose(StoredCommand old,UserPermissionContext actor,CancellationToken ct)
    {
        if(old.Operation is SendBack or Transfer or AddSign or ReturnedEdit or ReturnedResubmit or ResolveApprover)return await DiscloseTaskOperation(old,actor,ct);
        var result=Parse<PersonalFormResultV2>(old.Result);
        if(old.Draft is Guid draft)
        {
            var row=await Owned(draft,actor.UserId,ct);
            if(result.Result is {} value && value.TryGetProperty("draftId",out _))
            {
                var view=value.Deserialize<PersonalFormDraftV2>(Json)!;var source=await Publication(row.Head,view.FormVersionId,ct);
                var mask=await Mask(actor,row.Head,source,ct);
                result=result with{Result=JsonSerializer.SerializeToElement(view with{SchemaJson=ProjectSchema(view.SchemaJson,mask),DataJson=PersonalFormValuesV2.Project(view.DataJson,mask),Title=TitleReadable(actor)?view.Title:null,FieldMask=mask,Capabilities=[],TitleAccess=TitleAccess(actor)},Json)};
            }
            else await Mask(actor,row.Head,await Publication(row.Head,row.Version,ct),ct);
        }
        else if(old.Data is Guid data)
        {
            var source=await db.Database.SqlQuery<Guid>($"""
                SELECT head_id AS "Value" FROM cp6_personal_form_v2.submissions WHERE form_data_id={data} AND actor_id={actor.UserId} AND tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
                """).SingleAsync(ct);
            var version=result.Result!.Value.GetProperty("formDefVersionId").GetGuid();
            await Mask(actor,source,await Publication(source,version,ct),ct);
        }
        return result;
    }
    public async Task<PersonalFormResultV2> OriginalAsync(string operation,string resource,string command,CancellationToken ct=default)
    {
        if(operation is not(Create or Save or Delete or Restore or Rebase or Direct or Submit or Favorite or SendBack or Transfer or AddSign or ReturnedEdit or ReturnedResubmit or ResolveApprover) || !System.Text.RegularExpressions.Regex.IsMatch(command,"\\A[A-Za-z0-9_.-]{1,100}\\z"))throw Error("COMMAND_SCOPE_INVALID",400);
        resource=CommandResource(operation,resource);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);
        var actor=await Actor(ct);await Action(actor,"query",ct,operation is SendBack or Transfer or AddSign or ReturnedEdit or ReturnedResubmit or ResolveApprover?"oa-inbox":"oa-form-catalog");var old=await Original(actor.UserId,operation,resource,command,ct);
        var result=old==null?new PersonalFormResultV2(command,"NOT_OBSERVED",null,null,null,null,null):await Disclose(old,actor,ct);
        await Final(actor,lease,ct,false);return result;
    }
    public async Task<PersonalFormDraftV2> DraftAsync(Guid id,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);
        var actor=await Actor(ct);await Action(actor,"query",ct);var row=await Owned(id,actor.UserId,ct);
        var result=await DraftView(row,actor,ct);await Final(actor,lease,ct,false);return result;
    }
    public async Task<PersonalFormPageV2> DraftsAsync(int page=1,int pageSize=20,CancellationToken ct=default)
    {
        if(page is <1 or >100000 || pageSize is <1 or >100)throw Error("PAGE_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);
        var actor=await Actor(ct);await Action(actor,"query",ct);var roles=actor.RoleIds.ToArray();
        var allowed=await db.Database.SqlQuery<Guid>($"""
            SELECT p.head_id AS "Value" FROM cp6_personal_form_v2.policy p
            WHERE p.tenant_id={db.CurrentTenantId} AND p.environment_id={fence.EnvironmentId}
              AND EXISTS(SELECT FROM jsonb_array_elements(p.body::jsonb->'roles') r WHERE (r->>'roleId')::int=ANY({roles}))
            """).ToArrayAsync(ct);
        var query=Drafts().Where(x=>x.Owner==actor.UserId && x.Status==0 && allowed.Contains(x.Head));var total=await query.CountAsync(ct);
        var rows=await query.OrderByDescending(x=>x.Updated).ThenByDescending(x=>x.Id).Skip((page-1)*pageSize).Take(pageSize).ToArrayAsync(ct);
        var items=new List<PersonalFormDraftItemV2>();
        foreach(var row in rows)
        {
            var view=await DraftView(row,actor,ct);items.Add(new(view.DraftId,view.FormKey,view.Name,view.FormVersion,view.LatestPublishedVersion,view.Title,view.RowVersion,view.UpdatedAtUtc,view.Stale));
        }
        await Final(actor,lease,ct,false);return new(items.ToArray(),total,page,pageSize);
    }
    public async Task<PersonalFormResultV2> CreateDraftAsync(string key,byte[] raw,CancellationToken ct=default)
    {
        var command=CommandInput<PersonalFormCreateV2>(raw,false);if(command.Body.FormVersionId==Guid.Empty)throw Error("FORM_VERSION_REQUIRED",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await Action(actor,"query",ct);
            var old=await Original(actor.UserId,Create,key,command.Id,ct);
            if(old!=null){Match(old,command.Digest);var saved=await Disclose(old,actor,ct);await Final(actor,lease,ct,false);return saved;}
            await WriteReady(lease,ct);await Action(actor,"add",ct);var source=await Resolve(actor,key,false,ct);
            if(command.Body.FormVersionId!=source.Form.VersionId)throw Error("FORM_CONTEXT_CHANGED",409);
            var title=PersonalFormValuesV2.Title(command.Body.Title);TitleWritable(actor,null,title);
            var schema=source.Form.Fields["SchemaJson"]!;
            var data=PersonalFormValuesV2.Merge(PersonalFormValuesV2.Schema(schema),command.Body.Data,PersonalFormValuesV2.Initial(schema),source.Mask);
            await References(PersonalFormValuesV2.Schema(schema),data,ct);
            await CheckOptions(PersonalFormValuesV2.Schema(schema),data,null,true,ct);
            await CheckFiles(source.Head.Id,source.Form.VersionId,"FORM",null,actor.UserId,PersonalFormValuesV2.Schema(schema),data,null,true,source.Mask,ct);
            var id=Guid.NewGuid();var write=Guid.NewGuid();await BeginCommand(write,actor.UserId,Create,key,command,id,null,null,null,ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_personal_form_v2.drafts(id,tenant_id,environment_id,owner_id,head_id,form_version,data_json,title)
                VALUES({id},{db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{source.Head.Id},{source.Form.VersionId},{data},{title})
                """,ct);
            await History(write,id,null,ct);var view=await DraftView(await Owned(id,actor.UserId,ct),actor,ct);
            await BindFiles(write,source.Form.VersionId,"DRAFT",id,view.RowVersion,PersonalFormValuesV2.Schema(schema),data,ct);
            var result=await Complete(write,command,id,view.RowVersion,view,new{Scope=lease.Snapshot,Policy=source.Policy.Version,actor.UserId,source.Mask},ct);
            await Final(actor,lease,ct);committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
    public async Task<PersonalFormResultV2> SaveDraftAsync(Guid id,byte[] raw,CancellationToken ct=default)
    {
        var command=CommandInput<PersonalFormSaveV2>(raw,true);var resource=id.ToString("D");
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await Action(actor,"query",ct);
            var old=await Original(actor.UserId,Save,resource,command.Id,ct);
            if(old!=null){Match(old,command.Digest);var saved=await Disclose(old,actor,ct);await Final(actor,lease,ct,false);return saved;}
            await WriteReady(lease,ct);await Action(actor,"edit",ct);var row=await Owned(id,actor.UserId,ct);Active(row);Expected(row,command.Expected);
            var source=await Publication(row.Head,row.Version,ct);var mask=await Mask(actor,row.Head,source,ct);
            var title=PersonalFormValuesV2.Title(command.Body.Title);
            if(!TitleReadable(actor) && title==null)title=row.Title;else TitleWritable(actor,row.Title,title);
            var data=PersonalFormValuesV2.Merge(PersonalFormValuesV2.Schema(source.Fields["SchemaJson"]!),command.Body.Data,row.Data,mask);
            await References(PersonalFormValuesV2.Schema(source.Fields["SchemaJson"]!),command.Body.Data.GetRawText(),ct);
            await CheckOptions(PersonalFormValuesV2.Schema(source.Fields["SchemaJson"]!),data,row.Data,false,ct);
            await CheckFiles(row.Head,row.Version,"DRAFT",id,actor.UserId,PersonalFormValuesV2.Schema(source.Fields["SchemaJson"]!),data,row.Data,false,mask,ct);
            var write=Guid.NewGuid();await BeginCommand(write,actor.UserId,Save,resource,command,id,null,null,null,ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_personal_form_v2.drafts SET data_json={data},title={title} WHERE id={id}
                """,ct);
            await History(write,id,row.Snapshot,ct);var view=await DraftView(await Owned(id,actor.UserId,ct),actor,ct);
            await BindFiles(write,row.Version,"DRAFT",id,view.RowVersion,PersonalFormValuesV2.Schema(source.Fields["SchemaJson"]!),data,ct);
            var result=await Complete(write,command,id,view.RowVersion,view,new{Scope=lease.Snapshot,actor.UserId,Mask=mask,FormVersion=row.Version},ct);
            await Final(actor,lease,ct);committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
    public Task<PersonalFormResultV2> DeleteDraftAsync(Guid id,byte[] raw,CancellationToken ct=default)=>ChangeDeleted(id,raw,false,ct);
    public Task<PersonalFormResultV2> RestoreDraftAsync(Guid id,byte[] raw,CancellationToken ct=default)=>ChangeDeleted(id,raw,true,ct);
    private async Task<PersonalFormResultV2> ChangeDeleted(Guid id,byte[] raw,bool restore,CancellationToken ct)
    {
        var command=CommandInput<PersonalFormReasonV2>(raw,true);var operation=restore?Restore:Delete;var resource=id.ToString("D");
        if(string.IsNullOrWhiteSpace(command.Body.Reason)||command.Body.Reason.Length>500)throw Error("REASON_REQUIRED",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await Action(actor,"query",ct);
            var old=await Original(actor.UserId,operation,resource,command.Id,ct);
            if(old!=null){Match(old,command.Digest);var saved=await Disclose(old,actor,ct);await Final(actor,lease,ct,false);return saved;}
            await WriteReady(lease,ct);await Action(actor,restore?"edit":"del",ct);var row=await Owned(id,actor.UserId,ct);Expected(row,command.Expected);
            await Mask(actor,row.Head,await Publication(row.Head,row.Version,ct),ct);var policy=(await Policy(row.Head,ct))!;
            if(restore)
            {if(row.Status!=2 || !policy.AllowRestore || row.RetainUntil<=Clock() || row.RetentionRef!=policy.RetentionPolicyRef)throw Error("DRAFT_RESTORE_UNAVAILABLE",422);}
            else Active(row);
            var write=Guid.NewGuid();await BeginCommand(write,actor.UserId,operation,resource,command,id,null,null,null,ct);
            if(restore)await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_personal_form_v2.drafts SET status=0 WHERE id={id}",ct);
            else await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_personal_form_v2.drafts SET status=2,deleted_at={Clock()},retain_until={Clock().AddDays(policy.RetentionDays)},retention_ref={policy.RetentionPolicyRef} WHERE id={id}
                """,ct);
            await History(write,id,row.Snapshot,ct);var view=await DraftView(await Owned(id,actor.UserId,ct),actor,ct);
            var result=await Complete(write,command,id,view.RowVersion,view,new{Scope=lease.Snapshot,actor.UserId,command.Body.Reason,Policy=policy},ct);
            await Final(actor,lease,ct);
            if(restore&&row.RetainUntil<=Clock())throw Error("DRAFT_RESTORE_UNAVAILABLE",422);
            committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
}
