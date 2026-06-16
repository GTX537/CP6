using CP6.Core.Services.Fin;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// 财务每日对账后台服务（章10 §5）。每日跑 AP/AR 子账↔GL + 试算平衡勾稽，不一致 → LogError 告警（运维监控）。
/// 只读勾稽，不改账。复用 <see cref="IFinReconciliationService"/>。启动后延迟首跑，之后每 24h 一次。
/// </summary>
public class FinReconciliationWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FinReconciliationWorker> _logger;

    public FinReconciliationWorker(IServiceScopeFactory scopeFactory, ILogger<FinReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("财务每日对账 worker 启动");
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await ProcessOnceAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _logger.LogInformation("财务每日对账 worker 停止");
        }
    }

    /// <summary>跑一次对账并记录结果——按租户循环（章10 §5）：每租户独立作用域勾稽其子账↔GL。
    /// 单租户异常由 <see cref="TenantScopeRunner"/> 吞掉记日志，不影响其余租户与宿主。</summary>
    public async Task ProcessOnceAsync(CancellationToken ct = default)
    {
        await TenantScopeRunner.ForEachTenantAsync(_scopeFactory, async (sp, tenantId, c) =>
        {
            var svc = sp.GetRequiredService<IFinReconciliationService>();
            var r = await svc.ReconcileAsync();
            if (r.AllClear)
                _logger.LogInformation("[FinRecon] 租户 {Tenant} 每日对账：全部一致", tenantId);
            else
                _logger.LogError("[FinRecon] 租户 {Tenant} 每日对账发现 {Count} 项不一致：{Issues}",
                    tenantId, r.Issues.Count, string.Join("；", r.Issues.Select(i => $"[{i.Category}] {i.Detail}")));
        }, _logger, ct);
    }
}
