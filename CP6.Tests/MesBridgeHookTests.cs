using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Core.Services.Mes;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CP6.Tests;

/// <summary>
/// Phase1 ERP→MES 自動展開フック 単体・結合テスト
///
/// テスト観点：
/// 1. MesBridgeHook（標準実装）が IWorkOrderService.ExpandFromOrderAsync を呼ぶ
/// 2. InvalidOperationException（既に指図あり等）は Skipped に変換、例外は伝播しない
/// 3. NoOpMesBridgeHook は何もしない（既定＝無効）
/// 4. OrderService.CreateAsync 成功時に hook が発火し、受注が製造指図へ自動展開される（実書き込み）
/// 5. NoOp の場合は指図が生成されない（既定動作の回帰保護）
/// </summary>
public class MesBridgeHookTests
{
    private static Microsoft.Extensions.Logging.ILogger<MesBridgeHook> NullLogger
        => NullLogger<MesBridgeHook>.Instance;

    /// <summary>ExpandFromOrderAsync は明示トランザクションを使うため、InMemory 警告を抑止</summary>
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    // ───────── MesBridgeHook（標準実装） ─────────

    [Fact]
    public async Task MesBridgeHook_OnOrderCreated_Success()
    {
        var mockWo = new Mock<IWorkOrderService>();
        mockWo.Setup(s => s.ExpandFromOrderAsync(It.IsAny<ExpandFromOrderRequest>(), It.IsAny<string>()))
            .ReturnsAsync(new List<string> { "WO20260101-0001", "WO20260101-0002" });

        var hook = new MesBridgeHook(mockWo.Object, NullLogger);
        var result = await hook.OnOrderCreatedAsync("WO_PA001", "u");

        Assert.True(result.Success);
        Assert.Equal(2, result.CreatedCount);
        Assert.Equal("WO20260101-0001", result.WorkOrderNo);
    }

    [Fact]
    public async Task MesBridgeHook_BusinessExceptionShouldBeSkipped()
    {
        var mockWo = new Mock<IWorkOrderService>();
        mockWo.Setup(s => s.ExpandFromOrderAsync(It.IsAny<ExpandFromOrderRequest>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("ME-MSG-005")); // 既に指図あり

        var hook = new MesBridgeHook(mockWo.Object, NullLogger);
        var result = await hook.OnOrderCreatedAsync("WO_PA001", "u");

        Assert.False(result.Success);
        Assert.Contains("ME-MSG-005", result.Message);
    }

    [Fact]
    public async Task MesBridgeHook_UnexpectedExceptionShouldNotPropagate()
    {
        var mockWo = new Mock<IWorkOrderService>();
        mockWo.Setup(s => s.ExpandFromOrderAsync(It.IsAny<ExpandFromOrderRequest>(), It.IsAny<string>()))
            .ThrowsAsync(new TimeoutException("DB 接続失敗"));

        var hook = new MesBridgeHook(mockWo.Object, NullLogger);
        var result = await hook.OnOrderCreatedAsync("WO_PA001", "u");

        Assert.False(result.Success);
        Assert.Contains("DB 接続失敗", result.Message);
    }

    [Fact]
    public async Task NoOpHook_ShouldReturnSkippedWithoutSideEffect()
    {
        var noop = new NoOpMesBridgeHook();
        var r = await noop.OnOrderCreatedAsync("WO_PA001", "u");

        Assert.False(r.Success);
        Assert.Contains("MesBridge:Enabled=false", r.Message);
    }

    // ───────── OrderService.CreateAsync 連動（実書き込み） ─────────

    [Fact]
    public async Task OrderCreate_WithMesBridge_ShouldAutoExpandWorkOrder()
    {
        var db = NewDb();

        // 製品マスタ工程（指図展開で WorkOrderProcess に複写される）
        db.ProductProcesses.Add(new ProductProcess
        {
            ProductCd = "PROD-X", TaskCd = "T01", ProcessCd = "P01", SortOrder = 1,
            Spec01 = "印刷",
        });
        await db.SaveChangesAsync();

        // 実 WorkOrderService を直結（WMS 連動は NoOp）
        var woService = new WorkOrderService(db, new MesSequenceService(db), new NoOpWmsBridgeHook());
        var mesBridge = new MesBridgeHook(woService, NullLogger);

        var mockPe = new Mock<IPowerEggWorkflowService>();
        var svc = new OrderService(db, mockPe.Object, new NoOpWmsBridgeHook(), mesBridge);

        var dto = new OrderDto
        {
            CustomerCd = "C001",
            OrderType = "01",
            OrderDate = DateTime.Today,
            Details = new()
            {
                new OrderDetailDto { ProductCd = "PROD-X", Quantity = 1000m }
            }
        };

        var webOrderNo = await svc.CreateAsync(dto, "tester");

        // 受注作成 → 製造指図が自動展開された
        var wo = await db.WorkOrders.AsNoTracking()
            .SingleAsync(x => x.WebOrderNo == webOrderNo);
        Assert.Equal("PROD-X", wo.ProductCd);
        Assert.Equal(1000m, wo.ProductionQty);
        Assert.Equal(1, wo.Status); // 確定済（受注展開直後）

        // 製品工程が指図工程に複写された
        var procs = await db.WorkOrderProcesses.AsNoTracking()
            .Where(p => p.WorkOrderNo == wo.WorkOrderNo).ToListAsync();
        Assert.Single(procs);
        Assert.Equal("P01", procs[0].ProcessCd);
    }

    [Fact]
    public async Task OrderCreate_WithNoOpMesBridge_ShouldNotExpand()
    {
        var db = NewDb();
        var mockPe = new Mock<IPowerEggWorkflowService>();
        // MesBridge 既定無効（NoOp）
        var svc = new OrderService(db, mockPe.Object, new NoOpWmsBridgeHook(), new NoOpMesBridgeHook());

        var dto = new OrderDto
        {
            CustomerCd = "C001",
            OrderType = "01",
            OrderDate = DateTime.Today,
            Details = new()
            {
                new OrderDetailDto { ProductCd = "PROD-X", Quantity = 1000m }
            }
        };

        var webOrderNo = await svc.CreateAsync(dto, "tester");

        // 指図は生成されない
        Assert.False(await db.WorkOrders.AnyAsync(x => x.WebOrderNo == webOrderNo));
    }
}
