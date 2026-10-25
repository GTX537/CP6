using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Integration;

// Allocated by an actual current Owner after its authorization and native transaction fence.
// It owns only new transaction inheritance; it cannot authorize a business action or rebind an existing object.
internal sealed class StockTransactionInheritanceV1(CP6Context db,Guid actorId,string actor,string resource,string action,
    string initialVersion,Func<string,bool>? acceptsBaseline=null,Action<string>? advanceBaseline=null)
{
    private string expectedVersion=initialVersion;
    private readonly List<Func<CancellationToken,Task>> finals=[];
    internal (string Version,int FinalCount) Checkpoint()=>(expectedVersion,finals.Count);
    internal void RestoreAfterSavepointRollback((string Version,int FinalCount) checkpoint)
    {
        expectedVersion=checkpoint.Version;advanceBaseline?.Invoke(expectedVersion);
        finals.RemoveRange(checkpoint.FinalCount,finals.Count-checkpoint.FinalCount);
    }
    internal async Task ValidateFinalAsync(CancellationToken ct)
    {
        RequireStockInheritanceContext(db);
        foreach(var validate in finals)await validate(ct);
        if(expectedVersion!=await StandaloneDepartmentScopeV1.Version(db,ct))throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
    }
    private readonly IDbContextTransaction? capturedTransaction=db.Database.CurrentTransaction;
    internal void RequireStockInheritanceContext(CP6Context context)
    {
        if(!ReferenceEquals(db,context) || capturedTransaction==null
            || !ReferenceEquals(capturedTransaction,db.Database.CurrentTransaction))
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    }
    internal sealed record StockSourceBinding
    {
        public Guid Id {get;init;}
        public Guid DepartmentId {get;init;}
        public Guid Revision {get;init;}
    }
    internal sealed class NewStockTransactionBinding(StockTransactionInheritanceV1 access,Guid tenant,Guid actor,Guid stockId,StockSourceBinding? source)
    {
        internal StockTransactionInheritanceV1 Access {get;}=access;
        internal Guid Tenant {get;}=tenant;
        internal Guid Actor {get;}=actor;
        internal Guid StockId {get;}=stockId;
        internal StockSourceBinding? Source {get;}=source;
        internal Guid TransactionId {get;}=Guid.NewGuid();
        internal bool Consumed {get;set;}
    }
    private Task<StockSourceBinding?> StockBinding(Guid stockId,CancellationToken ct)
    {
        var sql=$$"""
            WITH RECURSIVE {{PostgreSqlOrganizationV1.HealthyCte(PostgreSqlOrganizationV1.Table(db),"@inherit_tenant")}}
            SELECT m.id AS "Id",m.department_id AS "DepartmentId",m.revision AS "Revision"
            FROM cp6_operations_owner_v1.department_mappings m
            JOIN healthy_departments d ON d."Id"=m.department_id
            WHERE m.tenant_id=@inherit_tenant AND m.object_kind='stock' AND m.object_id=@inherit_stock AND m.enabled
            """;
        return db.Database.SqlQueryRaw<StockSourceBinding>(sql,new NpgsqlParameter("inherit_tenant",db.CurrentTenantId),
            new NpgsqlParameter("inherit_stock",stockId)).SingleOrDefaultAsync(ct);
    }
    // Called by the actual movement primitive with the Stock entity it is about to mutate.
    // This does not infer ownership for historical transactions or grant a new Stock action to a parent Owner.
    internal async Task<NewStockTransactionBinding> BeginStockTransaction(Stock stock,CancellationToken ct)
    {
        RequireStockInheritanceContext(db);
        StockSourceBinding? source=null;
        if(db.Entry(stock).State!=EntityState.Added)
        {
            if(db.Entry(stock).State==EntityState.Detached || stock.TenantId!=db.CurrentTenantId
                || !await db.Stocks.AsNoTracking().AnyAsync(s=>s.Id==stock.Id && !s.IsDeleted,ct))
                throw new OrderPriceCorrectionException("STOCK_INHERITANCE_FORBIDDEN",403);
            source=await StockBinding(stock.Id,ct);
        }
        return new(this,db.CurrentTenantId,actorId,stock.Id,source);
    }
    internal async Task CompleteStockTransaction(NewStockTransactionBinding ticket,CancellationToken ct)
    {
        RequireStockInheritanceContext(db);
        if(ticket.Consumed || !ReferenceEquals(ticket.Access,this) || ticket.Tenant!=db.CurrentTenantId || ticket.Actor!=actorId)
            throw new OrderPriceCorrectionException("STOCK_INHERITANCE_FORBIDDEN",403);
        ticket.Consumed=true;
        if(!await db.StockTransactions.AsNoTracking().AnyAsync(t=>t.Id==ticket.TransactionId && !t.IsDeleted
            && t.Creator==actor && t.OperatorCd==actor,ct))throw new OrderPriceCorrectionException("STOCK_INHERITANCE_FORBIDDEN",403);
        // Unbound/new Stock keeps its existing legal behavior. It needs its own explicit creation contract.
        if(ticket.Source is not {} source)return;
        if(source!=await StockBinding(ticket.StockId,ct))throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
        var before=await StandaloneDepartmentScopeV1.Version(db,ct);
        if(expectedVersion!=before || acceptsBaseline?.Invoke(before)==false)throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
        var organization=await OrderDepartmentScopeV1.Version(db,ct);var mapping=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
            VALUES({mapping},{ticket.Tenant},'stock-transaction',{ticket.TransactionId},{source.DepartmentId},true)
            """,ct);
        // Only this parameterized new-object insert belongs in the version transition. No callback or business write.
        var after=await StandaloneDepartmentScopeV1.Version(db,ct);
        if(before==after || organization!=await OrderDepartmentScopeV1.Version(db,ct))
            throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
        expectedVersion=after;advanceBaseline?.Invoke(after);
        var revision=await db.Database.SqlQuery<Guid>($"""
            SELECT revision AS "Value" FROM cp6_operations_owner_v1.department_mappings
            WHERE tenant_id={ticket.Tenant} AND id={mapping} AND object_kind='stock-transaction'
              AND object_id={ticket.TransactionId} AND department_id={source.DepartmentId} AND enabled
            """).SingleAsync(ct);
        db.Sys_SecurityLogs.Add(new Sys_SecurityLog {TenantId=ticket.Tenant,UserId=actorId,UserName=actor,
            EventType=(int)SecurityEventType.IamAdministrationChanged,CreatedAt=DateTime.Now,
            Reason="New stock transaction inherited its actual Stock department",
            ContextJson=JsonSerializer.Serialize(new {kind="stock-transaction-inheritance.v1",stockId=ticket.StockId,
                sourceMappingId=source.Id,sourceMappingRevision=source.Revision,transactionId=ticket.TransactionId,
                departmentId=source.DepartmentId,mappingId=mapping,mappingRevision=revision,
                resource,action})});
        await db.SaveChangesAsync(ct);
        // Register on the actual root access, including delegated quality receipts, not an unexecuted child callback list.
        finals.Add(async t=>
        {
            if(source!=await StockBinding(ticket.StockId,t)
                || !await db.Stocks.AsNoTracking().AnyAsync(s=>s.Id==ticket.StockId && !s.IsDeleted,t)
                || !await db.StockTransactions.AsNoTracking().AnyAsync(x=>x.Id==ticket.TransactionId && !x.IsDeleted
                    && x.Creator==actor && x.OperatorCd==actor,t)
                || !await db.Database.SqlQuery<Guid>($"""
                    SELECT revision AS "Value" FROM cp6_operations_owner_v1.department_mappings
                    WHERE tenant_id={ticket.Tenant} AND id={mapping} AND object_kind='stock-transaction'
                      AND object_id={ticket.TransactionId} AND department_id={source.DepartmentId} AND enabled
                    """).AnyAsync(r=>r==revision,t))throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
        });
    }
}
