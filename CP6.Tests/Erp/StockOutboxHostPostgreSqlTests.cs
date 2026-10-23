using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Integration.StockOutbox;
using CP6.Platform.EntityFramework;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Stock_outbox_actual_Program_worker_delivers_empty_hint_to_authenticated_websocket()
    {
        await using var h = await Seed(); var owner = await GrantStocktakeDashboard(h); await SeedInventory(h);
        // This producer has no notifier. Only the actual hosted durable dispatcher can send this hint.
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, new Dictionary<string,string>
        {
            ["Startup__SkipHostedServices"] = "false", ["StockOutboxHost__Enabled"] = "true",
            ["StockOutboxHost__HostId"] = "Main.OwnedPostgreSqlTestHost",
            ["StockOutboxHost__Issuer"] = "https://stock-outbox.test.cp6.invalid", ["StockOutboxHost__Region"] = "owned-local",
            ["Kafka__BootstrapServers"] = "", ["RabbitMQ__HostName"] = "", ["ConnectionStrings__Redis"] = ""
        });
        var cookies = await Login(host, h);
        using var negotiate = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "hubs/wms/negotiate?negotiateVersion=1"));
        Assert.Equal(HttpStatusCode.OK, negotiate.StatusCode);
        using var body = JsonDocument.Parse(await negotiate.Content.ReadAsStringAsync());
        var address = new UriBuilder(negotiate.RequestMessage!.RequestUri!)
        { Scheme = "ws", Path = "/hubs/wms", Query = "id=" + Uri.EscapeDataString(body.RootElement.GetProperty("connectionToken").GetString()!) };
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Cookie", string.Join("; ", cookies.Select(x => x.Key + "=" + x.Value)));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        await socket.ConnectAsync(address.Uri, deadline.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, deadline.Token);
        var pending = "";
        async Task<JsonDocument> Receive()
        {
            var bytes = new byte[4096];
            while (true)
            {
                var end = pending.IndexOf('\u001e');
                if (end >= 0) { var frame = pending[..end]; pending = pending[(end + 1)..]; return JsonDocument.Parse(frame); }
                var message = await socket.ReceiveAsync(bytes, deadline.Token);
                Assert.NotEqual(WebSocketMessageType.Close, message.MessageType);
                pending += Encoding.UTF8.GetString(bytes, 0, message.Count);
            }
        }
        using (var handshake = await Receive()) Assert.False(handshake.RootElement.TryGetProperty("error", out _));
        // Await an actual Hub method completion so the authenticated tenant group is established.
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":1,\"invocationId\":\"ready\",\"target\":\"SubscribeWarehouse\",\"arguments\":[\"client-filter\"]}\u001e"), WebSocketMessageType.Text, true, deadline.Token);
        while (true)
        {
            using var ready = await Receive();
            if (ready.RootElement.GetProperty("type").GetInt32() == 6) continue;
            Assert.Equal(3, ready.RootElement.GetProperty("type").GetInt32());
            Assert.False(ready.RootElement.TryGetProperty("error", out _)); break;
        }
        await InventoryMover(h, owner).ApplyCommandAsync(StockApply());
        while (true)
        {
            using var frame = await Receive();
            if (frame.RootElement.GetProperty("type").GetInt32() == 6) continue;
            Assert.Equal("InventoryInvalidatedV1", frame.RootElement.GetProperty("target").GetString());
            Assert.Empty(frame.RootElement.GetProperty("arguments").EnumerateArray()); break;
        }
        await using var queue = StockQueue(h);
        while (await queue.Set<Cp6OutboxMessage>().Where(n => n.TenantId == h.Tenant).Select(n => n.Status).SingleAsync(deadline.Token) != Cp6OutboxStatus.Published)
            await Task.Delay(50, deadline.Token);
        Assert.Equal(1, await StockNotifications(h));
        Assert.NotEqual(Guid.Empty, (await queue.Set<StockOutboxRunV1>().AsNoTracking().SingleAsync(r => r.RootId == StockOutboxProtocolV1.Root(h.Tenant).Id, deadline.Token)).RequestId);
    }
}
