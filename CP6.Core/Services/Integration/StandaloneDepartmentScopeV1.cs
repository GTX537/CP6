using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Core.Services.Integration;

/// <summary>Explicit stored object identity. Never infer a historical department from the actor or business codes.</summary>
internal static class StandaloneDepartmentScopeV1
{
    internal static IQueryable<Guid> Objects(CP6Context db, OperationsOwnerV1.Authority a, string kind)
    {
        // Composed Owner/source predicates may carry distinct kinds and custom scopes.
        // EF retains explicit Npgsql parameter names: each closed internal resource/kind needs its own names.
        var prefix = "binding_" + kind.Replace('-', '_') + "_" + a.Resource.Replace('-', '_');
        var condition = a.Scope switch
        {
            2 => "target.\"Id\"=@object_department",
            3 => """
                EXISTS (SELECT FROM healthy_departments root WHERE root."Id"=@object_department AND
                  starts_with(convert_from(pg_catalog.bpcharsend(target."Path"),'UTF8') COLLATE "C",
                              convert_from(pg_catalog.bpcharsend(root."Path"),'UTF8') COLLATE "C"))
                """,
            4 => "target.\"Id\"=ANY(@object_custom)",
            _ => throw new OrderPriceCorrectionException("OWNER_SCOPE_MAPPING_UNAVAILABLE",503)
        };
        var sql = $$"""
            WITH RECURSIVE {{PostgreSqlOrganizationV1.HealthyCte(PostgreSqlOrganizationV1.Table(db), "@object_tenant")}}
            SELECT m.object_id AS "Value" FROM cp6_operations_owner_v1.department_mappings m
            JOIN healthy_departments target ON target."Id"=m.department_id
            WHERE m.tenant_id=@object_tenant AND m.enabled AND m.object_kind=@object_kind AND {{condition}}
            """;
        sql = sql.Replace("@object_", "@" + prefix + "_", StringComparison.Ordinal);
        return db.Database.SqlQueryRaw<Guid>(sql,
            new NpgsqlParameter(prefix + "_tenant", db.CurrentTenantId), new NpgsqlParameter(prefix + "_kind", kind),
            new NpgsqlParameter(prefix + "_department", a.Actor.DeptId ?? Guid.Empty),
            new NpgsqlParameter(prefix + "_custom", a.Actor.CustomDeptIds.GetValueOrDefault(a.Resource, []).ToArray()));
    }
    internal static async Task<string> Version(CP6Context db, CancellationToken ct)
    {
        var binding = await db.Database.SqlQuery<Guid?>($"""
            SELECT revision AS "Value" FROM cp6_authentication_v1.identity_admission_revisions
            WHERE tenant_id={db.CurrentTenantId} AND object_key='standalone-department-mapping'
            """).SingleOrDefaultAsync(ct);
        return AuthenticationPolicyServiceV1.Hash(new { organization = await OrderDepartmentScopeV1.Version(db,ct), binding });
    }
}
