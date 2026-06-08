using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace CP6.Tests;

/// <summary>
/// Phase 6 — OrderService.CancelAsync 状態機テスト
///
/// テスト観点：
/// 1. 受注不在 → Rejected
/// 2. 既に Cancelled → Rejected（冪等保護）
/// 3. 既に Shipped → Rejected
/// 4. ShipStatus &gt;= 5 (一部出荷以上) → Rejected
/// 5. force=false かつ 半路状態 → NeedsDecision（DB 未変更）
/// 6. force=false かつ 全項目可 → Cancelled（自動進行）
/// 7. force=true かつ FullyCascaded → Cancelled
/// 8. force=true かつ 半路あり → PartiallyCancelled
/// 9. 空白 reason → ArgumentException
/// </summary>
public class OrderServiceCancelTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static void SeedOrder(CP6Context db, string webOrderNo, string status = OrderLifecycleStatus.Confirmed, int shipStatus = 0)
    {
        db.Orders.Add(new Order
        {
            Id = Guid.NewGuid(),
            WebOrderNo = webOrderNo,
            CustomerCd = "C001",
            OrderType = "01",
            OrderStatus = status,
            ShipStatus = shipStatus,
            Creator = "seed",
            CreateDate = DateTime.Now,
        });
        db.SaveChanges();
    }

    /// <summary>Probe + Execute 両方とも同じ結果を返す mock</summary>
    private static Mock<IOrderCancelBridgeHook> MockHook(OrderCancelHookResult result)
    {
        var mock = new Mock<IOrderCancelBridgeHook>();
        mock.Setup(h => h.OnOrderCancelledAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(result);
        return mock;
    }

    private static OrderService NewSvc(CP6Context db, Mock<IOrderCancelBridgeHook>? hook = null)
    {
        hook ??= MockHook(new OrderCancelHookResult { Success = true, FullyCascaded = true });
        var pe = new Mock<IPowerEggWorkflowService>().Object;
        var wms = new Mock<IWmsBridgeHook>().Object;
        return new OrderService(db, pe, wms, null, hook.Object);
    }

    // ───────── Rejected 分岐 ─────────

    [Fact]
    public async Task CancelAsync_OrderNotFound_Rejected()
    {
        using var db = NewDb();
        var svc = NewSvc(db);

        var result = await svc.CancelAsync("NOPE-001", "客先取消", false, "u");

        Assert.Equal(CancelOutcome.Rejected, result.Outcome);
        Assert.Contains("404", result.Message);
        Assert.NotEqual(Guid.Empty, result.CorrelationId);
    }

    [Fact]
    public async Task CancelAsync_AlreadyCancelled_Rejected()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-CANCELLED", OrderLifecycleStatus.Cancelled);
        var svc = NewSvc(db);

        var result = await svc.CancelAsync("WEB-CANCELLED", "再取消", false, "u");

        Assert.Equal(CancelOutcome.Rejected, result.Outcome);
        Assert.Contains("既に取消済", result.Message);
    }

    [Fact]
    public async Task CancelAsync_AlreadyShipped_Rejected()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-SHIPPED", OrderLifecycleStatus.Shipped);
        var svc = NewSvc(db);

        var result = await svc.CancelAsync("WEB-SHIPPED", "理由", false, "u");

        Assert.Equal(CancelOutcome.Rejected, result.Outcome);
        Assert.Contains("出荷済", result.Message);
    }

    [Fact]
    public async Task CancelAsync_PartialShipment_Rejected()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-PARTSHIP", OrderLifecycleStatus.Confirmed, shipStatus: 5);
        var svc = NewSvc(db);

        var result = await svc.CancelAsync("WEB-PARTSHIP", "理由", true, "u");

        Assert.Equal(CancelOutcome.Rejected, result.Outcome);
        Assert.Contains("ShipStatus", result.Message);
    }

    // ───────── NeedsDecision 分岐 ─────────

    [Fact]
    public async Task CancelAsync_ProbeShowsHalfState_ForceFalse_NeedsDecision_NoDbChange()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-HALF");

        var hook = MockHook(new OrderCancelHookResult
        {
            Success = true,
            FullyCascaded = false,
            WorkOrders = new List<WorkOrderProbe>
            {
                new() { WorkOrderNo = "WO-A", Status = 3, AutoCancellable = false },
            },
        });
        var svc = NewSvc(db, hook);

        var result = await svc.CancelAsync("WEB-HALF", "客先取消", force: false, "u");

        Assert.Equal(CancelOutcome.NeedsDecision, result.Outcome);
        // 探査だけ呼ばれた、実施は呼ばれていない
        hook.Verify(h => h.OnOrderCancelledAsync(It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<Guid>()), Times.Once);
        hook.Verify(h => h.OnOrderCancelledAsync(It.IsAny<string>(), true, It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);

        // Order ヘッダは未変更
        var order = await db.Orders.FirstAsync(x => x.WebOrderNo == "WEB-HALF");
        Assert.Equal(OrderLifecycleStatus.Confirmed, order.OrderStatus);
        Assert.Null(order.CancelledAt);
        Assert.Null(order.CancelReason);
    }

    // ───────── Cancelled 分岐 ─────────

    [Fact]
    public async Task CancelAsync_ForceFalse_AllAutoCancellable_AutoProceedsToFullCancel()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-EASY");

        var probe = new OrderCancelHookResult
        {
            Success = true,
            FullyCascaded = false, // 探査は cascade なしの状態
            WorkOrders = new() { new() { WorkOrderNo = "WO-X", Status = 1, AutoCancellable = true } },
            Outbounds = new() { new() { OutboundNo = "OUT-X", Status = 1, AutoCancellable = true } },
        };
        var execute = new OrderCancelHookResult
        {
            Success = true,
            FullyCascaded = true,
            WorkOrders = probe.WorkOrders,
            Outbounds = probe.Outbounds,
        };

        // probe → false 呼び出し / execute → true 呼び出し
        var hook = new Mock<IOrderCancelBridgeHook>();
        hook.SetupSequence(h => h.OnOrderCancelledAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(probe)
            .ReturnsAsync(execute);

        var svc = NewSvc(db, hook);
        var result = await svc.CancelAsync("WEB-EASY", "客先取消", force: false, "u");

        Assert.Equal(CancelOutcome.Cancelled, result.Outcome);

        var order = await db.Orders.FirstAsync(x => x.WebOrderNo == "WEB-EASY");
        Assert.Equal(OrderLifecycleStatus.Cancelled, order.OrderStatus);
        Assert.NotNull(order.CancelledAt);
        Assert.Equal("客先取消", order.CancelReason);
        Assert.Equal("u", order.Modifier);
    }

    [Fact]
    public async Task CancelAsync_ForceTrue_FullyCascaded_Cancelled()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-FORCE");

        var hook = MockHook(new OrderCancelHookResult
        {
            Success = true,
            FullyCascaded = true,
            WorkOrders = new() { new() { WorkOrderNo = "WO-Y", Status = 1, AutoCancellable = true, Cancelled = true } },
        });
        var svc = NewSvc(db, hook);

        var result = await svc.CancelAsync("WEB-FORCE", "強制取消", force: true, "u");

        Assert.Equal(CancelOutcome.Cancelled, result.Outcome);
        var order = await db.Orders.FirstAsync(x => x.WebOrderNo == "WEB-FORCE");
        Assert.Equal(OrderLifecycleStatus.Cancelled, order.OrderStatus);
    }

    // ───────── PartiallyCancelled 分岐 ─────────

    [Fact]
    public async Task CancelAsync_ForceTrue_HalfStatePresent_PartiallyCancelled()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-PARTIAL");

        var hook = MockHook(new OrderCancelHookResult
        {
            Success = true,
            FullyCascaded = false, // 一部不可
            WorkOrders = new()
            {
                new() { WorkOrderNo = "WO-OK", Status = 1, AutoCancellable = true, Cancelled = true },
                new() { WorkOrderNo = "WO-BAD", Status = 3, AutoCancellable = false },
            },
        });
        var svc = NewSvc(db, hook);

        var result = await svc.CancelAsync("WEB-PARTIAL", "強制取消", force: true, "u");

        Assert.Equal(CancelOutcome.PartiallyCancelled, result.Outcome);
        var order = await db.Orders.FirstAsync(x => x.WebOrderNo == "WEB-PARTIAL");
        Assert.Equal(OrderLifecycleStatus.PartiallyCancelled, order.OrderStatus);
        Assert.NotNull(order.CancelledAt);
        Assert.Equal("強制取消", order.CancelReason);
    }

    // ───────── 引数バリデーション ─────────

    [Fact]
    public async Task CancelAsync_EmptyReason_Throws()
    {
        using var db = NewDb();
        SeedOrder(db, "WEB-ARG");
        var svc = NewSvc(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CancelAsync("WEB-ARG", "  ", false, "u"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CancelAsync("  ", "理由", false, "u"));
    }
}
