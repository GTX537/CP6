using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

internal sealed record CrmDepartmentSealAdvanceV1(Guid DepartmentId, int Version, string PredecessorHash, string SuccessorHash);

internal sealed class IamCrmDepartmentSealV1
{
    public Guid DepartmentId { get; set; }
    public Guid OperationId { get; set; }
    public Guid ActorId { get; set; }
    public string Kind { get; set; } = null!;
    public string RequestProtected { get; set; } = null!;
    public Guid StoreId { get; set; }
    public string SourceSeals { get; set; } = null!;
    public bool SealStarted { get; set; }

    internal CrmDepartmentSealAdvanceV1[] ReadAdvances() =>
        JsonSerializer.Deserialize<CrmDepartmentSealAdvanceV1[]>(SourceSeals, IdentityEventContracts.Json)!;

    internal void RequireOwner(Guid actor, string kind)
    {
        if (ActorId != actor) throw IamAdministrationV1.Error(403, "IAM_OPERATION_OWNER_MISMATCH");
        if (Kind != kind) throw IamAdministrationV1.Error(409, "IAM_OPERATION_KIND_CONFLICT");
    }

    internal static Task<IamCrmDepartmentSealV1?> ReadAsync(CP6Context db, Guid tenant, Guid operation, CancellationToken ct)
        => db.Database.SqlQuery<IamCrmDepartmentSealV1>($"""
            SELECT department_id AS "DepartmentId", operation_id AS "OperationId", actor_id AS "ActorId", kind AS "Kind",
                   request_protected AS "RequestProtected", store_id AS "StoreId", source_seals::text AS "SourceSeals",
                   seal_started AS "SealStarted"
            FROM cp6_authentication_v1.pending_crm_department_seals
            WHERE tenant_id={tenant} AND operation_id={operation}
            """).SingleOrDefaultAsync(ct);

    internal static Task<bool> ConflictsAsync(CP6Context db, Guid tenant, Guid[] departments, CancellationToken ct)
        => db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.pending_crm_department_seals
                          WHERE tenant_id={tenant} AND department_ids && {departments}) AS "Value"
            """).SingleAsync(ct);

    internal static Task<bool> BlocksIssuanceAsync(CP6Context db, Guid tenant, Guid? subjectDepartment, Guid[] targets,
        bool allTargets, Guid? targetRoot, CancellationToken ct)
        => db.Database.SqlQuery<bool>($"""
            WITH RECURSIVE pending AS MATERIALIZED (
                SELECT department_ids FROM cp6_authentication_v1.pending_crm_department_seals WHERE tenant_id={tenant}
            ), healthy(id,path,in_subtree) AS (
                SELECT d."Id", d."Path", d."Id" IS NOT DISTINCT FROM {targetRoot}::uuid
                FROM public."Sys_Dept" d WHERE EXISTS(SELECT 1 FROM pending)
                    AND d."TenantId"={tenant} AND d."ParentId" IS NULL AND d."Enable"
                    AND d."Path"=('/'||d."Id"::text||'/') COLLATE "C"
                UNION ALL
                SELECT d."Id", d."Path", h.in_subtree OR d."Id" IS NOT DISTINCT FROM {targetRoot}::uuid
                FROM healthy h JOIN public."Sys_Dept" d ON d."ParentId"=h.id
                WHERE d."TenantId"={tenant} AND d."Enable" AND d."Path"=(h.path||d."Id"::text||'/') COLLATE "C"
            ), sources(id) AS (
                SELECT {subjectDepartment}::uuid WHERE {subjectDepartment}::uuid IS NOT NULL AND EXISTS(SELECT 1 FROM pending)
                UNION
                SELECT id FROM healthy WHERE {allTargets} OR in_subtree OR id=ANY({targets})
            ), ancestry(id) AS (
                SELECT id FROM sources
                UNION
                SELECT d."ParentId" FROM ancestry a JOIN public."Sys_Dept" d ON d."Id"=a.id
                WHERE d."TenantId"={tenant} AND d."ParentId" IS NOT NULL
            )
            SELECT EXISTS(SELECT 1 FROM pending WHERE department_ids && ARRAY(SELECT id FROM ancestry)) AS "Value"
            """).SingleAsync(ct);

    internal static Task InsertAsync(CP6Context db, Guid tenant, Guid department, Guid actor, string kind, Guid operation,
        string requestProtected, Guid store, CrmDepartmentSealAdvanceV1[] advances, CancellationToken ct)
    {
        var departments = advances.Select(advance => advance.DepartmentId).ToArray();
        var json = JsonSerializer.Serialize(advances, IdentityEventContracts.Json);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_authentication_v1.pending_crm_department_seals
                (tenant_id,department_id,operation_id,actor_id,kind,request_protected,store_id,source_seals,department_ids)
            VALUES ({tenant},{department},{operation},{actor},{kind},{requestProtected},{store},{json}::jsonb,{departments})
            """, ct);
    }

    internal Task MarkStartedAsync(CP6Context db, Guid tenant, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE cp6_authentication_v1.pending_crm_department_seals SET seal_started=true
        WHERE tenant_id={tenant} AND operation_id={OperationId}
        """, ct);

    internal Task RemoveAsync(CP6Context db, Guid tenant, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        DELETE FROM cp6_authentication_v1.pending_crm_department_seals WHERE tenant_id={tenant} AND operation_id={OperationId}
        """, ct);
}
