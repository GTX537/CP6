using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

internal sealed record CrmRoleSealVersionV1(int SourceVersion, string SourceType, string SourceHash,
    string SuccessorType, string SuccessorHash)
{
    internal int SuccessorVersion => checked(SourceVersion + 1);
}

internal sealed class CrmRoleSealRequiredV1(CrmRoleSealVersionV1 version, InquirySiteFormScopeV1[]? scopes)
    : Exception("The native IAM writer requires a CRM role seal.")
{
    internal CrmRoleSealVersionV1 Version => version;
    internal InquirySiteFormScopeV1[]? Scopes => scopes;
}

internal sealed class IamCrmRoleSealV1
{
    public Guid TenantId { get; set; }
    public int RoleId { get; set; }
    public Guid OperationId { get; set; }
    public Guid ActorId { get; set; }
    public string Kind { get; set; } = null!;
    public string RequestProtected { get; set; } = null!;
    public Guid StoreId { get; set; }
    public int SourceVersion { get; set; }
    public string SourceType { get; set; } = null!;
    public string SourceHash { get; set; } = null!;
    public string SuccessorType { get; set; } = null!;
    public string SuccessorHash { get; set; } = null!;
    public bool SealStarted { get; set; }
    public Guid? MenuOperationId { get; set; }

    internal CrmRoleSealVersionV1 Version => new(SourceVersion, SourceType, SourceHash, SuccessorType, SuccessorHash);

    internal void RequireOwner(Guid actor, string kind)
    {
        if (MenuOperationId.HasValue) throw IamAdministrationV1.Error(409, "IAM_CRM_MENU_SEAL_OWNED");
        if (ActorId != actor) throw IamAdministrationV1.Error(403, "IAM_OPERATION_OWNER_MISMATCH");
        if (Kind != kind) throw IamAdministrationV1.Error(409, "IAM_OPERATION_KIND_CONFLICT");
    }

    internal static Task<IamCrmRoleSealV1?> ReadAsync(CP6Context db, Guid tenant, Guid operation,
        CancellationToken ct, int? role = null)
        => db.Database.SqlQuery<IamCrmRoleSealV1>($"""
            SELECT tenant_id AS "TenantId", role_id AS "RoleId", operation_id AS "OperationId", actor_id AS "ActorId", kind AS "Kind",
                   request_protected AS "RequestProtected", store_id AS "StoreId", source_version AS "SourceVersion",
                   source_type AS "SourceType", source_hash AS "SourceHash", successor_type AS "SuccessorType",
                   successor_hash AS "SuccessorHash", seal_started AS "SealStarted", menu_operation_id AS "MenuOperationId"
            FROM cp6_authentication_v1.pending_crm_role_seals
            WHERE tenant_id={tenant} AND (operation_id={operation} OR role_id={role})
            ORDER BY (operation_id={operation}) DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);

    internal static Task<bool> BlocksIssuanceAsync(CP6Context db, Guid tenant, int[] roles,
        Guid site, Guid form, CancellationToken ct)
        => db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.pending_crm_role_seals
              WHERE tenant_id={tenant} AND role_id=ANY({roles})
                AND (affected_site_forms IS NULL OR affected_site_forms @> jsonb_build_array(
                    jsonb_build_object('siteId',{site}::uuid,'formId',{form}::uuid)))) AS "Value"
            """).SingleAsync(ct);

    internal static Task InsertAsync(CP6Context db, Guid tenant, int role, Guid actor, string kind, IamCommandV1 command,
        string requestProtected, Guid store, CrmRoleSealRequiredV1 required, CancellationToken ct)
    {
        var version = required.Version;
        var scopes = kind == IamInquiryScopeAdministrationV1.Kind && required.Scopes is not null
            ? JsonSerializer.Serialize(required.Scopes, IdentityEventContracts.Json) : null;
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_authentication_v1.pending_crm_role_seals
              (tenant_id,role_id,operation_id,actor_id,kind,request_protected,store_id,source_version,
               source_type,source_hash,successor_type,successor_hash,affected_site_forms)
            VALUES ({tenant},{role},{command.OperationId},{actor},{kind},{requestProtected},{store},{version.SourceVersion},
               {version.SourceType},{version.SourceHash},{version.SuccessorType},{version.SuccessorHash},{scopes}::jsonb)
            """, ct);
    }

    internal Task MarkStartedAsync(CP6Context db, Guid tenant, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_authentication_v1.pending_crm_role_seals SET seal_started=true
            WHERE tenant_id={tenant} AND operation_id={OperationId} AND menu_operation_id IS NULL
            """, ct);

    internal Task RemoveAsync(CP6Context db, Guid tenant, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM cp6_authentication_v1.pending_crm_role_seals WHERE tenant_id={tenant} AND operation_id={OperationId} AND menu_operation_id IS NULL
            """, ct);
}
