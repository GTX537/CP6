using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

internal sealed record LegacyMenuMutationResultV1(Sys_Menu? Menu = null, int? DeletedCount = null);

internal sealed record CrmMenuRoleAdvanceV1(Guid TenantId, int RoleId, Guid StoreId, CrmRoleSealVersionV1 Version)
{
    public string Aggregate => "permission:" + IdentityEventContracts.RoleSubject(RoleId);
}

internal sealed class IamLegacyMenuSealV1
{
    public Guid OperationId { get; init; }
    public Guid ActorTenant { get; init; }
    public Guid ActorId { get; init; }
    public string Kind { get; init; } = "";
    public string RequestHash { get; init; } = "";
    public string SourceHash { get; init; } = "";
    public bool SealStarted { get; init; }
    public string? ResultJson { get; init; }

    internal LegacyMenuMutationResultV1 ReadResult() => JsonSerializer.Deserialize<LegacyMenuMutationResultV1>(ResultJson!)!;

    private const string OperationSelect = """
        SELECT operation_id AS "OperationId", actor_tenant AS "ActorTenant", actor_id AS "ActorId", kind AS "Kind",
               request_hash AS "RequestHash", source_hash AS "SourceHash", seal_started AS "SealStarted", result_json::text AS "ResultJson"
        FROM cp6_authentication_v1.legacy_menu_operations
        """;

    internal static Task<IamLegacyMenuSealV1?> ReadPendingAsync(CP6Context db, int[] menuIds, string? menuKey, CancellationToken ct)
        => db.Database.SqlQueryRaw<IamLegacyMenuSealV1>(OperationSelect + """
             WHERE result_json IS NULL AND (menu_ids && {0} OR ({1} AND desired_menu_key=CAST({2} AS bpchar)))
             ORDER BY accepted_at,operation_id LIMIT 1
            """, menuIds, menuKey is not null, menuKey ?? "").SingleOrDefaultAsync(ct);

    internal static Task<IamLegacyMenuSealV1> ReadAsync(CP6Context db, Guid operation, CancellationToken ct)
        => db.Database.SqlQueryRaw<IamLegacyMenuSealV1>(OperationSelect + """
             WHERE operation_id={0}
            """, operation).SingleAsync(ct);

    internal static Task<IamLegacyMenuSealV1?> ReadOriginalAsync(CP6Context db, Guid tenant, Guid actor, string kind,
        string requestHash, string resultHash, CancellationToken ct)
        => db.Database.SqlQueryRaw<IamLegacyMenuSealV1>(OperationSelect + """
             WHERE actor_tenant={0} AND actor_id={1} AND kind={2} AND request_hash={3}
               AND result_json IS NOT NULL AND result_hash={4}
             ORDER BY accepted_at DESC,operation_id LIMIT 1
            """, tenant, actor, kind, requestHash, resultHash).SingleOrDefaultAsync(ct);

    internal async Task InsertAsync(CP6Context db, int[] menuIds, string? menuKey, IReadOnlyList<CrmMenuRoleAdvanceV1> plan,
        string protectedRequest, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_authentication_v1.legacy_menu_operations
              (operation_id,actor_tenant,actor_id,kind,menu_ids,desired_menu_key,request_hash,source_hash)
            VALUES ({OperationId},{ActorTenant},{ActorId},{Kind},{menuIds},{menuKey}::bpchar,{RequestHash},{SourceHash})
            """, ct);
        foreach (var entry in plan)
        {
            if (await IamCrmRoleSealV1.ReadAsync(db, entry.TenantId, Guid.Empty, ct, entry.RoleId) is not null)
                throw IamAdministrationV1.Error(409, "IAM_CRM_ROLE_SEAL_PENDING");
            var child = Guid.NewGuid();
            var version = entry.Version;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_authentication_v1.pending_crm_role_seals
                  (tenant_id,role_id,operation_id,actor_id,kind,request_protected,store_id,source_version,
                   source_type,source_hash,successor_type,successor_hash,menu_operation_id)
                VALUES ({entry.TenantId},{entry.RoleId},{child},{ActorId},'menu-key-fanout',{protectedRequest},
                        {entry.StoreId},{version.SourceVersion},{version.SourceType},{version.SourceHash},
                        {version.SuccessorType},{version.SuccessorHash},{OperationId})
                """, ct);
        }
    }

    internal async Task<CrmMenuRoleAdvanceV1[]> PlanAsync(CP6Context db, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<IamCrmRoleSealV1>($"""
            SELECT tenant_id AS "TenantId", role_id AS "RoleId", operation_id AS "OperationId", actor_id AS "ActorId",
                   kind AS "Kind", request_protected AS "RequestProtected", store_id AS "StoreId", source_version AS "SourceVersion",
                   source_type AS "SourceType", source_hash AS "SourceHash", successor_type AS "SuccessorType",
                   successor_hash AS "SuccessorHash", seal_started AS "SealStarted", menu_operation_id AS "MenuOperationId"
            FROM cp6_authentication_v1.pending_crm_role_seals WHERE menu_operation_id={OperationId}
            """).ToListAsync(ct);
        return rows.Select(row => new CrmMenuRoleAdvanceV1(row.TenantId, row.RoleId, row.StoreId, row.Version))
            .OrderBy(entry => entry.TenantId).ThenBy(entry => entry.Aggregate, StringComparer.Ordinal).ToArray();
    }

    internal Task MarkStartedAsync(CP6Context db, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_authentication_v1.legacy_menu_operations SET seal_started=true WHERE operation_id={OperationId};
            UPDATE cp6_authentication_v1.pending_crm_role_seals SET seal_started=true WHERE menu_operation_id={OperationId}
            """, ct);

    internal async Task CompleteAsync(CP6Context db, string resultHash, LegacyMenuMutationResultV1 result, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(result);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_authentication_v1.legacy_menu_operations
              SET result_json={json}::jsonb,result_hash={resultHash} WHERE operation_id={OperationId};
            DELETE FROM cp6_authentication_v1.pending_crm_role_seals WHERE menu_operation_id={OperationId}
            """, ct);
    }

    internal static async Task<string> TargetHashAsync(CP6Context db, int[] menuIds, bool includeDisplay, bool includesMappings,
        CancellationToken ct)
    {
        var current = await db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object('treeRevision',CASE WHEN {includeDisplay}
              THEN (SELECT revision FROM cp6_menu_tree_v1.root WHERE singleton) END, 'menus',(
              SELECT jsonb_agg(CASE WHEN {includeDisplay} THEN to_jsonb(m)
                    ELSE jsonb_build_array(m."MenuId",m."MenuKey",m."CreateDate") END ORDER BY m."MenuId")
              FROM public."Sys_Menus" m WHERE m."MenuId"=ANY({menuIds})), 'mappings',(
              SELECT jsonb_agg(jsonb_build_array(r."Id",r."TenantId",r."RoleId",r."MenuId") ORDER BY r."Id")
              FROM public."Sys_RoleMenus" r WHERE {includesMappings} AND r."MenuId"=ANY({menuIds})
              ))::text AS "Value"
            """).SingleAsync(ct);
        return AuthenticationPolicyServiceV1.Hash(current);
    }
}
