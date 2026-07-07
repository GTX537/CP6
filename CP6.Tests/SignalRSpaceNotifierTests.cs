using CP6.WebApi.Hubs;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CP6.Tests;

/// <summary>
/// SignalRSpaceNotifier 側効果テスト（照 DeadLetterNotifierTests の Mock&lt;IHubContext&gt; 範式）。
/// </summary>
public class SignalRSpaceNotifierTests
{
    private static (SignalRSpaceNotifier Notifier, Mock<IClientProxy> ClientProxy) NewNotifier()
    {
        var clientProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        var hub = new Mock<IHubContext<SpaceHub>>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);
        hub.Setup(h => h.Clients).Returns(clients.Object);

        return (new SignalRSpaceNotifier(
            hub.Object,
            NullLogger<SignalRSpaceNotifier>.Instance), clientProxy);
    }

    [Fact]
    public async Task Notify_PushesLocationPublishedToAllClients()
    {
        var (notifier, clientProxy) = NewNotifier();

        await notifier.NotifyLocationPublishedAsync("LPUB-20260707-0001", 3, "SUCCESS");

        clientProxy.Verify(c => c.SendCoreAsync(
            "LocationPublished",
            It.IsAny<object[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Notify_SwallowsHubException_DoesNotPropagate()
    {
        var clientProxy = new Mock<IClientProxy>();
        clientProxy
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub down"));
        var clients = new Mock<IHubClients>();
        var hub = new Mock<IHubContext<SpaceHub>>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var notifier = new SignalRSpaceNotifier(hub.Object, NullLogger<SignalRSpaceNotifier>.Instance);

        // 契約：例外を投げない（吞錯）── 呼び出しが素通りすれば合格
        var ex = await Record.ExceptionAsync(
            () => notifier.NotifyLocationPublishedAsync("LPUB-20260707-0002", 1, "SUCCESS"));
        Assert.Null(ex);
    }
}
