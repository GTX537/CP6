using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Wf;
public sealed partial class FormAuthoringOwnerV2
{
    private async Task LegacySourceRead(UserPermissionContext actor,CancellationToken ct)
    {
        await ReadCapability(actor,ct);
        if(ResourceScope(actor)!=5)throw Error("LEGACY_OWNER_SCOPE_REQUIRED",403);
        if(!(await FieldAccess(false,ct)).Fields["SchemaJson"].CanRead)throw Error("FIELD_READ_FORBIDDEN",403);
    }
    private string LegacySourceJournal(Guid actor)=>"Main.Workflow.LegacySource.v1/"+fence.EnvironmentId.ToString("D")+"/"+actor.ToString("D");
    private static void LegacySourceInput(Guid source,string key,Guid target)
    {if(source==Guid.Empty||target==Guid.Empty||string.IsNullOrWhiteSpace(key)||key.Length>100||key.Contains('\0'))throw Error("LEGACY_SOURCE_INPUT_INVALID",400);}
    private async Task<LegacyFormSourcePreviewV1> LegacySourcePreview(Guid version,string key,Guid target,UserPermissionContext actor,CancellationToken ct)
    {
        var source=await LegacyFormSourceStoreV1.ReadVersion(db,version,ct);var head=await Heads().SingleOrDefaultAsync(x=>x.Key==key,ct)??throw Error("DEFINITION_NOT_FOUND",404);
        await Permission(actor,head,false,false,ct);
        var json=await db.Database.SqlQuery<string>($"SELECT snapshot_json AS \"Value\" FROM cp6_form_authoring_v2.versions WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head.Id} AND id={target}").SingleOrDefaultAsync(ct)??throw Error("FORM_VERSION_NOT_FOUND",404);
        var native=JsonSerializer.Deserialize<FormPublicationV2>(json,Json)!;var compatible=LegacyFormSourceStoreV1.SameSchema(source.Schema,native.Fields["SchemaJson"]!);
        return new(source.Form,source.Id,source.Number,LegacyDefinitionViewV2.Reconstructed(source.Creator)?"LEGACY_HEAD_BACKFILL":"LEGACY_VERSION_SNAPSHOT","UNVERIFIED_LEGACY",
            source.Revision,key,native.VersionId,native.Version,compatible,compatible?[]:["LEGACY_SCHEMA_MAPPING_REQUIRED"],await LegacyFormSourceStoreV1.Binding(db,fence.EnvironmentId,version,ct),actor.ActionKeys.Contains("oa-designer:form-legacy-source"));
    }
    public async Task<LegacyFormSourcePreviewV1> LegacySourcePreviewAsync(Guid version,string key,Guid target,CancellationToken ct=default)
    {
        LegacySourceInput(version,key,target);Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);await Root(ct);var actor=await Actor(ct);await LegacySourceRead(actor,ct);
        await LegacyFormSourceStoreV1.Ready(db,ct);var result=await LegacySourcePreview(version,key,target,actor,ct);await Actor(ct);CompletionClock();return result;
    }
    private async Task<LegacyFormSourceOperationV1?> LegacySourceOriginal(UserPermissionContext actor,Guid operation,byte[]? input,CancellationToken ct)
    {
        var old=await new MainOwnerOperationJournalV1(db).Read(LegacySourceJournal(actor.UserId),operation,ct);if(old==null)return null;
        if(input!=null&&old.State!="CLOSED"&&!MainOwnerOperationJournalV1.Matches(old,input))throw Error("LEGACY_SOURCE_OPERATION_CONFLICT",409);
        return JsonSerializer.Deserialize<LegacyFormSourceOperationV1>(old.Result)!;
    }
    public async Task<LegacyFormSourceOperationV1?> LegacySourceOperationAsync(Guid operation,bool close=false,CancellationToken ct=default)
    {
        if(operation==Guid.Empty)throw Error("LEGACY_SOURCE_INPUT_INVALID",400);Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            await Root(ct);var actor=await Actor(ct);await LegacySourceRead(actor,ct);var old=await LegacySourceOriginal(actor,operation,null,ct);
            if(old!=null||!close){await Actor(ct);CompletionClock();return old;}
            var result=new LegacyFormSourceOperationV1(operation,"CLOSED",null);
            await new MainOwnerOperationJournalV1(db).Store(LegacySourceJournal(actor.UserId),"Main.Workflow.LegacySource.v1",operation,actor.UserId,null,result,[],new{Reason="EXPLICIT_CLOSE"},ct);
            await Actor(ct);CompletionClock();committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception e){if(committing)throw new ScopeAuthorizationExceptionV1("LEGACY_SOURCE_OUTCOME_UNKNOWN",503,e);await tx.RollbackAsync(CancellationToken.None);throw;}
    }
    public async Task<LegacyFormSourceOperationV1> ConfigureLegacySourceAsync(LegacyFormSourceCommandV1 input,CancellationToken ct=default)
    {
        LegacySourceInput(input.LegacyVersionId,input.TargetKey,input.TargetVersionId);
        if(input.OperationId==Guid.Empty||input.ExpectedSourceRevision==Guid.Empty||input.ExpectedVersion!="absent"&&(!Guid.TryParseExact(input.ExpectedVersion,"D",out var expected)||expected==Guid.Empty)
            ||string.IsNullOrWhiteSpace(input.Reason)||input.Reason.Length>500)throw Error("LEGACY_SOURCE_INPUT_INVALID",400);
        var bytes=MainOwnerOperationJournalV1.Encode(input);Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            await Root(ct);var actor=await Actor(ct);await LegacySourceRead(actor,ct);var old=await LegacySourceOriginal(actor,input.OperationId,bytes,ct);
            if(old!=null){await Actor(ct);CompletionClock();return old;}
            await Action(actor,"oa-designer","form-legacy-source",ct);await fence.AcquireAsync(ct);await LegacyFormSourceStoreV1.Ready(db,ct);
            var preview=await LegacySourcePreview(input.LegacyVersionId,input.TargetKey,input.TargetVersionId,actor,ct);
            if(preview.SourceRevision!=input.ExpectedSourceRevision||input.ExpectedVersion!=(preview.Current?.Version.ToString("D")??"absent"))throw Error("LEGACY_SOURCE_CHANGED",409);
            if(input.Enabled&&!preview.Compatible)throw Error("LEGACY_SCHEMA_MAPPING_REQUIRED",422);
            var head=await Heads().SingleAsync(x=>x.Key==input.TargetKey,ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_personal_form_v2.legacy_source_bindings(tenant_id,environment_id,legacy_version_id,legacy_form_id,source_revision,head_id,form_version,enabled,bound_by,reason)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{input.LegacyVersionId},{preview.LegacyFormId},{input.ExpectedSourceRevision},{head.Id},{input.TargetVersionId},{input.Enabled},{actor.UserId},{input.Reason})
                ON CONFLICT(tenant_id,environment_id,legacy_version_id) DO UPDATE SET legacy_form_id=EXCLUDED.legacy_form_id,source_revision=EXCLUDED.source_revision,head_id=EXCLUDED.head_id,
                  form_version=EXCLUDED.form_version,enabled=EXCLUDED.enabled,bound_by=EXCLUDED.bound_by,reason=EXCLUDED.reason
                """,ct);
            var value=(await LegacyFormSourceStoreV1.Binding(db,fence.EnvironmentId,input.LegacyVersionId,ct))!;var result=new LegacyFormSourceOperationV1(input.OperationId,"COMMITTED",value);
            await new MainOwnerOperationJournalV1(db).Store(LegacySourceJournal(actor.UserId),"Main.Workflow.LegacySource.v1",input.OperationId,actor.UserId,bytes,result,
                [input.LegacyVersionId,value.FormVersionId],new{CurrentMappingOnly=true,SourceEvidenceState="UNVERIFIED_LEGACY",input.Reason,preview.SourceRevision,TargetVersion=input.TargetVersionId},ct);
            await Actor(ct);CompletionClock();committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception e){if(committing)throw new ScopeAuthorizationExceptionV1("LEGACY_SOURCE_OUTCOME_UNKNOWN",503,e);await tx.RollbackAsync(CancellationToken.None);throw;}
    }
}
