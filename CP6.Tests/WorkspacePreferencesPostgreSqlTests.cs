using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Workspace_preferences_personal_scope_concurrent_cas_and_original_read()
    {
        await using var a = await Seed();
        await using var b = await Seed();
        await using var host = await AuthenticationApiProcess.Start(a.Db.Database.GetConnectionString()!);
        var alice = await Login(host, a);
        var sameNameInOtherTenant = await Login(host, b);
        const string url = "api/dashboard/scoped-v1/preferences";
        async Task<JsonElement> Read(Dictionary<string, string> cookies, string path = url)
        {
            using var reply = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path));
            Assert.True(reply.IsSuccessStatusCode, await OperationsFailure(reply));
            return await reply.Content.ReadFromJsonAsync<JsonElement>();
        }

        var start = await Read(alice);
        Assert.Equal("0", start.GetProperty("revision").GetString());
        Assert.Equal(8, start.GetProperty("visibleMetricKeys").GetArrayLength());
        var first = new { OperationRef = Guid.NewGuid(), ExpectedRevision = "0", VisibleMetricKeys = new[] { "totalProducts", "todayOrders" } };
        var second = new { OperationRef = Guid.NewGuid(), ExpectedRevision = "0", VisibleMetricKeys = new[] { "monthOrders" } };
        var saves = new[]
        {
            host.Send(OperationsRequest(alice, HttpMethod.Post, url, first)),
            host.Send(OperationsRequest(alice, HttpMethod.Post, url, second))
        };
        var replies = await Task.WhenAll(saves);
        using var replyA = replies[0];
        using var replyB = replies[1];
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        var winner = replyA.IsSuccessStatusCode ? first : second;
        var loser = replyB.IsSuccessStatusCode ? first : second;
        // 模拟原保存响应丢失，仅用原操作号回查已提交状态。
        var originalPath = url + "/operations/" + winner.OperationRef;
        var original = await Read(alice, originalPath);
        Assert.Equal(winner.OperationRef, original.GetProperty("operationRef").GetGuid());
        Assert.Equal("1", original.GetProperty("snapshot").GetProperty("revision").GetString());
        Assert.Equal(winner.VisibleMetricKeys, original.GetProperty("snapshot").GetProperty("visibleMetricKeys").Deserialize<string[]>());
        using (var missed = await host.Send(OperationsRequest(alice, HttpMethod.Get, url + "/operations/" + loser.OperationRef)))
            Assert.Equal(HttpStatusCode.NotFound, missed.StatusCode);
        using (var foreign = await host.Send(OperationsRequest(sameNameInOtherTenant, HttpMethod.Get, originalPath)))
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("0", (await Read(sameNameInOtherTenant)).GetProperty("revision").GetString());

        using (var exact = await host.Send(OperationsRequest(alice, HttpMethod.Post, url, winner)))
        {
            Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
            Assert.Equal(original.GetRawText(), (await exact.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        }
        using (var changed = await host.Send(OperationsRequest(alice, HttpMethod.Post, url, winner with { VisibleMetricKeys = ["activeWorkOrders"] })))
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using (var invalid = await host.Send(OperationsRequest(alice, HttpMethod.Post, url, loser with { ExpectedRevision = "1", VisibleMetricKeys = ["unknown", "unknown"] })))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("1", (await Read(alice)).GetProperty("revision").GetString());

        await a.Db.Sys_Users.Where(user => user.Id == a.User).ExecuteUpdateAsync(set => set.SetProperty(user => user.Enable, false));
        using (var revoked = await host.Send(OperationsRequest(alice, HttpMethod.Get, originalPath)))
            Assert.Contains(revoked.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        await DeviceEvidence("wp02-18-preferences.json", new
        {
            Starting = start, Winner = original, Race = replies.Select(x => (int)x.StatusCode).ToArray(),
            TenantIsolation = true, OriginalRecovered = true, SameKeyDifferentPayloadRejected = true, PostRevocationDenied = true
        });
    }
}
