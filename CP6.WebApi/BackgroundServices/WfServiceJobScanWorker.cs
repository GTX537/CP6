using CP6.Core.Services.Wf;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// WFS §4.1 服务任务异步底座扫描 Worker。周期创建 scope 调
/// <see cref="IWfServiceJobService.ScanOnceAsync"/>，按 lease 抢占执行到期服务任务 job。
/// v1 单实例；多实例靠 RowVersion 乐观并发竞争，败方跳过（不重复执行）。
/// </summary>
public class WfServiceJobScanWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    /// <summary>租约持有者标识——进程级稳定，每次扫描不重新生成。</summary>
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WfServiceJobScanWorker> _logger;

    public WfServiceJobScanWorker(IServiceScopeFactory scopeFactory, ILogger<WfServiceJobScanWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Wf 服务任务扫描 Worker 启动");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TenantScopeRunner.ForEachTenantAsync(_scopeFactory, async (sp, tenantId, ct) =>
                    {
                        var svc = sp.GetRequiredService<IWfServiceJobService>();
                        var n = await svc.ScanOnceAsync(DateTime.UtcNow, _workerId, ct);
                        if (n > 0) _logger.LogInformation("Wf 服务任务扫描处理租户 {Tenant} {Count} 条", tenantId, n);
                    }, _logger, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "Wf 服务任务扫描异常"); }

                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { _logger.LogInformation("Wf 服务任务扫描 Worker 停止"); }
    }
}
