using CP6.Core.Services.Mes;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs;

namespace CP6.Tests;

/// <summary>
/// Gap 3.3 生産計画達成率レポート（<see cref="PlanAchievementService"/>）テスト。
///
/// 観点：
/// 1. 達成率 = CompletedQty / ProductionQty、グループ集計（製品）
/// 2. 全体サマリ：加重達成率 = 良品合計/計画合計、不良率、OnTarget 件数
/// 3. OnlyCompleted=true は完了/検査済のみ（着手中を除外）
/// 4. 基準日（ActualEndDate）の範囲フィルタ
/// 5. 月別グループ集計
/// </summary>
public class PlanAchievementServiceTests
{
    private static CP6.Core.EFDbContext.CP6Context NewDb() => TestHelper.CreateInMemoryContext();

    private static WorkOrder Wo(string no, string product, decimal planned, decimal good, decimal defect,
        int status = WorkOrderStatus.Completed, DateTime? actualEnd = null, string? customer = null, string? productName = null)
        => new()
        {
            WorkOrderNo = no,
            ProductCd = product,
            ProductName = productName,
            CustomerCd = customer,
            ProductionQty = planned,
            CompletedQty = good,
            DefectQty = defect,
            Status = status,
            ActualEndDate = actualEnd ?? new DateTime(2026, 3, 15),
            Creator = "test",
            CreateDate = DateTime.Now,
        };

    [Fact]
    public async Task Summary_ComputesAchievementRate_GroupedByProduct()
    {
        using var db = NewDb();
        db.WorkOrders.AddRange(
            Wo("WO1", "P001", planned: 100m, good: 90m, defect: 10m),
            Wo("WO2", "P001", planned: 100m, good: 100m, defect: 0m),
            Wo("WO3", "P002", planned: 50m, good: 25m, defect: 5m));
        await db.SaveChangesAsync();

        var summary = await new PlanAchievementService(db).GetSummaryAsync(
            new PlanAchievementQuery { GroupBy = PlanAchievementGroupBy.Product });

        Assert.Equal(2, summary.Rows.Count);
        var p001 = summary.Rows.Single(r => r.GroupKey == "P001");
        Assert.Equal(190m, p001.GoodQty);
        Assert.Equal(200m, p001.PlannedQty);
        Assert.Equal(0.95m, p001.AchievementRate);   // 190/200
        Assert.Equal(1, p001.OnTargetCount);          // WO2 が達成（100/100）

        var p002 = summary.Rows.Single(r => r.GroupKey == "P002");
        Assert.Equal(0.5m, p002.AchievementRate);     // 25/50
    }

    [Fact]
    public async Task Summary_OverallRatesAndOnTarget()
    {
        using var db = NewDb();
        db.WorkOrders.AddRange(
            Wo("WO1", "P001", 100m, 90m, 10m),
            Wo("WO2", "P002", 100m, 110m, 0m)); // 計画超過 → 達成
        await db.SaveChangesAsync();

        var s = await new PlanAchievementService(db).GetSummaryAsync(new PlanAchievementQuery());

        Assert.Equal(2, s.TotalWorkOrders);
        Assert.Equal(200m, s.TotalPlannedQty);
        Assert.Equal(200m, s.TotalGoodQty);
        Assert.Equal(1m, s.AchievementRate);                 // 200/200
        Assert.Equal(1, s.OnTargetCount);                    // WO2 のみ達成（WO1 は 90<100）
        Assert.Equal(0.0476m, s.DefectRate);                 // 10/(200+10)=0.047619→丸め
    }

    [Fact]
    public async Task Summary_OnlyCompleted_ExcludesInProgress()
    {
        using var db = NewDb();
        db.WorkOrders.AddRange(
            Wo("WO1", "P001", 100m, 100m, 0m, status: WorkOrderStatus.Completed),
            Wo("WO2", "P001", 100m, 40m, 0m, status: WorkOrderStatus.InProgress));
        await db.SaveChangesAsync();

        var svc = new PlanAchievementService(db);

        var onlyDone = await svc.GetSummaryAsync(new PlanAchievementQuery { OnlyCompleted = true });
        Assert.Equal(1, onlyDone.TotalWorkOrders);
        Assert.Equal(1m, onlyDone.AchievementRate);

        var all = await svc.GetSummaryAsync(new PlanAchievementQuery { OnlyCompleted = false });
        Assert.Equal(2, all.TotalWorkOrders);
        Assert.Equal(0.7m, all.AchievementRate);  // (100+40)/(100+100)
    }

    [Fact]
    public async Task Summary_DateRangeFilter_OnActualEndDate()
    {
        using var db = NewDb();
        db.WorkOrders.AddRange(
            Wo("WO1", "P001", 100m, 100m, 0m, actualEnd: new DateTime(2026, 1, 10)),
            Wo("WO2", "P001", 100m, 50m, 0m, actualEnd: new DateTime(2026, 3, 20)));
        await db.SaveChangesAsync();

        var s = await new PlanAchievementService(db).GetSummaryAsync(new PlanAchievementQuery
        {
            DateFrom = new DateTime(2026, 3, 1),
            DateTo = new DateTime(2026, 3, 31),
        });

        Assert.Equal(1, s.TotalWorkOrders);   // WO2 のみ範囲内
        Assert.Equal(0.5m, s.AchievementRate);
    }

    [Fact]
    public async Task Summary_GroupByMonth()
    {
        using var db = NewDb();
        db.WorkOrders.AddRange(
            Wo("WO1", "P001", 100m, 80m, 0m, actualEnd: new DateTime(2026, 1, 15)),
            Wo("WO2", "P002", 100m, 100m, 0m, actualEnd: new DateTime(2026, 1, 28)),
            Wo("WO3", "P003", 100m, 60m, 0m, actualEnd: new DateTime(2026, 2, 5)));
        await db.SaveChangesAsync();

        var s = await new PlanAchievementService(db).GetSummaryAsync(
            new PlanAchievementQuery { GroupBy = PlanAchievementGroupBy.Month });

        Assert.Equal(2, s.Rows.Count);
        var jan = s.Rows.Single(r => r.GroupKey == "202601");
        Assert.Equal("2026-01", jan.GroupLabel);
        Assert.Equal(0.9m, jan.AchievementRate);  // (80+100)/(100+100)
    }
}
