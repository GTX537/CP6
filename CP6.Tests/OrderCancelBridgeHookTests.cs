using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CP6.Tests;

/// <summary>
/// Phase 6 — IOrderCancelBridgeHook 単体テスト
///
/// テスト観点：
/// 1. 探査モード (force=false)：DB 未変更で関連項目のプローブのみ返却
/// 2. 全項目 AutoCancellable のときは探査モードで「全自動取消可能」メッセージ
/// 3. 半路状態（WO InProgress / Outbound Picking）含むときは「二段確認必要」
/// 4. 実施モード (force=true)：OutboundOrder 先、WorkOrder 後の順序で取消
/// 5. NoOpOrderCancelBridgeHook は Success=false かつ SKIPPED メッセージ
/// </summary>
public class OrderCancelBridgeHookTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static void SeedWO(CP6Context db, string no, string webOrderNo, int status)
    {
        db.WorkOrders.Add(new WorkOrder
        {
            Id = Guid.NewGuid(),
            WorkOrderNo = no,
            WebOrderNo = webOrderNo,
            Status = status,
            Creator = "seed",
            CreateDate = DateTime.Now,
        });
    }

    private static void SeedOutbound(CP6Context db, string no, string? webOrderNo, string? workOrderNo, int status, int type = OutboundType.Shipping)
    {
        db.OutboundOrders.Add(new OutboundOrder
        {
            Id = Guid.NewGuid(),
            OutboundNo = no,
            WebOrderNo = webOrderNo,
            WorkOrderNo = workOrderNo,
            Status = status,
            OutboundType = type,
            WarehouseCd = "WH01",
            Creator = "seed",
            CreateDate = DateTime.Now,
        });
    }

    private static OrderCancelBridgeHook NewHook(
        CP6Context db,
        Mock<IWorkOrderService>? mockWo = null,
        Mock<IOutboundService>? mockOut = null)
    {
        mockWo ??= new Mock<IWorkOrderService>();
        mockOut ??= new Mock<IOutboundService>();
        return new OrderCancelBridgeHook(
            db, mockWo.Object, mockOut.Object,
            NullLogger<OrderCancelBridgeHook>.Instance);
    }

    [Fact]
    public async Task ProbeMode_NoSideEffects_AllAutoCancellable()
    {
        using var db = NewDb();
        SeedWO(db, "WO-001", "WEB-001", WorkOrderStatus.Confirmed);
        SeedOutbound(db, "OUT-001", "WEB-001", null, OutboundOrderStatus.Confirmed);
        db.SaveChanges();

        var mockWo = new Mock<IWorkOrderService>();
        var mockOut = new Mock<IOutboundService>();
        var hook = NewHook(db, mockWo, mockOut);

        var result = await hook.OnOrderCancelledAsync("WEB-001", force: false, "u", Guid.NewGuid());

        Assert.True(result.Success);
        Assert.False(result.FullyCascaded); // 探査モードは cascade なし
        Assert.Single(result.WorkOrders);
        Assert.Single(result.Outbounds);
        Assert.True(result.WorkOrders[0].AutoCancellable);
        Assert.True(result.Outbounds[0].AutoCancellable);
        Assert.Contains("全関連項目が自動取消可能", result.Message);
        // 実装 service は呼ばれない
        mockWo.Verify(s => s.CancelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        mockOut.Verify(s => s.CancelOrderAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProbeMode_DetectsHalfStateAndReturnsNeedsDecision()
    {
        using var db = NewDb();
        SeedWO(db, "WO-002", "WEB-002", WorkOrderStatus.InProgress); // 着手済
        SeedOutbound(db, "OUT-002", "WEB-002", null, OutboundOrderStatus.Allocated);
        db.SaveChanges();

        var hook = NewHook(db);
        var result = await hook.OnOrderCancelledAsync("WEB-002", force: false, "u", Guid.NewGuid());

        Assert.True(result.Success);
        Assert.False(result.WorkOrders[0].AutoCancellable);
        Assert.True(result.Outbounds[0].AutoCancellable);
        Assert.Contains("二段確認必要", result.Message);
    }

    [Fact]
    public async Task ProbeMode_PickingOutboundNotAutoCancellable()
    {
        using var db = NewDb();
        SeedOutbound(db, "OUT-PICK", "WEB-003", null, OutboundOrderStatus.Picking);
        db.SaveChanges();

        var hook = NewHook(db);
        var result = await hook.OnOrderCancelledAsync("WEB-003", force: false, "u", Guid.NewGuid());

        Assert.Single(result.Outbounds);
        Assert.False(result.Outbounds[0].AutoCancellable);
    }

    [Fact]
    public async Task ForceMode_CascadesInOrder_OutboundFirstThenWO()
    {
        using var db = NewDb();
        SeedWO(db, "WO-004", "WEB-004", WorkOrderStatus.Issued);
        SeedOutbound(db, "OUT-004-A", "WEB-004", null, OutboundOrderStatus.Confirmed, OutboundType.Shipping);
        SeedOutbound(db, "OUT-004-B", null, "WO-004", OutboundOrderStatus.Allocated, OutboundType.Material);
        db.SaveChanges();

        var callOrder = new List<string>();
        var mockOut = new Mock<IOutboundService>();
        mockOut.Setup(s => s.CancelOrderAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string?>((no, u) => callOrder.Add($"OUT:{no}"))
            .Returns(Task.CompletedTask);

        var mockWo = new Mock<IWorkOrderService>();
        mockWo.Setup(s => s.CancelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string?>((no, _, _) => callOrder.Add($"WO:{no}"))
            .ReturnsAsync(true);

        var hook = NewHook(db, mockWo, mockOut);
        var corr = Guid.NewGuid();
        var result = await hook.OnOrderCancelledAsync("WEB-004", force: true, "u", corr);

        Assert.True(result.Success);
        Assert.True(result.FullyCascaded);
        Assert.Equal(2, result.Outbounds.Count);
        Assert.Single(result.WorkOrders);

        // OutboundOrder 全 → WO の順序が守られていること
        var outboundIndex = callOrder.FindIndex(s => s.StartsWith("OUT:"));
        var woIndex = callOrder.FindIndex(s => s.StartsWith("WO:"));
        Assert.True(outboundIndex >= 0 && woIndex >= 0);
        Assert.Equal(2, callOrder.Count(s => s.StartsWith("OUT:")));
        Assert.True(callOrder.Last().StartsWith("WO:"), $"Last call should be WO. Order: {string.Join(", ", callOrder)}");

        // 各プローブに結果が記録
        Assert.All(result.Outbounds, p => Assert.Equal(true, p.Cancelled));
        Assert.All(result.WorkOrders, p => Assert.Equal(true, p.Cancelled));
    }

    [Fact]
    public async Task ForceMode_SkipsNonAutoCancellableItems()
    {
        using var db = NewDb();
        SeedWO(db, "WO-005-A", "WEB-005", WorkOrderStatus.Confirmed); // 取消可
        SeedWO(db, "WO-005-B", "WEB-005", WorkOrderStatus.InProgress); // 不可
        SeedOutbound(db, "OUT-005", "WEB-005", null, OutboundOrderStatus.Picking); // 不可
        db.SaveChanges();

        var mockOut = new Mock<IOutboundService>();
        var mockWo = new Mock<IWorkOrderService>();
        mockWo.Setup(s => s.CancelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var hook = NewHook(db, mockWo, mockOut);
        var result = await hook.OnOrderCancelledAsync("WEB-005", force: true, "u", Guid.NewGuid());

        Assert.True(result.Success);
        Assert.False(result.FullyCascaded); // 半路あるため

        // 不可項目は service 呼ばれない
        mockOut.Verify(s => s.CancelOrderAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        mockWo.Verify(s => s.CancelAsync("WO-005-A", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        mockWo.Verify(s => s.CancelAsync("WO-005-B", It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task NoOpHook_AlwaysReturnsSkipped()
    {
        var hook = new NoOpOrderCancelBridgeHook();
        var result = await hook.OnOrderCancelledAsync("WEB-NOPE", force: true, "u", Guid.NewGuid());

        Assert.False(result.Success);
        Assert.False(result.FullyCascaded);
        Assert.Contains("SKIPPED", result.Message);
        Assert.Contains("Enabled=false", result.Message);
    }

    [Fact]
    public async Task ForceMode_RecordsFailureFromCancelOrderAsync()
    {
        using var db = NewDb();
        SeedOutbound(db, "OUT-FAIL", "WEB-FAIL", null, OutboundOrderStatus.Confirmed);
        db.SaveChanges();

        var mockOut = new Mock<IOutboundService>();
        mockOut.Setup(s => s.CancelOrderAsync("OUT-FAIL", It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("DB connection lost"));

        var hook = NewHook(db, null, mockOut);
        var result = await hook.OnOrderCancelledAsync("WEB-FAIL", force: true, "u", Guid.NewGuid());

        Assert.True(result.Success); // hook 自体は best-effort
        Assert.False(result.FullyCascaded);
        Assert.False(result.Outbounds[0].Cancelled);
        Assert.Contains("FAILED", result.Outbounds[0].Message);
        Assert.Contains("DB connection lost", result.Outbounds[0].Message);
    }
}
