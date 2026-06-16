using CP6.Core.Services.Wf;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// OA 章07 §4 审批超时扫描 Worker（仿 IntegrationEventRetryWorker）。周期创建 scope 调
/// <see cref="IWfTimeoutService.ScanOnceAsync"/>，按节点 TimeoutAction 处理到期待办。
/// v1 单实例；多实例需分布式锁/抢占（留待增强）。扫描异常吞掉记日志，不拖垮主机。
/// </summary>
public class WfTimeoutScanWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WfTimeoutScanWorker> _logger;

    public WfTimeoutScanWorker(IServiceScopeFactory scopeFactory, ILogger<WfTimeoutScanWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Wf 超时扫描 Worker 启动");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 章10 §5 按租户循环：每租户独立作用域扫到期待办，处理结果盖章到本租户
                    await TenantScopeRunner.ForEachTenantAsync(_scopeFactory, async (sp, tenantId, ct) =>
                    {
                        var svc = sp.GetRequiredService<IWfTimeoutService>();
                        var n = await svc.ScanOnceAsync(DateTime.Now, ct);
                        if (n > 0) _logger.LogInformation("Wf 超时扫描处理租户 {Tenant} {Count} 条到期待办", tenantId, n);
                    }, _logger, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "Wf 超时扫描异常"); }

                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { _logger.LogInformation("Wf 超时扫描 Worker 停止"); }
    }
}
