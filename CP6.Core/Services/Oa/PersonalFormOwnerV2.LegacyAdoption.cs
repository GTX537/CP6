using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Oa;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LegacyDraftAdoptInputV1(Guid MappingVersion,string Reason);
public sealed record LegacyDraftAdoptionPreviewV1(Guid LegacyDraftId,Guid LegacyVersionId,string SourceVersion,Guid MappingVersion,
    string SourceEvidenceState,string TargetKey,Guid FormVersionId,int TargetVersion,string SchemaJson,string DataJson,string? Title,
    Dictionary<string,string> FieldMask,bool CanAdopt,string[] Blockers,DateTimeOffset AdoptUntilUtc);
public sealed partial class PersonalFormOwnerV2
{
    internal const string AdoptLegacy="FORM_DRAFT_ADOPT_LEGACY";
    private sealed record LegacyDraftRow(Guid Id,Guid Owner,Guid Form,Guid Version,string Data,string? Title,int Status,Guid Revision);
    private sealed record LegacyAdoptionSource(LegacyDraftRow Row,LegacyFormSourceBindingV1 Binding,FormPublicationV2 Publication,PersonalFormPolicyV2 Policy,Dictionary<string,string> Mask,DateTimeOffset Until);
    private async Task<LegacyDraftRow> OwnedLegacy(Guid id,Guid actor,CancellationToken ct)
    {
        var row=await db.Wf_FormDrafts.AsNoTracking().Where(x=>x.Id==id&&x.OwnerUserId==actor).Select(x=>new{x.Id,x.OwnerUserId,x.FormDefId,x.FormDefVersionId,x.DataJson,x.Title,x.Status}).SingleOrDefaultAsync(ct)
            ??throw Error("LEGACY_DRAFT_NOT_FOUND",404);
        return new(row.Id,row.OwnerUserId,row.FormDefId,row.FormDefVersionId,row.DataJson,row.Title,row.Status,await LegacyFormSourceStoreV1.Revision(db,"DRAFT",row.Id,ct));
    }
    private async Task<LegacyAdoptionSource> LegacyAdoptionSourceFor(LegacyDraftRow row,UserPermissionContext actor,CancellationToken ct)
    {
        var binding=await LegacyFormSourceStoreV1.Binding(db,fence.EnvironmentId,row.Version,ct)??throw Error("LEGACY_SOURCE_MAPPING_REQUIRED",409);
        if(!binding.Enabled)throw Error("LEGACY_SOURCE_MAPPING_DISABLED",403);
        if(binding.LegacyFormId!=row.Form||binding.SourceRevision!=await LegacyFormSourceStoreV1.Revision(db,"VERSION",row.Version,ct))throw Error("LEGACY_SOURCE_MAPPING_STALE",409);
        var publication=await Publication(binding.HeadId,binding.FormVersionId,ct);var policy=(await Policy(binding.HeadId,ct))??throw Error("FORM_USE_POLICY_REQUIRED",403);
        var mask=Mask(actor,publication,policy);if(!policy.Enabled)throw Error("FORM_USE_REVOKED",403);
        return new(row,binding,publication,policy,mask,binding.BoundAtUtc.AddDays(policy.RetentionDays));
    }
    private static void LegacyData(LegacyDraftRow row,FormSchema schema)
    {
        try
        {
            using var source=CP6.Entity.Contracts.Wire.WorkflowCommandJsonV1.Parse(Encoding.UTF8.GetBytes(row.Data),1024*1024,8);
            PersonalFormValuesV2.Validate(schema,source.RootElement,partial:true);
        }
        catch(JsonException e){throw new ScopeAuthorizationExceptionV1("LEGACY_SOURCE_DATA_INVALID",422,e);}
    }
    private string[] LegacyAdoptionBlocked(LegacyAdoptionSource source,UserPermissionContext actor)
    {
        var result=new List<string>();
        if(source.Row.Status!=0)result.Add("LEGACY_DRAFT_NOT_ACTIVE");
        if(!actor.ActionKeys.Contains("oa-form-catalog:add")||!actor.ActionKeys.Contains("oa-form-catalog:edit"))result.Add("LEGACY_ADOPTION_FORBIDDEN");
        if(!source.Policy.AllowRestore)result.Add("LEGACY_ADOPTION_DISABLED");
        if(source.Until<=Clock())result.Add("LEGACY_ADOPTION_WINDOW_EXPIRED");return result.ToArray();
    }
    public async Task<LegacyDraftAdoptionPreviewV1> LegacyAdoptionPreviewAsync(Guid id,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("LEGACY_DRAFT_NOT_FOUND",404);Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);
        var actor=await Actor(ct);await Action(actor,"query",ct);await LegacyFormSourceStoreV1.Ready(db,ct);
        var row=await OwnedLegacy(id,actor.UserId,ct);var source=await LegacyAdoptionSourceFor(row,actor,ct);var schema=source.Publication.Fields["SchemaJson"]!;
        LegacyData(row,PersonalFormValuesV2.Schema(schema));var blockers=LegacyAdoptionBlocked(source,actor);
        var result=new LegacyDraftAdoptionPreviewV1(row.Id,row.Version,row.Revision.ToString("D"),source.Binding.Version,"UNVERIFIED_LEGACY",source.Publication.Key,
            source.Publication.VersionId,source.Publication.Version,ProjectSchema(schema,source.Mask),PersonalFormValuesV2.Project(row.Data,source.Mask),TitleReadable(actor)?row.Title:null,source.Mask,blockers.Length==0,blockers,source.Until);
        await Final(actor,lease,ct,false);return result;
    }
    public async Task<PersonalFormResultV2> AdoptLegacyDraftAsync(Guid id,byte[] raw,CancellationToken ct=default)
    {
        var command=CommandInput<LegacyDraftAdoptInputV1>(raw,true);var body=command.Body;var resource=id.ToString("D");
        if(id==Guid.Empty||body.MappingVersion==Guid.Empty||string.IsNullOrWhiteSpace(body.Reason)||body.Reason.Length>500)throw Error("LEGACY_ADOPTION_INPUT_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await Action(actor,"query",ct);
            var old=await Original(actor.UserId,AdoptLegacy,resource,command.Id,ct);
            if(old!=null){Match(old,command.Digest);var known=await Disclose(old,actor,ct);await Final(actor,lease,ct,false);return known;}
            await WriteReady(lease,ct);await Action(actor,"add",ct);await Action(actor,"edit",ct);await LegacyFormSourceStoreV1.Ready(db,ct);
            var row=await OwnedLegacy(id,actor.UserId,ct);var source=await LegacyAdoptionSourceFor(row,actor,ct);
            if(command.Expected!=row.Revision.ToString("D")||body.MappingVersion!=source.Binding.Version)throw Error("LEGACY_SOURCE_CHANGED",409);
            if(LegacyAdoptionBlocked(source,actor) is {Length:>0} blockers)throw Error(blockers[0],409);
            var schema=PersonalFormValuesV2.Schema(source.Publication.Fields["SchemaJson"]!);LegacyData(row,schema);
            await References(schema,row.Data,ct);await CheckOptions(schema,row.Data,null,true,ct);
            // Existing claimed content is not upgraded from an unverified legacy string into a new parent.
            await CheckFiles(source.Binding.HeadId,source.Binding.FormVersionId,"FORM",null,actor.UserId,schema,row.Data,row.Data,true,source.Mask,ct);
            var draft=Guid.NewGuid();var write=Guid.NewGuid();await BeginCommand(write,actor.UserId,AdoptLegacy,resource,command,draft,null,null,null,ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_personal_form_v2.drafts(id,tenant_id,environment_id,owner_id,head_id,form_version,data_json,title)
                VALUES({draft},{db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{source.Binding.HeadId},{source.Binding.FormVersionId},{row.Data},{row.Title});
                INSERT INTO cp6_personal_form_v2.legacy_adoptions(draft_id,command_id,tenant_id,environment_id,owner_id,source_draft_id,source_version_id,
                  source_draft_revision,source_schema_revision,binding_revision,policy_version,policy_ref,adopt_until)
                VALUES({draft},{write},{db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{row.Id},{row.Version},{row.Revision},{source.Binding.SourceRevision},
                  {source.Binding.Version},{source.Policy.Version},{source.Policy.RetentionPolicyRef},{source.Until})
                """,ct);
            await History(write,draft,null,ct);var view=await DraftView(await Owned(draft,actor.UserId,ct),actor,ct);
            await BindFiles(write,source.Binding.FormVersionId,"DRAFT",draft,view.RowVersion,schema,row.Data,ct);
            var result=await Complete(write,command,draft,view.RowVersion,view,new{SourceKind="LEGACY_RECORDED_CURRENT_MAPPING",SourceEvidenceState="UNVERIFIED_LEGACY",
                SourceDraft=row.Id,SourceVersion=row.Version,SourceRevision=row.Revision,Mapping=source.Binding.Version,source.Policy.Version,source.Policy.RetentionPolicyRef,source.Until,body.Reason,Scope=lease.Snapshot,actor.UserId},ct);
            await Final(actor,lease,ct);if(source.Until<=Clock())throw Error("LEGACY_ADOPTION_WINDOW_EXPIRED",409);
            committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception error){await Rollback(tx,committing,error);throw;}
    }
}
