using CP6.Core.Services.Mes;
using CP6.Entity.DTOs.Mes;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// OEE 定时自動計算 BackgroundService（多線程例題）
/// </summary>
/// <remarks>
/// JD「多线程・委托」要件のサンプル実装：
///   - 5 分間隔で本日 OEE を再計算 → T_OeeDaily に永続化
///   - 日付変更時（00:05）に前日分を一括再計算
///   - 各回 Scoped IOeeService を新規生成（DbContext 寿命を正しく管理）
/// 委托 (Func/Action) や Task.Delay の典型パターンを示す。
/// </remarks>
public class OeeCalculationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OeeCalculationService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public OeeCalculationService(IServiceScopeFactory scopeFactory, ILogger<OeeCalculationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OEE Calculation Service 起動");

        // 起動時に少し待ってから（DB 準備完了確認）
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        DateTime? lastDay = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var today = DateTime.Today;
                var recalcYesterday = lastDay.HasValue && lastDay.Value < today;
                var yesterday = today.AddDays(-1);

                // 章10 §5 按租户循环：各租户独立作用域重算其 OEE（T_OeeDaily 是 BaseBizEntity，按租户隔离）
                await TenantScopeRunner.ForEachTenantAsync(_scopeFactory, async (sp, tenantId, ct) =>
                {
                    var oee = sp.GetRequiredService<IOeeService>();

                    if (recalcYesterday)
                    {
                        var ny = await oee.RecalculateAsync(new OeeRecalcRequest { TargetDate = yesterday }, "OeeWorker");
                        _logger.LogInformation("前日 OEE 再計算完了 租户 {Tenant}：{Date} {Count}件", tenantId, yesterday.ToString("yyyy-MM-dd"), ny);
                    }

                    var n = await oee.RecalculateAsync(new OeeRecalcRequest { TargetDate = today }, "OeeWorker");
                    _logger.LogDebug("本日 OEE 再計算 租户 {Tenant}：{Count}件", tenantId, n);
                }, _logger, stoppingToken);

                lastDay = today;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OEE Calculation Service 例外");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("OEE Calculation Service 停止");
    }
}
