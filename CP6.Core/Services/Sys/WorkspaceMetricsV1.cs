using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CP6.Core.Services.Sys;

public sealed record WorkspaceMetricDefinitionV1(string Key, string Version, string SourceOwner, string SourceRef,
    string Selection, string TimeBasis)
{
    public string TargetPath => "/workspace/metrics/" + Key;
}
public sealed record WorkspaceMetricQueryRefV1(string Key, string Version, DateTimeOffset AsOfUtc);
public sealed record WorkspaceBusinessRefV1(string Owner, Guid Id, string Version);
public sealed record WorkspaceMetricRowV1(WorkspaceBusinessRefV1? Reference, string OwnerState);
public sealed record WorkspaceCardV1(WorkspaceMetricDefinitionV1 Definition, string Availability, long? Value,
    DateTimeOffset? AsOfUtc = null, DateTimeOffset? StaleAfterUtc = null, DateTime? WindowStart = null,
    WorkspaceMetricQueryRefV1? QueryRef = null, IReadOnlyList<WorkspaceMetricRowV1>? Rows = null);

public sealed class WorkspaceMetricsV1(CP6Context db, OrderReadOwnerV1 orders, OperationsOwnerV1 owner,
    StockOperationsService stock, IMemoryCache cache)
{
    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(25);
    private const string LocalClock = "Main 应用业务时钟";
    private const string CurrentRows = "当前记录";
    private const string Order = "Main.ERP.Order", Mes = "Main.MES.WorkOrder", Outbound = "Main.WMS.OutboundOrder",
        Stock = "Main.WMS.Stock", StockTake = "Main.WMS.StockTake", Product = "Main.ERP.Product";

    private static readonly WorkspaceMetricDefinitionV1[] Catalog =
    [
        new("todayOrders", "1", Order, "T_Order.CreateDate", "应用当日零点起的可读订单数", LocalClock),
        new("monthOrders", "1", Order, "T_Order.CreateDate", "应用月初起的可读订单数", LocalClock),
        new("activeWorkOrders", "1", Mes, "T_WorkOrder.Status", "状态 1 至 5 的可读制造指图数", CurrentRows),
        new("monthCompleted", "1", Mes, "T_WorkOrder.Status,ActualEndDate", "状态 4 或 6 且实际结束日期从应用月初起", LocalClock),
        new("pendingOutbound", "1", Outbound, "T_OutboundOrder.Status", "状态 1 至 3 的可读出库单数", CurrentRows),
        new("stockWarnings", "1", Stock, "T_Stock.AvailableQty", "可用数量小于或等于零的可读库存记录数", CurrentRows),
        new("pendingApprovals", "1", Product + " + " + StockTake, "T_ProductMaster.Status + T_StockTake.Status",
            "产品状态 0 加盘点状态 3 的可读记录数", CurrentRows),
        new("totalProducts", "1", Product, "T_ProductMaster", "可读产品记录数", CurrentRows),
    ];

    public static IReadOnlyList<WorkspaceMetricDefinitionV1> Definitions { get; } = Array.AsReadOnly(Catalog);

    private sealed class Projection
    {
        public Guid Id { get; init; }
        public byte[]? Version { get; init; }
        public string Owner { get; init; } = "";
    }
    private sealed record QueryKey(Guid Tenant, Guid Subject, string ScopeVersion, string Snapshot, string Metric,
        string Version, string TimeZone, DateTime? WindowStart);
    private sealed record Observed(long Count, DateTimeOffset AtUtc);
    private sealed record Reading(Observed? Value, IReadOnlyList<WorkspaceMetricRowV1>? Rows = null, QueryKey? CacheKey = null);

    public async Task<WorkspaceCardV1?> ReadAsync(string key, string version, DateTimeOffset? asOfUtc, int page, CancellationToken ct)
    {
        var definition = Catalog.SingleOrDefault(x => x.Key == key && x.Version == version);
        if (definition is null) return null;
        if (!db.Database.IsNpgsql()) return new(definition, "UNAVAILABLE", null);

        var now = DateTimeOffset.UtcNow;
        var drilldown = asOfUtc.HasValue;
        var anchor = asOfUtc?.ToUniversalTime() ?? now;
        var expired = drilldown && (anchor < now - Freshness || anchor > now.AddSeconds(2));
        var localDay = TimeZoneInfo.ConvertTime(anchor, TimeZoneInfo.Local).Date;
        var month = new DateTime(localDay.Year, localDay.Month, 1);
        DateTime? window = key switch
        {
            "todayOrders" => localDay,
            "monthOrders" or "monthCompleted" => month,
            _ => null
        };

        try
        {
            Reading data = key switch
            {
                "todayOrders" or "monthOrders" => await orders.ReadAsync("order-list", FieldPermissionUseV1.Read,
                    false, (access, token) => Count(access.Headers.Where(x => x.CreateDate >= window!.Value)
                        .Select(x => new Projection { Id = x.Id, Version = x.RowVersion, Owner = Order }), access.SubjectId, access.ReadScopeVersion, token), ct),
                "activeWorkOrders" or "monthCompleted" => await owner.ExecuteAsync(OperationsOwnerV1.Mes, "query", false,
                    (access, token) => Count(WorkOrders(access).Select(x => new Projection { Id = x.Id, Version = x.RowVersion, Owner = Mes }),
                        access.ActorId, access.ReadScopeVersion, token), ct),
                "pendingOutbound" => await owner.ExecuteAsync(OperationsOwnerV1.Outbound, "query", false,
                    (access, token) => Count(access.Outbounds().Where(x => x.Status >= 1 && x.Status <= 3)
                        .Select(x => new Projection { Id = x.Id, Version = x.RowVersion, Owner = Outbound }), access.ActorId, access.ReadScopeVersion, token), ct),
                "stockWarnings" => await stock.ReadAsync<Reading>(async (access, token) => await Count(access.InventoryStocks()
                    .Where(x => x.AvailableQty <= 0m).Select(x => new Projection { Id = x.Id, Version = x.RowVersion, Owner = Stock }),
                    access.ActorId, access.ReadScopeVersion, token, cacheable: false), ct),
                "pendingApprovals" => await owner.ExecuteAsync(OperationsOwnerV1.Product, "query", false, async (access, token) =>
                {
                    var takes = await access.SourceAsync(OperationsOwnerV1.StockTake, token);
                    var products = access.Products().Where(x => x.Status == 0).Select(x => new { x.Id, x.RowVersion, Owner = Product });
                    var stockTakes = takes.StockTakes().Where(x => x.Status == 3).Select(x => new { x.Id, x.RowVersion, Owner = StockTake });
                    var rows = products.Concat(stockTakes).Select(x => new Projection { Id = x.Id, Version = x.RowVersion, Owner = x.Owner });
                    return await Count(rows, access.ActorId, access.ReadScopeVersion + "/" + takes.ReadScopeVersion, token);
                }, ct),
                "totalProducts" => await owner.ExecuteAsync(OperationsOwnerV1.Product, "query", false,
                    (access, token) => Count(access.Products().Select(x => new Projection { Id = x.Id, Version = x.RowVersion, Owner = Product }),
                        access.ActorId, access.ReadScopeVersion, token), ct),
                _ => throw new InvalidOperationException("工作台指标缺少查询定义。")
            };

            if (data.Value is null) return new(definition, "STALE", null);
            if (data.CacheKey is not null) cache.Set(data.CacheKey, data.Value, CacheLifetime);
            var time = drilldown ? anchor : data.Value.AtUtc;
            return new(definition, "READY", data.Value.Count, data.Value.AtUtc, time + Freshness, window,
                new(key, version, time), data.Rows);
        }
        catch (OrderPriceCorrectionException error) when (error.Status is 401 or 403)
        {
            return new(definition, "HIDDEN", null);
        }
        catch (OrderPriceCorrectionException)
        {
            return new(definition, "UNAVAILABLE", null);
        }

        IQueryable<CP6.Entity.DomainModels.Mes.WorkOrder> WorkOrders(OperationsAccessV1 access)
        {
            var source = access.WorkOrders();
            return key == "activeWorkOrders" ? source.Where(x => x.Status >= 1 && x.Status <= 5)
                : source.Where(x => (x.Status == 4 || x.Status == 6) && x.ActualEndDate >= window!.Value);
        }

        async Task<Reading> Count(IQueryable<Projection> source, Guid subject, string scopeVersion,
            CancellationToken token, bool cacheable = true)
        {
            if (expired) return new(null);
            // 版本、当前身份和真实 PostgreSQL 可见性同时纳入键。任一对象归属变化都使旧快照失效。
            QueryKey? observedKey = null;
            if (!drilldown && cacheable)
            {
                var stamp = await Snapshot(token);
                observedKey = new(db.CurrentTenantId, subject, scopeVersion, stamp, key, version, TimeZoneInfo.Local.Id, window);
                if (cache.TryGetValue<Observed>(observedKey, out var previous) && previous is not null &&
                    stamp == await Snapshot(token)) return new(previous);
            }

            var total = await source.LongCountAsync(token);
            var observed = new Observed(total, now);
            if (drilldown)
            {
                const int size = 50;
                var byId = source.OrderBy(x => x.Id);
                var stable = key == "pendingApprovals" ? byId.ThenBy(x => x.Owner) : byId;
                var pageRows = await stable.Skip((page - 1) * size).Take(size).ToArrayAsync(token);
                var records = pageRows.Select(row => row.Version is { Length: > 0 }
                    ? new WorkspaceMetricRowV1(new(row.Owner, row.Id, Convert.ToBase64String(row.Version)), "CURRENT")
                    : new WorkspaceMetricRowV1(null, "TO_VERIFY")).ToArray();
                return new(observed, records);
            }
            if (observedKey is not null && observedKey.Snapshot != await Snapshot(token)) observedKey = null;
            return new(observed, CacheKey: observedKey);
        }
    }

    private Task<string> Snapshot(CancellationToken ct)
        => db.Database.SqlQueryRaw<string>("""
            SELECT concat_ws('/',current_database(),pg_postmaster_start_time(),pg_current_snapshot()) AS "Value"
            """).SingleAsync(ct);
}
