using CP6.Core.EFDbContext;
using CP6.Core.Services.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// 設備状態監視 BackgroundService（多線程例題）
/// </summary>
/// <remarks>
/// 製造現場のロジック例：
///   - 30 秒間隔で稼働中(Status=1)設備をスキャン
///   - 直近 N 分間に ProductionResult が無い → 「アイドル」と判断
///   - 一定時間アイドル → 自動的に「停止(Status=0)」に変更 + SignalR 通知
///   - アイドル閾値はマシン CapacityPerHour から動的算出（簡略：10 分固定）
/// </remarks>
public class MachineStatusMonitor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MachineStatusMonitor> _logger;
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(10);

    public MachineStatusMonitor(IServiceScopeFactory scopeFactory, ILogger<MachineStatusMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Machine Status Monitor 起動");
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
                var notifier = scope.ServiceProvider.GetRequiredService<IMesNotifier>();

                // 稼働中設備
                var runningMachines = await db.Machines.AsNoTracking()
                    .Where(m => !m.IsDeleted && m.Status == 1)
                    .Select(m => m.MachineCd)
                    .ToListAsync(stoppingToken);

                if (runningMachines.Count == 0)
                {
                    await DelaySafe(stoppingToken);
                    continue;
                }

                var idleSince = DateTime.Now - IdleThreshold;

                // 各設備の最終実績日時
                var lastResults = await db.ProductionResults.AsNoTracking()
                    .Where(r => !r.IsDeleted && r.MachineCd != null && runningMachines.Contains(r.MachineCd!))
                    .GroupBy(r => r.MachineCd!)
                    .Select(g => new
                    {
                        MachineCd = g.Key,
                        Last = g.Max(x => x.CreateDate),
                    })
                    .ToDictionaryAsync(x => x.MachineCd, x => x.Last, stoppingToken);

                // 直近実績が IdleThreshold 以上前の設備をアイドル判定
                var idleMachines = runningMachines
                    .Where(cd => !lastResults.ContainsKey(cd) || lastResults[cd] < idleSince)
                    .ToList();

                if (idleMachines.Count == 0)
                {
                    await DelaySafe(stoppingToken);
                    continue;
                }

                // ステータス変更（停止）+ SignalR 通知
                var tracked = await db.Machines
                    .Where(m => idleMachines.Contains(m.MachineCd) && m.Status == 1)
                    .ToListAsync(stoppingToken);

                foreach (var m in tracked)
                {
                    m.Status = 0;
                    m.Modifier = "MachineMonitor";
                    m.ModifyDate = DateTime.Now;
                    _logger.LogInformation("設備 {Cd} アイドル検知 → 停止状態に変更", m.MachineCd);
                }
                if (tracked.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    foreach (var m in tracked)
                    {
                        try { await notifier.NotifyMachineStatusChangedAsync(m.MachineCd, 0); } catch { }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Machine Status Monitor 例外");
            }

            await DelaySafe(stoppingToken);
        }

        _logger.LogInformation("Machine Status Monitor 停止");
    }

    private static async Task DelaySafe(CancellationToken ct)
    {
        try { await Task.Delay(ScanInterval, ct); }
        catch (OperationCanceledException) { }
    }
}
