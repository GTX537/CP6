using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests.Fin;

/// <summary>
/// 审计 T4（spec §8 尾项）：IntegrationEvent.Creator 由桥调用方 operator 透传，非硬编码 "system"。
/// BridgeHookBase.PersistEventAsync 扩展了可选 operator 参数；本支油路桥（StockFinBridge/FinBridgeHook 等）
/// 各将 userName 透传落 Creator。无 operator 的存量桥（SpaceBridgeHook）回退 "system"。
/// </summary>
public class BridgeOperatorPassthroughTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    /// <summary>Skip 路径不触引擎，仅需一个占位实现。</summary>
    private sealed class NoopEngine : IAutoVoucherEngine
    {
        public Task<FinResult> GenerateAsync(FinBizEvent evt) => Task.FromResult(FinResult.Pass());
    }

    [Fact]
    public async Task StockFinBridge_PersistsOperatorAsCreator_NotSystem()
    {
        using var db = NewDb();
        var bridge = new StockFinBridge(db, new NoopEngine(), NullLogger<StockFinBridge>.Instance);

        // MOVE = 显式 Skipped 路径（无 GL 影响）→ 落一条 Skipped IntegrationEvent，不触引擎。
        var txn = new StockTransaction
        {
            TxnNo = "TXN-OP-001",
            TxnType = WmsTxnType.OUT,
            Qty = 5m,
            UnitPrice = 10m,
            TxnDateTime = DateTime.Now,
        };

        var r = await bridge.OnStockMovedAsync(txn, "MOVE", "alice");
        Assert.False(r.Success);
        Assert.StartsWith("SKIPPED:", r.Message);

        var evt = db.IntegrationEvents.IgnoreQueryFilters().Single();
        Assert.Equal("alice", evt.Creator);   // ★ 操作者透传，非 "system"
    }

    [Fact]
    public async Task StockFinBridge_NullOperator_FallsBackToSystem()
    {
        using var db = NewDb();
        var bridge = new StockFinBridge(db, new NoopEngine(), NullLogger<StockFinBridge>.Instance);
        var txn = new StockTransaction
        {
            TxnNo = "TXN-OP-002",
            TxnType = WmsTxnType.OUT,
            Qty = 1m,
            UnitPrice = 1m,
            TxnDateTime = DateTime.Now,
        };

        await bridge.OnStockMovedAsync(txn, "MOVE", null);

        var evt = db.IntegrationEvents.IgnoreQueryFilters().Single();
        Assert.Equal("system", evt.Creator);   // operator 缺省 → 回退 system
    }
}
