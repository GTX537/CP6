using System.Data;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Erp;

/// <summary>
/// Main ERP Owner adapter for PA090 reads and writes. The HTTP authorization filter is an early
/// check, never the authority for this commit. No remote effects execute inside this gate.
/// </summary>
public sealed partial class OrderPriceCorrectionOwnerV1
{
    internal const string MappingVersion = "Main.Order.PriceCorrection.Owner.v2";
    private readonly CP6Context db;
    private readonly IHttpContextAccessor http;
    private readonly Func<CancellationToken, Task<Guid>> currentActor;
    private readonly Func<AuthorizationCatalogSnapshotV1> catalog;

    public OrderPriceCorrectionOwnerV1(CP6Context db, IHttpContextAccessor http,
        Func<CancellationToken, Task<Guid>> currentActor)
        : this(db, http, currentActor, AuthorizationCatalogV1.Capture) { }

    internal OrderPriceCorrectionOwnerV1(CP6Context db, IHttpContextAccessor http,
        Func<CancellationToken, Task<Guid>> currentActor,
        Func<AuthorizationCatalogSnapshotV1> catalog)
    { this.db = db; this.http = http; this.catalog = catalog; this.currentActor = currentActor; }

    internal bool Owns(CP6Context context) => ReferenceEquals(db, context);

    // Owner-controlled, versioned PA090 paths. No export/download grant is supplied.
    // Approval here means this bounded repository-owned adapter, not external tenant approval.
    private static readonly FieldPermissionMappingV1[] Mappings =
    [
        new("UnitPrice", FieldPermissionUseV1.Write, "/items/*/individualUnitPriceAfter", true, []),
        new("UnitPrice", FieldPermissionUseV1.Write, "/items/*/setUnitPriceAfter", true, []),
        new("UnitPrice", FieldPermissionUseV1.Write, "/items/*/specialPriceFlg", true, []),
        new("UnitPrice", FieldPermissionUseV1.Write, "/items/*/priceChangeReason", true, []),
        new("UnitPrice", FieldPermissionUseV1.Write, "/effects/orderDetails/*/setUnitPrice", true, []),
        new("UnitPrice", FieldPermissionUseV1.Write, "/effects/orderDetails/*/provisionalPriceFlg", true, []),
        new("Amount", FieldPermissionUseV1.Write, "/effects/orderDetails/*/amount", true, ["UnitPrice"]),
        new("UnitPrice", FieldPermissionUseV1.Read, "/rows/*/individualUnitPriceBefore", true, []),
        new("UnitPrice", FieldPermissionUseV1.Read, "/rows/*/individualUnitPriceAfter", true, []),
        new("UnitPrice", FieldPermissionUseV1.Read, "/rows/*/setUnitPriceBefore", true, []),
        new("UnitPrice", FieldPermissionUseV1.Read, "/rows/*/setUnitPriceAfter", true, []),
        new("UnitPrice", FieldPermissionUseV1.Read, "/rows/*/specialPriceFlg", true, []),
        new("UnitPrice", FieldPermissionUseV1.Read, "/rows/*/provisionalPriceFlg", true, []),
        new("UnitPrice", FieldPermissionUseV1.Read, "/rows/*/priceChangeReason", true, []),
        new("Amount", FieldPermissionUseV1.Read, "/rows/*/amount", true, ["UnitPrice"]),
        new("Amount", FieldPermissionUseV1.Read, "/calculation/amount", true, ["UnitPrice"])
    ];

    private sealed record Authority(UserPermissionContext Actor, FieldPermissionSourceV1 Source,
        FieldPermissionDecisionV1 Decision, int Scope, string OrganizationVersion);

    private async Task<Authority> ReadAuthority(CancellationToken ct, bool reading = false)
    {
        try
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
            var operations = await (from action in db.Sys_MenuActions.AsNoTracking()
                                    join menu in db.Sys_Menus.AsNoTracking() on action.MenuId equals menu.MenuId
                                    where menu.Enable && menu.MenuKey == "erp-order-price-correction"
                                    select new { menu.MenuKey, action.ActionCode }).ToArrayAsync(ct);
            bool Can(string action) => actor.ActionKeys.Contains("erp-order-price-correction:" + action)
                && operations.Any(x => x.MenuKey == "erp-order-price-correction" && x.ActionCode == action);
            if (!Can(reading ? "query" : "correct"))
                throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
            var type = actor.DataScopes.GetValueOrDefault("order", scope.Default);
            if (!scope.Supports.Contains(type) || type is < 1 or > 5
                || type is 2 or 3 && actor.DeptId == null)
                throw new OrderPriceCorrectionException("OWNER_SCOPE_MAPPING_UNAVAILABLE", 503);
            var constraints = source.Fields.SelectMany(f => new[] { FieldPermissionUseV1.Read, FieldPermissionUseV1.Write }
                .Select(use => new FieldPermissionConstraintsV1(f, use,
                Can(use == FieldPermissionUseV1.Read ? "query" : "correct") ? FieldPermissionCheckV1.Allowed : FieldPermissionCheckV1.Denied,
                FieldPermissionCheckV1.Allowed,
                FieldPermissionCheckV1.Allowed, FieldPermissionCheckV1.NotApplicable,
                FieldPermissionCheckV1.NotApplicable, FieldPermissionCheckV1.NotApplicable,
                FieldPermissionCheckV1.Allowed)));
            var decision = source.Evaluate(source.Versions(MappingVersion), MappingVersion, Mappings, constraints);
            if (!decision.IsAvailable) throw new CurrentPermissionUnavailableException();
            return new(actor, source, decision, type, await OrderDepartmentScopeV1.Version(db, ct));
        }
        catch (CurrentPermissionUnavailableException error)
        { throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503, error); }
    }

    private sealed record ObjectScope(HashSet<Guid> Allowed, HashSet<Guid> Mapped);
    private async Task<ObjectScope> Objects(Authority authority, Guid[] ids, CancellationToken ct)
    {
        var allowed = (await OrderDepartmentScopeV1.Query(db, authority.Actor, authority.Scope).AsNoTracking()
            .Where(d => ids.Contains(d.Id)).Select(d => d.Id).ToArrayAsync(ct)).ToHashSet();
        var mapped = authority.Scope is 2 or 3 or 4
            ? (await OrderDepartmentScopeV1.Query(db, authority.Actor, authority.Scope, true).AsNoTracking()
                .Where(d => ids.Contains(d.Id)).Select(d => d.Id).ToArrayAsync(ct)).ToHashSet() : allowed;
        return new(allowed, mapped);
    }
    private static void RequireObject(Authority authority, OrderDetail row, ObjectScope objects)
    {
        if (row.TenantId != authority.Source.TenantId || row.IsDeleted
            || (authority.Scope is 1 or 5 && !objects.Allowed.Contains(row.Id)))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
        if (authority.Scope is 2 or 3 or 4 && !objects.Mapped.Contains(row.Id))
            throw new OrderPriceCorrectionException("OWNER_SCOPE_MAPPING_UNAVAILABLE", 503);
        if (!objects.Allowed.Contains(row.Id)) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }

    private static void RequireWrite(Authority authority, string field)
    {
        if (!authority.Decision.Fields[field].CanWrite)
            throw new OrderPriceCorrectionException("FIELD_WRITE_FORBIDDEN", 403);
    }

    public async Task<OrderPriceCorrectionBatchResultDto> ExecuteAsync(OrderPriceCorrectionBatchUpdateDto request,
        CancellationToken ct = default)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct,
            http.HttpContext?.RequestAborted ?? CancellationToken.None);
        ct = cancellation.Token;
        ct.ThrowIfCancellationRequested();
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null
            || db.ChangeTracker.HasChanges()
            || db.ChangeTracker.Entries().Any(e => e.Entity is Order or OrderDetail))
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        if (request?.Items == null || request.Items.Any(x => x == null || string.IsNullOrWhiteSpace(x.WebOrderNo)))
            throw new OrderPriceCorrectionException("PRICE_CORRECTION_INVALID", 400);
        // Freeze the request before waiting on authority/business locks.
        var items = request.Items.Select(x => new OrderPriceCorrectionUpdateDto
        {
            WebOrderNo = x.WebOrderNo, WebOrderDetailNo = x.WebOrderDetailNo,
            IndividualUnitPriceAfter = x.IndividualUnitPriceAfter, SetUnitPriceAfter = x.SetUnitPriceAfter,
            SpecialPriceFlg = x.SpecialPriceFlg, PriceChangeReason = x.PriceChangeReason, RowVersion = x.RowVersion?.ToArray()
        }).ToArray();
        var original = db.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var committing = false;
        try
        {
            // Same transaction root as ordinary/raw-SQL authority writers; retain through COMMIT.
            // Explicit READ COMMITTED prevents a caller's old repeatable-read authorization snapshot.
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            var authority = await ReadAuthority(ct);
            var result = new OrderPriceCorrectionBatchResultDto { WorkflowDeliveryState = "AwaitingAdapter" };
            var numbers = items.Select(x => x.WebOrderNo).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var rows = new List<OrderDetail>();
            if (numbers.Length != 0)
            {
                string Table<T>()
                {
                    var entity = db.Model.FindEntityType(typeof(T))!;
                    return db.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema() ?? "public");
                }
                // Lock parent rows first: concurrent FK inserts cannot add an unexamined sibling.
                var headers = await db.Orders.FromSqlRaw($"""
                    SELECT * FROM {Table<Order>()} WHERE "TenantId"=@tenant AND "WebOrderNo"=ANY(CAST(@numbers AS bpchar[]))
                    AND NOT "IsDeleted" ORDER BY "WebOrderNo" FOR UPDATE
                    """, new NpgsqlParameter("tenant", db.CurrentTenantId), new NpgsqlParameter("numbers", numbers))
                    .AsNoTracking().ToArrayAsync(ct);
                var present = headers.Select(x => x.WebOrderNo).ToArray();
                if (present.Length != 0)
                    rows = (await db.OrderDetails.FromSqlRaw($"""
                        SELECT * FROM {Table<OrderDetail>()} WHERE "TenantId"=@tenant AND "WebOrderNo"=ANY(CAST(@numbers AS bpchar[]))
                        ORDER BY "WebOrderNo","WebOrderDetailNo" FOR UPDATE
                        """, new NpgsqlParameter("tenant", db.CurrentTenantId), new NpgsqlParameter("numbers", present))
                        .ToListAsync(ct)).Where(d => !d.IsDeleted).ToList();
                // Deleted siblings stay locked too: an ordinary IsDeleted=false update does
                // not change the FK and otherwise could evade the parent's insert fence.
            }
            var byId = rows.ToDictionary(d => d.Id);
            var ids = byId.Keys.ToArray();
            // Preserve the established database semantics of Creator/UserName and business keys,
            // including the provider's case-insensitive collation and bpchar trailing spaces.
            var objects = await Objects(authority, ids, ct);
            var affected = new HashSet<OrderDetail>();
            var selected = new HashSet<OrderDetail>();
            var propagation = new List<(string Number, decimal Price)>();
            foreach (var item in items)
            {
                var id = await db.OrderDetails.AsNoTracking().Where(d => d.WebOrderNo == item.WebOrderNo
                    && d.WebOrderDetailNo == item.WebOrderDetailNo && !d.IsDeleted).Select(d => (Guid?)d.Id).SingleOrDefaultAsync(ct);
                if (id is null || !byId.TryGetValue(id.Value, out var row))
                { result.ConflictedKeys.Add($"{item.WebOrderNo}-{item.WebOrderDetailNo}"); continue; }
                RequireObject(authority, row, objects);
                if (item.RowVersion is { Length: > 0 } version
                    && !version.AsSpan().SequenceEqual(db.Entry(row).OriginalValues.GetValue<byte[]>(nameof(row.RowVersion))))
                    throw new DbUpdateConcurrencyException("Order price correction version conflict.");
                row.IndividualUnitPrice = item.IndividualUnitPriceAfter;
                row.SetUnitPrice = item.SetUnitPriceAfter;
                row.SpecialPriceFlg = item.SpecialPriceFlg;
                row.PriceChangeReason = item.PriceChangeReason;
                row.ProvisionalPriceFlg = false;
                selected.Add(row); affected.Add(row); result.UpdatedCount++;
                // Existing PA090 rule: the last supplied non-null set price propagates within the order.
                if (item.SetUnitPriceAfter is decimal set) propagation.Add((item.WebOrderNo, set));
            }
            foreach (var change in propagation)
            {
                var siblings = await db.OrderDetails.AsNoTracking().Where(d => d.WebOrderNo == change.Number && !d.IsDeleted)
                    .Select(d => d.Id).ToArrayAsync(ct);
                foreach (var id in siblings)
                {
                    if (!byId.TryGetValue(id, out var row))
                        throw new OrderPriceCorrectionException("OWNER_OBJECT_CHANGED", 409);
                    if (row.SetUnitPrice == change.Price) continue;
                    RequireObject(authority, row, objects);
                    row.SetUnitPrice = change.Price; affected.Add(row);
                }
            }

            var batch = Guid.NewGuid();
            var now = DateTime.Now;
            foreach (var row in affected.OrderBy(r => r.WebOrderNo, StringComparer.Ordinal).ThenBy(r => r.WebOrderDetailNo))
            {
                var before = db.Entry(row).OriginalValues;
                var priceChanged = row.IndividualUnitPrice != before.GetValue<decimal?>(nameof(row.IndividualUnitPrice))
                    || row.SetUnitPrice != before.GetValue<decimal?>(nameof(row.SetUnitPrice))
                    || row.SpecialPriceFlg != before.GetValue<string?>(nameof(row.SpecialPriceFlg));
                if (priceChanged || row.ProvisionalPriceFlg != before.GetValue<bool>(nameof(row.ProvisionalPriceFlg))
                    || row.PriceChangeReason != before.GetValue<string?>(nameof(row.PriceChangeReason)))
                    RequireWrite(authority, "UnitPrice");
                // Money invariant also covers siblings changed by the same set-price intent.
                row.Amount = (row.Quantity ?? 0m) * (row.SalesPriceDiv == "1"
                    ? row.IndividualUnitPrice ?? 0m : row.SetUnitPrice ?? 0m);
                if (row.Amount != before.GetValue<decimal?>(nameof(row.Amount))) RequireWrite(authority, "Amount");
                row.Modifier = authority.Actor.UserName; row.ModifyDate = now;
                if (priceChanged)
                {
                    row.ApprovalStatus = 1; row.WfApprovalFlg = false;
                    // A durable business intent, explicitly NOT successful external delivery.
                    // The original host uses a logging stub. The future 46-field adapter must
                    // adopt this schema explicitly; no generic dispatcher consumes it.
                    var payload = JsonSerializer.Serialize(new
                    {
                        schema = "Main.Order.PriceCorrection.Intent.v1", row.WebOrderNo, row.WebOrderDetailNo,
                        row.ProductCd, row.Quantity, row.SalesPriceDiv, row.IndividualUnitPrice, row.SetUnitPrice,
                        row.Amount, row.SpecialPriceFlg, row.PriceChangeReason,
                        previousIndividualUnitPrice = before.GetValue<decimal?>(nameof(row.IndividualUnitPrice)),
                        previousSetUnitPrice = before.GetValue<decimal?>(nameof(row.SetUnitPrice)),
                        selected = selected.Contains(row)
                    });
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO cp6_order_price_v1.workflow_intents
                        (id,batch_id,tenant_id,actor_id,detail_id,created_at_utc,mapping_version,registry_version,source_version,payload)
                        VALUES ({Guid.NewGuid()},{batch},{db.CurrentTenantId},{authority.Source.SubjectId},{row.Id},
                          {DateTime.UtcNow},{MappingVersion},{authority.Source.RegistryVersion},{authority.Source.SourcePermissionVersion},
                          CAST({payload} AS jsonb))
                        """, ct);
                    result.WfRequestedCount++;
                }
            }
            await db.SaveChangesAsync(ct);
            // Recheck after writes as well: time expiry, canceled request and authority faults
            // must roll back every business row and intent. No old allow/cache fallback.
            var final = await ReadAuthority(ct);
            if (final.Source.SourcePermissionVersion != authority.Source.SourcePermissionVersion
                || final.Source.RegistryVersion != authority.Source.RegistryVersion || final.Scope != authority.Scope
                || final.OrganizationVersion != authority.OrganizationVersion)
                throw new OrderPriceCorrectionException("AUTHORITY_CHANGED", 403);
            var finalObjects = await Objects(final, ids, ct);
            foreach (var row in affected) RequireObject(final, row, finalObjects);
            // Reconsume original login/session evidence after the final permission reads too.
            if (await currentActor(ct) != authority.Source.SubjectId)
                throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
            if (final.Source.TenantExpiresAt <= DateTime.Now)
                throw new OrderPriceCorrectionException("AUTHORITY_CHANGED", 403);
            ct.ThrowIfCancellationRequested();
            committing = true;
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception error)
        {
            Exception? rollbackFailure = null;
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception failure) { rollbackFailure = failure; }
            finally
            {
                foreach (var entry in db.ChangeTracker.Entries().ToArray())
                    if (!original.Contains(entry.Entity)) entry.State = EntityState.Detached;
            }
            if (committing || rollbackFailure != null)
                throw new OrderPriceCorrectionException("PRICE_CORRECTION_OUTCOME_UNKNOWN", 503,
                    rollbackFailure is null ? error : new AggregateException(error, rollbackFailure));
            throw;
        }
    }
}
