using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task RmaApplication_entry_source_picker_applies_current_scope_before_paging_and_search()
    {
        await using var h = await Seed(); await GrantRma(h);
        for (var i = 0; i < 52; i++) h.Db.OutboundOrders.Add(new() { OutboundNo = $"SHIP-{i:D3}",
            OutboundType = OutboundType.Shipping, Status = OutboundOrderStatus.Completed, WarehouseCd = "W1", CustomerCd = "C1", Creator = "price-owner" });
        h.Db.OutboundOrders.AddRange(
            new() { OutboundNo = "000-HIDDEN", OutboundType = OutboundType.Shipping, Status = OutboundOrderStatus.Completed, WarehouseCd = "W2" },
            new() { OutboundNo = "000-DRAFT", OutboundType = OutboundType.Shipping, Status = OutboundOrderStatus.Draft, WarehouseCd = "W1" },
            new() { OutboundNo = "000-MATERIAL", OutboundType = OutboundType.Material, Status = OutboundOrderStatus.Completed, WarehouseCd = "W1" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); var o = RmaApplications(h);
        var scope = await o.ScopeAsync(); Assert.Equal(h.User, scope.ActorId); Assert.Equal(h.Tenant, scope.TenantId);
        var first = await o.SourcesAsync(null); Assert.Equal(50, first.Length); Assert.Equal("SHIP-000", first[0].Number);
        Assert.Equal(new[] { "SHIP-050", "SHIP-051" }, (await o.SourcesAsync(null, 2)).Select(x => x.Number));
        Assert.Equal("SHIP-051", Assert.Single(await o.SourcesAsync("051")).Number);
        Assert.Equal(50, (await o.SourcesAsync("C1")).Length);
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync(); Assert.Empty(await o.SourcesAsync(null));
        await RevokeTransferAction(h, "wms-rma", "query");
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.ScopeAsync())).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaApplication_entry_actual_HTTP_picker_scope_and_submit_reuse_existing_owner()
    {
        await using var h = await Seed(); var c = await ApplicationCommand(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        const string path = "api/wms/rma/native-applications-v1";
        using var scope = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path + "/scope"));
        Assert.Equal(HttpStatusCode.OK, scope.StatusCode); Assert.True(scope.Headers.CacheControl?.NoStore);
        using var identity = JsonDocument.Parse(await scope.Content.ReadAsStringAsync()); Assert.Equal(h.User, identity.RootElement.GetProperty("data").GetProperty("actorId").GetGuid());
        using var choices = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path + "/sources?keyword=RETURN")); Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        using var choice = JsonDocument.Parse(await choices.Content.ReadAsStringAsync()); Assert.Equal(c.OutboundId, choice.RootElement.GetProperty("data")[0].GetProperty("id").GetGuid());
        using var source = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{path}/sources/{c.OutboundId}")); Assert.Equal(HttpStatusCode.OK, source.StatusCode);
        using var created = await host.Send(OperationsRequest(cookies, HttpMethod.Post, path, c)); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var command = OperationsRequest(cookies, HttpMethod.Post, $"{path}/{c.ApplicationId}/submit", new RmaApplicationSubmitV1(Guid.NewGuid(), 1)); command.Headers.IfMatch.Add(created.Headers.ETag!);
        using var submitted = await host.Send(command); Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal("SUBMITTED", (await RmaApplications(h).ReadAsync(c.ApplicationId)).State);
    }
}
