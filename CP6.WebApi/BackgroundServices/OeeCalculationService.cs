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
                using var scope = _scopeFactory.CreateScope();
                var oee = scope.ServiceProvider.GetRequiredService<IOeeService>();

                var today = DateTime.Today;

                // 日付変更検知 → 昨日分を 1 回だけ再計算
                if (lastDay.HasValue && lastDay.Value < today)
                {
                    var yesterday = today.AddDays(-1);
                    var ny = await oee.RecalculateAsync(new OeeRecalcRequest { TargetDate = yesterday }, "OeeWorker");
                    _logger.LogInformation("前日 OEE 再計算完了：{Date} {Count}件", yesterday.ToString("yyyy-MM-dd"), ny);
                }
                lastDay = today;

                // 本日 OEE 再計算
                var n = await oee.RecalculateAsync(new OeeRecalcRequest { TargetDate = today }, "OeeWorker");
                _logger.LogDebug("本日 OEE 再計算：{Count}件", n);
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
