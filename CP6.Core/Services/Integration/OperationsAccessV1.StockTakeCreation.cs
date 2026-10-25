using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    // Only the actual stocktake add Owner can allocate this one-use new-header binding.
    // The independently authorized complete stock snapshot remains part of that Owner transaction.
    internal sealed class NewStockTakeBinding(OperationsAccessV1 access,IDbContextTransaction transaction,Guid tenant,
        Guid actor,Guid id,Guid? department)
    {
        internal OperationsAccessV1 Access {get;}=access;
        internal IDbContextTransaction Transaction {get;}=transaction;
        internal Guid Tenant {get;}=tenant;
        internal Guid Actor {get;}=actor;
        internal Guid Id {get;}=id;
        internal Guid? Department {get;}=department;
        internal bool Consumed {get;set;}
    }
    internal async Task<StockTakeCreationContextV1> StockTakeCreationContext(CancellationToken ct)
    {
        if(authority.Resource!=OperationsOwnerV1.StockTake || authority.Action!="add")
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN",403);
        var condition=authority.Scope switch
        {
            1=>"false",2=>"target.\"Id\"=@creation_department",
            3=>"""
                EXISTS (SELECT FROM healthy_departments root WHERE root."Id"=@creation_department AND
                  starts_with(convert_from(pg_catalog.bpcharsend(target."Path"),'UTF8') COLLATE "C",
                              convert_from(pg_catalog.bpcharsend(root."Path"),'UTF8') COLLATE "C"))
                """,
            4=>"target.\"Id\"=ANY(@creation_custom)",5=>"true",
            _=>throw new OrderPriceCorrectionException("OWNER_SCOPE_MAPPING_UNAVAILABLE",503)
        };
        var sql=$$"""
            WITH RECURSIVE {{PostgreSqlOrganizationV1.HealthyCte(PostgreSqlOrganizationV1.Table(db),"@creation_tenant")}}
            SELECT target."Id",target."DeptCode" AS "Code",target."DeptName" AS "Name"
            FROM healthy_departments target WHERE {{condition}} ORDER BY target."Sort",target."Id"
            """;
        var departments=await db.Database.SqlQueryRaw<StockTakeCreationDepartmentV1>(sql,
            new NpgsqlParameter("creation_tenant",db.CurrentTenantId),
            new NpgsqlParameter("creation_department",authority.Actor.DeptId??Guid.Empty),
            new NpgsqlParameter("creation_custom",authority.Actor.CustomDeptIds.GetValueOrDefault(authority.Resource,[]).ToArray())).ToArrayAsync(ct);
        return new(db.CurrentTenantId,ActorId,CreationVersion(),authority.Scope is 1 or 5,departments);
    }
    internal async Task<NewStockTakeBinding> BeginStockTakeCreation(string version,Guid? department,CancellationToken ct)
    {
        var context=await StockTakeCreationContext(ct);
        if(context.AuthorityVersion!=version)throw new OrderPriceCorrectionException("CREATION_AUTHORITY_CHANGED",412);
        if(department==null?!context.AllowsUnbound:!context.Departments.Any(d=>d.Id==department))
            throw new OrderPriceCorrectionException("CREATION_DEPARTMENT_FORBIDDEN",403);
        var id=Guid.NewGuid();
        if(await db.StockTakes.IgnoreQueryFilters().AnyAsync(m=>m.Id==id,ct))
            throw new OrderPriceCorrectionException("CREATION_ID_UNAVAILABLE",503);
        return new(this,db.Database.CurrentTransaction??throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503),
            db.CurrentTenantId,ActorId,id,department);
    }
    internal async Task CompleteStockTakeCreation(NewStockTakeBinding ticket,CancellationToken ct)
    {
        if(ticket.Consumed || !ReferenceEquals(ticket.Access,this) || !ReferenceEquals(ticket.Transaction,db.Database.CurrentTransaction)
            || ticket.Tenant!=db.CurrentTenantId || ticket.Actor!=ActorId)
            throw new OrderPriceCorrectionException("CREATION_BINDING_FORBIDDEN",403);
        ticket.Consumed=true;
        if(!await db.StockTakes.AsNoTracking().AnyAsync(m=>m.Id==ticket.Id && !m.IsDeleted && m.Creator==Actor,ct))
            throw new OrderPriceCorrectionException("CREATION_BINDING_FORBIDDEN",403);
        if(ticket.Department is Guid department)
        {
            // Recheck every captured baseline before the one authorized transition. Business creation
            // must not have changed any identity/organization mapping before this point.
            var before=await StandaloneDepartmentScopeV1.Version(db,ct);
            if(Authorities.Any(a=>a.OrganizationVersion!=before))throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
            var organization=await OrderDepartmentScopeV1.Version(db,ct);
            var mapping=Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
                VALUES({mapping},{db.CurrentTenantId},'stocktake',{ticket.Id},{department},true)
                """,ct);
            // No service callback, arbitrary SQL, business write or external I/O belongs between the
            // parameterized insert and this transition. Native triggers only validate and bump the root.
            var after=await StandaloneDepartmentScopeV1.Version(db,ct);
            if(before==after || organization!=await OrderDepartmentScopeV1.Version(db,ct))
                throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
            for(var i=0;i<Authorities.Count;i++)Authorities[i]=Authorities[i] with {OrganizationVersion=after};
            var revision=await db.Database.SqlQuery<Guid>($"""
                SELECT revision AS "Value" FROM cp6_operations_owner_v1.department_mappings
                WHERE tenant_id={db.CurrentTenantId} AND id={mapping} AND object_kind='stocktake'
                  AND object_id={ticket.Id} AND department_id={department} AND enabled
                """).SingleAsync(ct);
            AtCommit(async t=>
            {
                if(!await db.Database.SqlQuery<Guid>($"""
                    SELECT revision AS "Value" FROM cp6_operations_owner_v1.department_mappings
                    WHERE tenant_id={db.CurrentTenantId} AND id={mapping} AND object_kind='stocktake'
                      AND object_id={ticket.Id} AND department_id={department} AND enabled
                    """).AnyAsync(r=>r==revision,t))throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
            });
        }
        AtCommit(t=>RequireStockTakeIdentity(ticket.Id,t));
    }
    internal async Task RequireStockTakeIdentity(Guid id,CancellationToken ct)
    {
        if(!await StockTakes().AnyAsync(m=>m.Id==id,ct))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
    }
}
