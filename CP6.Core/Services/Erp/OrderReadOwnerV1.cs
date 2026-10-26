using System.Data;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

/// <summary>Repository-owned ordinary Order read/export/download mapping; never a write grant.</summary>
public sealed partial class OrderReadOwnerV1
{
    internal const string MappingVersion = "Main.Order.Read.Owner.v1";
    private readonly CP6Context db;
    private readonly IHttpContextAccessor http;
    private readonly Func<CancellationToken, Task<Guid>> currentActor;
    private readonly Func<AuthorizationCatalogSnapshotV1> catalog;

    public OrderReadOwnerV1(CP6Context db, IHttpContextAccessor http, Func<CancellationToken, Task<Guid>> currentActor)
        : this(db, http, currentActor, AuthorizationCatalogV1.Capture) { }
    internal OrderReadOwnerV1(CP6Context db, IHttpContextAccessor http, Func<CancellationToken, Task<Guid>> currentActor,
        Func<AuthorizationCatalogSnapshotV1> catalog)
    { this.db = db; this.http = http; this.currentActor = currentActor; this.catalog = catalog; }
    internal bool Owns(CP6Context context) => ReferenceEquals(db, context);

    // Explicit paths include nested purchase/supply prices: a full edit graph requires both fields.
    private static readonly FieldPermissionMappingV1[] Mappings =
        new[] { FieldPermissionUseV1.Read, FieldPermissionUseV1.Export, FieldPermissionUseV1.Download }
        .SelectMany(use => new[] { "individualUnitPrice", "setUnitPrice", "unitPriceUnit" }
            .Select(path => new FieldPermissionMappingV1("UnitPrice", use, "/rows/*/" + path, true, []))
            .Append(new("Amount", use, "/rows/*/amount", true, ["UnitPrice"])))
        .Concat(new[] { "individualUnitPrice", "setUnitPrice", "purchaseUnitPrice", "unitPriceUnit",
            "specialPriceFlg", "provisionalPriceFlg", "priceChangeReason", "processes/*/purchaseUnitPrice",
            "processes/*/fixedPrice", "materials/*/supplyUnitPrice" }
            .Select(path => new FieldPermissionMappingV1("UnitPrice", FieldPermissionUseV1.Read,
                "/order/details/*/" + path, true, [])))
        .Append(new("Amount", FieldPermissionUseV1.Read, "/order/details/*/amount", true, ["UnitPrice"]))
        .ToArray();

    internal sealed record Access(IQueryable<OrderDetail> Rows, IQueryable<Order> Headers, bool Price, bool Amount);
    private sealed record Authority(UserPermissionContext Actor, FieldPermissionSourceV1 Source,
        FieldPermissionDecisionV1 Decision, int Scope, string OrganizationVersion);

    private async Task<Authority> AuthorityAsync(string menu, FieldPermissionUseV1 use, CancellationToken ct)
    {
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
        string Action(FieldPermissionUseV1 value) => value switch
        { FieldPermissionUseV1.Read => "query", FieldPermissionUseV1.Export => "export", FieldPermissionUseV1.Download => "download", _ => "" };
        if (!Can("query") || !Can(Action(use))) throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
        var type = actor.DataScopes.GetValueOrDefault("order", scope.Default);
        if (!scope.Supports.Contains(type) || type is < 1 or > 5 || type is 2 or 3 && actor.DeptId == null)
            throw new OrderPriceCorrectionException("OWNER_SCOPE_MAPPING_UNAVAILABLE", 503);
        var constraints = source.Fields.SelectMany(f => new[] { FieldPermissionUseV1.Read, FieldPermissionUseV1.Export, FieldPermissionUseV1.Download }
            .Select(u => new FieldPermissionConstraintsV1(f, u,
                Can("query") && Can(Action(u)) ? FieldPermissionCheckV1.Allowed : FieldPermissionCheckV1.Denied,
                FieldPermissionCheckV1.Allowed, FieldPermissionCheckV1.Allowed, FieldPermissionCheckV1.NotApplicable,
                FieldPermissionCheckV1.NotApplicable, FieldPermissionCheckV1.NotApplicable, FieldPermissionCheckV1.Allowed)));
        var decision = source.Evaluate(source.Versions(MappingVersion), MappingVersion, Mappings, constraints);
        if (!decision.IsAvailable) throw new CurrentPermissionUnavailableException();
        return new(actor, source, decision, type, await OrderDepartmentScopeV1.Version(db, ct));
    }

    internal async Task<T> ReadAsync<T>(string menu, FieldPermissionUseV1 use, bool includeDeleted,
        Func<Access, CancellationToken, Task<T>> body, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, http.HttpContext?.RequestAborted ?? CancellationToken.None);
        ct = linked.Token;
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null
            || System.Transactions.Transaction.Current != null || db.ChangeTracker.HasChanges())
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            var authority = await AuthorityAsync(menu, use, ct);
            var result = await body(new(
                OrderDepartmentScopeV1.Query(db, authority.Actor, authority.Scope, includeDeleted: includeDeleted, readLocks: true).AsNoTracking(),
                OrderDepartmentScopeV1.Headers(db, authority.Actor, authority.Scope, includeDeleted).AsNoTracking(),
                authority.Decision.Fields["UnitPrice"].Uses[use].Allowed,
                authority.Decision.Fields["Amount"].Uses[use].Allowed), ct);
            var final = await AuthorityAsync(menu, use, ct);
            if (final.Source.SourcePermissionVersion != authority.Source.SourcePermissionVersion
                || final.Source.RegistryVersion != authority.Source.RegistryVersion
                || final.OrganizationVersion != authority.OrganizationVersion || final.Scope != authority.Scope)
                throw new OrderPriceCorrectionException("AUTHORITY_CHANGED", 403);
            if (await currentActor(ct) != authority.Source.SubjectId) throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
            if (final.Source.TenantExpiresAt <= DateTime.Now) throw new OrderPriceCorrectionException("AUTHORITY_CHANGED", 403);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (OrderPriceCorrectionException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) { throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503, error); }
    }
}
