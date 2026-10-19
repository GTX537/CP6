using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.CrmIdentity;

public sealed record IdentityVersion(string AggregateId, string EventType, int Version, string PayloadSha256, bool IsDeleted);
public sealed record IdentityVersionPage(Guid TenantId, string Boundary, IdentityVersion[] Items, string? NextCursor);
public sealed record IdentitySnapshot(Guid TenantId, string AggregateId, string EventType, int Version, string PayloadSha256,
    bool IsDeleted, JsonElement Data);

public sealed class IdentityReadException(string code) : Exception(code);

public sealed class IdentitySnapshotReader(CP6Context db, CrmIdentityRuntime runtime, IDataProtectionProvider protection) : IIdentitySnapshotReader
{
    private readonly IDataProtector protector = protection.CreateProtector("CP6.C02.IdentityVersions.Cursor.v2");

    public async Task<IdentityVersionPage> ReadVersionsAsync(Guid tenant, string? cursor, int size = 200, CancellationToken cancellationToken = default)
    {
        if (size is < 1 or > 200) throw new IdentityReadException("C02_INVALID_PAGE_SIZE");
        var position = DecodeCursor(tenant, cursor);
        if (db.Database.IsNpgsql())
        {
            if (!runtime.Options.Tenants.ContainsKey(tenant)) throw new IdentityReadException("C02_BOOTSTRAP_NOT_READY");
            var snapshots = db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant);
            if (position is not null) snapshots = snapshots.Where(x => string.Compare(x.AggregateId, position.LastAggregate) > 0);
            // One statement binds readiness, generation and page to the same snapshot. It also
            // works in a caller's ReadCommitted transaction after the tenant mapping guard has
            // waited for a control writer. No inner transaction or caller commit is introduced.
            var page = await (from state in db.CrmIdentityBootstrapStates.AsNoTracking()
                where state.TenantId == tenant && state.CompletedAtUtc != null && state.ContractBundleSha256 == runtime.ContractBundleSha256
                from generation in db.CrmIdentityTenantGenerations.AsNoTracking().Where(x => x.TenantId == tenant).DefaultIfEmpty()
                from snapshot in snapshots.OrderBy(x => x.AggregateId).Take(size + 1).DefaultIfEmpty()
                orderby snapshot.AggregateId
                select new
                {
                    Boundary = (long?)generation.Generation ?? 0,
                    Item = snapshot == null ? null : new IdentityVersion(snapshot.AggregateId, snapshot.EventType,
                        snapshot.Version, snapshot.PayloadSha256, snapshot.IsDeleted)
                }).ToArrayAsync(cancellationToken);
            if (page.Length == 0) throw new IdentityReadException("C02_BOOTSTRAP_NOT_READY");
            var generationBoundary = page[0].Boundary;
            if (position is not null && position.Boundary != generationBoundary) throw new IdentityReadException("C02_SNAPSHOT_BOUNDARY_CHANGED");
            return Page(tenant, generationBoundary, page.Where(x => x.Item is not null).Select(x => x.Item!).ToArray(), size);
        }
        // Preserve the historical non-PG transaction behavior.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await RequireReadyAsync(tenant, cancellationToken);
        var boundary = await db.CrmIdentityTenantGenerations.AsNoTracking()
            .Where(x => x.TenantId == tenant).Select(x => (long?)x.Generation)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
        // With only current snapshots, a write between pages can move a row beyond the boundary.
        // Reject/restart the entire baseline instead of silently omitting that row.
        if (position is not null && position.Boundary != boundary) throw new IdentityReadException("C02_SNAPSHOT_BOUNDARY_CHANGED");
        var query = db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant);
        if (position is not null) query = query.Where(x => string.Compare(x.AggregateId, position.LastAggregate) > 0);
        var rows = await query.OrderBy(x => x.AggregateId).Take(size + 1)
            .Select(x => new IdentityVersion(x.AggregateId, x.EventType, x.Version, x.PayloadSha256, x.IsDeleted)).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Page(tenant, boundary, rows, size);
    }

    private IdentityVersionPage Page(Guid tenant, long boundary, IdentityVersion[] rows, int size)
    {
        var items = rows.Take(size).ToArray();
        var next = rows.Length <= size ? null : protector.Protect(JsonSerializer.Serialize(
            new Cursor(tenant, boundary, items[^1].AggregateId, runtime.Clock.GetUtcNow().AddMinutes(10)), IdentityEventContracts.Json));
        return new(tenant, boundary.ToString(System.Globalization.CultureInfo.InvariantCulture), items, next);
    }

    public async Task<IdentitySnapshot?> ReadSnapshotAsync(Guid tenant, string aggregate, CancellationToken cancellationToken = default)
    {
        if (aggregate.Length is < 1 or > 128 || aggregate.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or ':' or '-')))
            throw new IdentityReadException("C02_INVALID_AGGREGATE");
        CP6.Entity.DomainModels.Sys.CrmIdentitySnapshot? row;
        if (db.Database.IsNpgsql())
        {
            if (!runtime.Options.Tenants.ContainsKey(tenant)) throw new IdentityReadException("C02_BOOTSTRAP_NOT_READY");
            // One PG statement sees readiness and contents in the same committed snapshot,
            // including inside a caller-owned ReadCommitted transaction. The left join preserves
            // the distinction between an unavailable directory and a ready but absent aggregate.
            var ready = await (from state in db.CrmIdentityBootstrapStates.AsNoTracking()
                where state.TenantId == tenant && state.CompletedAtUtc != null && state.ContractBundleSha256 == runtime.ContractBundleSha256
                from snapshot in db.CrmIdentitySnapshots.AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.AggregateId == aggregate).DefaultIfEmpty()
                select new { Snapshot = snapshot }).SingleOrDefaultAsync(cancellationToken);
            if (ready is null) throw new IdentityReadException("C02_BOOTSTRAP_NOT_READY");
            row = ready.Snapshot;
        }
        else
        {
            await RequireReadyAsync(tenant, cancellationToken);
            row = await db.CrmIdentitySnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant && x.AggregateId == aggregate, cancellationToken);
        }
        if (row is null) return null;
        using var data = JsonDocument.Parse(row.PayloadJson);
        return new(row.TenantId, row.AggregateId, row.EventType, row.Version, row.PayloadSha256, row.IsDeleted, data.RootElement.Clone());
    }

    public async Task RequireReadyAsync(Guid tenant, CancellationToken cancellationToken = default)
    {
        if (!runtime.Options.Tenants.ContainsKey(tenant) || !await db.CrmIdentityBootstrapStates.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.CompletedAtUtc != null && x.ContractBundleSha256 == runtime.ContractBundleSha256, cancellationToken))
            throw new IdentityReadException("C02_BOOTSTRAP_NOT_READY");
    }

    private Cursor? DecodeCursor(Guid tenant, string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > 2048) throw new IdentityReadException("C02_INVALID_CURSOR");
        try
        {
            var cursor = JsonSerializer.Deserialize<Cursor>(protector.Unprotect(value), IdentityEventContracts.Json);
            if (cursor is null || cursor.Tenant != tenant || cursor.ExpiresAtUtc <= runtime.Clock.GetUtcNow() ||
                cursor.Boundary < 0 || string.IsNullOrEmpty(cursor.LastAggregate) || cursor.LastAggregate.Length > 128)
                throw new IdentityReadException("C02_INVALID_CURSOR");
            return cursor;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        { throw new IdentityReadException("C02_INVALID_CURSOR"); }
    }

    private sealed record Cursor(Guid Tenant, long Boundary, string LastAggregate, DateTimeOffset ExpiresAtUtc);
}
