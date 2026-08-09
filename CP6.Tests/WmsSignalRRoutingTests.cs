using CP6.Core.Services.Wms;
using CP6.Core.Utilities;
using CP6.WebApi.Hubs;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CP6.Tests;

public class WmsSignalRRoutingTests
{
    [Fact]
    public async Task OnConnected_JoinsGeneralGroup_AlongsideTenantRouting()
    {
        var context = new Mock<HubCallerContext>();
        context.SetupGet(x => x.ConnectionId).Returns("connection-1");
        context.SetupGet(x => x.User).Returns((System.Security.Claims.ClaimsPrincipal?)null);
        var groups = new Mock<IGroupManager>();
        var hub = new WmsHub(NullLogger<WmsHub>.Instance)
        {
            Context = context.Object,
            Groups = groups.Object,
        };

        await hub.OnConnectedAsync();

        groups.Verify(x => x.AddToGroupAsync(
            "connection-1", WmsHub.GeneralGroup, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StockChanged_UsesExclusiveGeneralAndFilterGroups_NotClientsAll()
    {
        var general = new Mock<IClientProxy>();
        var warehouse = new Mock<IClientProxy>();
        var product = new Mock<IClientProxy>();
        var all = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.All).Returns(all.Object);
        clients.Setup(x => x.Group(WmsHub.GeneralGroup)).Returns(general.Object);
        clients.Setup(x => x.Group("wh:W1")).Returns(warehouse.Object);
        clients.Setup(x => x.Group("product:P1")).Returns(product.Object);
        var hub = new Mock<IHubContext<WmsHub>>();
        hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var notifier = new SignalRWmsNotifier(
            hub.Object,
            Mock.Of<INotificationPublisher>(),
            NullLogger<SignalRWmsNotifier>.Instance,
            Mock.Of<IStringLocalizer>());

        await notifier.NotifyStockChangedAsync(new StockChangedEvent
        {
            WarehouseCd = "W1",
            ProductCd = "P1",
            LocationCd = "A-01",
        });

        foreach (var proxy in new[] { general, warehouse, product })
        {
            proxy.Verify(x => x.SendCoreAsync(
                "StockChanged", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        all.Verify(x => x.SendCoreAsync(
            It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
