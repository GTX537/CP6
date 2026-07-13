using CP6.Core.Services.Wf;

namespace CP6.WebApi.BackgroundServices;

/// <summary>流程触发器 timer 扫描（spec §3.2）。逐租户 scope 切换照 TenantScopeRunner 现状口径（spec §6）；
/// 多实例安全：抢占 = Wf_FlowTrigger.RowVersion 乐观并发 + NextDueUtc 前移 + 占坑唯一键，无需 lease。</summary>
public class WfTriggerWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);   // cron 最小粒度 1min，30s 扫描足够
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WfTriggerWorker> _logger;

    public WfTriggerWorker(IServiceScopeFactory scopeFactory, ILogger<WfTriggerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Wf 触发器扫描 Worker 启动");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TenantScopeRunner.ForEachTenantAsync(_scopeFactory, async (sp, tenantId, ct) =>
                    {
                        var svc = sp.GetRequiredService<IFlowTriggerService>();
                        var n = await svc.ScanTimersOnceAsync(ct);
                        if (n > 0) _logger.LogInformation("Wf 触发器扫描处理租户 {Tenant} {Count} 条", tenantId, n);
                    }, _logger, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "Wf 触发器扫描异常"); }

                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { _logger.LogInformation("Wf 触发器扫描 Worker 停止"); }
    }
}
