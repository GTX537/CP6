using System.Data;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.BackgroundServices;

/// <summary>月末折旧 Worker（spec §6.2）：每日检查，当前开启期为月末且无本期批量批次 → 生成 Draft 草稿（不过账）。
/// 过账权交人工复核或结账钩子兜底。</summary>
public class AssetDepreciationWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AssetDepreciationWorker> _logger;
    private readonly TimeProvider _clock;

    public AssetDepreciationWorker(IServiceScopeFactory scopeFactory, ILogger<AssetDepreciationWorker> logger, TimeProvider? clock = null)
    {
        _scopeFactory = scopeFactory; _logger = logger; _clock = clock ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("固定资产折旧 worker 启动");
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await ProcessOnceAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { _logger.LogInformation("固定资产折旧 worker 停止"); }
    }

    public Task ProcessOnceAsync(CancellationToken ct = default) => ProcessOnceAsync(_clock.GetLocalNow().Date, ct);

    internal async Task ProcessOnceAsync(DateTime today, CancellationToken ct = default)
    {
        await TenantScopeRunner.ForEachTenantAsync(_scopeFactory, async (sp, tenantId, c) =>
        {
            if (today.Day != DateTime.DaysInMonth(today.Year, today.Month)) return;

            var db = sp.GetRequiredService<CP6Context>();
            await using var transaction = db.Database.IsNpgsql()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, c)
                : null;
            DateTime? expiresAt = null;
            if (transaction != null)
            {
                await AuthenticationCompletionGateV1.AcquireAsync(db, c);
                var tenant = await db.Sys_Tenants.AsNoTracking().Where(t => t.Id == tenantId)
                    .Select(t => new { t.Enable, t.ExpireDate }).SingleOrDefaultAsync(c);
                if (tenant is not { Enable: true } || tenant.ExpireDate <= _clock.GetLocalNow().DateTime) return;
                expiresAt = tenant.ExpireDate;
            }

            var period = await db.FiscalPeriods.FirstOrDefaultAsync(
                p => p.Year == today.Year && p.Month == today.Month && p.Status == PeriodStatus.Open, c);
            if (period == null) return;

            bool batchExists = await db.DepreciationRuns.AnyAsync(r => r.FiscalPeriodId == period.Id
                && r.RunMode != DepreciationRunMode.DisposalFinal && r.Status != DepreciationRunStatus.Reversed, c);
            if (batchExists) return;

            var dep = sp.GetRequiredService<IAssetDepreciationService>();
            var r = await dep.RunAsync(period.Id, "worker", DepreciationRunMode.Worker);
            if (!r.Ok)
            {
                _logger.LogWarning("[AssetDeprec] 租户 {Tenant} 备草稿失败：{Code}", tenantId, r.Code);
                return;
            }

            if (transaction != null)
            {
                c.ThrowIfCancellationRequested();
                if (expiresAt <= _clock.GetLocalNow().DateTime) return;
                try { await transaction.CommitAsync(c); }
                catch (Exception error)
                {
                    // The next scan first checks for the committed batch for this period.
                    throw new InvalidOperationException("ASSET_DEPRECIATION_COMMIT_UNKNOWN", error);
                }
            }
            _logger.LogInformation("[AssetDeprec] 租户 {Tenant} {Ym} 已备折旧草稿待复核", tenantId, $"{period.Year}-{period.Month:D2}");
        }, _logger, ct);
    }
}
