using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CP6.Tests;

/// <summary>
/// Phase 6 S5 — BridgeHookBase 持久化挙動の単体テスト
///
/// テスト観点：
/// 1. Success: T_IntegrationEvent に 1 行 (Status=SUCCESS, TargetNo セット済)
/// 2. Skipped (InvalidOperationException): 1 行 (Status=SKIPPED, LastError=例外メッセージ)
/// 3. Failed (想定外例外): 1 行 (Status=FAILED, NextRetryAt セット済 ≈ now + 60s)
/// 4. 各呼び出しは独立 CorrelationId（同一 hook 連続 2 回で異なる corrId）
/// 5. ペイロードが JSON で保存される（後で Dispatcher が反序列化できること）
/// </summary>
public class BridgeHookPersistenceTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static WmsBridgeHook NewWmsHook(CP6Context db, Mock<IOutboundService>? mockOut = null, Mock<IInboundService>? mockIn = null)
    {
        mockOut ??= new Mock<IOutboundService>();
        mockIn ??= new Mock<IInboundService>();
        return new WmsBridgeHook(db, mockOut.Object, mockIn.Object,
            NullLogger<WmsBridgeHook>.Instance);
    }

    [Fact]
    public async Task Success_WritesIntegrationEvent_WithTargetNo()
    {
        using var db = NewDb();
        var mockOut = new Mock<IOutboundService>();
        mockOut.Setup(s => s.CreateFromWorkOrderAsync("WO-S-001", "u"))
            .ReturnsAsync("OUT-S-001");

        var hook = NewWmsHook(db, mockOut);
        var result = await hook.OnWorkOrderIssuedAsync("WO-S-001", "u");

        Assert.True(result.Success);

        var evt = await db.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Success, evt.Status);
        Assert.Equal("MES", evt.SourceModule);
        Assert.Equal("WMS", evt.TargetModule);
        Assert.Equal("WO-S-001", evt.SourceNo);
        Assert.Equal("OUT-S-001", evt.TargetNo);
        Assert.Null(evt.LastError);
        Assert.Null(evt.NextRetryAt); // Success は retry なし
        Assert.NotEqual(Guid.Empty, evt.CorrelationId);
        Assert.Contains("WO-S-001", evt.PayloadJson); // payload に workOrderNo 含む
    }

    [Fact]
    public async Task BusinessException_WritesSkippedEvent()
    {
        using var db = NewDb();
        var mockOut = new Mock<IOutboundService>();
        mockOut.Setup(s => s.CreateFromWorkOrderAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("既に展開済"));

        var hook = NewWmsHook(db, mockOut);
        var result = await hook.OnWorkOrderIssuedAsync("WO-SKIP-001", "u");

        Assert.False(result.Success);
        var evt = await db.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Skipped, evt.Status);
        Assert.Contains("既に展開済", evt.LastError ?? "");
        Assert.Null(evt.NextRetryAt); // Skipped は retry なし
    }

    [Fact]
    public async Task UnexpectedException_WritesFailedEvent_WithRetryAt()
    {
        using var db = NewDb();
        var mockOut = new Mock<IOutboundService>();
        mockOut.Setup(s => s.CreateFromWorkOrderAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new TimeoutException("DB 接続失敗"));

        var hook = NewWmsHook(db, mockOut);
        var before = DateTime.UtcNow;
        var result = await hook.OnWorkOrderIssuedAsync("WO-FAIL-001", "u");
        var after = DateTime.UtcNow.AddSeconds(70);

        Assert.False(result.Success);
        var evt = await db.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Failed, evt.Status);
        Assert.Contains("DB 接続失敗", evt.LastError ?? "");
        Assert.NotNull(evt.NextRetryAt);
        // 60s 退避（誤差は許容）
        Assert.True(evt.NextRetryAt >= before.AddSeconds(55) && evt.NextRetryAt <= after);
    }

    [Fact]
    public async Task MultipleCalls_GenerateUniqueCorrelationIds()
    {
        using var db = NewDb();
        var mockOut = new Mock<IOutboundService>();
        mockOut.Setup(s => s.CreateFromWorkOrderAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string wo, string? u) => $"OUT-{wo}");

        var hook = NewWmsHook(db, mockOut);
        await hook.OnWorkOrderIssuedAsync("WO-A", "u");
        await hook.OnWorkOrderIssuedAsync("WO-B", "u");
        await hook.OnWorkOrderIssuedAsync("WO-C", "u");

        var events = await db.IntegrationEvents.ToListAsync();
        Assert.Equal(3, events.Count);
        Assert.Equal(3, events.Select(e => e.CorrelationId).Distinct().Count());
        Assert.All(events, e => Assert.Equal(IntegrationEventStatus.Success, e.Status));
    }

    [Fact]
    public async Task MesBridgeHook_AlsoPersistsToIntegrationEvent()
    {
        using var db = NewDb();
        var mockWo = new Mock<IWorkOrderService>();
        mockWo.Setup(s => s.ExpandFromOrderAsync(It.IsAny<ExpandFromOrderRequest>(), It.IsAny<string>()))
            .ReturnsAsync(new List<string> { "WO-M-001" });

        var hook = new MesBridgeHook(db, mockWo.Object, NullLogger<MesBridgeHook>.Instance);
        await hook.OnOrderCreatedAsync("WEB-M-001", "u");

        var evt = await db.IntegrationEvents.SingleAsync();
        Assert.Equal("ERP", evt.SourceModule);
        Assert.Equal("MES", evt.TargetModule);
        Assert.Equal("WEB-M-001", evt.SourceNo);
        Assert.Equal("WO-M-001", evt.TargetNo); // 最初の指図 No
        Assert.Equal(IntegrationEventStatus.Success, evt.Status);
    }

    [Fact]
    public async Task ErpBridgeHook_SkippedNonShippingOutbound_PersistsSkipped()
    {
        using var db = NewDb();
        db.OutboundOrders.Add(new OutboundOrder
        {
            Id = Guid.NewGuid(),
            OutboundNo = "OUT-MAT-001",
            OutboundType = OutboundType.Material, // 材料出庫（Shipping ではない）
            WarehouseCd = "WH01",
            Status = OutboundOrderStatus.Confirmed,
            Creator = "seed",
            CreateDate = DateTime.Now,
        });
        await db.SaveChangesAsync();

        var hook = new ErpBridgeHook(db, NullLogger<ErpBridgeHook>.Instance);
        var result = await hook.OnShipmentConfirmedAsync("OUT-MAT-001", "u");

        Assert.False(result.Success);
        var evt = await db.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Skipped, evt.Status);
        Assert.Equal("WMS", evt.SourceModule);
        Assert.Equal("ERP", evt.TargetModule);
    }
}
