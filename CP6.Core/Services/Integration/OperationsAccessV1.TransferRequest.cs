using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Wms.Transfer;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal IQueryable<TransferRequestHeadV1> TransferRequests()
    {
        var rows = TransferRequestStoreV1.Heads(db);
        if (authority.Scope == 1) return rows.Where(x => x.CreatorId == ActorId);
        if (authority.Scope == 5) return rows;
        var ids = StandaloneDepartmentScopeV1.Objects(db, authority, "transfer-request");
        return rows.Where(x => ids.Contains(x.Id));
    }
    internal async Task RequireTransferRequest(Guid id, CancellationToken ct)
    {
        if (!await TransferRequests().AnyAsync(x => x.Id == id, ct)) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }
    internal IQueryable<Location> TransferLocations()
    {
        var rows = db.Locations.AsNoTracking().Where(x => !x.IsDeleted && !x.IsInitializationQuarantined
            && db.Warehouses.Any(w => !w.IsDeleted && w.WarehouseCd == x.WarehouseCd));
        if (authority.Actor.RoleIds.Contains(1)) return rows;
        var grants = db.WmsRoleScopes.Where(g => authority.Actor.RoleIds.Contains(g.RoleId));
        return rows.Where(x => grants.Any(g => (g.WarehouseCd == "*" || g.WarehouseCd == x.WarehouseCd)
            && (g.AreaCd == null || g.AreaCd == "" || g.AreaCd == "*" || g.AreaCd == x.AreaCd)));
    }
    internal async Task<(bool AllowsUnbound, InventoryIdentityDepartmentV1[] Departments)> TransferDepartments(CancellationToken ct)
    {
        var condition = authority.Scope switch
        {
            1 => "false", 2 => "target.\"Id\"=@transfer_department",
            3 => """
                EXISTS (SELECT FROM healthy_departments root WHERE root."Id"=@transfer_department AND
                  starts_with(convert_from(pg_catalog.bpcharsend(target."Path"),'UTF8') COLLATE "C",
                              convert_from(pg_catalog.bpcharsend(root."Path"),'UTF8') COLLATE "C"))
                """,
            4 => "target.\"Id\"=ANY(@transfer_custom)", 5 => "true",
            _ => throw new OrderPriceCorrectionException("OWNER_SCOPE_MAPPING_UNAVAILABLE", 503)
        };
        var sql = $$"""
            WITH RECURSIVE {{PostgreSqlOrganizationV1.HealthyCte(PostgreSqlOrganizationV1.Table(db), "@transfer_tenant")}}
            SELECT target."Id",target."DeptCode" AS "Code",target."DeptName" AS "Name" FROM healthy_departments target
            WHERE {{condition}} ORDER BY target."Sort",target."Id"
            """;
        var departments = await db.Database.SqlQueryRaw<InventoryIdentityDepartmentV1>(sql,
            new NpgsqlParameter("transfer_tenant", db.CurrentTenantId), new NpgsqlParameter("transfer_department", authority.Actor.DeptId ?? Guid.Empty),
            new NpgsqlParameter("transfer_custom", authority.Actor.CustomDeptIds.GetValueOrDefault(authority.Resource, []).ToArray())).ToArrayAsync(ct);
        return (authority.Scope is 1 or 5, departments);
    }
    internal async Task BindTransferDepartment(Guid id, Guid? department, CancellationToken ct)
    {
        var allowed = await TransferDepartments(ct);
        if (department == null ? !allowed.AllowsUnbound : !allowed.Departments.Any(x => x.Id == department))
            throw new OrderPriceCorrectionException("CREATION_DEPARTMENT_FORBIDDEN", 403);
        if (department is {} selected)
        {
            var before = await StandaloneDepartmentScopeV1.Version(db, ct);
            if (Authorities.Any(x => x.OrganizationVersion != before)) throw new OrderPriceCorrectionException("AUTHORITY_CHANGED", 403);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
                VALUES({Guid.NewGuid()},{db.CurrentTenantId},'transfer-request',{id},{selected},true)
                """, ct);
            var after = await StandaloneDepartmentScopeV1.Version(db, ct);
            for (var i = 0; i < Authorities.Count; i++) Authorities[i] = Authorities[i] with { OrganizationVersion = after };
        }
        AtCommit(t => RequireTransferRequest(id, t));
    }
}
