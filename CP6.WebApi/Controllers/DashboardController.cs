using System.Data;
using CP6.Core.Utilities;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers;

/// <summary>
/// 仪表盘 — Dapper 聚合查询 + Redis 缓存
///
/// 面试要点：
/// 1. Dapper 适合复杂 SQL（报表/统计），EF Core 适合 CRUD
/// 2. 统计结果用缓存（1分钟），避免每次刷新都查DB
/// 3. Dapper + Cache 组合 = 高性能报表方案
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
    /// 获取仪表盘汇总数据（缓存1分钟）
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSummary()
    {
        var result = await _cache.GetOrSetAsync(CacheService.DashboardKey, async () =>
        {
            var today = DateTime.Today;
            var weekStart = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
            if (today.DayOfWeek == DayOfWeek.Sunday) weekStart = weekStart.AddDays(-7);

            const string sql = @"
                SELECT
                    (SELECT COUNT(*) FROM Sys_OperLogs WHERE CreateDate >= @Today) AS TodayOps,
                    (SELECT COUNT(*) FROM Sys_OperLogs WHERE CreateDate >= @WeekStart) AS WeekOps,
                    (SELECT COUNT(*) FROM Sys_OperLogs) AS TotalOps,
                    (SELECT COUNT(*) FROM Sys_Users) AS TotalUsers,
                    (SELECT COUNT(*) FROM Articles) AS TotalArticles";

            var summary = await _db.QueryFirstAsync<SummaryDto>(sql, new { Today = today, WeekStart = weekStart });

            const string topControllersSql = @"
                SELECT TOP 5 Controller AS Name, COUNT(*) AS Count
                FROM Sys_OperLogs
                WHERE Controller IS NOT NULL
                GROUP BY Controller
                ORDER BY Count DESC";
            var topControllers = (await _db.QueryAsync<NameCountDto>(topControllersSql)).ToList();

            const string trendSql = @"
                SELECT CONVERT(varchar(10), CreateDate, 23) AS Date, COUNT(*) AS Count
                FROM Sys_OperLogs
                WHERE CreateDate >= @Start
                GROUP BY CONVERT(varchar(10), CreateDate, 23)
                ORDER BY Date";
            var trend = (await _db.QueryAsync<DateCountDto>(trendSql, new { Start = today.AddDays(-6) })).ToList();

            const string methodSql = @"
                SELECT HttpMethod AS Name, COUNT(*) AS Count
                FROM Sys_OperLogs
                WHERE HttpMethod IS NOT NULL
                GROUP BY HttpMethod
                ORDER BY Count DESC";
            var methods = (await _db.QueryAsync<NameCountDto>(methodSql)).ToList();

            return new DashboardData(summary, topControllers, trend, methods);
        }, TimeSpan.FromMinutes(1));  // 统计数据缓存1分钟

        return Ok(new
        {
            result!.Summary,
            result.TopControllers,
            result.Trend,
            result.Methods
        });
    }

    // DTO 定义
    public record SummaryDto(int TodayOps, int WeekOps, int TotalOps, int TotalUsers, int TotalArticles);
    public record NameCountDto(string Name, int Count);
    public record DateCountDto(string Date, int Count);
    public record DashboardData(SummaryDto Summary, List<NameCountDto> TopControllers, List<DateCountDto> Trend, List<NameCountDto> Methods);
}
