using System.Data;
using CP6.Core.Services.Integration;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Erp;

/// <summary>Ordinary Order create/edit/delete authority, independent of read and trusted C03 integration.</summary>
public sealed partial class OrderWriteOwnerV1
{
    internal const string MappingVersion = "Main.Order.Write.Owner.v1";
    private readonly CP6Context db;
    private readonly IHttpContextAccessor http;
    private readonly Func<CancellationToken, Task<Guid>> currentActor;
    private readonly Func<AuthorizationCatalogSnapshotV1> catalog;
    public OrderWriteOwnerV1(CP6Context db, IHttpContextAccessor http, Func<CancellationToken, Task<Guid>> currentActor)
        : this(db, http, currentActor, AuthorizationCatalogV1.Capture) { }
    internal OrderWriteOwnerV1(CP6Context db, IHttpContextAccessor http, Func<CancellationToken, Task<Guid>> currentActor,
        Func<AuthorizationCatalogSnapshotV1> catalog)
    { this.db=db; this.http=http; this.currentActor=currentActor; this.catalog=catalog; }
    internal bool Owns(CP6Context context) => ReferenceEquals(db, context);
    private static readonly FieldPermissionMappingV1[] Mappings =
        new[] { "individualUnitPrice", "setUnitPrice", "purchaseUnitPrice", "unitPriceUnit", "specialPriceFlg",
            "provisionalPriceFlg", "priceChangeReason", "processes/*/purchaseUnitPrice", "processes/*/fixedPrice", "materials/*/supplyUnitPrice" }
        .Select(path => new FieldPermissionMappingV1("UnitPrice", FieldPermissionUseV1.Write, "/details/*/"+path, true, []))
        .Append(new("Amount", FieldPermissionUseV1.Write, "/details/*/amount", true, ["UnitPrice"])).ToArray();
    private sealed record Authority(UserPermissionContext Actor, FieldPermissionSourceV1 Source,
        FieldPermissionDecisionV1 Decision, int Scope, string OrganizationVersion);
    internal sealed record Result(string Number, string UserName, OperationsDelegationV1? Delegation = null);

    private async Task<Authority> AuthorityAsync(string action, CancellationToken ct)
    {
        const string menu = "erp-order";
        var authenticated = await currentActor(ct);
        var definition = catalog();
        if (!definition.IsSealed || !definition.Scopes.TryGetValue("order", out var scope))
            throw new CurrentPermissionUnavailableException();
        var source = await new CurrentFieldPermissionSourceReaderV1(db, http, () => definition).ReadAsync("order", ct);
        if (source.SubjectId != authenticated) throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
        if (!source.Fields.Order(StringComparer.Ordinal).SequenceEqual(new[] { "Amount", "UnitPrice" }))
            throw new CurrentPermissionUnavailableException();
        var actor = await PostgreSqlPermissionReader.ReadAsync(db, source.SubjectId, ct);
        if (!actor.RoleIds.Order().SequenceEqual(source.Roles.Select(r => r.RoleId).Order()))
            throw new CurrentPermissionUnavailableException();
        var actions = await (from a in db.Sys_MenuActions.AsNoTracking()
                             join m in db.Sys_Menus.AsNoTracking() on a.MenuId equals m.MenuId
                             where m.Enable && m.MenuKey == menu select a.ActionCode).ToArrayAsync(ct);
        bool Can(string action) => actor.ActionKeys.Contains(menu + ":" + action) && actions.Contains(action, StringComparer.Ordinal);
        if (!Can(action)) throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
        var type = actor.DataScopes.GetValueOrDefault("order", scope.Default);
        if (!scope.Supports.Contains(type) || type is < 1 or > 5 || type is 2 or 3 && actor.DeptId == null)
            throw new OrderPriceCorrectionException("OWNER_SCOPE_MAPPING_UNAVAILABLE", 503);
        var constraints = source.Fields.SelectMany(f => new[] { FieldPermissionUseV1.Write }
            .Select(u => new FieldPermissionConstraintsV1(f, u,
                Can(action) ? FieldPermissionCheckV1.Allowed : FieldPermissionCheckV1.Denied,
                FieldPermissionCheckV1.Allowed, FieldPermissionCheckV1.Allowed, FieldPermissionCheckV1.NotApplicable,
                FieldPermissionCheckV1.NotApplicable, FieldPermissionCheckV1.NotApplicable, FieldPermissionCheckV1.Allowed)));
        var mapping = action == "cancel" ? "Main.Order.Cancel.Owner.v1" : MappingVersion;
        var decision = source.Evaluate(source.Versions(mapping), mapping, Mappings, constraints);
        if (!decision.IsAvailable) throw new CurrentPermissionUnavailableException();
        return new(actor, source, decision, type, await OrderDepartmentScopeV1.Version(db, ct));
    }

    private static bool Business(object entity) => entity is Order or OrderDetail or OrderProcess or OrderProcessNote or OrderMaterial;
    private static bool CancellationBusiness(object entity) => entity is WorkOrder or OutboundOrder or OutboundOrderDetail or Stock or StockTransaction or WmsSequence;
    internal Task<Result> ExecuteAsync(string action, string? number, OrderDto? request,
        Func<OrderDto?, string, Task<string>> body, CancellationToken ct = default)
        =>ExecuteCoreAsync(action,number,request,(dto,actor,_)=>body(dto,actor),ct);
    internal Task<Result> ExecuteCancellationAsync(string number,Func<string,StockTransactionInheritanceV1,Task<string>> body,
        CancellationToken ct=default)=>ExecuteCoreAsync("cancel",number,null,(_,actor,inheritance)=>body(actor,inheritance!),ct);
    private async Task<Result> ExecuteCoreAsync(string action,string? number,OrderDto? request,
        Func<OrderDto?,string,StockTransactionInheritanceV1?,Task<string>> body,CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, http.HttpContext?.RequestAborted ?? CancellationToken.None);
        ct=linked.Token; ct.ThrowIfCancellationRequested();
        if (action is not ("add" or "edit" or "del" or "cancel") || !db.Database.IsNpgsql() || db.Database.CurrentTransaction != null
            || System.Transactions.Transaction.Current != null || db.ChangeTracker.HasChanges()
            || db.ChangeTracker.Entries().Any(e => Business(e.Entity) || action == "cancel" && CancellationBusiness(e.Entity)))
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        // Freeze the entire nested graph before any authority or row-lock wait.
        var frozen = request == null ? null : JsonSerializer.Deserialize<OrderDto>(JsonSerializer.SerializeToUtf8Bytes(request));
        if (action is not ("del" or "cancel") && (frozen?.Details == null || frozen.Details.Count is < 1 or > OrderService.MaxDetailLimit
            || frozen.Details.Any(d => d == null || d.Processes == null || d.ProcessNotes == null || d.Materials == null
                || d.Processes.Any(p => p == null) || d.ProcessNotes.Any(p => p == null) || d.Materials.Any(p => p == null))
            || string.IsNullOrWhiteSpace(frozen.CustomerCd) || string.IsNullOrWhiteSpace(frozen.OrderType)))
            throw new OrderPriceCorrectionException("ORDER_WRITE_INVALID", 400);
        var original = db.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        var committing=false;
        try
        {
            tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db,ct);
            var authority=await AuthorityAsync(action,ct);
            if (string.IsNullOrWhiteSpace(authority.Actor.UserName)) throw new OrderPriceCorrectionException("REAUTH_REQUIRED",401);
            RequireFields(authority,action);
            if (action != "add")
            {
                if (string.IsNullOrWhiteSpace(number)) throw new OrderPriceCorrectionException("ORDER_WRITE_INVALID",400);
                await LockGraph(number,ct);
                await RequireObject(authority,number,false,ct);
            }
            var inheritance=action=="cancel"?new StockTransactionInheritanceV1(db,authority.Source.SubjectId,
                authority.Actor.UserName,"order","cancel",await StandaloneDepartmentScopeV1.Version(db,ct)):null;
            var saved=await body(frozen,authority.Actor.UserName,inheritance);
            if(inheritance!=null)await inheritance.ValidateFinalAsync(ct);
            var final=await AuthorityAsync(action,ct);
            if (final.Source.SourcePermissionVersion!=authority.Source.SourcePermissionVersion
                || final.Source.RegistryVersion!=authority.Source.RegistryVersion
                || final.OrganizationVersion!=authority.OrganizationVersion || final.Scope!=authority.Scope)
                throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
            RequireFields(final,action);
            // Checks persisted proposed department and all effects as well as the old graph.
            // Scope1/5 retain their established self/tenant meaning; department scopes never infer a mapping.
            await RequireObject(final,saved,action=="del",ct);
            if (await currentActor(ct)!=authority.Source.SubjectId) throw new OrderPriceCorrectionException("REAUTH_REQUIRED",401);
            if (final.Source.TenantExpiresAt<=DateTime.Now) throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
            ct.ThrowIfCancellationRequested(); committing=true;
            await tx.CommitAsync(ct);
            return new(saved,authority.Actor.UserName,action=="add"?new(db,db.CurrentTenantId,authority.Source.SubjectId,
                authority.Actor.UserName,"order","add",saved,authority.Source.SourcePermissionVersion):null);
        }
        catch (Exception error)
        {
            Exception? rollbackFailure=null;
            if (tx!=null) try { await tx.RollbackAsync(CancellationToken.None); } catch(Exception failure) { rollbackFailure=failure; }
            if (committing || rollbackFailure!=null)
                throw new OrderPriceCorrectionException(action == "cancel" ? "ORDER_CANCEL_OUTCOME_UNKNOWN" : "ORDER_WRITE_OUTCOME_UNKNOWN",503,
                    rollbackFailure==null ? error : new AggregateException(error,rollbackFailure));
            if (error is OrderPriceCorrectionException or DbUpdateConcurrencyException) throw;
            if (error is OperationCanceledException && ct.IsCancellationRequested) throw;
            if (error is InvalidOperationException && error is not CurrentPermissionUnavailableException) throw;
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503,error);
        }
        finally
        {
            foreach(var entry in db.ChangeTracker.Entries().ToArray())
                if(!original.Contains(entry.Entity)) entry.State=EntityState.Detached;
            if(tx!=null) await tx.DisposeAsync();
        }
    }
    private static void RequireFields(Authority authority,string action)
    {
        // Deletion/cancellation have independent actions and never write price/amount values.
        // Cancellation delegates only stored reservation release and state transitions; no price data is returned.
        if(action is not ("del" or "cancel") && (!authority.Decision.Fields["UnitPrice"].CanWrite || !authority.Decision.Fields["Amount"].CanWrite))
            throw new OrderPriceCorrectionException("FIELD_WRITE_FORBIDDEN",403);
    }
    private async Task RequireObject(Authority authority,string number,bool deleted,CancellationToken ct)
    {
        if(!await db.Orders.AsNoTracking().AnyAsync(o=>o.WebOrderNo==number && (deleted || !o.IsDeleted),ct)
            || !await OrderDepartmentScopeV1.Headers(db,authority.Actor,authority.Scope,true).AsNoTracking().AnyAsync(o=>o.WebOrderNo==number,ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
        var allowed=OrderDepartmentScopeV1.Query(db,authority.Actor,authority.Scope,includeDeleted:true).Select(d=>d.Id);
        if(await db.OrderDetails.AsNoTracking().AnyAsync(d=>d.WebOrderNo==number && !allowed.Contains(d.Id),ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
    }
    private async Task LockGraph(string number,CancellationToken ct)
    {
        await Lock<Order>(number,ct); await Lock<OrderDetail>(number,ct);
        await Lock<OrderProcess>(number,ct); await Lock<OrderProcessNote>(number,ct); await Lock<OrderMaterial>(number,ct);
    }
    private async Task Lock<T>(string number,CancellationToken ct) where T:class
    {
        var entity=db.Model.FindEntityType(typeof(T))!;
        var table=db.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!,entity.GetSchema()??"public");
        await db.Set<T>().FromSqlRaw($"SELECT * FROM {table} WHERE \"TenantId\"=@tenant AND \"WebOrderNo\"=CAST(@number AS bpchar) ORDER BY \"Id\" FOR UPDATE",
            new NpgsqlParameter("tenant",db.CurrentTenantId),new NpgsqlParameter("number",number)).AsNoTracking().ToArrayAsync(ct);
    }
}
