using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.ErpIntegration.IntegrationOpsContractV1;

namespace CP6.Core.Services.ErpIntegration;

public sealed class IntegrationOpsDirectoryV1(IDbContextFactory<ErpIntegrationContext> factory,
    ErpIntegrationRuntime runtime, IHttpContextAccessor http, IDataProtectionProvider protection,
    IEnumerable<IIntegrationOpsSourceV1> registered)
{
    internal sealed record Locator(Guid Tenant, IntegrationQueueRefV1 Queue, string FailureKey);
    private sealed record Source(string Owner, string? RouteEpoch);
    private sealed record PageCursor(Guid Tenant, Guid Actor, IntegrationFailureWindowV1 Window, string? FilterOwner,
        int Size, Source[] Routes, string[] Selected, string[] Missing, IntegrationFailureKeyV1 After);

    private readonly IntegrationOpsAuthorityV1 authority = new(runtime, http);
    private readonly Dictionary<string, IIntegrationOpsSourceV1> sources = registered.ToDictionary(x => x.OwnerId, StringComparer.Ordinal);

    public async Task<IntegrationFailurePageV1> ReadAsync(Guid tenant, string? owner, DateTimeOffset? fromUtc,
        DateTimeOffset? untilUtc, int? size, string? cursor, CancellationToken ct)
    {
        await using var queue = await factory.CreateDbContextAsync(ct);
        await using var db = await IntegrationOpsAuthorityV1.BindAsync(queue, tenant, ct);
        var access = await authority.ReadAsync(db, ct);
        var prior = cursor is null ? null : Decode<PageCursor>("Cursor", cursor, "CURSOR_INVALID");
        var limit = size ?? prior?.Size ?? 50;
        var clock = runtime.Clock.GetUtcNow();
        var snapshot = prior?.Window.SnapshotAt ?? clock;
        var filter = owner ?? prior?.FilterOwner;
        var end = untilUtc?.ToUniversalTime() ?? prior?.Window.UntilUtc ?? snapshot;
        if (end < DateTimeOffset.MinValue.AddDays(31)) throw Error("FAILURE_FILTER_INVALID", 400);
        var start = fromUtc?.ToUniversalTime() ?? prior?.Window.FromUtc ?? end.AddDays(-7);
        var window = new IntegrationFailureWindowV1(start, end, snapshot);
        if (limit is < 1 or > 100 || start >= end || end - start > TimeSpan.FromDays(31) || end > snapshot ||
            (filter is not null && !runtime.Options.OpsOwners.Contains(filter, StringComparer.Ordinal)))
            throw Error("FAILURE_FILTER_INVALID", 400);
        if (prior is not null && (prior.Tenant != tenant || prior.Actor != access.Actor || prior.Size != limit ||
            prior.Window != window || prior.FilterOwner != filter || snapshot < clock.AddMinutes(-30)))
            throw Error("CURSOR_INVALID", 400);
        var routes = new List<Source>();
        foreach (var id in runtime.Options.OpsOwners.Where(id => filter == null || filter == id).Order(StringComparer.Ordinal))
        {
            if (!sources.TryGetValue(id, out var source)) { routes.Add(new(id, null)); continue; }
            try
            {
                if (await source.CanReadAsync(tenant, access.Actor, ct)) routes.Add(new(id, source.RouteEpoch));
            }
            catch (Exception failure) when (IsUnavailable(failure))
            {
                routes.Add(new(id, null));
            }
        }
        if (prior is not null && prior.Selected.Any(id => !routes.Any(r =>
                r.Owner == id && prior.Routes.Contains(r))))
            throw Error("SOURCE_SET_UNAVAILABLE", 503);
        var missing = prior?.Missing.ToList() ?? [];
        var selected = new List<string>();
        var candidates = new List<IntegrationFailureRecordV1>();
        var requested = prior?.Selected ?? routes.Select(x => x.Owner).ToArray();
        foreach (var id in requested)
        {
            var rows = routes.Any(route => route.Owner == id && route.RouteEpoch is not null)
                ? await ReadSourceAsync(sources[id], tenant, access.Actor, window, prior?.After, limit + 1, ct) : null;
            if (rows is null)
            {
                if (prior is not null) throw Error("SOURCE_SET_UNAVAILABLE", 503);
                missing.Add(id);
                continue;
            }
            selected.Add(id);
            candidates.AddRange(rows);
        }
        if (selected.Count == 0) throw Error("SOURCE_UNAVAILABLE", 503);
        var ordered = candidates.OrderBy(row => row.Key).Take(limit + 1).ToArray();
        var next = ordered.Length <= limit ? null : Encode("Cursor", new PageCursor(tenant, access.Actor, window,
            filter, limit, prior?.Routes ?? routes.ToArray(), selected.ToArray(), missing.ToArray(), ordered[limit - 1].Key));
        var items = ordered.Take(limit).Select(row => Project(tenant, row, access.Evidence,
            routes.Single(x => x.Owner == row.Key.OwnerId).RouteEpoch!)).ToArray();
        return new(items, missing.Count != 0, missing.ToArray(), selected.ToArray(), snapshot, next);
    }

    private static async Task<IReadOnlyList<IntegrationFailureRecordV1>?> ReadSourceAsync(IIntegrationOpsSourceV1 source,
        Guid tenant, Guid actor, IntegrationFailureWindowV1 window, IntegrationFailureKeyV1? after, int take, CancellationToken ct)
    {
        try
        {
            var rows = await source.ReadAsync(tenant, actor, window, after, take, ct);
            if (rows is null || rows.Count > take) return null;
            var previous = after;
            foreach (var row in rows)
            {
                if (row.Key.OwnerId != source.OwnerId || row.Key.TimePartition != (row.Key.OccurredAtUtc.HasValue ? 0 : 1) ||
                    (row.Key.OccurredAtUtc is { } timestamp && (timestamp < window.FromUtc || timestamp > window.UntilUtc)) ||
                    previous?.CompareTo(row.Key) >= 0) return null;
                previous = row.Key;
            }
            return rows;
        }
        catch (Exception error) when (IsUnavailable(error))
        {
            return null;
        }
    }

    internal IntegrationFailureV1 Project(Guid tenant, IntegrationFailureRecordV1 row, bool evidence, string epoch)
    {
        var key = row.Key;
        var queue = new IntegrationQueueRefV1(key.OwnerId, key.NativeType, key.NativeId);
        return new(Encode("Locator", new Locator(tenant, queue, key.FailureKey)), key.OwnerId, key.NativeType,
            evidence ? key.NativeId : null, epoch, row.NativeStatus, evidence ? row.NativeVersion : null,
            key.TimePartition, key.OccurredAtUtc, SafeCode(row.SafeCode), row.AttemptCount, row.NativeStatus,
            "UNKNOWN", "METADATA");
    }

    internal Locator ReadLocator(Guid tenant, string failureRef)
    {
        var locator = Decode<Locator>("Locator", failureRef, "FAILURE_NOT_FOUND", 404);
        if (locator.Tenant != tenant) throw Error("FAILURE_NOT_FOUND", 404);
        return locator;
    }

    internal string Encode<T>(string purpose, T value) =>
        protection.CreateProtector("CP6.IntegrationOps.1", purpose).Protect(JsonSerializer.Serialize(value));

    internal T Decode<T>(string purpose, string token, string code, int status = 400)
    {
        if (token is not { Length: > 0 and <= 8192 }) throw Error(code, status);
        try
        {
            return JsonSerializer.Deserialize<T>(protection.CreateProtector("CP6.IntegrationOps.1", purpose).Unprotect(token))
                ?? throw Error(code, status);
        }
        catch (Exception error) when (error is CryptographicException or FormatException or JsonException)
        {
            throw Error(code, status);
        }
    }

    internal static string SafeCode(string? code) => code switch
    {
        "C03_REPLAY_REQUIRED" or "C03_ERP_UNAVAILABLE" or "C03_BRIDGE_REPLAY_REQUIRED" or "BLOCKED_WAITING_AUTHORITY" or
        "C03_BRIDGE_UNAVAILABLE" => code,
        _ => "DELIVERY_FAILURE"
    };
}
