using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Sys;

/// <summary>
/// Reads all permission facts in one PostgreSQL statement snapshot. It participates in an existing
/// caller transaction without changing its isolation, committing it or retrying its business work.
/// A caller's repeatable-read snapshot is still that caller's snapshot, not a commit-time fence.
/// </summary>
internal static class PostgreSqlPermissionReader
{
    internal static async Task<UserPermissionContext> ReadAsync(CP6Context db, Guid userId, CancellationToken cancellationToken)
    {
        var identifiers = db.GetService<ISqlGenerationHelper>();
        string Table<T>()
        {
            var entity = db.Model.FindEntityType(typeof(T))
                ?? throw new CurrentPermissionUnavailableException();
            return identifiers.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema() ?? "public");
        }

        // Identifiers come only from the application's EF model. Actor and tenant are parameters.
        // JSON conversion preserves stored text (including trailing spaces); casting bpchar facts
        // to text before collecting them would silently trim values. Only the final JSON is text.
        // Independent aggregates avoid multiplying menus, actions, scopes and fields together.
        var sql = $$"""
            WITH RECURSIVE {{PostgreSqlOrganizationV1.HealthyCte(Table<Sys_Dept>(),"@permission_tenant")}}, permission_user AS MATERIALIZED (
                SELECT u."Id", u."UserName", u."DeptId", u."RoleId"
                FROM {{Table<Sys_User>()}} u
                WHERE u."TenantId" = @permission_tenant AND u."Id" = @permission_user AND u."Enable"
            ), permission_roles AS MATERIALIZED (
                SELECT r."RoleId"
                FROM {{Table<Sys_Role>()}} r
                WHERE r."TenantId" = @permission_tenant AND r."Enable"
                  AND EXISTS (SELECT 1 FROM permission_user u WHERE r."RoleId" = u."RoleId"
                    OR EXISTS (SELECT 1 FROM {{Table<Sys_UserRole>()}} ur
                        WHERE ur."TenantId" = @permission_tenant AND ur."UserId" = u."Id"
                          AND ur."RoleId" = r."RoleId"))
            )
            SELECT pg_catalog.jsonb_build_object(
                'UserId', u."Id", 'UserName', u."UserName", 'DeptId', (SELECT d."Id" FROM healthy_departments d WHERE d."Id"=u."DeptId"),
                'DeptPath', COALESCE((SELECT d."Path" FROM healthy_departments d WHERE d."Id" = u."DeptId"), ''),
                'HealthyDepartmentIds', COALESCE((SELECT pg_catalog.jsonb_agg(d."Id") FROM healthy_departments d),'[]'::jsonb),
                'RoleIds', COALESCE((SELECT pg_catalog.jsonb_agg(r."RoleId" ORDER BY r."RoleId")
                    FROM permission_roles r), '[]'::jsonb),
                'MenuKeys', COALESCE((SELECT pg_catalog.jsonb_agg(m."MenuKey")
                    FROM {{Table<Sys_RoleMenu>()}} rm
                    JOIN permission_roles r ON r."RoleId" = rm."RoleId"
                    JOIN {{Table<Sys_Menu>()}} m ON m."MenuId" = rm."MenuId"
                    WHERE rm."TenantId" = @permission_tenant AND m."MenuKey" IS NOT NULL), '[]'::jsonb),
                'ActionKeys', COALESCE((SELECT pg_catalog.jsonb_agg(
                    pg_catalog.convert_from(pg_catalog.bpcharsend(m."MenuKey"), 'UTF8') || ':' ||
                    pg_catalog.convert_from(pg_catalog.bpcharsend(a."ActionCode"), 'UTF8'))
                    FROM {{Table<Sys_RoleAction>()}} a
                    JOIN permission_roles r ON r."RoleId" = a."RoleId"
                    JOIN {{Table<Sys_Menu>()}} m ON m."MenuId" = a."MenuId"
                    WHERE a."TenantId" = @permission_tenant AND m."MenuKey" IS NOT NULL), '[]'::jsonb),
                'DataScopes', COALESCE((SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                    'ResourceKey', s."ResourceKey", 'ScopeType', s."ScopeType", 'CustomDeptIds', s."CustomDeptIds"))
                    FROM {{Table<Sys_RoleDataScope>()}} s
                    JOIN permission_roles r ON r."RoleId" = s."RoleId"
                    WHERE s."TenantId" = @permission_tenant), '[]'::jsonb),
                'FieldPerms', COALESCE((SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                    'ResourceKey', f."ResourceKey", 'FieldName', f."FieldName", 'Access', f."Access"))
                    FROM {{Table<Sys_RoleFieldPerm>()}} f
                    JOIN permission_roles r ON r."RoleId" = f."RoleId"
                    WHERE f."TenantId" = @permission_tenant), '[]'::jsonb)
            )::text AS "Value"
            FROM permission_user u
            """;
        var json = await db.Database.SqlQueryRaw<string>(sql,
            new NpgsqlParameter("permission_tenant", db.CurrentTenantId),
            new NpgsqlParameter("permission_user", userId)).SingleOrDefaultAsync(cancellationToken);
        var snapshot = json is null ? null : JsonSerializer.Deserialize<Snapshot>(json);
        if (snapshot is null) throw new CurrentPermissionUnavailableException();

        return new UserPermissionContext
        {
            UserId = snapshot.UserId,
            UserName = snapshot.UserName,
            DeptId = snapshot.DeptId,
            DeptPath = snapshot.DeptPath,
            RoleIds = snapshot.RoleIds.ToList(),
            MenuKeys = snapshot.MenuKeys.ToHashSet(),
            ActionKeys = snapshot.ActionKeys.ToHashSet(),
            DataScopes = snapshot.DataScopes.GroupBy(x => x.ResourceKey)
                .ToDictionary(g => g.Key, g => g.Max(x => x.ScopeType)),
            CustomDeptIds = snapshot.DataScopes.Where(x => x.ScopeType == 4).GroupBy(x => x.ResourceKey)
                .ToDictionary(g => g.Key, g => g.SelectMany(x => PermissionAggregator.ParseGuids(x.CustomDeptIds))
                    .Where(snapshot.HealthyDepartmentIds.Contains).Distinct().ToList()),
            FieldPerms = snapshot.FieldPerms.GroupBy(x => x.ResourceKey)
                .ToDictionary(g => g.Key, g => g.GroupBy(x => x.FieldName)
                    .ToDictionary(f => f.Key, f => f.Min(x => x.Access)))
        };
    }

    private sealed record Snapshot(Guid UserId, string UserName, Guid? DeptId, string DeptPath, Guid[] HealthyDepartmentIds,
        int[] RoleIds, string[] MenuKeys, string[] ActionKeys, DataScope[] DataScopes, FieldPermission[] FieldPerms);
    private sealed record DataScope(string ResourceKey, int ScopeType, string? CustomDeptIds);
    private sealed record FieldPermission(string ResourceKey, string FieldName, int Access);
}
