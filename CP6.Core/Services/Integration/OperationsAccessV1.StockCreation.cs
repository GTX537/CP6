using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal sealed class NewStockBinding(OperationsAccessV1 access,IDbContextTransaction transaction,Guid tenant,
        Guid actor,Guid id,Guid? department,StockCreationKeyV2 key)
    {
        internal OperationsAccessV1 Access {get;}=access;
        internal IDbContextTransaction Transaction {get;}=transaction;
        internal Guid Tenant {get;}=tenant;
        internal Guid Actor {get;}=actor;
        internal Guid Id {get;}=id;
        internal Guid? Department {get;}=department;
        internal StockCreationKeyV2 Key {get;}=key;
        internal bool Consumed {get;set;}
    }
    internal async Task<StockCreationContextV2> StockCreationContext(StockCreationKeyV2 key,CancellationToken ct)
    {
        if(authority.Resource!=OperationsOwnerV1.Stock || authority.Action is not ("adjust" or "move"))
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN",403);
        if(string.IsNullOrWhiteSpace(key.WarehouseCd) || string.IsNullOrWhiteSpace(key.LocationCd)
            || string.IsNullOrWhiteSpace(key.ProductCd) || key.LotNo==null)
            throw new OrderPriceCorrectionException("STOCK_CREATION_KEY_REQUIRED",400);
        await RequireLocation(key.WarehouseCd,key.LocationCd,ct);
        var rows=db.Stocks.AsNoTracking().Where(s=>s.WarehouseCd==key.WarehouseCd && s.LocationCd==key.LocationCd
            && s.ProductCd==key.ProductCd && s.LotNo==key.LotNo);
        if(await rows.AnyAsync(ct))
        {
            // A deleted or inaccessible historical key is not a new-stock creation opportunity.
            if(!await InventoryStocks(rows.Where(s=>!s.IsDeleted)).AnyAsync(ct))
                throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
            return new(db.CurrentTenantId,ActorId,CreationVersion(),false,authority.Scope is 1 or 5,[]);
        }
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
        var departments=await db.Database.SqlQueryRaw<StockCreationDepartmentV2>(sql,
            new NpgsqlParameter("creation_tenant",db.CurrentTenantId),
            new NpgsqlParameter("creation_department",authority.Actor.DeptId??Guid.Empty),
            new NpgsqlParameter("creation_custom",authority.Actor.CustomDeptIds.GetValueOrDefault(authority.Resource,[]).ToArray())).ToArrayAsync(ct);
        return new(db.CurrentTenantId,ActorId,CreationVersion(),true,authority.Scope is 1 or 5,departments);
    }
    internal async Task<NewStockBinding?> BeginStockCreation(StockCreationKeyV2 key,StockCreationIntentV2? intent,CancellationToken ct)
    {
        var context=await StockCreationContext(key,ct);
        if(!context.RequiresCreation)
        {
            if(intent!=null)throw new OrderPriceCorrectionException("STOCK_ALREADY_EXISTS",409);
            return null;
        }
        if(intent==null)throw new OrderPriceCorrectionException("STOCK_CREATION_INTENT_REQUIRED",403);
        if(context.AuthorityVersion!=intent.ExpectedAuthorityVersion)throw new OrderPriceCorrectionException("CREATION_AUTHORITY_CHANGED",412);
        if(intent.DepartmentId==null?!context.AllowsUnbound:!context.Departments.Any(d=>d.Id==intent.DepartmentId))
            throw new OrderPriceCorrectionException("CREATION_DEPARTMENT_FORBIDDEN",403);
        if(stockInheritance!=null)throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
        var id=Guid.NewGuid();
        if(await db.Stocks.IgnoreQueryFilters().AnyAsync(s=>s.Id==id,ct))throw new OrderPriceCorrectionException("CREATION_ID_UNAVAILABLE",503);
        return new(this,db.Database.CurrentTransaction??throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503),
            db.CurrentTenantId,ActorId,id,intent.DepartmentId,key);
    }
    internal async Task CompleteStockCreation(NewStockBinding ticket,CancellationToken ct)
    {
        if(ticket.Consumed || !ReferenceEquals(ticket.Access,this) || !ReferenceEquals(ticket.Transaction,db.Database.CurrentTransaction)
            || ticket.Tenant!=db.CurrentTenantId || ticket.Actor!=ActorId || stockInheritance!=null)
            throw new OrderPriceCorrectionException("CREATION_BINDING_FORBIDDEN",403);
        ticket.Consumed=true;var key=ticket.Key;
        if(!await db.Stocks.AsNoTracking().AnyAsync(s=>s.Id==ticket.Id && !s.IsDeleted && s.Creator==Actor
            && s.WarehouseCd==key.WarehouseCd && s.LocationCd==key.LocationCd && s.ProductCd==key.ProductCd && s.LotNo==key.LotNo,ct))
            throw new OrderPriceCorrectionException("CREATION_BINDING_FORBIDDEN",403);
        if(ticket.Department is Guid department)
        {
            var before=await StandaloneDepartmentScopeV1.Version(db,ct);
            if(Authorities.Any(a=>a.OrganizationVersion!=before))throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
            var organization=await OrderDepartmentScopeV1.Version(db,ct);var mapping=Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
                VALUES({mapping},{ticket.Tenant},'stock',{ticket.Id},{department},true)
                """,ct);
            // Only this new-object insert may advance the baseline; never absorb other writes or callbacks.
            var after=await StandaloneDepartmentScopeV1.Version(db,ct);
            if(before==after || organization!=await OrderDepartmentScopeV1.Version(db,ct))
                throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
            for(var i=0;i<Authorities.Count;i++)Authorities[i]=Authorities[i] with {OrganizationVersion=after};
            var revision=await db.Database.SqlQuery<Guid>($"""
                SELECT revision AS "Value" FROM cp6_operations_owner_v1.department_mappings
                WHERE tenant_id={ticket.Tenant} AND id={mapping} AND object_kind='stock'
                  AND object_id={ticket.Id} AND department_id={department} AND enabled
                """).SingleAsync(ct);
            db.Sys_SecurityLogs.Add(new(){TenantId=ticket.Tenant,UserId=ActorId,UserName=Actor,
                EventType=(int)SecurityEventType.IamAdministrationChanged,CreatedAt=DateTime.Now,
                Reason="New Stock assigned its explicit current department",
                ContextJson=JsonSerializer.Serialize(new{kind="stock-creation.v2",stockId=ticket.Id,departmentId=department,
                    mappingId=mapping,mappingRevision=revision,resource=authority.Resource,action=authority.Action})});
            await db.SaveChangesAsync(ct);
            AtCommit(async t=>
            {
                if(!await db.Database.SqlQuery<Guid>($"""
                    SELECT revision AS "Value" FROM cp6_operations_owner_v1.department_mappings
                    WHERE tenant_id={ticket.Tenant} AND id={mapping} AND object_kind='stock'
                      AND object_id={ticket.Id} AND department_id={department} AND enabled
                    """).AnyAsync(r=>r==revision,t))throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
            });
        }
        AtCommit(async t=>
        {
            if(!await InventoryStocks().AnyAsync(s=>s.Id==ticket.Id && s.WarehouseCd==key.WarehouseCd
                && s.LocationCd==key.LocationCd && s.ProductCd==key.ProductCd && s.LotNo==key.LotNo,t))
                throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
        });
    }
}
