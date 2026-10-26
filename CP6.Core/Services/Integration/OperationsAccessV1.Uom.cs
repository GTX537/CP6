using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal sealed class NewUomBinding(OperationsAccessV1 access, IDbContextTransaction transaction,
        Guid tenant, Guid actor, Guid id, Guid? department)
    {
        internal OperationsAccessV1 Access { get; } = access;
        internal IDbContextTransaction Transaction { get; } = transaction;
        internal Guid Tenant { get; } = tenant;
        internal Guid Actor { get; } = actor;
        internal Guid Id { get; } = id;
        internal Guid? Department { get; } = department;
        internal bool Consumed { get; set; }
    }
    internal IQueryable<UomProfileHeadV1> UomProfiles()
    {
        if (authority.Source.TenantId != db.CurrentTenantId) throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        if (authority.Resource != OperationsOwnerV1.UomProfile || authority.Action is not ("query" or "add" or "edit"))
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
        var rows = UomProfileStoreV1.Heads(db);
        if (authority.Scope == 1) return rows.Where(row => row.CreatorId == ActorId);
        if (authority.Scope == 5) return rows;
        var allowed = StandaloneDepartmentScopeV1.Objects(db, authority, "uom-profile");
        return rows.Where(row => allowed.Contains(row.Id));
    }
    internal async Task RequireUomProfile(Guid id, CancellationToken ct)
    {
        if (!await UomProfiles().AnyAsync(row => row.Id == id, ct)) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }
    internal void RequireUomSourceAccess(CP6.Core.EFDbContext.CP6Context context)
    {
        if (!ReferenceEquals(db, context) || db.Database.CurrentTransaction == null || authority.Source.TenantId != db.CurrentTenantId)
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        if (authority.Resource != OperationsOwnerV1.UomProfile || authority.Action != "query")
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
    }
    internal async Task<UomProfileCreationContextV1> UomCreationContext(CancellationToken ct)
    {
        if (authority.Resource != OperationsOwnerV1.UomProfile || authority.Action != "add")
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
        var condition = authority.Scope switch
        {
            1 => "false", 2 => "target.\"Id\"=@uom_department",
            3 => """
                EXISTS (SELECT FROM healthy_departments root WHERE root."Id"=@uom_department AND
                  starts_with(convert_from(pg_catalog.bpcharsend(target."Path"),'UTF8') COLLATE "C",
                              convert_from(pg_catalog.bpcharsend(root."Path"),'UTF8') COLLATE "C"))
                """,
            4 => "target.\"Id\"=ANY(@uom_custom)", 5 => "true",
            _ => throw new OrderPriceCorrectionException("OWNER_SCOPE_MAPPING_UNAVAILABLE", 503)
        };
        var sql = $$"""
            WITH RECURSIVE {{PostgreSqlOrganizationV1.HealthyCte(PostgreSqlOrganizationV1.Table(db), "@uom_tenant")}}
            SELECT target."Id",target."DeptCode" AS "Code",target."DeptName" AS "Name"
            FROM healthy_departments target WHERE {{condition}} ORDER BY target."Sort",target."Id"
            """;
        var departments = await db.Database.SqlQueryRaw<UomProfileDepartmentV1>(sql,
            new NpgsqlParameter("uom_tenant", db.CurrentTenantId),
            new NpgsqlParameter("uom_department", authority.Actor.DeptId ?? Guid.Empty),
            new NpgsqlParameter("uom_custom", authority.Actor.CustomDeptIds.GetValueOrDefault(authority.Resource, []).ToArray())).ToArrayAsync(ct);
        return new(db.CurrentTenantId, ActorId, CreationVersion(), authority.Scope is 1 or 5, departments);
    }
    internal async Task<NewUomBinding> BeginUomCreation(Guid id, string? expected, Guid? department, CancellationToken ct)
    {
        var context = await UomCreationContext(ct);
        if (context.AuthorityVersion != expected) throw new OrderPriceCorrectionException("CREATION_AUTHORITY_CHANGED", 412);
        if (department == null ? !context.AllowsUnbound : !context.Departments.Any(row => row.Id == department))
            throw new OrderPriceCorrectionException("CREATION_DEPARTMENT_FORBIDDEN", 403);
        if (id == Guid.Empty || await UomProfileStoreV1.Heads(db).AnyAsync(row => row.Id == id, ct))
            throw new OrderPriceCorrectionException("UOM_IDENTITY_CONFLICT", 409);
        return new(this, db.Database.CurrentTransaction ?? throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503),
            db.CurrentTenantId, ActorId, id, department);
    }
    internal async Task CompleteUomCreation(NewUomBinding ticket, CancellationToken ct)
    {
        var id = ticket.Id;
        if (ticket.Consumed || !ReferenceEquals(ticket.Access, this) || !ReferenceEquals(ticket.Transaction, db.Database.CurrentTransaction)
            || ticket.Tenant != db.CurrentTenantId || ticket.Actor != ActorId
            || authority.Resource != OperationsOwnerV1.UomProfile || authority.Action != "add"
            || !await UomProfileStoreV1.Heads(db).AnyAsync(row => row.Id == id && row.CreatorId == ActorId && row.Revision == 1, ct))
            throw new OrderPriceCorrectionException("CREATION_BINDING_FORBIDDEN", 403);
        ticket.Consumed = true;
        if (ticket.Department is Guid selected)
        {
            var before = await StandaloneDepartmentScopeV1.Version(db, ct);
            if (Authorities.Any(row => row.OrganizationVersion != before)) throw new OrderPriceCorrectionException("AUTHORITY_CHANGED", 403);
            var organization = await OrderDepartmentScopeV1.Version(db, ct);
            var mapping = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
                VALUES({mapping},{db.CurrentTenantId},'uom-profile',{id},{selected},true)
                """, ct);
            var after = await StandaloneDepartmentScopeV1.Version(db, ct);
            if (before == after || organization != await OrderDepartmentScopeV1.Version(db, ct))
                throw new OrderPriceCorrectionException("AUTHORITY_CHANGED", 403);
            for (var i = 0; i < Authorities.Count; i++) Authorities[i] = Authorities[i] with { OrganizationVersion = after };
            var revision = await db.Database.SqlQuery<Guid>($"""
                SELECT revision AS "Value" FROM cp6_operations_owner_v1.department_mappings
                WHERE tenant_id={db.CurrentTenantId} AND id={mapping} AND object_kind='uom-profile'
                  AND object_id={id} AND department_id={selected} AND enabled
                """).SingleAsync(ct);
            AtCommit(async token =>
            {
                if (!await db.Database.SqlQuery<Guid>($"""
                    SELECT revision AS "Value" FROM cp6_operations_owner_v1.department_mappings
                    WHERE tenant_id={db.CurrentTenantId} AND id={mapping} AND object_kind='uom-profile'
                      AND object_id={id} AND department_id={selected} AND enabled
                    """).AnyAsync(value => value == revision, token)) throw new OrderPriceCorrectionException("AUTHORITY_CHANGED", 403);
            });
        }
        await RequireUomProfile(id, ct); AtCommit(token => RequireUomProfile(id, token));
    }
}
