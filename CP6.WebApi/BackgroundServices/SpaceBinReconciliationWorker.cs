using CP6.Core.EFDbContext;
using CP6.Core.Services.Space;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// Space↔WMS 库位对账后台服务（波5）。每日扫描已发布库位（Status=1）与其 WMS 消费 bin 的
/// 停用漂移（bin.IsActive=false），只读不自愈，漂移逐条 LogError 告警交人工核查。
/// 照 <see cref="FinReconciliationWorker"/> 同构：启动后延迟首跑，之后每 24h 一次，
/// 经 <see cref="TenantScopeRunner"/> 逐租户作用域扫描。
/// </summary>
public class SpaceBinReconciliationWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SpaceBinReconciliationWorker> _logger;

    public SpaceBinReconciliationWorker(IServiceScopeFactory scopeFactory, ILogger<SpaceBinReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Space 库位对账 worker 启动");
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Tenant enumeration/infrastructure failures must not silently terminate the hosted service.
                    _logger.LogError(ex, "Space bin reconciliation cycle failed; the next daily cycle will retry");
                }
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _logger.LogInformation("Space 库位对账 worker 停止");
        }
    }

    /// <summary>跑一次对账并记录结果——按租户循环：每租户独立作用域扫描其 Space↔WMS 漂移。
    /// 单租户异常由 <see cref="TenantScopeRunner"/> 吞掉记日志，不影响其余租户与宿主。</summary>
    public async Task ProcessOnceAsync(CancellationToken ct = default)
    {
        await TenantScopeRunner.ForEachTenantAsync(_scopeFactory, async (sp, tenantId, c) =>
        {
            var db = sp.GetRequiredService<CP6Context>();
            var drifts = await SpaceBinDriftScanner.ScanAsync(db, c);
            if (drifts.Count == 0)
                _logger.LogInformation("[SpaceBinDrift] 租户 {Tenant} 库位对账：无漂移", tenantId);
            else
                foreach (var d in drifts)
                    _logger.LogError(
                        "[SpaceBinDrift] 租户 {Tenant} 已发布库位 {LocationId}({Code}) 对应 WMS bin 处于停用态(version={V})——发布/停用链路漂移，需人工核查",
                        tenantId, d.LocationId, d.LocationCode, d.BinVersion);
        }, _logger, ct);
    }
}
