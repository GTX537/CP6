using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.CrmIdentity.Baselines;

// Created only inside the managed ordinary-principal transaction after mapping and installed
// identity validation. No Owner or non-PostgreSQL fallback is available for this protocol.
internal sealed class IdentityRawSnapshotReader(CP6Context db, CrmIdentityRuntime runtime,
    IDataProtectionProvider protection, IdentityBaselineSourceV1 source) : IIdentityRawSnapshotReader
{
    private readonly IDataProtector _protector = protection.CreateProtector("CP6.C02.IdentityDirectory.Baseline.Cursor.v1");
    private readonly string _sourceHash = IdentityEventContracts.Hash(JsonSerializer.SerializeToUtf8Bytes(source, IdentityEventContracts.Json));

    public async Task<IdentityBaselinePageV1> ReadBaselinePageAsync(Guid tenant, string? cursor, int size = 200, CancellationToken ct = default)
    {
        RequireSource(tenant);
        if (size is < 1 or > 200) throw new IdentityReadException("C02_INVALID_PAGE_SIZE");
        var position = Decode(cursor);
        var all = db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant);
        var rows = position is null ? all : all.Where(x => string.Compare(x.AggregateId, position.LastAggregate) > 0);
        // Readiness, generation, count and page share one PostgreSQL statement snapshot.
        var page = await (from state in db.CrmIdentityBootstrapStates.AsNoTracking()
            where state.TenantId == tenant && state.CompletedAtUtc != null && state.ContractBundleSha256 == source.ContractBundleSha256
            from generation in db.CrmIdentityTenantGenerations.AsNoTracking().Where(x => x.TenantId == tenant).DefaultIfEmpty()
            from snapshot in rows.OrderBy(x => x.AggregateId).Take(size + 1).DefaultIfEmpty()
            orderby snapshot.AggregateId
            select new
            {
                Boundary = (long?)generation.Generation ?? 0,
                // Correlate with the outer state. Counting a captured IQueryable here is
                // evaluated synchronously during EF parameter extraction, before this SQL.
                Count = db.CrmIdentitySnapshots.LongCount(x => x.TenantId == state.TenantId),
                Item = snapshot == null ? null : new IdentityVersion(snapshot.AggregateId, snapshot.EventType,
                    snapshot.Version, snapshot.PayloadSha256, snapshot.IsDeleted)
            }).ToArrayAsync(ct).ConfigureAwait(false);
        if (page.Length == 0) throw new IdentityReadException("C02_BOOTSTRAP_NOT_READY");
        var boundary = page[0].Boundary;
        if (position is not null && position.Boundary != boundary) throw new IdentityReadException("C02_SNAPSHOT_BOUNDARY_CHANGED");
        var values = page.Where(x => x.Item is not null).Select(x => x.Item!).ToArray();
        var items = values.Take(size).ToArray();
        var next = values.Length <= size ? null : _protector.Protect(JsonSerializer.Serialize(
            new Cursor(_sourceHash, boundary, items[^1].AggregateId, runtime.Clock.GetUtcNow().AddMinutes(10)), IdentityEventContracts.Json));
        return new(IdentityBaselineVerifier.Contract, source, boundary.ToString(CultureInfo.InvariantCulture), page[0].Count, items, next);
    }

    public async Task<IdentityRawSnapshotV1?> ReadRawSnapshotAsync(Guid tenant, string aggregate, CancellationToken ct = default)
    {
        RequireSource(tenant);
        if (!Aggregate(aggregate)) throw new IdentityReadException("C02_INVALID_AGGREGATE");
        var observation = await (from state in db.CrmIdentityBootstrapStates.AsNoTracking()
            where state.TenantId == tenant && state.CompletedAtUtc != null && state.ContractBundleSha256 == source.ContractBundleSha256
            from generation in db.CrmIdentityTenantGenerations.AsNoTracking().Where(x => x.TenantId == tenant).DefaultIfEmpty()
            from snapshot in db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant && x.AggregateId == aggregate).DefaultIfEmpty()
            select new { Boundary = (long?)generation.Generation ?? 0, Snapshot = snapshot }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (observation is null) throw new IdentityReadException("C02_BOOTSTRAP_NOT_READY");
        var row = observation.Snapshot;
        if (row is null) return null;
        var item = new IdentityVersion(row.AggregateId, row.EventType, row.Version, row.PayloadSha256, row.IsDeleted);
        byte[] bytes;
        try { bytes = new UTF8Encoding(false, true).GetBytes(row.PayloadJson); }
        catch (EncoderFallbackException) { throw Unavailable(); }
        if (bytes.Length > IdentityBaselineVerifier.MaximumItemPayloadBytes || IdentityEventContracts.Hash(bytes) != row.PayloadSha256 ||
            !runtime.Validator.IsValidSnapshotData(tenant, item, source.Region, bytes)) throw Unavailable();
        ct.ThrowIfCancellationRequested();
        return new(IdentityBaselineVerifier.Contract, source, observation.Boundary.ToString(CultureInfo.InvariantCulture), item, Convert.ToBase64String(bytes));
    }

    private void RequireSource(Guid tenant)
    {
        if (!db.Database.IsNpgsql() || tenant != source.TenantId || !runtime.Options.Tenants.TryGetValue(tenant, out var region) ||
            region != source.Region || runtime.Options.Issuer != source.Issuer || runtime.ContractBundleSha256 != source.ContractBundleSha256) throw Unavailable();
    }
    private Cursor? Decode(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > 4096) throw new IdentityReadException("C02_INVALID_CURSOR");
        try
        {
            var cursor = JsonSerializer.Deserialize<Cursor>(_protector.Unprotect(value), IdentityEventContracts.Json);
            if (cursor is null || cursor.SourceSha256 != _sourceHash || cursor.ExpiresAtUtc <= runtime.Clock.GetUtcNow() ||
                cursor.Boundary < 0 || !Aggregate(cursor.LastAggregate)) throw new IdentityReadException("C02_INVALID_CURSOR");
            return cursor;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        { throw new IdentityReadException("C02_INVALID_CURSOR"); }
    }
    private static bool Aggregate(string? value) => value is { Length: >= 1 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-');
    private static IdentityReadException Unavailable() => new("C02_IDENTITY_STORE_UNAVAILABLE");
    private sealed record Cursor(string SourceSha256, long Boundary, string LastAggregate, DateTimeOffset ExpiresAtUtc);
}
