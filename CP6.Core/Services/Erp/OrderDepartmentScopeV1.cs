using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Erp;

/// <summary>Trusted stored header code, never request/claim department. Caller holds the current authority root.</summary>
internal static class OrderDepartmentScopeV1
{
    internal static IQueryable<OrderDetail> Query(CP6Context db, UserPermissionContext actor, int scope, bool mappingOnly = false, bool includeDeleted = false, bool readLocks = false)
    {
        var (sql, args) = Sql(db, actor, scope, mappingOnly, includeDeleted, false, readLocks);
        return db.OrderDetails.FromSqlRaw(sql, args);
    }

    internal static IQueryable<Order> Headers(CP6Context db, UserPermissionContext actor, int scope, bool includeDeleted)
    {
        var (sql, args) = Sql(db, actor, scope, false, includeDeleted, true, false);
        return db.Orders.FromSqlRaw(sql, args);
    }

    private static (string, object[]) Sql(CP6Context db, UserPermissionContext actor, int scope,
        bool mappingOnly, bool includeDeleted, bool headers, bool readLocks)
    {
        string Table<T>()
        {
            var entity = db.Model.FindEntityType(typeof(T))!;
            return db.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema() ?? "public");
        }
        if (scope is < 1 or > 5) throw new OrderPriceCorrectionException("OWNER_SCOPE_MAPPING_UNAVAILABLE", 503);
        var department = mappingOnly ? "TRUE" : scope switch
        {
            2 => """target."Id"=@owner_department""",
            3 => """
                EXISTS (SELECT FROM healthy_departments root WHERE root."Id"=@owner_department AND
                  starts_with(convert_from(pg_catalog.bpcharsend(target."Path"),'UTF8') COLLATE "C",
                              convert_from(pg_catalog.bpcharsend(root."Path"),'UTF8') COLLATE "C"))
                """,
            _ => """target."Id"=ANY(@owner_custom)"""
        };
        var condition = scope switch
        {
            1 => headers ? """@owner_name<>'' AND o."Creator"=@owner_name""" : """@owner_name<>'' AND d."Creator"=@owner_name""",
            5 => "TRUE",
            _ => $$"""
                EXISTS (SELECT FROM cp6_order_owner_v1.department_mappings m
                  JOIN healthy_departments target ON target."Id"=m.department_id
                  WHERE m.tenant_id=@owner_tenant AND m.enabled AND m.code=o."OrderDepartment"
                    AND {{department}})
                """
        };
        var from = headers ? $"{Table<Order>()} o" : $"{Table<OrderDetail>()} d JOIN {Table<Order>()} o ON d.\"TenantId\"=o.\"TenantId\" AND d.\"WebOrderNo\"=o.\"WebOrderNo\"";
        var alias = headers ? "o" : "d";
        var deleted = includeDeleted ? "TRUE" : headers ? "NOT o.\"IsDeleted\"" : "NOT d.\"IsDeleted\" AND NOT o.\"IsDeleted\"";
        var locking = readLocks ? "FOR SHARE OF d,o" : "";
        return ($$"""
            WITH RECURSIVE {{PostgreSqlOrganizationV1.HealthyCte(PostgreSqlOrganizationV1.Table(db), "@owner_tenant")}}
            SELECT {{alias}}.* FROM {{from}}
            WHERE {{alias}}."TenantId"=@owner_tenant AND {{deleted}} AND {{condition}}
            {{locking}}
            """, [
            new NpgsqlParameter("owner_tenant", db.CurrentTenantId),
            new NpgsqlParameter("owner_name", actor.UserName ?? ""),
            new NpgsqlParameter("owner_department", actor.DeptId ?? Guid.Empty),
            new NpgsqlParameter("owner_custom", actor.CustomDeptIds.GetValueOrDefault("order", []).ToArray())]);
    }

    internal static async Task<string> Version(CP6Context db, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<string>($"""
            SELECT object_key||':'||revision::text AS "Value" FROM cp6_authentication_v1.identity_admission_revisions
            WHERE tenant_id={db.CurrentTenantId} AND object_key IN ('tree','order-department-mapping')
            ORDER BY object_key COLLATE "C"
            """).ToArrayAsync(ct);
        return AuthenticationPolicyServiceV1.Hash(rows);
    }
}
