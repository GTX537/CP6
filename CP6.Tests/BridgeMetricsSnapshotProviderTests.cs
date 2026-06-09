using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>
/// T15 / Gap 2.3 — Prometheus /metrics 集計ロジック（<see cref="BridgeMetricsSnapshotProvider"/>）の単体テスト。
///
/// テスト観点：
/// 1. (HookName, Status) ごとに件数が正しく集計される
/// 2. RetryQueueDepth は FAILED のみ、DeadLetterCount は DEAD のみを数える。論理削除行は除外
/// </summary>
public class BridgeMetricsSnapshotProviderTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static IntegrationEvent Evt(string hook, string status, bool deleted = false) => new()
    {
        Id = Guid.NewGuid(),
        SourceModule = "MES",
        TargetModule = "WMS",
        HookName = hook,
        SourceNo = "WO-" + Guid.NewGuid().ToString("N")[..6],
        Status = status,
        Attempts = 1,
        CorrelationId = Guid.NewGuid(),
        IsDeleted = deleted,
        Creator = "test",
        CreateDate = DateTime.Now,
    };

    [Fact]
    public async Task GetSnapshot_GroupsByHookAndStatus()
    {
        using var db = NewDb();
        db.IntegrationEvents.AddRange(
            Evt("OnWorkOrderIssuedAsync", IntegrationEventStatus.Success),
            Evt("OnWorkOrderIssuedAsync", IntegrationEventStatus.Success),
            Evt("OnWorkOrderIssuedAsync", IntegrationEventStatus.Skipped),
            Evt("OnShipmentConfirmedAsync", IntegrationEventStatus.Success));
        await db.SaveChangesAsync();

        var snapshot = await new BridgeMetricsSnapshotProvider(db).GetSnapshotAsync();

        Assert.Equal(3, snapshot.HookStatusCounts.Count); // 3 つの (hook,status) 組合せ
        Assert.Equal(2, snapshot.HookStatusCounts
            .Single(x => x.Hook == "OnWorkOrderIssuedAsync" && x.Status == IntegrationEventStatus.Success).Count);
        Assert.Equal(1, snapshot.HookStatusCounts
            .Single(x => x.Hook == "OnWorkOrderIssuedAsync" && x.Status == IntegrationEventStatus.Skipped).Count);
        Assert.Equal(1, snapshot.HookStatusCounts
            .Single(x => x.Hook == "OnShipmentConfirmedAsync" && x.Status == IntegrationEventStatus.Success).Count);
    }

    [Fact]
    public async Task GetSnapshot_CountsRetryQueueAndDeadLetter_ExcludesDeleted()
    {
        using var db = NewDb();
        db.IntegrationEvents.AddRange(
            Evt("OnOrderCreatedAsync", IntegrationEventStatus.Failed),
            Evt("OnOrderCreatedAsync", IntegrationEventStatus.Failed),
            Evt("OnProductionCompletedAsync", IntegrationEventStatus.DeadLetter),
            Evt("OnProductionCompletedAsync", IntegrationEventStatus.Success),
            Evt("OnOrderCreatedAsync", IntegrationEventStatus.Failed, deleted: true)); // 論理削除 → 除外
        await db.SaveChangesAsync();

        var snapshot = await new BridgeMetricsSnapshotProvider(db).GetSnapshotAsync();

        Assert.Equal(2, snapshot.RetryQueueDepth);   // FAILED 2 件（削除済は数えない）
        Assert.Equal(1, snapshot.DeadLetterCount);   // DEAD 1 件
        Assert.DoesNotContain(snapshot.HookStatusCounts, x => x.Count == 0);
    }
}
