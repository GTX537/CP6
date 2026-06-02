using System.Data;
using CP6.Core.Utilities;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers;

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

    public DashboardController(IDbConnection db, CacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <summary>
    /// 获取业务经营总览数据（缓存 1 分钟）
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSummary()
    {
        var result = await _cache.GetOrSetAsync(CacheService.DashboardKey, async () =>
        {
            var today = DateTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);

            // ───── 核心 KPI（一次往返聚合）─────
            const string kpiSql = @"
                SELECT
                    (SELECT COUNT(*) FROM T_Order        WHERE CreateDate >= @Today)                              AS TodayOrders,
                    (SELECT COUNT(*) FROM T_Order        WHERE CreateDate >= @MonthStart)                         AS MonthOrders,
                    (SELECT COUNT(*) FROM T_WorkOrder    WHERE Status BETWEEN 1 AND 5)                            AS ActiveWorkOrders,
                    (SELECT COUNT(*) FROM T_WorkOrder    WHERE Status IN (4,6) AND ActualEndDate >= @MonthStart)  AS MonthCompleted,
                    (SELECT COUNT(*) FROM T_OutboundOrder WHERE Status BETWEEN 1 AND 3)                           AS PendingOutbound,
                    (SELECT COUNT(*) FROM T_Stock        WHERE AvailableQty <= 0)                                 AS StockWarnings,
                    (SELECT COUNT(*) FROM T_ProductMaster WHERE Status = 0)
                        + (SELECT COUNT(*) FROM T_StockTake WHERE Status = 3)                                     AS PendingApprovals,
                    (SELECT COUNT(*) FROM T_ProductMaster)                                                        AS TotalProducts";

            var summary = await _db.QueryFirstAsync<SummaryDto>(kpiSql, new { Today = today, MonthStart = monthStart });

            // ───── 最近の受注（TOP 8）─────
            const string recentOrdersSql = @"
                SELECT TOP 8
                    WebOrderNo, CustomerCd, Quantity, OrderDate, ShipStatus
                FROM T_Order
                ORDER BY CreateDate DESC";
            var recentOrders = (await _db.QueryAsync<RecentOrderDto>(recentOrdersSql)).ToList();

            // ───── 製造指図ステータス分布 ─────
            const string woStatusSql = @"
                SELECT Status, COUNT(*) AS Count
                FROM T_WorkOrder
                GROUP BY Status
                ORDER BY Status";
            var workOrderStatus = (await _db.QueryAsync<StatusCountDto>(woStatusSql)).ToList();

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
