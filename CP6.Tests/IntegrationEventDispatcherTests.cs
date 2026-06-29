using CP6.Core.Services;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels;
using Moq;

namespace CP6.Tests;

/// <summary>
/// IntegrationEvent dispatcher route tests.
/// </summary>
public class IntegrationEventDispatcherTests
{
    [Fact]
    public async Task Dispatch_MesBridgeRoute_CallsMesHook()
    {
        var mes = new Mock<IMesBridgeHook>();
        var wms = new Mock<IWmsBridgeHook>();
        var erp = new Mock<IErpBridgeHook>();
        var cancel = new Mock<IOrderCancelBridgeHook>();
        var fin = new Mock<IFinBridgeHook>();
        mes.Setup(h => h.OnOrderCreatedAsync("WEB-001", "tester"))
            .ReturnsAsync(MesBridgeResult.Ok(new[] { "WO-001" }));
        var dispatcher = new IntegrationEventDispatcher(
            mes.Object, wms.Object, erp.Object, cancel.Object, fin.Object, Mock.Of<ISpaceBridgeHook>());

        var ok = await dispatcher.DispatchAsync(new IntegrationEvent
        {
            SourceModule = "ERP",
            TargetModule = "MES",
            HookName = "OnOrderCreatedAsync",
            PayloadJson = """{"webOrderNo":"WEB-001","userName":"tester"}""",
        });

        Assert.True(ok);
        mes.Verify(h => h.OnOrderCreatedAsync("WEB-001", "tester"), Times.Once);
    }

    [Fact]
    public async Task Dispatch_WmsProductionCompletedRoute_PassesGoodQty()
    {
        var mes = new Mock<IMesBridgeHook>();
        var wms = new Mock<IWmsBridgeHook>();
        var erp = new Mock<IErpBridgeHook>();
        var cancel = new Mock<IOrderCancelBridgeHook>();
        var fin = new Mock<IFinBridgeHook>();
        wms.Setup(h => h.OnProductionCompletedAsync("WO-100", 12.34m, "u1"))
            .ReturnsAsync(WmsBridgeResult.Ok("IN-100"));
        var dispatcher = new IntegrationEventDispatcher(
            mes.Object, wms.Object, erp.Object, cancel.Object, fin.Object, Mock.Of<ISpaceBridgeHook>());

        var ok = await dispatcher.DispatchAsync(new IntegrationEvent
        {
            SourceModule = "MES",
            TargetModule = "WMS",
            HookName = "OnProductionCompletedAsync",
            PayloadJson = """{"workOrderNo":"WO-100","goodQty":12.34,"userName":"u1"}""",
        });

        Assert.True(ok);
        wms.Verify(h => h.OnProductionCompletedAsync("WO-100", 12.34m, "u1"), Times.Once);
    }

    [Fact]
    public async Task Dispatch_UnknownRoute_Throws()
    {
        var dispatcher = new IntegrationEventDispatcher(
            Mock.Of<IMesBridgeHook>(),
            Mock.Of<IWmsBridgeHook>(),
            Mock.Of<IErpBridgeHook>(),
            Mock.Of<IOrderCancelBridgeHook>(),
            Mock.Of<IFinBridgeHook>(),
            Mock.Of<ISpaceBridgeHook>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(new IntegrationEvent
            {
                SourceModule = "ERP",
                TargetModule = "ERP",
                HookName = "UnknownHook",
                PayloadJson = "{}",
            }));

        Assert.Contains("DISPATCH-404", ex.Message);
    }

    [Fact]
    public async Task Dispatch_HookReturnsFailedResult_ReturnsFalse()
    {
        var wms = new Mock<IWmsBridgeHook>();
        wms.Setup(h => h.OnWorkOrderIssuedAsync("WO-F", "retry"))
            .ReturnsAsync(WmsBridgeResult.Failed("x"));
        var dispatcher = new IntegrationEventDispatcher(
            Mock.Of<IMesBridgeHook>(),
            wms.Object,
            Mock.Of<IErpBridgeHook>(),
            Mock.Of<IOrderCancelBridgeHook>(),
            Mock.Of<IFinBridgeHook>(),
            Mock.Of<ISpaceBridgeHook>());

        var ok = await dispatcher.DispatchAsync(new IntegrationEvent
        {
            SourceModule = "MES",
            TargetModule = "WMS",
            HookName = "OnWorkOrderIssuedAsync",
            PayloadJson = """{"workOrderNo":"WO-F","userName":"retry"}""",
        });

        Assert.False(ok);
    }
}
