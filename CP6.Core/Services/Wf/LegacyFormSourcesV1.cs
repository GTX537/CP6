using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Wf;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LegacyFormSourceCommandV1(Guid OperationId,Guid LegacyVersionId,Guid ExpectedSourceRevision,string TargetKey,Guid TargetVersionId,string ExpectedVersion,bool Enabled,string Reason);
public sealed record LegacyFormSourcePreviewV1(Guid LegacyFormId,Guid LegacyVersionId,int RecordedVersion,string SourceKind,string SourceEvidenceState,
    Guid SourceRevision,string TargetKey,Guid TargetVersionId,int TargetVersion,bool Compatible,string[] Blockers,LegacyFormSourceBindingV1? Current,bool CanConfigure=false);
public sealed record LegacyFormSourceBindingV1(Guid LegacyVersionId,Guid LegacyFormId,Guid SourceRevision,Guid HeadId,Guid FormVersionId,string TargetKey,
    Guid Version,bool Enabled,DateTimeOffset BoundAtUtc,Guid BoundBy,string Reason,string SourceEvidenceState="UNVERIFIED_LEGACY");
public sealed record LegacyFormSourceOperationV1(Guid OperationId,string State,LegacyFormSourceBindingV1? Result);
internal static class LegacyFormSourceStoreV1
{
    internal sealed record Version(Guid Form,Guid Id,int Number,string Key,string Schema,string? Creator,Guid Revision);
    internal static async Task<Version> ReadVersion(CP6Context db,Guid id,CancellationToken ct)
    {
        var value=await (from version in db.Wf_FormDefVersions.AsNoTracking() join head in db.Wf_FormDefs.AsNoTracking() on version.FormDefId equals head.Id
            where version.Id==id select new{version.FormDefId,version.Id,version.Version,head.FormKey,version.SchemaJson,version.Creator}).SingleOrDefaultAsync(ct)
            ??throw new ScopeAuthorizationExceptionV1("LEGACY_SOURCE_NOT_FOUND",404);
        var revision=await Revision(db,"VERSION",id,ct);return new(value.FormDefId,value.Id,value.Version,value.FormKey,value.SchemaJson,value.Creator,revision);
    }
    internal static Task<Guid> Revision(CP6Context db,string kind,Guid id,CancellationToken ct)=>db.Database.SqlQuery<Guid>($"SELECT revision AS \"Value\" FROM cp6_personal_form_v2.legacy_source_revisions WHERE kind={kind} AND id={id} AND tenant_id={db.CurrentTenantId}").SingleAsync(ct);
    internal static Task<LegacyFormSourceBindingV1?> Binding(CP6Context db,Guid environment,Guid legacy,CancellationToken ct)=>db.Database.SqlQuery<LegacyFormSourceBindingV1>($"""
        SELECT b.legacy_version_id AS "LegacyVersionId",b.legacy_form_id AS "LegacyFormId",b.source_revision AS "SourceRevision",b.head_id AS "HeadId",
          b.form_version AS "FormVersionId",h.form_key AS "TargetKey",b.revision AS "Version",b.enabled AS "Enabled",b.bound_at AS "BoundAtUtc",b.bound_by AS "BoundBy",b.reason AS "Reason",
          'UNVERIFIED_LEGACY'::text AS "SourceEvidenceState"
        FROM cp6_personal_form_v2.legacy_source_bindings b JOIN cp6_form_authoring_v2.heads h ON h.tenant_id=b.tenant_id AND h.environment_id=b.environment_id AND h.id=b.head_id
        WHERE b.tenant_id={db.CurrentTenantId} AND b.environment_id={environment} AND b.legacy_version_id={legacy}
        """).SingleOrDefaultAsync(ct);
    internal static async Task Ready(CP6Context db,CancellationToken ct)
    {if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_personal_form_v2.legacy_adoption_ready() AS \"Value\"").SingleAsync(ct))throw new ScopeAuthorizationExceptionV1("LEGACY_SOURCE_WRITER_COVERAGE_UNAVAILABLE",503);}
    // Only the external legacy schema crosses this new comparison. The native publication is already qualified.
    internal static bool SameSchema(string legacy,string native)
    {
        if(legacy==native)return true;
        try
        {
            using var left=JsonDocument.Parse(legacy);using var right=JsonDocument.Parse(native);
            return CP6.Entity.Contracts.Wire.WorkflowCommandJsonV1.Canonical(left.RootElement)==CP6.Entity.Contracts.Wire.WorkflowCommandJsonV1.Canonical(right.RootElement);
        }
        catch(JsonException){return false;}
    }
}
