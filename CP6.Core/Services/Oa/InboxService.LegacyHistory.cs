using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LegacyFormHistoryFilter(Guid? StarterId = null, Guid? HandlerId = null, string? FlowKey = null,
    string? Keyword = null, int? Status = null, string? From = null, string? To = null, int Page = 1, int PageSize = 20,
    string? TimeZoneId = null, string? SnapshotToken = null);

public sealed record LegacyFormHistoryPage(IReadOnlyList<FormQueryItem> Items, int Total, int Page, int PageSize,
    DateTimeOffset SnapshotAsOf, string SnapshotToken, string? TimeZoneId, NativeQueryDateWindowV2 Window);

public partial class InboxService
{
    private sealed record LegacyHistoryDatabaseCut(string Visibility, DateTimeOffset AsOf);
    private sealed record LegacyHistorySnapshot(LegacyHistoryDatabaseCut Cut, LegacyFormHistoryFilter Filter);
    private sealed record LegacyHistoryTime(Guid Id, DateTime CreatedAt);

    private Task<string?> LegacyTenantZone(CancellationToken ct) => _db.Sys_Tenants.AsNoTracking()
        .Where(t => t.Id == _db.CurrentTenantId).Select(t => t.TimeZoneId).SingleAsync(ct);

    public async Task<NativeQueryContextV2> LegacyHistoryContextAsync(CancellationToken ct)
    {
        var id = await LegacyTenantZone(ct);
        _ = NativeQueryDatesV2.Zone(id);
        return new(id!);
    }

    private static string LegacyStorageZone()
    {
        // 旧引擎以应用主机墙钟保存 CreateDate。该来源时区与查询选用的租户日历时区分别处理。
        var zone = TimeZoneInfo.Local;
        if (zone.HasIanaId || zone.Id == "UTC") return zone.Id;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var id)) return id;
        throw new ScopeAuthorizationExceptionV1("QUERY_LEGACY_CLOCK_UNAVAILABLE", 503);
    }

    private static LegacyHistorySnapshot ReadLegacySnapshot(IDataProtector protector, string token, LegacyFormHistoryFilter filter)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<LegacyHistorySnapshot>(protector.Unprotect(token));
            if (snapshot != null && snapshot.Filter == filter) return snapshot;
        }
        catch (Exception e) when (e is CryptographicException or JsonException)
        {
            throw new ScopeAuthorizationExceptionV1("QUERY_SNAPSHOT_INVALID", 422, e);
        }

        throw new ScopeAuthorizationExceptionV1("QUERY_SNAPSHOT_INVALID", 422);
    }

    public async Task<LegacyFormHistoryPage> LegacyHistoryAsync(Guid actualUserId, Guid effectiveUserId,
        LegacyFormHistoryFilter filter, IDataProtectionProvider protection, CancellationToken ct)
    {
        if (filter.Page is < 1 or > 100000 || filter.PageSize is < 1 or > 100 || filter.Status is < 0 or > 5
            || filter.FlowKey?.Length > 100 || filter.Keyword?.Length > 200
            || filter.StarterId == Guid.Empty || filter.HandlerId == Guid.Empty
            || filter.Page > 1 && string.IsNullOrEmpty(filter.SnapshotToken))
            throw new ScopeAuthorizationExceptionV1("QUERY_FILTER_INVALID", 422);

        var storageZone = LegacyStorageZone();
        var protector = protection.CreateProtector("CP6.Oa.LegacyFormHistorySnapshot.v1",
            _db.CurrentTenantId.ToString("D"), actualUserId.ToString("D"), effectiveUserId.ToString("D"), storageZone);
        var inputs = filter with { Page = 1, SnapshotToken = null };
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        NativeQueryDateWindowV2 window = new(null, null);
        string? tenantZone = null;
        if (filter.From != null || filter.To != null)
        {
            tenantZone = await LegacyTenantZone(ct);
            var zone = NativeQueryDatesV2.Zone(tenantZone);
            if (filter.TimeZoneId != tenantZone) throw new ScopeAuthorizationExceptionV1("QUERY_TIMEZONE_CHANGED", 409);
            window = NativeQueryDatesV2.Window(zone, filter.From, filter.To);
        }

        var snapshot = filter.SnapshotToken is { } token
            ? ReadLegacySnapshot(protector, token, inputs)
            : new LegacyHistorySnapshot(await _db.Database.SqlQueryRaw<LegacyHistoryDatabaseCut>(
                """SELECT pg_current_snapshot()::text AS "Visibility", clock_timestamp() AS "AsOf" """).SingleAsync(ct), inputs);
        // 每次都按当前权限读取。首次数据库截点只冻结记录的入库资格，保护翻页免受新增或晚提交影响。
        var timestamps = _db.Database.SqlQuery<LegacyHistoryTime>($"""
            SELECT "Id", timezone({storageZone}, "CreateDate") AS "CreatedAt"
            FROM public."Wf_FlowInstance"
            WHERE "TenantId" = {_db.CurrentTenantId}
              AND (legacy_query_insert_xid = '0'::xid8
                   OR pg_visible_in_snapshot(legacy_query_insert_xid, {snapshot.Cut.Visibility}::pg_snapshot))
            """);
        var candidates = LegacyQueryCandidates(effectiveUserId,
            new(filter.StarterId, filter.HandlerId, filter.FlowKey, filter.Keyword, filter.Status, null, null));
        var entries = from instance in candidates
                      join timestamp in timestamps on instance.Id equals timestamp.Id
                      select new LegacyQueryEntry { Instance = instance, CreatedAt = timestamp.CreatedAt };
        if (window.FromInclusiveUtc is { } from) entries = entries.Where(e => e.CreatedAt >= from.UtcDateTime);
        if (window.ToExclusiveUtc is { } to) entries = entries.Where(e => e.CreatedAt < to.UtcDateTime);

        var page = await LegacyQueryPage(entries, filter.Page, filter.PageSize, ct);
        await tx.CommitAsync(ct);
        return new(page.Items, page.Total, page.Page, page.PageSize, snapshot.Cut.AsOf,
            filter.SnapshotToken ?? protector.Protect(JsonSerializer.Serialize(snapshot)), tenantZone, window);
    }
}
