using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Mes;
using CP6.Tests.Platform;
using CP6.WebApi.Hubs;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Mes_realtime_actual_websocket_current_session_and_permission_revocation()
    {
        await using var h=await Seed();await GrantStocktakeDashboard(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies=await Login(host,h);
        using var negotiate=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"hubs/mes/negotiate?negotiateVersion=1"));
        Assert.Equal(HttpStatusCode.OK,negotiate.StatusCode);
        using var body=JsonDocument.Parse(await negotiate.Content.ReadAsStringAsync());
        var address=new UriBuilder(negotiate.RequestMessage!.RequestUri!){Scheme="ws",Path="/hubs/mes",Query="id="+Uri.EscapeDataString(body.RootElement.GetProperty("connectionToken").GetString()!)};
        using var socket=new ClientWebSocket();socket.Options.SetRequestHeader("Cookie",string.Join("; ",cookies.Select(x=>x.Key+"="+x.Value)));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await socket.ConnectAsync(address.Uri,timeout.Token);
        async Task Send(object value)=>await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)+"\u001e"),WebSocketMessageType.Text,true,timeout.Token);
        var pending="";
        async Task<JsonDocument> Receive()
        {
            var bytes=new byte[4096];
            while(true)
            {
                var end=pending.IndexOf('\u001e');
                if(end>=0){var frame=pending[..end];pending=pending[(end+1)..];return JsonDocument.Parse(frame);}
                var message=await socket.ReceiveAsync(bytes,timeout.Token);
                Assert.NotEqual(WebSocketMessageType.Close,message.MessageType);
                pending+=Encoding.UTF8.GetString(bytes,0,message.Count);
            }
        }
        async Task<JsonDocument> Completion(string id)
        {
            while(true)
            {
                var frame=await Receive();
                if(frame.RootElement.TryGetProperty("type",out var type)&&type.GetInt32()==6){frame.Dispose();continue;}
                Assert.Equal(3,frame.RootElement.GetProperty("type").GetInt32());
                Assert.Equal(id,frame.RootElement.GetProperty("invocationId").GetString());return frame;
            }
        }
        await Send(new{protocol="json",version=1});using(var handshake=await Receive())Assert.False(handshake.RootElement.TryGetProperty("error",out _));
        await Send(new{type=1,invocationId="current",target="SubscribeMachine",arguments=new[]{"CLIENT-CANNOT-SELECT-TENANT"}});
        using(var current=await Completion("current"))Assert.False(current.RootElement.TryGetProperty("error",out _),current.RootElement.ToString());
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==309&&a.ActionCode=="query").ExecuteDeleteAsync();
        await Send(new{type=1,invocationId="revoked",target="SubscribeWorkOrder",arguments=new[]{"FOREIGN-WO"}});
        using(var revoked=await Completion("revoked"))Assert.True(revoked.RootElement.TryGetProperty("error",out _));
        h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=309,ActionCode="query"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await Send(new{type=1,invocationId="restored",target="SubscribeMachine",arguments=new[]{"FOREIGN-MACHINE"}});
        using(var restored=await Completion("restored"))Assert.False(restored.RootElement.TryGetProperty("error",out _));
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Enable,false));
        await Send(new{type=1,invocationId="session-revoked",target="SubscribeMachine",arguments=new[]{"FOREIGN-MACHINE"}});
        using(var revoked=await Completion("session-revoked"))Assert.True(revoked.RootElement.TryGetProperty("error",out _));
    }
    [CurrentPermissionPostgresFact]
    public async Task Mes_realtime_actual_anonymous_negotiate_requires_authentication()
    {
        await using var h=await Seed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        using var response=await host.Send(new HttpRequestMessage(HttpMethod.Post,"hubs/mes/negotiate?negotiateVersion=1"));
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Mes_realtime_notifier_never_broadcasts_business_payload_or_cross_tenant_groups()
    {
        await using var h=await Seed();await using var other=await Seed();
        var allSends=0;var deliveries=new List<(string group,string method,object?[] args)>();
        var all=new Mock<IClientProxy>();all.Setup(x=>x.SendCoreAsync(It.IsAny<string>(),It.IsAny<object?[]>(),It.IsAny<CancellationToken>()))
            .Callback(()=>allSends++).Returns(Task.CompletedTask);
        var clients=new Mock<IHubClients>();clients.SetupGet(x=>x.All).Returns(all.Object);
        clients.Setup(x=>x.Group(It.IsAny<string>())).Returns((string name)=>
        {
            var target=new Mock<IClientProxy>();target.Setup(x=>x.SendCoreAsync(It.IsAny<string>(),It.IsAny<object?[]>(),It.IsAny<CancellationToken>()))
                .Callback<string,object?[],CancellationToken>((method,args,_)=>deliveries.Add((name,method,args))).Returns(Task.CompletedTask);
            return target.Object;
        });
        var context=new Mock<IHubContext<MesHub>>();context.SetupGet(x=>x.Clients).Returns(clients.Object);
        foreach(var tenant in new[]{h,other})
        {
            using var services=new ServiceCollection().AddSingleton(tenant.Db).BuildServiceProvider();
            var notifier=ActivatorUtilities.CreateInstance<SignalRMesNotifier>(services,context.Object,NullLogger<SignalRMesNotifier>.Instance);
            await notifier.NotifyProductionReportedAsync("PRIVATE-WO","PRIVATE-PROC",999m,333m);
            await notifier.NotifyDefectIssuedAsync("PRIVATE-DEFECT","PRIVATE-WO","PRIVATE-CATEGORY");
            await notifier.NotifyMachineStatusChangedAsync("PRIVATE-MACHINE",3);
            await notifier.NotifyWorkOrderStatusChangedAsync("PRIVATE-WO",4);
            await notifier.NotifyDowntimeRegisteredAsync("PRIVATE-DOWN","PRIVATE-MACHINE",1);
        }
        Assert.Equal(0,allSends);Assert.Equal(10,deliveries.Count);
        Assert.All(deliveries,x=>{Assert.Equal("MesInvalidatedV1",x.method);Assert.Empty(x.args);});
        Assert.Equal(5,deliveries.Count(x=>x.group==$"mes:v1:tenant:{h.Tenant:N}"));
        Assert.Equal(5,deliveries.Count(x=>x.group==$"mes:v1:tenant:{other.Tenant:N}"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Mes_realtime_subscription_uses_current_Owner_and_never_joins_client_named_groups()
    {
        await using var h=await Seed();var owner=await GrantStocktakeDashboard(h);
        using var services=new ServiceCollection().AddSingleton(h.Db).AddSingleton(new MesDashboardService(h.Db,owner)).BuildServiceProvider();
        var hub=ActivatorUtilities.CreateInstance<MesHub>(services,NullLogger<MesHub>.Instance);
        var caller=new Mock<HubCallerContext>();caller.SetupGet(x=>x.ConnectionId).Returns("owned-mes-connection");caller.SetupGet(x=>x.User).Returns(h.Http.User);
        var groups=new Mock<IGroupManager>();hub.Context=caller.Object;hub.Groups=groups.Object;
        await hub.OnConnectedAsync();await hub.SubscribeMachine("OTHER-TENANT-MACHINE");await hub.SubscribeWorkOrder("OTHER-TENANT-WO");
        groups.Verify(x=>x.AddToGroupAsync("owned-mes-connection",$"mes:v1:tenant:{h.Tenant:N}",It.IsAny<CancellationToken>()),Times.Once);
        groups.VerifyNoOtherCalls();
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==309 && a.ActionCode=="query").ExecuteDeleteAsync();
        await Assert.ThrowsAnyAsync<Exception>(()=>hub.SubscribeMachine("OTHER-TENANT-MACHINE"));
        await Assert.ThrowsAnyAsync<Exception>(()=>hub.SubscribeWorkOrder("OTHER-TENANT-WO"));
    }
}
