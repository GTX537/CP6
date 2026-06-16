using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// 操作日志保留期清理 — 定期删除超过保留天数的 Sys_OperLog 记录。
///
/// 配置：
/// - OperLog:RetentionDays  保留天数（默认 7 天，可配置；&lt;=0 表示永久保留、不清理）
/// - OperLog:CleanupIntervalHours  清理周期小时数（默认 24）
///
/// 用 EF Core 8 的 ExecuteDeleteAsync 批量删除，避免把整表加载进内存。
/// </summary>
public class OperLogCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<OperLogCleanupService> _logger;

    public OperLogCleanupService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<OperLogCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var section = _config.GetSection("OperLog");
        var retentionDays = section.GetValue<int?>("RetentionDays") ?? 7;
        var intervalHours = section.GetValue<int?>("CleanupIntervalHours") ?? 24;
        if (intervalHours <= 0) intervalHours = 24;

        if (retentionDays <= 0)
        {
            _logger.LogInformation("OperLog 保留天数 <= 0，日志永久保留，清理服务不运行");
            return;
        }

        // 启动后稍等，避开应用初始化/迁移高峰
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        var interval = TimeSpan.FromHours(intervalHours);
        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupOnceAsync(retentionDays, stoppingToken);

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CleanupOnceAsync(int retentionDays, CancellationToken stoppingToken)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-retentionDays);
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CP6Context>();

            // 章10：保留期清理是按时间的跨租户运维操作 → IgnoreQueryFilters 删所有租户的过期日志
            // （否则后台默认租户作用域只会删到默认租户的日志）
            var deleted = await db.Sys_OperLogs
                .IgnoreQueryFilters()
                .Where(l => l.CreateDate < cutoff)
                .ExecuteDeleteAsync(stoppingToken);

            if (deleted > 0)
                _logger.LogInformation("OperLog 清理：删除 {Count} 条超过 {Days} 天的日志（早于 {Cutoff:yyyy-MM-dd}）",
                    deleted, retentionDays, cutoff);
        }
        catch (OperationCanceledException)
        {
            // 停止中，忽略
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OperLog 清理失败");
        }
    }
}
