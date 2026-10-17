using System.Data;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace CP6.WebApi.Controllers.Sys;

/// <summary>
/// 仪表盘 — 业务经营总览（ERP / MES / WMS 横断 KPI）
///
/// 设计要点：
/// 1. 用 Dapper 做跨表聚合（报表/统计场景），EF Core 负责 CRUD。
/// 2. 登录落地页：一眼看清今日经营状态（受注・製造・出荷・在庫・承認）。
/// 3. 统计结果缓存 1 分钟，避免每次刷新都打 DB。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly CacheService _cache;
    private readonly ITenantContext _tenant;
    private readonly ICurrentPermissionContext _permissions;

    public DashboardController(IDbConnection db, CacheService cache, ITenantContext tenant, ICurrentPermissionContext permissions)
    {
        _db = db;
        _cache = cache;
        _tenant = tenant;
        _permissions = permissions;
    }

    private bool IsPostgreSql => _db switch
    {
        NpgsqlConnection => true,
        SqlConnection => false,
        _ => throw new InvalidOperationException("Dashboard requires a configured SQL Server or PostgreSQL connection.")
    };

    /// <summary>
    /// 获取业务经营总览数据（缓存 1 分钟）
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSummary()
    {
        // A nonempty TenantContext alone is not trusted: missing claims fall back to DefaultTenant.
        // Validate the authenticated GUID/tenant and enabled persisted user even on a warm cache hit.
        var tenantId = _tenant.CurrentTenantId;
        try { await _permissions.GetAsync(); }
        catch (CurrentPermissionUnavailableException) { return Forbid(); }
        if (tenantId == Guid.Empty || tenantId != _tenant.CurrentTenantId) return Forbid();

        var postgres = IsPostgreSql;
        var result = await _cache.GetOrSetAsync(CacheService.DashboardKey(tenantId), async () =>
        {
            var today = DateTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);

            // ───── 核心 KPI（一次往返聚合）─────
            const string kpiSql = @"
                SELECT
                    (SELECT COUNT(*) FROM T_Order        WHERE TenantId = @TenantId AND CreateDate >= @Today)     AS TodayOrders,
                    (SELECT COUNT(*) FROM T_Order        WHERE TenantId = @TenantId AND CreateDate >= @MonthStart) AS MonthOrders,
                    (SELECT COUNT(*) FROM T_WorkOrder    WHERE TenantId = @TenantId AND Status BETWEEN 1 AND 5)   AS ActiveWorkOrders,
                    (SELECT COUNT(*) FROM T_WorkOrder    WHERE TenantId = @TenantId AND Status IN (4,6) AND ActualEndDate >= @MonthStart) AS MonthCompleted,
                    (SELECT COUNT(*) FROM T_OutboundOrder WHERE TenantId = @TenantId AND Status BETWEEN 1 AND 3)  AS PendingOutbound,
                    (SELECT COUNT(*) FROM T_Stock        WHERE TenantId = @TenantId AND AvailableQty <= 0)        AS StockWarnings,
                    (SELECT COUNT(*) FROM T_ProductMaster WHERE TenantId = @TenantId AND Status = 0)
                        + (SELECT COUNT(*) FROM T_StockTake WHERE TenantId = @TenantId AND Status = 3)             AS PendingApprovals,
                    (SELECT COUNT(*) FROM T_ProductMaster WHERE TenantId = @TenantId)                             AS TotalProducts";

            // Preserve application wall-clock boundaries, soft-delete semantics and the int DTO contract.
            const string postgresKpiSql = """
                SELECT
                    (SELECT COUNT(*)::integer FROM public."T_Order" WHERE "TenantId"=@TenantId AND "CreateDate">=@Today) AS "TodayOrders",
                    (SELECT COUNT(*)::integer FROM public."T_Order" WHERE "TenantId"=@TenantId AND "CreateDate">=@MonthStart) AS "MonthOrders",
                    (SELECT COUNT(*)::integer FROM public."T_WorkOrder" WHERE "TenantId"=@TenantId AND "Status" BETWEEN 1 AND 5) AS "ActiveWorkOrders",
                    (SELECT COUNT(*)::integer FROM public."T_WorkOrder" WHERE "TenantId"=@TenantId AND "Status" IN (4,6) AND "ActualEndDate">=@MonthStart) AS "MonthCompleted",
                    (SELECT COUNT(*)::integer FROM public."T_OutboundOrder" WHERE "TenantId"=@TenantId AND "Status" BETWEEN 1 AND 3) AS "PendingOutbound",
                    (SELECT COUNT(*)::integer FROM public."T_Stock" WHERE "TenantId"=@TenantId AND "AvailableQty"<=0) AS "StockWarnings",
                    (SELECT COUNT(*)::integer FROM public."T_ProductMaster" WHERE "TenantId"=@TenantId AND "Status"=0)
                        +(SELECT COUNT(*)::integer FROM public."T_StockTake" WHERE "TenantId"=@TenantId AND "Status"=3) AS "PendingApprovals",
                    (SELECT COUNT(*)::integer FROM public."T_ProductMaster" WHERE "TenantId"=@TenantId) AS "TotalProducts"
                """;
            var summary = await _db.QueryFirstAsync<SummaryDto>(postgres ? postgresKpiSql : kpiSql, new
            {
                TenantId = tenantId,
                Today = postgres ? DateTime.SpecifyKind(today, DateTimeKind.Unspecified) : today,
                MonthStart = postgres ? DateTime.SpecifyKind(monthStart, DateTimeKind.Unspecified) : monthStart
            });

            // ───── 最近の受注（TOP 8）─────
            const string recentOrdersSql = @"
                SELECT TOP 8
                    WebOrderNo, CustomerCd, Quantity, OrderDate, ShipStatus
                FROM T_Order
                WHERE TenantId = @TenantId
                ORDER BY CreateDate DESC";
            const string postgresRecentOrdersSql = """
                SELECT "WebOrderNo", "CustomerCd", "Quantity", "OrderDate", "ShipStatus"
                FROM public."T_Order"
                WHERE "TenantId" = @TenantId
                ORDER BY "CreateDate" DESC
                LIMIT 8
                """;
            var recentOrders = (await _db.QueryAsync<RecentOrderDto>(postgres ? postgresRecentOrdersSql : recentOrdersSql, new { TenantId = tenantId })).ToList();

            // ───── 製造指図ステータス分布 ─────
            const string woStatusSql = @"
                SELECT Status, COUNT(*) AS Count
                FROM T_WorkOrder
                WHERE TenantId = @TenantId
                GROUP BY Status
                ORDER BY Status";
            const string postgresWoStatusSql = """
                SELECT "Status", COUNT(*)::integer AS "Count"
                FROM public."T_WorkOrder"
                WHERE "TenantId" = @TenantId
                GROUP BY "Status"
                ORDER BY "Status"
                """;
            var workOrderStatus = (await _db.QueryAsync<StatusCountDto>(postgres ? postgresWoStatusSql : woStatusSql, new { TenantId = tenantId })).ToList();

            return new DashboardData(summary, recentOrders, workOrderStatus);
        }, TimeSpan.FromMinutes(1));

        return Ok(new
        {
            result!.Summary,
            result.RecentOrders,
            result.WorkOrderStatus
        });
    }

    // ───── DTO 定义 ─────
    public record SummaryDto(
        int TodayOrders,
        int MonthOrders,
        int ActiveWorkOrders,
        int MonthCompleted,
        int PendingOutbound,
        int StockWarnings,
        int PendingApprovals,
        int TotalProducts);

    public record RecentOrderDto(string WebOrderNo, string? CustomerCd, decimal? Quantity, DateTime? OrderDate, int ShipStatus);
    public record StatusCountDto(int Status, int Count);
    public record DashboardData(SummaryDto Summary, List<RecentOrderDto> RecentOrders, List<StatusCountDto> WorkOrderStatus);
}
