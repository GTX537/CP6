using CP6.Core.Services;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CP6.Tests;

/// <summary>
/// WM-3.5 自動展開フック 単体テスト
///
/// テスト観点：
/// 1. WmsBridgeHook（標準実装）が IOutboundService を正しく呼ぶ
/// 2. InvalidOperationException（既に展開済等）は Skipped 結果に変換、例外は伝播しない
/// 3. NoOpWmsBridgeHook は何もしない（無効化時）
/// 4. WorkOrderService.IssueAsync 成功時に hook が発火
/// 5. OrderService.CreateAsync 成功時に hook が発火
/// 6. hook が例外を投げても WorkOrder/Order の操作は成功（best-effort）
/// </summary>
public class WmsBridgeHookTests
{
    private static Microsoft.Extensions.Logging.ILogger<WmsBridgeHook> NullLogger
        => Microsoft.Extensions.Logging.Abstractions.NullLogger<WmsBridgeHook>.Instance;

    // ───────── WmsBridgeHook（標準実装） ─────────

    [Fact]
    public async Task WmsBridgeHook_OnWorkOrderIssued_Success()
    {
        var mockOutbound = new Mock<IOutboundService>();
        mockOutbound.Setup(o => o.CreateFromWorkOrderAsync("WO001", It.IsAny<string>()))
            .ReturnsAsync("OUT20260523-00001");

        var hook = new WmsBridgeHook(mockOutbound.Object, NullLogger);
        var result = await hook.OnWorkOrderIssuedAsync("WO001", "u");

        Assert.True(result.Success);
        Assert.Equal("OUT20260523-00001", result.OutboundNo);
        mockOutbound.Verify(o => o.CreateFromWorkOrderAsync("WO001", "u"), Times.Once);
    }

    [Fact]
    public async Task WmsBridgeHook_OnWorkOrderIssued_BusinessExceptionShouldBeSkipped()
    {
        var mockOutbound = new Mock<IOutboundService>();
        mockOutbound.Setup(o => o.CreateFromWorkOrderAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("既に展開済"));

        var hook = new WmsBridgeHook(mockOutbound.Object, NullLogger);
        var result = await hook.OnWorkOrderIssuedAsync("WO001", "u");

        Assert.False(result.Success);
        Assert.Contains("既に展開済", result.Message);
    }

    [Fact]
    public async Task WmsBridgeHook_OnWorkOrderIssued_UnexpectedExceptionShouldNotPropagate()
    {
        var mockOutbound = new Mock<IOutboundService>();
        mockOutbound.Setup(o => o.CreateFromWorkOrderAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new TimeoutException("DB 接続失敗"));

        var hook = new WmsBridgeHook(mockOutbound.Object, NullLogger);

        // 例外は伝播せず、Failed 結果として返る
        var result = await hook.OnWorkOrderIssuedAsync("WO001", "u");
        Assert.False(result.Success);
        Assert.Contains("DB 接続失敗", result.Message);
    }

    [Fact]
    public async Task WmsBridgeHook_OnOrderCreated_Success()
    {
        var mockOutbound = new Mock<IOutboundService>();
        mockOutbound.Setup(o => o.CreateFromOrderAsync("WO_PA001", It.IsAny<string>()))
            .ReturnsAsync("OUT20260523-00002");

        var hook = new WmsBridgeHook(mockOutbound.Object, NullLogger);
        var result = await hook.OnOrderCreatedAsync("WO_PA001", "u");

        Assert.True(result.Success);
        Assert.Equal("OUT20260523-00002", result.OutboundNo);
    }

    // ───────── NoOpWmsBridgeHook ─────────

    [Fact]
    public async Task NoOpHook_ShouldReturnSkippedWithoutSideEffect()
    {
        var noop = new NoOpWmsBridgeHook();
        var r1 = await noop.OnWorkOrderIssuedAsync("WO001", "u");
        var r2 = await noop.OnOrderCreatedAsync("WO_PA001", "u");

        Assert.False(r1.Success);
        Assert.False(r2.Success);
        Assert.Contains("WmsBridge:Enabled=false", r1.Message);
        Assert.Contains("WmsBridge:Enabled=false", r2.Message);
    }

    // ───────── WorkOrderService.IssueAsync 連動 ─────────

    [Fact]
    public async Task WorkOrderIssue_ShouldInvokeBridge()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.WorkOrders.Add(new WorkOrder
        {
            WorkOrderNo = "WO001", Status = 1, ProductCd = "P1",
            ProductionQty = 100m,
        });
        db.WorkOrderProcesses.Add(new WorkOrderProcess
        {
            WorkOrderNo = "WO001", ProcessCd = "P01", TaskCd = "T01", SortOrder = 1,
        });
        db.SaveChanges();

        var mockBridge = new Mock<IWmsBridgeHook>();
        mockBridge.Setup(b => b.OnWorkOrderIssuedAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(WmsBridgeResult.Ok("OUT001"));

        var svc = new WorkOrderService(db, new MesSequenceService(db), mockBridge.Object);
        await svc.IssueAsync("WO001", "tester");

        Assert.Equal(2, (await db.WorkOrders.SingleAsync()).Status);
        mockBridge.Verify(b => b.OnWorkOrderIssuedAsync("WO001", "tester"), Times.Once);
    }

    [Fact]
    public async Task WorkOrderIssue_BridgeFailureShouldNotBlockIssue()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.WorkOrders.Add(new WorkOrder
        {
            WorkOrderNo = "WO001", Status = 1, ProductCd = "P1", ProductionQty = 10m,
        });
        db.WorkOrderProcesses.Add(new WorkOrderProcess
        {
            WorkOrderNo = "WO001", ProcessCd = "P01", TaskCd = "T01", SortOrder = 1,
        });
        db.SaveChanges();

        // hook 自体が例外を返す想定（実装で握り潰されるはずなので Failed 結果）
        var mockBridge = new Mock<IWmsBridgeHook>();
        mockBridge.Setup(b => b.OnWorkOrderIssuedAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(WmsBridgeResult.Failed("WMS DB unreachable"));

        var svc = new WorkOrderService(db, new MesSequenceService(db), mockBridge.Object);
        // 例外伝播しないこと
        await svc.IssueAsync("WO001", "tester");

        // Issue 自体は成功
        Assert.Equal(2, (await db.WorkOrders.SingleAsync()).Status);
    }

    // ───────── OrderService.CreateAsync 連動 ─────────

    [Fact]
    public async Task OrderCreate_ShouldInvokeBridge()
    {
        var db = TestHelper.CreateInMemoryContext();

        var mockBridge = new Mock<IWmsBridgeHook>();
        mockBridge.Setup(b => b.OnOrderCreatedAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(WmsBridgeResult.Ok("OUT_S001"));

        var mockPe = new Mock<IPowerEggWorkflowService>();
        var svc = new OrderService(db, mockPe.Object, mockBridge.Object);

        var dto = new OrderDto
        {
            CustomerCd = "C001",
            OrderType = "01",
            OrderDate = DateTime.Today,
            Details = new()
            {
                new OrderDetailDto
                {
                    ProductCd = "PROD-X",
                    Quantity = 1000m,
                }
            }
        };

        var no = await svc.CreateAsync(dto, "tester");

        Assert.StartsWith("WO", no);
        mockBridge.Verify(b => b.OnOrderCreatedAsync(no, "tester"), Times.Once);
    }
}
