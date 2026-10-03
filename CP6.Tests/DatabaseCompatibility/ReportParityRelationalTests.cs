using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Mes;
using CP6.Core.Utilities;
using CP6.Entity;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.WebApi.Controllers.Sys;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace CP6.Tests.DatabaseCompatibility;

// These assertions preserve the existing reports' global scope; they do not exercise HTTP authorization.
[Collection(Wp5ReportsRelationalCollection.Name)]
public sealed class ReportParityRelationalTests(Wp5ReportsRelationalFixture fixture, ITestOutputHelper output)
{
    [Wp5ReportsFact]
    public async Task Mes_summary_and_trend_preserve_global_dates_states_and_decimal_rounding()
    {
        output.WriteLine(fixture.SetupSummary);
        await using var owned = new OwnedReportRows(fixture);
        await owned.InitializeAsync();
        await using var connection = fixture.CreateConnection();
        var day = await ReadDatabaseDayAsync(connection);
        var today = day.ToDateTime(TimeOnly.MinValue);
        var a = owned.WorkOrder(owned.A, 3, today.AddDays(-1));
        var b = owned.WorkOrder(owned.B, 3, today.AddDays(1));
        await owned.SaveAsync(a, b,
            owned.WorkOrder(owned.A, 4, today.AddDays(-1), today),
            owned.WorkOrder(owned.B, 6, today.AddDays(-1), today.AddDays(1)),
            owned.WorkOrder(owned.A, 4, today.AddDays(-1), today.AddSeconds(-1)),
            owned.WorkOrder(owned.B, 1, today.AddDays(-1)),
            owned.WorkOrder(owned.B, 9, today.AddDays(-1)),
            owned.WorkOrder(owned.A, 5, today.AddDays(-1)),
            owned.WorkOrder(owned.A, 3, today.AddDays(-1), deleted: true));

        var first = owned.Result(a, today, 9000m, 0m);
        var second = owned.Result(b, today.AddDays(1).AddSeconds(-1), 899.50004000m, 100.49996000m);
        second.ResultType = 1; // The stored procedure aggregates quantities regardless of result type.
        var deleted = owned.Result(b, today.AddHours(1), 99m, 99m);
        deleted.IsDeleted = true;
        await owned.SaveAsync(first, second, deleted,
            owned.Result(a, today.AddDays(-2), 3m, 2m),
            owned.Result(b, today.AddDays(-1).AddHours(12), 9m, 1m),
            owned.Result(a, today.AddDays(-2).AddSeconds(-1), 88m, 88m),
            owned.Result(b, today.AddDays(1), 20m, 2m));

        var service = new MesDashboardDapperService(connection);
        var summary = await service.GetSummaryAsync();
        Assert.Equal((2, 1, 3), (summary.InProgressCount, summary.CompletedCount, summary.DelayedCount));
        Assert.Equal((9899.50004000m, 100.49996000m, 1.00m),
            (summary.TotalGoodQty, summary.TotalDefectQty, summary.DefectRate));
        var trend = await service.GetDailyTrendAsync(3);
        Assert.Equal(new[] { (Format(day.AddDays(-2)), 3m, 2m), (Format(day.AddDays(-1)), 9m, 1m),
                (Format(day), 9899.50004000m, 100.49996000m) },
            trend.Select(row => (row.Date, row.GoodQty, row.DefectQty)));

        // A second actual database aggregate distinguishes scale truncation from midpoint rounding.
        await using (var db = fixture.CreateContext(owned.A))
        {
            (await db.ProductionResults.SingleAsync(x => x.Id == first.Id)).GoodQty = 90m;
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.CreateContext(owned.B))
        {
            var row = await db.ProductionResults.SingleAsync(x => x.Id == second.Id);
            row.GoodQty = 8.995m;
            row.DefectQty = 1.005m;
            await db.SaveChangesAsync();
        }
        summary = await service.GetSummaryAsync();
        Assert.Equal((98.995m, 1.005m, 1.01m), (summary.TotalGoodQty, summary.TotalDefectQty, summary.DefectRate));
        Assert.Equal(day, await ReadDatabaseDayAsync(connection));
        output.WriteLine("Native MES aggregates: two tenants; defect rates 1.00 and 1.01; inclusive lower/exclusive upper dates verified.");
    }

    [Wp5ReportsFact]
    public async Task Mes_process_progress_preserves_code_and_name_groups_and_code_order()
    {
        output.WriteLine(fixture.SetupSummary);
        await using var owned = new OwnedReportRows(fixture);
        await owned.InitializeAsync();
        var a = owned.WorkOrder(owned.A, 3);
        var b = owned.WorkOrder(owned.B, 3);
        await owned.SaveAsync(a, b);
        await owned.SaveAsync(
            owned.Process(b, "B", "Finish", 2),
            owned.Process(a, "A", "Cut", 0),
            owned.Process(b, "A", "Cut", 1),
            owned.Process(a, "A", "Cut", 3),
            owned.Process(b, "A", "Cut", 2),
            owned.Process(b, "A", "Pack", 0),
            owned.Process(a, "B", "Finish", 9),
            owned.Process(b, "A", "Cut", 0, deleted: true));
        await using var connection = fixture.CreateConnection();
        var actual = await new MesDashboardDapperService(connection).GetProcessProgressAsync();
        Assert.Equal(new[] { "A", "A", "B" }, actual.Select(row => row.ProcessCd));
        // The original contract orders by code only; it does not promise an order between equal-code names.
        var groups = actual.ToDictionary(row => (row.ProcessCd, row.ProcessName!), row => (row.NotStarted, row.InProgress, row.Completed));
        Assert.Equal(3, groups.Count);
        Assert.Equal((1, 2, 1), groups[("A", "Cut")]);
        Assert.Equal((1, 0, 0), groups[("A", "Pack")]);
        Assert.Equal((0, 0, 1), groups[("B", "Finish")]);
    }

    [Wp5ReportsFact]
    public async Task Dashboard_preserves_global_kpis_recent_top_eight_soft_deleted_rows_and_cache()
    {
        output.WriteLine(fixture.SetupSummary);
        await using var owned = new OwnedReportRows(fixture);
        await owned.InitializeAsync();
        var today = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
        var month = new DateTime(today.Year, today.Month, 1);
        var orders = Enumerable.Range(0, 8).Select(i => owned.Order(i % 2 == 0 ? owned.A : owned.B,
            today.AddHours(i + 1), i + 0.125m, deleted: i == 7)).ToList();
        orders.Add(owned.Order(owned.B, today.AddDays(1), 80.25m));
        orders.Add(owned.Order(owned.A, month, 90.5m));
        orders.Add(owned.Order(owned.B, month.AddSeconds(-1), 100.75m));
        await owned.SaveAsync(orders.ToArray());
        await owned.SaveAsync(
            owned.WorkOrder(owned.A, 1), owned.WorkOrder(owned.B, 5, deleted: true),
            owned.WorkOrder(owned.A, 4, actualEnd: month),
            owned.WorkOrder(owned.B, 6, actualEnd: today.AddDays(1), deleted: true),
            owned.WorkOrder(owned.B, 4, actualEnd: month.AddSeconds(-1)), owned.WorkOrder(owned.A, 9),
            new OutboundOrder { TenantId = owned.A, OutboundNo = owned.Key("O"), WarehouseCd = "REPORT", Status = 1 },
            new OutboundOrder { TenantId = owned.B, OutboundNo = owned.Key("O"), WarehouseCd = "REPORT", Status = 3, IsDeleted = true },
            new OutboundOrder { TenantId = owned.A, OutboundNo = owned.Key("O"), WarehouseCd = "REPORT", Status = 4 },
            owned.Stock(owned.A, 0m), owned.Stock(owned.B, -1m, deleted: true), owned.Stock(owned.B, 1m),
            owned.Product(owned.A, 0), owned.Product(owned.B, 0, deleted: true), owned.Product(owned.B, 1),
            new StockTake { TenantId = owned.A, StockTakeNo = owned.Key("T"), TargetWarehouseCd = "REPORT", Status = 3, IsDeleted = true },
            new StockTake { TenantId = owned.B, StockTakeNo = owned.Key("T"), TargetWarehouseCd = "REPORT", Status = 4 });

        await using var connection = fixture.CreateConnection();
        using var services = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var cache = new CacheService(services.GetRequiredService<IDistributedCache>());
        var controller = new DashboardController(connection, cache);
        var first = await DashboardAsync(controller);
        var todayCount = 9 + (month == today ? 1 : 0);
        Assert.Equal(new DashboardController.SummaryDto(todayCount, 10, 4, 2, 2, 2, 3, 3), first.Summary);
        AssertRecentOrders(orders, first.RecentOrders);
        Assert.Equal(new DashboardController.StatusCountDto[] { new(1, 1), new(4, 2), new(5, 1), new(6, 1), new(9, 1) }, first.WorkOrderStatus);

        var added = owned.Order(owned.B, today.AddDays(2), 123.45678901m);
        await owned.SaveAsync(added);
        var cached = await DashboardAsync(controller);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(cached));
        await cache.RemoveAsync(CacheService.DashboardKey);
        var refreshed = await DashboardAsync(controller);
        Assert.Equal(first.Summary with { TodayOrders = todayCount + 1, MonthOrders = 11 }, refreshed.Summary);
        orders.Add(added);
        AssertRecentOrders(orders, refreshed.RecentOrders);
        Assert.Equal(first.WorkOrderStatus, refreshed.WorkOrderStatus);
        Assert.Equal(today, DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified));
    }

    [Wp5ReportsFact]
    public async Task Mes_daily_trend_preserves_reachable_day_boundaries_and_failure_category()
    {
        output.WriteLine(fixture.SetupSummary);
        await fixture.RequireEmptyReportTablesAsync();
        await using var connection = fixture.CreateConnection();
        var day = await ReadDatabaseDayAsync(connection);
        var service = new MesDashboardDapperService(connection);
        foreach (var days in new[] { -1, 0, 1, 366, 367 })
        {
            var rows = await service.GetDailyTrendAsync(days);
            Assert.Equal(Math.Max(1, days), rows.Count);
            Assert.Equal(Enumerable.Range(0, Math.Max(1, days)).Select(i => Format(day.AddDays(1 - days + i))), rows.Select(row => row.Date));
            Assert.All(rows, row => { Assert.Equal(0m, row.GoodQty); Assert.Equal(0m, row.DefectQty); });
        }
        if (fixture.Database.Provider == DatabaseProvider.SqlServer)
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => service.GetDailyTrendAsync(368));
            Assert.Equal(530, error.Number);
            output.WriteLine("Native days=368: SQL Server error 530; no successful report result.");
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetDailyTrendAsync(368));
            output.WriteLine("days=368: PostgreSQL adapter preserves an unhandled report error, not argument validation.");
        }
        Assert.Equal(day, await ReadDatabaseDayAsync(connection));
    }

    private async Task<DateOnly> ReadDatabaseDayAsync(DbConnection connection) => DateOnly.ParseExact(
        await connection.QuerySingleAsync<string>(fixture.Database.Provider == DatabaseProvider.PostgreSql
            ? "SELECT to_char(clock_timestamp()::date,'YYYY-MM-DD')" : "SELECT CONVERT(varchar(10),GETDATE(),23)"),
        "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Format(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<DashboardController.DashboardData> DashboardAsync(DashboardController controller)
    {
        var response = Assert.IsType<OkObjectResult>(await controller.GetSummary());
        return JsonSerializer.SerializeToElement(response.Value).Deserialize<DashboardController.DashboardData>()!;
    }

    private static void AssertRecentOrders(IEnumerable<Order> orders, IReadOnlyList<DashboardController.RecentOrderDto> actual)
    {
        Assert.Equal(8, actual.Count);
        Assert.Equal(orders.OrderByDescending(row => row.CreateDate).Take(8)
            .Select(row => new DashboardController.RecentOrderDto(row.WebOrderNo, row.CustomerCd, row.Quantity, row.OrderDate, row.ShipStatus)), actual);
    }

    // Every write is registered before SaveChanges. Disposal also handles a partially failed seed.
    private sealed class OwnedReportRows(Wp5ReportsRelationalFixture fixture) : IAsyncDisposable
    {
        private readonly HashSet<Guid> ids = [];
        private readonly string prefix = Guid.NewGuid().ToString("N")[..10];
        private int sequence;
        public Guid A { get; } = Guid.NewGuid();
        public Guid B { get; } = Guid.NewGuid();
        public string Key(string kind) => kind + prefix + (++sequence).ToString("D3", CultureInfo.InvariantCulture);

        public async Task InitializeAsync()
        {
            await fixture.RequireEmptyReportTablesAsync();
            foreach (var tenant in new[] { A, B })
            {
                await using var db = fixture.CreateContext(tenant);
                db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = "wp5-report-" + tenant.ToString("N"), TenantName = "WP5 report parity" });
                await db.SaveChangesAsync();
            }
        }

        public async Task SaveAsync(params BaseBizEntity[] rows)
        {
            foreach (var row in rows)
            {
                Assert.True(row.TenantId == A || row.TenantId == B);
                if (row.Id == Guid.Empty) row.Id = Guid.NewGuid();
                ids.Add(row.Id);
            }
            foreach (var group in rows.GroupBy(row => row.TenantId))
            {
                await using var db = fixture.CreateContext(group.Key);
                db.AddRange(group);
                await db.SaveChangesAsync();
            }
        }

        public WorkOrder WorkOrder(Guid tenant, int status, DateTime? planEnd = null, DateTime? actualEnd = null, bool deleted = false)
            => new() { TenantId = tenant, WorkOrderNo = Key("W"), ProductCd = "REPORT", Status = status, PlanEndDate = planEnd, ActualEndDate = actualEnd, IsDeleted = deleted };

        public ProductionResult Result(WorkOrder workOrder, DateTime date, decimal good, decimal defect)
            => new() { TenantId = workOrder.TenantId, ResultNo = Key("R"), WorkOrderNo = workOrder.WorkOrderNo, ProcessCd = "REPORT", OperatorCd = "REPORT", CreateDate = date, GoodQty = good, DefectQty = defect };

        public WorkOrderProcess Process(WorkOrder workOrder, string code, string name, int status, bool deleted = false)
            => new() { TenantId = workOrder.TenantId, WorkOrderNo = workOrder.WorkOrderNo, ProcessCd = code, ProcessName = name, TaskCd = (++sequence).ToString("D3", CultureInfo.InvariantCulture), ProcessStatus = status, IsDeleted = deleted };

        public Order Order(Guid tenant, DateTime created, decimal quantity, bool deleted = false)
            => new() { TenantId = tenant, WebOrderNo = Key("E"), CustomerCd = tenant == A ? "CUSTOMER-A" : "CUSTOMER-B", OrderType = "0100", CreateDate = created, OrderDate = created.Date, Quantity = quantity, ShipStatus = deleted ? 9 : 0, IsDeleted = deleted };

        public Stock Stock(Guid tenant, decimal available, bool deleted = false)
            => new() { TenantId = tenant, WarehouseCd = "REPORT", LocationCd = Key("L"), ProductCd = "REPORT", LotNo = Key("S"), PhysicalQty = Math.Max(0m, available), AllocatedQty = Math.Max(0m, -available), AvailableQty = available, IsDeleted = deleted };

        public ProductMaster Product(Guid tenant, int status, bool deleted = false)
        {
            var code = Key("P");
            return new ProductMaster { TenantId = tenant, ProductCd = code, ItemCd = code, CustomerCd = "REPORT", SetProductCd = code, Status = status, IsDeleted = deleted };
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = fixture.CreateContext(A);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var ownedIds = ids.ToArray();
            var tenants = new[] { A, B };
            async Task DeleteAsync<T>() where T : BaseBizEntity
            {
                await db.Set<T>().IgnoreQueryFilters()
                    .Where(row => ownedIds.Contains(row.Id) && tenants.Contains(row.TenantId)).ExecuteDeleteAsync();
            }
            await DeleteAsync<ProductionResult>();
            await DeleteAsync<WorkOrderProcess>();
            await DeleteAsync<WorkOrder>();
            await DeleteAsync<Order>();
            await DeleteAsync<OutboundOrder>();
            await DeleteAsync<Stock>();
            await DeleteAsync<StockTake>();
            await DeleteAsync<ProductMaster>();
            var keys = ownedIds.Concat(tenants).Select(id => id.ToString()).ToArray();
            await db.Sys_FieldAuditLogs.IgnoreQueryFilters().Where(row => tenants.Contains(row.TenantId) && keys.Contains(row.EntityKey)).ExecuteDeleteAsync();
            await db.Sys_Tenants.IgnoreQueryFilters().Where(row => tenants.Contains(row.Id)).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
    }
}
