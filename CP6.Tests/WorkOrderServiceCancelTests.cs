using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Core.Services.Mes;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace CP6.Tests;

/// <summary>
/// Phase 6 — WorkOrderService.CancelAsync 単体テスト
///
/// テスト観点：
/// 1. Status &lt;= Issued の指図は取消成功（Status=9 + Remarks に取消理由刻印）
/// 2. Status &gt;= InProgress の指図は InvalidOperationException
/// 3. 既に Cancelled の指図は false を返す（冪等）
/// 4. 指図不在は InvalidOperationException
/// 5. reason 空白は ArgumentException（監査用必須）
/// </summary>
public class WorkOrderServiceCancelTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static WorkOrderService NewSvc(CP6Context db)
    {
        var seq = new Mock<IMesSequenceService>().Object;
        var wmsBridge = new Mock<IWmsBridgeHook>().Object;
        return new WorkOrderService(db, seq, wmsBridge);
    }

    private static WorkOrder SeedWO(CP6Context db, string no, int status)
    {
        var wo = new WorkOrder
        {
            Id = Guid.NewGuid(),
            WorkOrderNo = no,
            Status = status,
            Creator = "seed",
            CreateDate = DateTime.Now,
        };
        db.WorkOrders.Add(wo);
        db.SaveChanges();
        return wo;
    }

    [Theory]
    [InlineData(WorkOrderStatus.Draft)]
    [InlineData(WorkOrderStatus.Confirmed)]
    [InlineData(WorkOrderStatus.Issued)]
    public async Task CancelAsync_AllowedStatuses_Succeed(int initialStatus)
    {
        using var db = NewDb();
        SeedWO(db, "WO-CANCEL-001", initialStatus);
        var svc = NewSvc(db);

        var result = await svc.CancelAsync("WO-CANCEL-001", "客先取消", "tester");

        Assert.True(result);
        var wo = await db.WorkOrders.FirstAsync(x => x.WorkOrderNo == "WO-CANCEL-001");
        Assert.Equal(WorkOrderStatus.Cancelled, wo.Status);
        Assert.Equal("tester", wo.Modifier);
        Assert.Contains("CANCELLED", wo.Remarks ?? "");
        Assert.Contains("客先取消", wo.Remarks ?? "");
    }

    [Theory]
    [InlineData(WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.Completed)]
    [InlineData(WorkOrderStatus.Interrupted)]
    [InlineData(WorkOrderStatus.Inspected)]
    public async Task CancelAsync_BlockedStatuses_Throw(int initialStatus)
    {
        using var db = NewDb();
        SeedWO(db, "WO-BLOCK-002", initialStatus);
        var svc = NewSvc(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CancelAsync("WO-BLOCK-002", "理由", "u"));
        Assert.Contains("ME-MSG-CANCEL-001", ex.Message);

        // 副作用なしを保証
        var wo = await db.WorkOrders.FirstAsync(x => x.WorkOrderNo == "WO-BLOCK-002");
        Assert.Equal(initialStatus, wo.Status);
    }

    [Fact]
    public async Task CancelAsync_AlreadyCancelled_ReturnsFalse_Idempotent()
    {
        using var db = NewDb();
        SeedWO(db, "WO-IDEMP-003", WorkOrderStatus.Cancelled);
        var svc = NewSvc(db);

        var result = await svc.CancelAsync("WO-IDEMP-003", "再取消", "u");

        Assert.False(result);
        var wo = await db.WorkOrders.FirstAsync(x => x.WorkOrderNo == "WO-IDEMP-003");
        Assert.Equal(WorkOrderStatus.Cancelled, wo.Status);
    }

    [Fact]
    public async Task CancelAsync_NotFound_Throws()
    {
        using var db = NewDb();
        var svc = NewSvc(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CancelAsync("WO-NOPE-999", "理由", "u"));
        Assert.Contains("ME-MSG-043", ex.Message);
    }

    [Fact]
    public async Task CancelAsync_EmptyReason_ThrowsArgumentException()
    {
        using var db = NewDb();
        SeedWO(db, "WO-VALID-004", WorkOrderStatus.Confirmed);
        var svc = NewSvc(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CancelAsync("WO-VALID-004", "  ", "u"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CancelAsync("WO-VALID-004", "", "u"));
    }

    [Fact]
    public async Task CancelAsync_AppendsReason_PreservesExistingRemarks()
    {
        using var db = NewDb();
        var wo = SeedWO(db, "WO-APPEND-005", WorkOrderStatus.Issued);
        wo.Remarks = "既存メモ：優先対応";
        await db.SaveChangesAsync();
        var svc = NewSvc(db);

        await svc.CancelAsync("WO-APPEND-005", "客先取消", "tester");

        var updated = await db.WorkOrders.FirstAsync(x => x.WorkOrderNo == "WO-APPEND-005");
        Assert.Contains("既存メモ：優先対応", updated.Remarks ?? "");
        Assert.Contains("CANCELLED", updated.Remarks ?? "");
        Assert.Contains("客先取消", updated.Remarks ?? "");
    }
}
